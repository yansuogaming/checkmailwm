using System.Windows.Media;

namespace CheckMailWM2.Models
{
    public enum CheckStatus
    {
        Pending,
        Checking,
        Registered,       // Email đã đăng ký
        NotRegistered,    // Email chưa đăng ký
        Suspended,        // Tài khoản bị tạm đình chỉ
        Error,            // Lỗi / Captcha / Block / Timeout
        Skipped           // Đã có kết quả từ trước nên bỏ qua
    }

    public static class CheckStatusExtensions
    {
        /// <summary>
        /// Hiển thị tiếng Việt thuần túy trên giao diện UI
        /// </summary>
        public static string ToDisplayString(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pending => "Chờ duyệt",
                CheckStatus.Checking => "Đang kiểm tra...",
                CheckStatus.Registered => "Đã đăng ký",
                CheckStatus.NotRegistered => "Chưa đăng ký",
                CheckStatus.Suspended => "Bị đình chỉ",
                CheckStatus.Error => "Lỗi / Captcha",
                CheckStatus.Skipped => "Đã bỏ qua",
                _ => "Không rõ"
            };
        }

        /// <summary>
        /// Chuẩn tiếng Anh khi đẩy lên Google Sheet hoặc xuất file Excel/TXT
        /// </summary>
        public static string ToEnglishString(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pending => "Pending",
                CheckStatus.Checking => "Checking",
                CheckStatus.Registered => "Registered",
                CheckStatus.NotRegistered => "Not Registered",
                CheckStatus.Suspended => "Suspended",
                CheckStatus.Error => "Error",
                CheckStatus.Skipped => "Skipped",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Chuẩn kết quả khi xuất ra Google Sheet hoặc file Excel/TXT.
        /// Đối với email Chưa đăng ký (Not Registered), điền thẳng địa chỉ email ra ngoài theo yêu cầu.
        /// </summary>
        public static string ToOutputString(this CheckStatus status, string? email = null)
        {
            if (status == CheckStatus.NotRegistered && !string.IsNullOrWhiteSpace(email))
            {
                return email;
            }

            return status.ToEnglishString();
        }

        /// <summary>
        /// Mã màu Hex tương ứng khi tô màu trên Google Sheet
        /// </summary>
        public static string ToHexColor(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Registered => "#16a34a",     // Xanh lá (Green)
                CheckStatus.NotRegistered => "#d97706",  // Cam (Amber)
                CheckStatus.Suspended => "#9333ea",      // Tím (Purple)
                CheckStatus.Error => "#dc2626",          // Đỏ (Red)
                _ => "#64748b"
            };
        }

        public static Brush GetBadgeBackground(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pending => new SolidColorBrush(Color.FromArgb(35, 100, 116, 139)),
                CheckStatus.Checking => new SolidColorBrush(Color.FromArgb(40, 2, 132, 199)),
                CheckStatus.Registered => new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
                CheckStatus.NotRegistered => new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
                CheckStatus.Suspended => new SolidColorBrush(Color.FromArgb(40, 168, 85, 247)),
                CheckStatus.Error => new SolidColorBrush(Color.FromArgb(40, 239, 68, 68)),
                CheckStatus.Skipped => new SolidColorBrush(Color.FromArgb(30, 100, 116, 139)),
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }

        public static Brush GetBadgeBorder(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pending => new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                CheckStatus.Checking => new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                CheckStatus.Registered => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                CheckStatus.NotRegistered => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                CheckStatus.Suspended => new SolidColorBrush(Color.FromRgb(168, 85, 247)),
                CheckStatus.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                CheckStatus.Skipped => new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                _ => new SolidColorBrush(Colors.Transparent)
            };
        }

        public static Brush GetBadgeForeground(this CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pending => new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                CheckStatus.Checking => new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                CheckStatus.Registered => new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                CheckStatus.NotRegistered => new SolidColorBrush(Color.FromRgb(217, 119, 6)),
                CheckStatus.Suspended => new SolidColorBrush(Color.FromRgb(147, 51, 234)),
                CheckStatus.Error => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                CheckStatus.Skipped => new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
            };
        }
    }
}
