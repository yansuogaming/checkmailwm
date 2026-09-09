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
                }
                catch
                {
                    // Lỗi vòng lặp hủy tác vụ
                }
            }
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
