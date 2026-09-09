using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using CheckMailWM2.Models;
using Microsoft.Web.WebView2.Core;

namespace CheckMailWM2.Services
{
    public class WalmartCheckerEngine : IDisposable
    {
        private readonly int _threadId;
        private readonly int _timeoutSeconds;
        private Microsoft.Web.WebView2.Wpf.WebView2? _webView;
        private Grid? _hiddenContainer;
        private Panel? _hostContainer;
        private bool _isInitialized = false;
        private string _lastEmailEscaped = string.Empty;

        public event Action<string>? OnLog;

        public WalmartCheckerEngine(int threadId, int timeoutSeconds = 30)
        {
            _threadId = threadId;
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task InitializeAsync(Panel hostContainer)
        {
            if (_isInitialized && _webView?.CoreWebView2 != null) return;
            _hostContainer = hostContainer;

            OnLog?.Invoke($"[Luồng {_threadId}] Đang kết nối môi trường WebView2...");

            await hostContainer.Dispatcher.InvokeAsync(() =>
            {
                _hiddenContainer = new Grid
                {
                    Width = 1280,
                    Height = 800,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };
                hostContainer.Children.Add(_hiddenContainer);

                _webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                _hiddenContainer.Children.Add(_webView);
            });

            var environment = await WebViewUserDataManager.GetOrCreateSharedEnvironmentAsync();
            await hostContainer.Dispatcher.InvokeAsync(async () =>
            {
                if (_webView == null) return;
                await _webView.EnsureCoreWebView2Async(environment);

                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsScriptEnabled = true;
                _webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";

                await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                ");
            });

            _isInitialized = true;
            OnLog?.Invoke($"[Luồng {_threadId}] Môi trường WebView2 đã sẵn sàng.");

            // Làm ấm phiên và chờ redirect đến identity.walmart.com
            await NavigateToLoginAndWaitAsync(CancellationToken.None);
        }

        public async Task<(CheckStatus Status, string Note, bool IsCaptcha)> CheckEmailAsync(string email, Panel hostParent, CancellationToken cancellationToken = default)
        {
            if (!_isInitialized || _webView?.CoreWebView2 == null)
            {
                await InitializeAsync(hostParent);
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                return (CheckStatus.Error, "Email trống", false);
            }

            try
            {
                OnLog?.Invoke($"[Luồng {_threadId}] Chuẩn bị form đăng nhập Walmart: {email}...");

                bool formReady = await ResetToFreshLoginFormAsync(cancellationToken);

                if (!formReady)
                {
                    // Kiểm tra xem có dính Captcha không và tự động giải
                    if (_webView != null && await PerimeterXSolver.IsCaptchaPresentAsync(_webView))
                    {
                        OnLog?.Invoke($"[Luồng {_threadId}] 🤖 Phát hiện Captcha PerimeterX! Đang tự động nhấn giữ nút Press & Hold trong tối đa 15s...");
                        bool solved = await PerimeterXSolver.AutoSolvePressAndHoldAsync(_webView, msg => OnLog?.Invoke($"[Luồng {_threadId}] {msg}"), cancellationToken, maxHoldSeconds: 15);
                        if (solved)
                        {
                            OnLog?.Invoke($"[Luồng {_threadId}] ✅ Tự động vượt Captcha thành công! Đang tải lại form đăng nhập...");
                            formReady = await ResetToFreshLoginFormAsync(cancellationToken);
                        }
                    }

                    if (!formReady)
                    {
                        string checkCaptchaScript = @"
                            (function() {
                                const body = document.body ? (document.body.innerText || '') : '';
                                return (body.includes('Robot or human') || body.includes('Press & Hold') || body.includes('Press and Hold') || document.querySelector('#px-captcha')) ? 'CAPTCHA' : 'NO';
                            })();
                        ";
                        string cRes = await ExecuteScriptTextAsync(checkCaptchaScript);
                        if (cRes?.Contains("CAPTCHA") == true)
                        {
                            OnLog?.Invoke($"[Luồng {_threadId}] ⚠️ Captcha PerimeterX chưa được giải.");
                            return (CheckStatus.Error, "Phát hiện Captcha trên trang đăng nhập", true);
                        }

                        OnLog?.Invoke($"[Luồng {_threadId}] Hết thời gian chờ tải form nhập email.");
                        return (CheckStatus.Error, "Timeout load form", false);
                    }
                }

                OnLog?.Invoke($"[Luồng {_threadId}] Đang điền email {email}...");

                string emailEscaped = email.Replace("\\", "\\\\").Replace("'", "\\'");
                _lastEmailEscaped = emailEscaped;

                await FillEmailInputAsync(emailEscaped, cancellationToken);

                // Đợi 600ms để React state cập nhật
                await Task.Delay(600, cancellationToken);

                OnLog?.Invoke($"[Luồng {_threadId}] Đang gửi yêu cầu kiểm tra (Submit Continue)...");

                await SubmitLoginFormAsync(cancellationToken);

                OnLog?.Invoke($"[Luồng {_threadId}] Đang phân tích kết quả cho {email}...");

                var checkResult = await AnalyzeWalmartResultAsync(email, _timeoutSeconds * 1000, cancellationToken);

                OnLog?.Invoke($"[Luồng {_threadId}] Hoàn tất: {checkResult.Status.ToDisplayString()} ({checkResult.Note})");

                return checkResult;
            }
            catch (OperationCanceledException)
            {
                return (CheckStatus.Pending, "Đã hủy", false);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Luồng {_threadId}] Lỗi: {ex.Message}");
                return (CheckStatus.Error, $"Lỗi: {ex.Message}", false);
            }
        }

