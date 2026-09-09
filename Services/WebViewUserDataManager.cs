using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace CheckMailWM2.Services
{
    public static class WebViewUserDataManager
    {
        private const string FolderPrefix = "WMCheck_UserData_";
        private static CoreWebView2Environment? _sharedEnvironment;
        private static readonly SemaphoreSlim _envLock = new(1, 1);

        /// <summary>
        /// Đường dẫn thư mục UserData dùng chung cố định để lưu phiên / Cookies giải Captcha
        /// </summary>
        public static string GetSharedUserDataFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string path = Path.Combine(appData, "CheckMailWM2", "UserData_Shared");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        /// <summary>
        /// Khởi tạo hoặc lấy CoreWebView2Environment dùng chung cho cả phiên giải Captcha lẫn các luồng quét
        /// </summary>
        public static async Task<CoreWebView2Environment> GetOrCreateSharedEnvironmentAsync()
        {
            if (_sharedEnvironment != null)
                return _sharedEnvironment;

            await _envLock.WaitAsync();
            try
            {
                if (_sharedEnvironment != null)
                    return _sharedEnvironment;

                string userDataPath = GetSharedUserDataFolder();
                var envOptions = new CoreWebView2EnvironmentOptions(
                    additionalBrowserArguments: "--disable-gpu --mute-audio --disable-background-networking --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding"
                );

                _sharedEnvironment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userDataPath,
                    options: envOptions
                );

                return _sharedEnvironment;
            }
            finally
            {
                _envLock.Release();
            }
        }

        /// <summary>
        /// Dọn dẹp tất cả các thư mục tạm còn sót lại từ các phiên chạy trước
        /// </summary>
        public static void CleanAllOrphanUserDataFolders()
        {
            Task.Run(() =>
            {
                try
                {
                    string tempBase = Path.GetTempPath();
                    var dirs = Directory.GetDirectories(tempBase, $"{FolderPrefix}*");
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            Debug.WriteLine($"[WebViewUserDataManager] Đã dọn dẹp thư mục cũ: {dir}");
                        }
                        catch
                        {
                            // Bỏ qua nếu có thư mục đang bị lock
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebViewUserDataManager] Lỗi dọn dẹp toàn cục: {ex.Message}");
                }
            });
        }
    }
}
