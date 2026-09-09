using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CheckMailWM2.Services;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui.Controls;

namespace CheckMailWM2.Views
{
    public partial class BrowserSessionWindow : FluentWindow
    {
        private CancellationTokenSource? _cts;
        private bool _isClosing = false;
        private bool _isAutoSolving = false;

        public BrowserSessionWindow()
        {
            InitializeComponent();
            this.Loaded += BrowserSessionWindow_Loaded;
            this.Closing += BrowserSessionWindow_Closing;
        }

        private async void BrowserSessionWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                txtStatus.Text = "⏳ Đang khởi tạo môi trường trình duyệt chia sẻ...";

                var sharedEnv = await WebViewUserDataManager.GetOrCreateSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(sharedEnv);

                webView.CoreWebView2.Settings.IsStatusBarEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.IsScriptEnabled = true;
                webView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                    Object.defineProperty(navigator, 'webdriver', {
                        get: () => undefined
                    });
                ");

                webView.CoreWebView2.NavigationStarting += (s, args) =>
                {
                    progressBar.Visibility = Visibility.Visible;
                    txtStatus.Text = $"🌐 Đang tải trang: {args.Uri}";
                };

                webView.CoreWebView2.NavigationCompleted += (s, args) =>
                {
                    progressBar.Visibility = Visibility.Collapsed;
                    txtStatus.Text = "💡 Hãy giải Captcha nếu có. Khi vào đến form đăng nhập, hệ thống sẽ tự động lưu phiên.";
                };

                webView.CoreWebView2.Navigate("https://www.walmart.com/account/login");

                _cts = new CancellationTokenSource();
                _ = StartAutoDetectionLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                progressBar.Visibility = Visibility.Collapsed;
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                txtStatus.Text = $"❌ Lỗi khởi tạo WebView2: {ex.Message}";
                System.Windows.MessageBox.Show($"Không thể khởi tạo trình duyệt: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task StartAutoDetectionLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_isClosing)
            {
                try
                {
                    await Task.Delay(800, ct);

                    if (webView?.CoreWebView2 == null || _isClosing)
                        break;

                    string checkScript = @"
                        (function() {
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

                            const bodyText = document.body ? (document.body.innerText || '') : '';
                            const isCaptcha = bodyText.includes('Robot or human') || 
                                              bodyText.includes('Press & Hold') || 
                                              bodyText.includes('Press and Hold') ||
                                              bodyText.includes('Verify your identity') ||
                                              bodyText.includes('Please verify you are a human') ||
                                              bodyText.includes('PerimeterX') ||
                                              document.querySelector(""#px-captcha, [id*='captcha' i], iframe[src*='captcha' i], iframe[src*='perimeter' i], div.px-modal"") !== null;

                            if (input && !isCaptcha) {
                                return 'READY';
                            }
                            if (isCaptcha) {
                                return 'CAPTCHA';
                            }
                            return 'WAITING';
                        })();
                    ";

                    string result = await webView.ExecuteScriptAsync(checkScript);
                    result = result?.Trim('"', ' ') ?? "";

                    if (result == "READY" && !_isClosing)
                    {
                        _isClosing = true;
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                            txtStatus.Text = "✅ ĐÃ VƯỢT QUA CAPTCHA THÀNH CÔNG! Đang tự động lưu phiên và đóng trong 1.5 giây...";

                            await Task.Delay(1500);
                            CloseWithSuccess();
                        });
                        break;
                    }
                    else if (result == "CAPTCHA" && !_isAutoSolving && !_isClosing)
                    {
                        _ = TriggerAutoSolveAsync(ct);
                    }
                }
                catch
                {
                    // Lỗi vòng lặp hủy tác vụ
                }
            }
        }

        private async Task TriggerAutoSolveAsync(CancellationToken ct)
        {
            if (_isAutoSolving || _isClosing || webView == null) return;
            _isAutoSolving = true;

            try
            {
                btnAutoSolve.IsEnabled = false;
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                txtStatus.Text = "🤖 Phát hiện Captcha PerimeterX! Đang tự động nhấn giữ nút Press & Hold (tối đa 15s)...";

                bool solved = await PerimeterXSolver.AutoSolvePressAndHoldAsync(
                    webView,
                    msg => Dispatcher.Invoke(() => txtStatus.Text = msg),
                    ct,
                    maxHoldSeconds: 15);

                if (solved && !_isClosing)
                {
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    txtStatus.Text = "✅ ĐÃ TỰ ĐỘNG VƯỢT QUA CAPTCHA THÀNH CÔNG! Đang lưu phiên và đóng...";
                    await Task.Delay(1500, ct);
                    CloseWithSuccess();
                }
                else if (!solved && !_isClosing)
                {
                    txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    txtStatus.Text = "⚠️ Chưa vượt qua được Captcha sau 15s. Bạn có thể bấm [Tự giải Captcha] để thử lại hoặc tự tay nhấn giữ.";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                txtStatus.Text = $"❌ Lỗi tự giải Captcha: {ex.Message}";
            }
            finally
            {
                _isAutoSolving = false;
                btnAutoSolve.IsEnabled = true;
            }
        }

        private void BtnAutoSolve_Click(object sender, RoutedEventArgs e)
        {
            _ = TriggerAutoSolveAsync(_cts?.Token ?? CancellationToken.None);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            webView?.CoreWebView2?.Reload();
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            webView?.CoreWebView2?.Navigate("https://www.walmart.com/account/login");
        }

        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            CloseWithSuccess();
        }

        private void CloseWithSuccess()
        {
            if (_isClosing && this.DialogResult.HasValue) return;
            _isClosing = true;
            _cts?.Cancel();
            try
            {
                this.DialogResult = true;
            }
            catch
            {
                try { this.Close(); } catch { }
            }
        }

        private void BrowserSessionWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            _cts?.Cancel();
        }
    }
}