        private async Task<bool> ResetToFreshLoginFormAsync(CancellationToken ct)
        {
            if (_webView == null) return false;

            string checkStateScript = @"
                (function() {
                    try {
                        const url = window.location.href || '';
                        const body = document.body ? (document.body.innerText || '') : '';

                        // 1. Nếu có nút Change từ màn hình trước (màn hình mật khẩu / OTP của email đã đăng ký)
                        const changeBtn = Array.from(document.querySelectorAll('button, a')).find(el => {
                            const txt = (el.innerText || el.textContent || '').trim().toLowerCase();
                            return txt === 'change' || txt === 'change email' || txt === 'not you?';
                        });
                        if (changeBtn) {
                            changeBtn.click();
                            return 'CLICKED_CHANGE';
                        }

                        // 2. Nếu đang ở màn hình Create Account (của email chưa đăng ký) -> Bấm Sign In để về lại form email
                        const signInBtn = Array.from(document.querySelectorAll('button, a')).find(el => {
                            const txt = (el.innerText || el.textContent || '').trim().toLowerCase();
                            return txt === 'sign in' || txt === 'sign in instead' || txt.includes('already have an account');
                        });
                        if (signInBtn && (url.includes('/create') || url.includes('/register') || body.includes('Create your Walmart account'))) {
                            signInBtn.click();
                            return 'CLICKED_CHANGE';
                        }

                        // 3. Nếu có nút Back
                        const backBtn = document.querySelector(""button[aria-label*='back' i], button[data-automation-id*='back' i]"");
                        if (backBtn && !url.includes('/account/login')) {
                            backBtn.click();
                            return 'CLICKED_CHANGE';
                        }

                        // 4. Nếu đang ở màn hình nhập email sẵn
                        const SELECTORS = [
                            ""input[autocomplete='email']"",
                            ""input[name='Phone number or email (required)']"",
                            ""input[name*='email' i]"",
                            ""input[data-automation-id='email-input']"",
                            ""input#email-input"",
                            ""input[id*='email' i]"",
                            ""input[type='email']"",
                            ""input[aria-label*='email' i]"",
                            ""input[placeholder*='email' i]"",
                            ""input[placeholder*='phone' i]""
                        ];
                        let input = null;
                        for (const sel of SELECTORS) {
                            input = document.querySelector(sel);
                            if (input) break;
                        }

                        if (input) {
                            const proto = window.HTMLInputElement.prototype;
                            const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                            if (nativeSetter) {
                                nativeSetter.call(input, '');
                            } else {
                                input.value = '';
                            }
                            if (input._valueTracker) { input._valueTracker.setValue('__clear__'); }
                            input.dispatchEvent(new Event('input', { bubbles: true }));
                            input.dispatchEvent(new Event('change', { bubbles: true }));
                            return 'INPUT_READY';
                        }
                    } catch(e) {}
                    return 'NAVIGATE';
                })();
            ";

            string res = await ExecuteScriptTextAsync(checkStateScript);
            res = res?.Trim('"', ' ') ?? "";

            if (res == "INPUT_READY")
            {
                return true;
            }

            if (res == "CLICKED_CHANGE")
            {
                await Task.Delay(800, ct);
                return true;
            }

            return await NavigateToLoginAndWaitAsync(ct);
        }

