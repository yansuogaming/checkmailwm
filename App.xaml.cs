using System.Windows;
using CheckMailWM2.Services;

namespace CheckMailWM2
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Tự động dọn dẹp các thư mục profile cũ khi khởi động
            WebViewUserDataManager.CleanAllOrphanUserDataFolders();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            // Dọn dẹp lại khi thoát
            WebViewUserDataManager.CleanAllOrphanUserDataFolders();
        }
    }
}
