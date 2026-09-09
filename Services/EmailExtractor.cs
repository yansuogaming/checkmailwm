using System;
using System.Text.RegularExpressions;

namespace CheckMailWM2.Services
{
    public static class EmailExtractor
    {
        private static readonly Regex EmailRegex = new Regex(
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        /// <summary>
        /// Trích xuất địa chỉ email đầu tiên tìm thấy trong chuỗi bất kỳ (kể cả chuỗi ngăn cách bằng | hoặc tab)
        /// </summary>
        public static string ExtractEmail(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var match = EmailRegex.Match(input);
            if (match.Success)
            {
                return match.Value.Trim().ToLowerInvariant();
            }

            return string.Empty;
        }

        /// <summary>
        /// Kiểm tra xem chuỗi có chứa định dạng email hợp lệ hay không
        /// </summary>
        public static bool ContainsEmail(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return EmailRegex.IsMatch(input);
        }

        /// <summary>
        /// Kiểm tra xem giá trị ô đã có trạng thái đã check từ trước hay chưa (hỗ trợ cả tiếng Anh và tiếng Việt)
        /// </summary>
        public static bool IsAlreadyCheckedStatus(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value.Trim().ToLowerInvariant();

            // Nếu cột trạng thái chứa email (do Not Registered điền thẳng email)
            if (normalized.Contains("@") && normalized.Contains("."))
            {
                return true;
            }

            // Từ khóa tiếng Anh
            if (normalized == "registered" || 
                normalized == "not registered" || 
                normalized == "notregistered" ||
                normalized == "used" || 
                normalized == "not used" || 
                normalized == "unregistered" ||
                normalized == "live" || 
                normalized == "die" ||
                normalized == "suspended" ||
                normalized == "exists" ||
                normalized == "active")
            {
                return true;
            }

            // Từ khóa tiếng Việt
            if (normalized.Contains("đã có") || normalized.Contains("da co") ||
                normalized.Contains("chưa đăng ký") || normalized.Contains("chua dang ky") ||
                normalized.Contains("đã đăng ký") || normalized.Contains("da dang ky") ||
                normalized.Contains("đã dùng") || normalized.Contains("da dung") ||
                normalized.Contains("chưa dùng") || normalized.Contains("chua dung") ||
                normalized.Contains("đình chỉ") || normalized.Contains("dinh chi") ||
                normalized.Contains("đã check") || normalized.Contains("da check") ||
                normalized.Contains("chưa check") || normalized.Contains("chua check"))
            {
                return true;
            }

            return false;
        }
    }
}
