using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace CheckMailWM2.Services
{
    /// <summary>
    /// Bộ giải tự động Captcha PerimeterX ("Press & Hold" / "Robot or human")
    /// Sử dụng Chrome DevTools Protocol (CDP) Input.dispatchMouseEvent để mô phỏng tương tác giữ chuột vật lý chân thực (isTrusted = true)
    /// </summary>
    public static class PerimeterXSolver
    {
        /// <summary>
        /// Kiểm tra nhanh xem trang hiện tại có xuất hiện thử thách Captcha PerimeterX hay không
        /// </summary>
        public static async Task<bool> IsCaptchaPresentAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string checkScript = @"
                (function() {
                    const body = document.body ? (document.body.innerText || document.body.textContent || '') : '';
                    const url = window.location.href || '';
                    const hasCaptcha = body.includes('Robot or human') || 
                                       body.includes('Press & Hold') || 
                                       body.includes('Press and Hold') || 
                                       body.includes('Verify your identity') ||
                                       body.includes('Please verify you are a human') ||
                                       body.includes('PerimeterX') ||
                                       body.includes('Access Denied') ||
                                       url.includes('/blocked') ||
                                       url.includes('/challenge') ||
                                       url.includes('/captcha') ||
                                       document.querySelector(""#px-captcha, [id*='px-captcha'], [id*='captcha' i], iframe[src*='captcha' i], iframe[src*='perimeter' i], iframe[title*='challenge' i], div.px-modal"") !== null;
                    return hasCaptcha ? 'YES' : 'NO';
                })();
            ";

            string res = await ExecuteScriptAsync(webView, checkScript);
            res = res?.Trim('"', ' ') ?? "";
            return res == "YES";
        }

        /// <summary>
        /// Tự động định vị nút "Press & Hold" và nhấn giữ chuột liên tục trong tối đa maxHoldSeconds (mặc định 15s).
        /// Khi nhận diện Captcha đã vượt qua thành công, hệ thống tự động nhả nút và hoàn tất sớm.
        /// </summary>
        public static async Task<bool> AutoSolvePressAndHoldAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 webView,
            Action<string>? onLog = null,
            CancellationToken ct = default,
            int maxHoldSeconds = 15)
        {
            if (webView == null) return false;

            try
            {
                // 1. Kiểm tra ban đầu xem có Captcha không
                bool hasCaptcha = await IsCaptchaPresentAsync(webView);
                if (!hasCaptcha)
                {
                    onLog?.Invoke("ℹ️ Không phát hiện Captcha PerimeterX trên trang.");
                    return true;
                }

                onLog?.Invoke($"🤖 [PerimeterX] Bắt đầu quy trình tự động giữ nút 'Press & Hold' (tối đa {maxHoldSeconds}s)...");

                // Đợi 800ms để DOM và canvas/iframe Captcha ổn định tọa độ
                await Task.Delay(800, ct);

                // 2. Tìm tọa độ chính xác của nút hoặc vùng Captcha
                string locateScript = @"
                    (function() {
                        function getCenter(el) {
                            if (!el) return null;
                            const r = el.getBoundingClientRect();
                            if (r.width > 15 && r.height > 15 && r.top < window.innerHeight && r.bottom > 0) {
                                return {
                                    x: Math.round(r.left + r.width / 2),
                                    y: Math.round(r.top + r.height / 2),
                                    w: Math.round(r.width),
                                    h: Math.round(r.height)
                                };
                            }
                            return null;
                        }

                        // Ưu tiên 1: Nút hoặc phần tử có text/aria-label 'Press & Hold'
                        const all = Array.from(document.querySelectorAll('*'));
                        for (const el of all) {
                            const aria = (el.getAttribute('aria-label') || '').toLowerCase();
                            const txt = (el.innerText || el.textContent || '').trim().toLowerCase();
                            if (aria.includes('press & hold') || aria.includes('press and hold') ||
                                (txt.includes('press & hold') && txt.length < 50) ||
                                (txt.includes('press and hold') && txt.length < 50)) {
                                const c = getCenter(el);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: 'text_match' });
                            }
                        }

                        // Ưu tiên 2: Container #px-captcha và các biến thể
                        const pxSelectors = [
                            '#px-captcha',
                            'div[id*=""px-captcha""]',
                            '#px-captcha-wrapper',
                            '#px-captcha-box',
                            'div.px-captcha-container',
                            'div[role=""button""][aria-label*=""Hold"" i]',
                            'div.px-modal'
                        ];
                        for (const sel of pxSelectors) {
                            const el = document.querySelector(sel);
                            if (el) {
                                const btn = el.querySelector('button, [role=""button""], div') || el;
                                const c = getCenter(btn) || getCenter(el);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: sel });
                            }
                        }

                        // Ưu tiên 3: Iframe chứa Captcha
                        const iframes = document.querySelectorAll('iframe');
                        for (const ifr of iframes) {
                            const src = (ifr.src || '') + ' ' + (ifr.title || '');
                            if (src.includes('captcha') || src.includes('perimeter') || src.includes('challenge')) {
                                const c = getCenter(ifr);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: 'iframe' });
                            }
                        }

                        // Ưu tiên 4: Trung tâm màn hình (Vị trí mặc định của box PerimeterX trên Walmart)
                        const bodyText = document.body ? (document.body.innerText || '') : '';
                        if (bodyText.includes('Robot or human') || bodyText.includes('PerimeterX') || bodyText.includes('Press & Hold')) {
                            return JSON.stringify({
                                found: true,
                                x: Math.round(window.innerWidth / 2),
                                y: Math.round(window.innerHeight / 2),
                                w: 320,
                                h: 90,
                                source: 'viewport_center'
                            });
                        }

                        return JSON.stringify({ found: false });
                    })();
                ";

                string locJson = await ExecuteScriptAsync(webView, locateScript);
                locJson = locJson?.StartsWith("\"") == true ? JsonSerializer.Deserialize<string>(locJson) ?? "{}" : locJson ?? "{}";

                double targetX = 640;
                double targetY = 400;

                try
                {
                    using var doc = JsonDocument.Parse(locJson);
                    if (doc.RootElement.TryGetProperty("found", out var pFound) && pFound.GetBoolean())
                    {
                        targetX = doc.RootElement.GetProperty("x").GetDouble();
                        targetY = doc.RootElement.GetProperty("y").GetDouble();
                        string src = doc.RootElement.TryGetProperty("source", out var pSrc) ? pSrc.GetString() ?? "" : "";
                        onLog?.Invoke($"🎯 [PerimeterX] Đã định vị nút Captcha tại tọa độ ({targetX}, {targetY}) [Nguồn: {src}].");
                    }
                    else
                    {
                        onLog?.Invoke($"⚠️ [PerimeterX] Không tìm thấy phần tử trực tiếp, sử dụng tọa độ trung tâm ({targetX}, {targetY}).");
                    }
                }
                catch
                {
                    onLog?.Invoke($"⚠️ [PerimeterX] Lỗi đọc tọa độ, sử dụng vị trí mặc định ({targetX}, {targetY}).");
                }

                // 3. Di chuyển chuột đến vị trí nút
                string moveInitial = FormattableString.Invariant($"{{\"type\":\"mouseMoved\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"modifiers\":0}}");
                await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", moveInitial);
                await Task.Delay(150, ct);

                // 4. Gửi sự kiện nhấn chuột xuống (mousePressed)
                string pressEvent = FormattableString.Invariant($"{{\"type\":\"mousePressed\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"button\":\"left\",\"buttons\":1,\"clickCount\":1,\"modifiers\":0}}");
                await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", pressEvent);

                // Kích hoạt thêm sự kiện DOM dự phòng (nếu trang gắn listener trực tiếp vào element)
                await ExecuteScriptAsync(webView, @"
                    (function() {
                        const el = document.querySelector('#px-captcha') || document.querySelector('[aria-label*=""Hold"" i]');
                        if (el) {
                            const opt = { bubbles: true, cancelable: true, view: window, buttons: 1 };
                            try { el.dispatchEvent(new PointerEvent('pointerdown', opt)); } catch(e){}
                            try { el.dispatchEvent(new MouseEvent('mousedown', opt)); } catch(e){}
                        }
                    })();
                ");

                onLog?.Invoke($"⏳ [PerimeterX] Đang nhấn giữ nút... Sẽ giữ tối đa {maxHoldSeconds}s (hoặc thả ngay khi qua).");

                var sw = Stopwatch.StartNew();
                bool passed = false;

                // 5. Vòng lặp nhấn giữ liên tục với độ trễ vi mô mô phỏng tay người
                try
                {
                    while (sw.ElapsedMilliseconds < maxHoldSeconds * 1000)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Rung nhẹ chuột mô phỏng chuyển động tay thực tế (jitter ±1.5px)
                        double jitterX = targetX + (Random.Shared.NextDouble() * 3.0 - 1.5);
                        double jitterY = targetY + (Random.Shared.NextDouble() * 3.0 - 1.5);
                        string jitterMove = FormattableString.Invariant($"{{\"type\":\"mouseMoved\",\"x\":{jitterX:0.00},\"y\":{jitterY:0.00},\"buttons\":1}}");
                        await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", jitterMove);

                        // Kiểm tra xem Captcha đã qua chưa
                        string statusCheckScript = @"
                            (function() {
                                const body = document.body ? (document.body.innerText || document.body.textContent || '') : '';
                                const url = window.location.href || '';

                                // 1. Form nhập email đã xuất hiện -> ĐÃ VƯỢT THÀNH CÔNG
                                const emailInput = document.querySelector(""input[autocomplete='email'], input[name*='email' i], input[type='email'], input[data-automation-id='email-input']"");
                                if (emailInput) return 'PASSED';

                                // 2. Kiểm tra các dấu hiệu của Captcha
                                const hasCaptcha = body.includes('Robot or human') || 
                                                   body.includes('Press & Hold') || 
                                                   body.includes('Press and Hold') || 
                                                   document.querySelector('#px-captcha') !== null;

                                if (!hasCaptcha) {
                                    return 'PASSED';
                                }

                                return 'HOLDING';
                            })();
                        ";

                        string statusRes = await ExecuteScriptAsync(webView, statusCheckScript);
                        statusRes = statusRes?.Trim('"', ' ') ?? "";

                        if (statusRes == "PASSED")
                        {
                            passed = true;
                            double elapsedSec = sw.ElapsedMilliseconds / 1000.0;
                            onLog?.Invoke($"🎉 [PerimeterX] Nhận diện Captcha đã mở sau {elapsedSec:0.1}s! Đang nhả chuột...");
                            break;
                        }

                        // Đợi 250ms cho chu kỳ tiếp theo
                        await Task.Delay(250, ct);
                    }
                }
                finally
                {
                    // 6. Luôn luôn nhả chuột (mouseReleased) ngay cả khi bị hủy hoặc gặp lỗi
                    string releaseEvent = FormattableString.Invariant($"{{\"type\":\"mouseReleased\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"button\":\"left\",\"buttons\":0,\"clickCount\":1}}");
                    await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", releaseEvent);

                    await ExecuteScriptAsync(webView, @"
                        (function() {
                            const el = document.querySelector('#px-captcha') || document.querySelector('[aria-label*=""Hold"" i]');
                            if (el) {
                                const opt = { bubbles: true, cancelable: true, view: window, buttons: 0 };
                                try { el.dispatchEvent(new PointerEvent('pointerup', opt)); } catch(e){}
                                try { el.dispatchEvent(new MouseEvent('mouseup', opt)); } catch(e){}
                            }
                        })();
                    ");
                }

                // 7. Chờ và kiểm tra trạng thái cuối cùng sau khi nhả chuột
                // Nhiều khi PerimeterX kích hoạt token validation ngay khi nhả nút
                onLog?.Invoke("🔄 [PerimeterX] Đã nhả nút, đang chờ máy chủ Walmart xác thực phiên...");
                await Task.Delay(1200, ct);

                for (int i = 0; i < 4; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    bool stillCaptcha = await IsCaptchaPresentAsync(webView);
                    if (!stillCaptcha)
                    {
                        passed = true;
                        onLog?.Invoke("✅ [PerimeterX] XÁC NHẬN: Đã vượt qua thử thách Captcha thành công!");
                        return true;
                    }
                    await Task.Delay(600, ct);
                }

                if (passed)
                {
                    onLog?.Invoke("✅ [PerimeterX] Captcha đã giải thành công!");
                    return true;
                }
                else
                {
                    onLog?.Invoke("⚠️ [PerimeterX] Đã giữ đủ thời gian nhưng Captcha chưa vượt qua.");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                onLog?.Invoke("🛑 [PerimeterX] Quá trình giải Captcha bị hủy.");
                return false;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"❌ [PerimeterX] Lỗi trong quá trình giải Captcha: {ex.Message}");
                return false;
            }
        }

        private static async Task<string> ExecuteScriptAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string script)
        {
            try
            {
                if (webView.Dispatcher.CheckAccess())
                {
                    if (webView.CoreWebView2 != null)
                    {
                        return await webView.ExecuteScriptAsync(script);
                    }
                }
                else
                {
                    return await webView.Dispatcher.InvokeAsync(async () =>
                    {
                        if (webView.CoreWebView2 != null)
                        {
                            return await webView.ExecuteScriptAsync(script);
                        }
                        return string.Empty;
                    }).Task.Unwrap();
                }
            }
            catch
            {
                // Bỏ qua lỗi hủy hoặc script context
            }
            return string.Empty;
        }

        private static async Task<string> CallCdpMethodAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string method, string jsonParams)
        {
            try
            {
                if (webView.Dispatcher.CheckAccess())
                {
                    if (webView.CoreWebView2 != null)
                    {
                        return await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, jsonParams);
                    }
                }
                else
                {
                    return await webView.Dispatcher.InvokeAsync(async () =>
                    {
                        if (webView.CoreWebView2 != null)
                        {
                            return await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, jsonParams);
                        }
                        return string.Empty;
                    }).Task.Unwrap();
                }
            }
            catch
            {
                // Bỏ qua lỗi giao thức khi trang đang chuyển tiếp
            }
            return string.Empty;
        }
    }
}