        private async Task<bool> NavigateToLoginAndWaitAsync(CancellationToken ct)
        {
            if (_webView == null || _hostContainer == null) return false;

            OnLog?.Invoke($"[Luồng {_threadId}] Đang kết nối tới trang đăng nhập Walmart (chờ redirect sang identity.walmart.com)...");

            // 1. Xóa sạch DOM cũ và đặt placeholder để tuyệt đối không kiểm tra nhầm DOM của trang Captcha cũ
            await ExecuteScriptTextAsync(@"
                try {
                    document.open();
                    document.write('<!DOCTYPE html><html><head><title>Loading</title></head><body><div id=""wm_nav_placeholder"">Connecting...</div></body></html>');
                    document.close();
                } catch(e) {}
            ");

            await Task.Delay(200, ct);

            // 2. Bắt đầu Navigate
            await _hostContainer.Dispatcher.InvokeAsync(() =>
            {
                if (_webView?.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Navigate("https://www.walmart.com/account/login");
                }
            });

            // 3. Chờ cho toàn bộ chuỗi redirect kết thúc và form đăng nhập xuất hiện
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 35000 && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);

                string readyScript = @"
                    (function() {
                        // Nếu vẫn là trang placeholder tạm thời thì tiếp tục đợi
                        if (document.getElementById('wm_nav_placeholder')) {
                            return 'LOADING';
                        }

                        const url = window.location.href || '';
                        const body = document.body ? (document.body.innerText || '') : '';

                        // 1. Kiểm tra ô nhập email trước tiên
                        const SELECTORS = [
                            ""input[autocomplete='email']"",
                            ""input[name='Phone number or email (required)']"",
                            ""input[name*='email' i]"",
                            ""input[data-automation-id='email-input']"",
                            ""input#email-input"",
                            ""input[id*='email' i]"",
                            ""input[type='email']"",
                            ""input[aria-label*='email' i]"",
                            ""input[placeholder*='email' i]"",
                            ""input[placeholder*='phone' i]""
                        ];
                        for (const sel of SELECTORS) {
                            const el = document.querySelector(sel);
                            if (el) return 'READY';
                        }

                        // 2. Kiểm tra Captcha PerimeterX thật sự (nút Press & Hold)
                        const hasCaptcha = body.includes('Robot or human') || 
                                           body.includes('Press & Hold') || 
                                           body.includes('Press and Hold') || 
                                           document.querySelector('#px-captcha') !== null;
                        if (hasCaptcha) {
                            return 'CAPTCHA';
                        }

                        return 'WAITING';
                    })();
                ";

                string r = await ExecuteScriptTextAsync(readyScript);
                r = r?.Trim('"', ' ') ?? "";

                if (r == "READY")
                {
                    OnLog?.Invoke($"[Luồng {_threadId}] Đã tải xong form nhập email trên identity.walmart.com.");
                    return true;
                }

                if (r == "CAPTCHA")
                {
                    // Chỉ xác nhận là Captcha nếu đã qua 1.5s (trang mới đã tải từ mạng xong)
                    if (sw.ElapsedMilliseconds > 1500)
                    {
                        OnLog?.Invoke($"[Luồng {_threadId}] 🤖 Phát hiện Captcha PerimeterX! Đang tự động nhấn giữ nút Press & Hold (tối đa 15s)...");
                        if (_webView != null)
                        {
                            bool solved = await PerimeterXSolver.AutoSolvePressAndHoldAsync(_webView, msg => OnLog?.Invoke($"[Luồng {_threadId}] {msg}"), ct, maxHoldSeconds: 15);
                            if (solved)
                            {
                                OnLog?.Invoke($"[Luồng {_threadId}] ✅ Tự động vượt Captcha thành công! Đang tải lại form...");
                                sw.Restart();
                                continue;
                            }
                        }
                        OnLog?.Invoke($"[Luồng {_threadId}] ⚠️ Không thể tự giải Captcha trên trang đăng nhập.");
                        return false;
                    }
                }
            }

            return false;
        }

        private async Task FillEmailInputAsync(string emailEscaped, CancellationToken ct)
        {
            string fillScript = $@"
                (function() {{
                    const SELECTORS = [
                        ""input[autocomplete='email']"",
                        ""input[name='Phone number or email (required)']"",
                        ""input[name*='email' i]"",
                        ""input[data-automation-id='email-input']"",
                        ""input#email-input"",
                        ""input[id*='email' i]"",
                        ""input[type='email']"",
                        ""input[aria-label*='email' i]"",
                        ""input[placeholder*='email' i]"",
                        ""input[placeholder*='phone' i]""
                    ];
                    let input = null;
                    for (const sel of SELECTORS) {{
                        input = document.querySelector(sel);
                        if (input) break;
                    }}
                    if (input) {{
                        input.focus();
                        input.select();
                        const proto = window.HTMLInputElement.prototype;
                        const nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
                        if (nativeSetter) {{
                            nativeSetter.call(input, '');
                            nativeSetter.call(input, '{emailEscaped}');
                        }} else {{
                            input.value = '{emailEscaped}';
                        }}
                        if (input._valueTracker) {{ input._valueTracker.setValue('__val_before__'); }}
                        input.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
                        input.dispatchEvent(new Event('change', {{ bubbles: true, cancelable: true }}));
                        return true;
                    }}
                    return false;
                }})();
            ";
            await ExecuteScriptTextAsync(fillScript);
        }

        private async Task SubmitLoginFormAsync(CancellationToken ct)
        {
            if (_webView == null || _hostContainer == null) return;

            // 1. Giả lập phím Enter qua DevTools Protocol
            try
            {
                var enterDown = JsonSerializer.Serialize(new { type = "rawKeyDown", windowsVirtualKeyCode = 13, code = "Enter", key = "Enter" });
                var enterUp = JsonSerializer.Serialize(new { type = "keyUp", windowsVirtualKeyCode = 13, code = "Enter", key = "Enter" });

                await _hostContainer.Dispatcher.InvokeAsync(async () =>
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", enterDown);
                        await Task.Delay(50, ct);
                        await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", enterUp);
                    }
                });
            }
            catch { }

            // 2. Click nút Submit trên giao diện
            string clickScript = @"
                (function() {
                    const btn = document.querySelector(""button[type='submit'], button[data-automation-id='signin-submit-btn'], button#signin-submit-btn"") 
                        || Array.from(document.querySelectorAll('button')).find(b => {
                            const t = (b.innerText || b.textContent || '').trim().toLowerCase();
                            return t.includes('sign in') || t.includes('continue') || t.includes('next');
                        });

                    if (btn) {
                        btn.disabled = false;
                        btn.removeAttribute('disabled');
                        btn.focus();
                        ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'].forEach(evt => {
                            try {
                                btn.dispatchEvent(new MouseEvent(evt, { bubbles: true, cancelable: true, view: window }));
                            } catch(e) {}
                        });
                        try { btn.click(); } catch(e){}
                        try { if (btn.form) btn.form.requestSubmit(); } catch(e){}
                        return true;
                    }

                    const form = document.querySelector('form');
                    if (form) {
                        try { form.requestSubmit(); } catch(e) { form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); }
                        return true;
                    }
                    return false;
                })();
            ";
            await ExecuteScriptTextAsync(clickScript);
        }

        private async Task<(CheckStatus Status, string Note, bool IsCaptcha)> AnalyzeWalmartResultAsync(string email, int timeoutMs, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            int retrySubmitCount = 0;

            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                if (ct.IsCancellationRequested)
                    return (CheckStatus.Pending, "Đã hủy", false);

                string checkScript = @"
                    (function() {
                        const url = window.location.href || '';
                        const bodyText = document.body ? (document.body.innerText || document.body.textContent || '') : '';
                        const titleHeading = Array.from(document.querySelectorAll('h1, h2, h3, [role=""heading""]')).map(h => (h.innerText || h.textContent || '').trim()).join(' ');

                        // 1. CAPTCHA / BOT DETECTION / PERIMETERX
                        const hasCaptcha = bodyText.includes('Robot or human') || 
                                           bodyText.includes('Press & Hold') || 
                                           bodyText.includes('Press and Hold') || 
                                           bodyText.includes('Verify your identity') ||
                                           bodyText.includes('Please verify you are a human') ||
                                           bodyText.includes('PerimeterX') ||
                                           bodyText.includes('Access Denied') ||
                                           url.includes('/blocked') ||
                                           url.includes('/challenge') ||
                                           url.includes('/captcha') ||
                                           document.querySelector(""#px-captcha, [id*='captcha' i], iframe[src*='captcha' i], iframe[src*='perimeter' i], iframe[title*='challenge' i], iframe[title*='human' i], div.px-modal"") !== null;
                        if (hasCaptcha) {
                            return JSON.stringify({ status: 'CAPTCHA', reason: 'PerimeterX Block' });
                        }

                        // 2. SUSPENDED
                        const isSuspended = bodyText.includes('This account has been suspended') ||
                                            bodyText.includes('account has been suspended') ||
                                            bodyText.includes('Your account has been suspended');
                        if (isSuspended) {
                            return JSON.stringify({ status: 'SUSPENDED', reason: 'Account suspended' });
                        }

                        // 3. INVALID EMAIL
                        const isInvalidEmail = bodyText.includes('Please enter a valid email address') ||
                                               bodyText.includes('Enter a valid email') ||
                                               bodyText.includes('Please enter a valid email');
                        if (isInvalidEmail) {
                            return JSON.stringify({ status: 'INVALID_EMAIL', reason: 'Invalid email' });
                        }

                        // 4. REGISTERED
                        const hasWelcomeBack = titleHeading.toLowerCase().includes('welcome back') || bodyText.toLowerCase().includes('welcome back');
                        const hasChooseMethod = bodyText.includes('Choose a sign in method') || bodyText.includes('Choose a sign-in method') || bodyText.includes('Choose how you want to sign in');
                        const hasSendCode = bodyText.includes('Send a verification code') || bodyText.includes('Email me a verification code') || bodyText.includes('verification code') || bodyText.includes('Enter the 6-digit code') || bodyText.includes('Check your email for a code');
                        const hasForgotPassword = bodyText.includes('Forgot password') || bodyText.includes('Forgot password?');
                        const hasEnterPassword = bodyText.includes('Enter your password') || bodyText.includes('Sign in to your Walmart account') || bodyText.includes('Sign in with a password') || bodyText.includes('Sign in with password');
                        const hasWithPasswordBtn = document.querySelector(""button#withpassword-sign-in-button, [data-automation-id='withpassword-sign-in-button'], input[aria-label*='Enter your password' i]"") !== null;
                        const hasPasswordInput = document.querySelector(""input[name*='password' i]:not([name*='new' i]), input[type='password']:not([name*='new' i])"") !== null;
                        const isSignInUrl = url.includes('/signin') || url.includes('/withotp') || url.includes('/password') || url.includes('/verify') || url.includes('/auth') || url.includes('/verifyToken');

                        if (hasWelcomeBack || hasChooseMethod || hasSendCode || hasForgotPassword || hasEnterPassword || hasWithPasswordBtn || hasPasswordInput || isSignInUrl) {
                            return JSON.stringify({ status: 'REGISTERED', reason: 'Account exists' });
                        }

                        // 5. NOT REGISTERED
                        const firstNameInput = document.querySelector(""input[id*='firstName' i], input[name*='firstName' i], input[data-automation-id*='firstName' i]"");
                        const lastNameInput = document.querySelector(""input[id*='lastName' i], input[name*='lastName' i], input[data-automation-id*='lastName' i]"");
                        const hasCreateAccountHeading = titleHeading.includes('Create your Walmart account') || 
                                                        titleHeading.includes('Create your account') || 
                                                        titleHeading.includes('Create account') ||
                                                        titleHeading.includes(""Let's create your account"") ||
                                                        bodyText.includes('Create your Walmart account') ||
                                                        bodyText.includes(""Let's create your account"") ||
                                                        bodyText.includes('Tell us your name');
                        const isCreateUrl = url.includes('/create') || url.includes('/register') || url.includes('/signup');

                        if (hasCreateAccountHeading || (firstNameInput && lastNameInput) || (firstNameInput && bodyText.includes('Create a password')) || isCreateUrl) {
                            return JSON.stringify({ status: 'NOT_REGISTERED', reason: 'Account does not exist' });
                        }

                        // 6. ERROR BANNER
                        const hasErrorBanner = (bodyText.includes('We are having technical difficulties') ||
                                               bodyText.includes('Account temporarily locked') ||
                                               bodyText.includes('Too many attempts') ||
                                               bodyText.includes(""We're sorry, something went wrong"") ||
                                               bodyText.includes('Something went wrong. Please try again')) &&
                                              !document.querySelector(""input[autocomplete='email'], input[name*='email' i], input[type='email']"");
                        if (hasErrorBanner) {
                            return JSON.stringify({ status: 'ERROR_BANNER', reason: 'Walmart error page' });
                        }

                        // 7. WAITING: kiểm tra ô email hiện tại
                        const SELECTORS = [
                            ""input[autocomplete='email']"",
                            ""input[name='Phone number or email (required)']"",
                            ""input[name*='email' i]"",
                            ""input[data-automation-id='email-input']"",
                            ""input#email-input"",
                            ""input[id*='email' i]"",
                            ""input[type='email']"",
                            ""input[aria-label*='email' i]"",
                            ""input[placeholder*='email' i]"",
                            ""input[placeholder*='phone' i]""
                        ];
                        let inp = null;
                        for (const s of SELECTORS) {
                            inp = document.querySelector(s);
                            if (inp) break;
                        }
                        const emailVal = inp ? inp.value : '';
                        return JSON.stringify({ status: 'WAITING', reason: inp ? 'email_form' : 'loading', emailVal: emailVal });
                    })();
                ";

                string rawJson = await ExecuteScriptTextAsync(checkScript);
                if (!string.IsNullOrWhiteSpace(rawJson) && rawJson != "null")
                {
                    try
                    {
                        string unescaped = rawJson.StartsWith("\"") ? JsonSerializer.Deserialize<string>(rawJson) ?? "" : rawJson;
                        using var doc = JsonDocument.Parse(unescaped);
                        string status = doc.RootElement.GetProperty("status").GetString() ?? "";
                        string reason = doc.RootElement.GetProperty("reason").GetString() ?? "";
                        string emailVal = doc.RootElement.TryGetProperty("emailVal", out var pVal) ? pVal.GetString() ?? "" : "";

                        if (status == "REGISTERED")
                        {
                            return (CheckStatus.Registered, reason, false);
                        }
                        if (status == "NOT_REGISTERED")
                        {
                            return (CheckStatus.NotRegistered, reason, false);
                        }
                        if (status == "CAPTCHA")
                        {
                            OnLog?.Invoke($"[Luồng {_threadId}] 🤖 Phát hiện Captcha PerimeterX sau khi submit! Đang tự động nhấn giữ nút Press & Hold (tối đa 15s)...");
                            if (_webView != null)
                            {
                                bool solved = await PerimeterXSolver.AutoSolvePressAndHoldAsync(_webView, msg => OnLog?.Invoke($"[Luồng {_threadId}] {msg}"), ct, maxHoldSeconds: 15);
                                if (solved)
                                {
                                    OnLog?.Invoke($"[Luồng {_threadId}] ✅ Đã tự vượt Captcha sau submit thành công! Tiếp tục phân tích kết quả...");
                                    stopwatch.Restart();
                                    continue;
                                }
                            }
                            OnLog?.Invoke($"[Luồng {_threadId}] ⚠️ Không thể tự giải Captcha sau khi submit.");
                            return (CheckStatus.Error, "Phát hiện Captcha trên trang đăng nhập", true);
                        }
                        if (status == "SUSPENDED")
                        {
                            OnLog?.Invoke($"[Luồng {_threadId}] ⛔ Phát hiện: Tài khoản đã bị tạm đình chỉ (Suspended).");
                            return (CheckStatus.Suspended, "Account suspended", false);
                        }
                        if (status == "INVALID_EMAIL")
                        {
                            OnLog?.Invoke($"[Luồng {_threadId}] ⚠️ Walmart báo: Email không hợp lệ (Please enter a valid email address).");
                            return (CheckStatus.Error, "Email không hợp lệ", false);
                        }
                        if (status == "ERROR_BANNER")
                        {
                            return (CheckStatus.Error, reason, false);
                        }

                        if (status == "WAITING" && reason == "email_form")
                        {
                            if (string.IsNullOrEmpty(emailVal))
                            {
                                OnLog?.Invoke($"[Luồng {_threadId}] Ô email bị trống (trang vừa tải xong). Đang điền lại...");
                                await FillEmailInputAsync(_lastEmailEscaped, ct);
                                await Task.Delay(500, ct);
                                await SubmitLoginFormAsync(ct);
                                continue;
                            }

                            if (stopwatch.ElapsedMilliseconds > (retrySubmitCount + 1) * 2500 && retrySubmitCount < 4)
                            {
                                retrySubmitCount++;
                                OnLog?.Invoke($"[Luồng {_threadId}] Thử gửi lại submit ({retrySubmitCount}/4)...");
                                await SubmitLoginFormAsync(ct);
                            }
                        }
                    }
                    catch { }
                }

                await Task.Delay(400, ct);
            }

            return (CheckStatus.Error, "Check timeout", false);
        }

        private async Task<string> ExecuteScriptTextAsync(string script)
        {
            if (_webView == null || _hostContainer == null) return string.Empty;

            try
            {
                Task<string>? task = null;
                _hostContainer.Dispatcher.Invoke(() =>
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        task = _webView.ExecuteScriptAsync(script);
                    }
                });

                if (task != null)
                {
                    return await task;
                }
            }
            catch
            {
                return string.Empty;
            }
            return string.Empty;
        }

        public void Dispose()
        {
            try
            {
                _hostContainer?.Dispatcher.Invoke(() =>
                {
                    _webView?.Dispose();
                    if (_hiddenContainer != null && _hostContainer != null && _hostContainer.Children.Contains(_hiddenContainer))
                    {
                        _hostContainer.Children.Remove(_hiddenContainer);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WalmartCheckerEngine] Lỗi dispose: {ex.Message}");
            }
        }
    }
}
