# 🛒 Walmart Email Checker Pro v2.0

<p align="center">
  <img src="app_icon.ico" width="100" height="100" alt="App Icon" />
</p>

<p align="center">
  <b>Phần mềm kiểm tra tài khoản email Walmart tự động, đa luồng với giao diện Fluent Design Windows 11 hiện đại.</b><br/>
  <i>Automated, high-performance Walmart email account checker powered by WPF-UI and Microsoft Edge WebView2.</i>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0_Windows-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Platform-Windows_10_|_11_x64-0078D6?style=for-the-badge&logo=windows&logoColor=white" alt="Platform" />
  <img src="https://img.shields.io/badge/UI-Fluent_Design_(WPF--UI)-005FB8?style=for-the-badge&logo=fluentui&logoColor=white" alt="WPF-UI" />
  <img src="https://img.shields.io/badge/Engine-WebView2_Runtime-008080?style=for-the-badge&logo=microsoftedge&logoColor=white" alt="WebView2" />
  <img src="https://img.shields.io/badge/Version-v2.0.0_Pro-blue?style=for-the-badge" alt="Version" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg?style=for-the-badge" alt="License" />
</p>

---

## 📖 Giới Thiệu (Overview)

**Walmart Email Checker Pro v2.0** là giải pháp phần mềm chuyên nghiệp chạy trên hệ điều hành Windows, giúp kiểm tra hàng loạt tình trạng đăng ký của email trên hệ sinh thái Walmart một cách tự động, nhanh chóng và chuẩn xác.

Được xây dựng trên nền tảng **.NET 10 (WPF)** kết hợp với thư viện giao diện cao cấp **WPF-UI (Lepo)** và nhân trình duyệt **Microsoft Edge WebView2**, ứng dụng mang lại trải nghiệm mượt mà với hiệu ứng kính Mica/Acrylic của Windows 11, đồng thời sở hữu khả năng xử lý DOM thông minh và vượt rào bảo mật PerimeterX hiệu quả.

---

## ✨ Tính Năng Nổi Bật (Key Features)

### 1. 🎨 Giao Diện Fluent Design Chuẩn Windows 11
- Tích hợp cửa sổ `ui:FluentWindow` hỗ trợ hiệu ứng hiển thị cao cấp **Mica / Acrylic**.
- Chuyển đổi linh hoạt giữa giao diện **Sáng (Light Mode)** và **Tối (Dark Mode)** ngay trong ứng dụng.
- Hệ thống thẻ **KPI Dashboard** trực quan: Tổng số, Đã kiểm tra, Đã đăng ký (Live), Chưa đăng ký, Bị đình chỉ, Lỗi / Captcha.
- Bảng dữ liệu `ui:DataGrid` mượt mà, hỗ trợ tìm kiếm từ khóa và lọc trạng thái tức thời (Real-time Filter).

### 2. ⚡ Đa Luồng & Kiểm Soát Tốc Độ (Multi-Threading Engine)
- Tự do cấu hình số luồng chạy song song (1 – 5 luồng tùy theo tài nguyên và proxy/IP).
- Tùy chỉnh khoảng thời gian trễ ngẫu nhiên (Delay) giữa mỗi lần kiểm tra để mô phỏng hành vi người dùng thật và giảm nguy cơ bị khóa IP.
- Quản lý trạng thái thông minh: Hỗ trợ **Bắt đầu**, **Tạm dừng**, **Tiếp tục** và **Dừng lại khẩn cấp** mà không làm mất dữ liệu.

### 3. 🛡️ Tự Động Giải Captcha PerimeterX (Human-like Mouse Trajectory)
- **Mô phỏng di chuột người thật (Human-like Bezier Curve)**: Di chuyển con trỏ chuột theo quỹ đạo cong Cubic Bezier tự nhiên, kết hợp gia tốc phi tuyến (Easing / Fitts's Law: khởi đầu chậm, lướt nhanh ở giữa, giảm tốc nhẹ nhàng khi đến nút).
- **Rung lắc vi mô sinh học (Muscle Tremors & Hesitation)**: Tạo độ rung tay sinh học ($\pm 1.2\text{px}$) trong lúc di chuyển và ngập ngừng dừng quan sát ($140\text{ms} - 260\text{ms}$) trước khi bấm.
- **Tương tác vật lý nguyên bản qua CDP (`isTrusted = true`)**: Sử dụng Chrome DevTools Protocol (`Input.dispatchMouseEvent`) để tạo sự kiện giữ chuột vật lý hoàn hảo, không bị hệ thống chống bot phát hiện.
- **Thời gian giữ linh hoạt tối đa 15 giây**: Liên tục kiểm tra trạng thái DOM, khi nhận diện Captcha đã giải xong, hệ thống sẽ **ngay lập tức nhả chuột tự động** và lướt nhẹ ra khỏi nút như phản xạ người thật.
- **Con trỏ ảo trực quan (Visual Ghost Cursor)**: Hiển thị chấm con trỏ màu đỏ/xanh lá chuyển động mượt mà trên giao diện trình duyệt để người dùng dễ dàng theo dõi trực tiếp.
- **Hoạt động đa tầng**: Hoạt động mượt mà trên cả các luồng quét ngầm lẫn cửa sổ làm ấm phiên (với nút bấm kích hoạt **`🤖 Tự giải Captcha (15s)`**).

### 4. 🧠 Nhận Diện Trạng Thái Email Toàn Diện
Engine phân tích phản hồi DOM của Walmart chính xác:
| Trạng Thái | Ý Nghĩa Kỹ Thuật | Màu Sắc Nhận Diện |
| :--- | :--- | :--- |
| **Đã đăng ký (Live)** | Nhận diện form yêu cầu mật khẩu, OTP hoặc thông báo "Welcome back" | 🟢 Xanh lá (`#16a34a`) |
| **Chưa đăng ký** | Nhận diện form tạo tài khoản mới (First Name, Last Name, Password) | 🟠 Cam (`#d97706`) |
| **Bị đình chỉ (Suspended)** | Nhận diện thông báo tài khoản bị tạm ngưng / vô hiệu hóa | 🟣 Tím (`#9333ea`) |
| **Lỗi / Captcha** | Gặp PerimeterX chặn luồng, lỗi mạng hoặc timeout | 🔴 Đỏ (`#dc2626`) |
| **Đã bỏ qua** | Tài khoản đã có kết quả kiểm tra từ trước đó | ⚪ Xám (`#64748b`) |

### 5. 📊 Đa Dạng Nguồn Dữ Liệu Vào / Ra (I/O)
- **Google Sheets (Live 2-Way Sync)**:
  - Kết nối trực tiếp qua Google Apps Script Web App URL.
  - Tự động lấy danh sách tabs, đọc hàng nghìn dòng dữ liệu.
  - **Tự động ghi kết quả và tô màu trực tiếp lên Google Sheet theo thời gian thực**.
- **File Excel (.xlsx / .csv)**:
  - Xử lý siêu tốc qua thư viện **ClosedXML**.
  - Tự động nhận diện dòng tiêu đề (Header), tùy chọn cột ghi kết quả, bỏ qua dòng đã check.
- **File TXT Thô (Regex Extractor)**:
  - Bóc tách email chuẩn xác từ bất kỳ định dạng chuỗi hỗn hợp nào (ví dụ: `Address | City | email@example.com | Note`).
- **Xuất Báo Cáo Chuyên Nghiệp**:
  - Xuất ra file Excel (.xlsx) với định dạng màu sắc cell tương ứng từng trạng thái.
  - Xuất file TXT phân loại theo từng nhóm kết quả.

---

## 🛠️ Yêu Cầu Hệ Thống (System Requirements)

- **Hệ điều hành**: Windows 10 (Phiên bản 1809 trở lên) hoặc Windows 11 (64-bit).
- **Môi trường**:
  - [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (Thường có sẵn trên Windows 10/11 cập nhật mới).
  - [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Nếu chạy qua source code hoặc bản framework-dependent).

---

## 🚀 Hướng Dẫn Sử Dụng Nhanh (Quick Start)

### Bước 1: Khởi động và làm ấm phiên (Warm-up Session)
1. Khởi chạy ứng dụng `CheckMailWM2.exe`.
2. Bấm nút màu tím: **`1. MỞ TRÌNH DUYỆT / GIẢI CAPTCHA`**.
3. Cửa sổ WebView2 sẽ mở trang xác thực của Walmart. Hệ thống sẽ **tự động định vị và nhấn giữ nút Press & Hold trong tối đa 15 giây**. Bạn cũng có thể bấm nút **`🤖 Tự giải Captcha (15s)`** hoặc tự tay nhấn giữ nếu muốn.
4. Khi vượt qua thành công, ứng dụng sẽ thông báo và tự động lưu phiên làm việc.

### Bước 2: Nạp dữ liệu vào ứng dụng
- **Dùng Google Sheets**: Chuyển sang tab Google Sheets -> Dán URL Web App -> Bấm `Kết nối Sheets` -> Chọn Tab làm việc -> Bấm `Nạp Dữ Liệu Sheet`.
- **Dùng File Excel**: Chuyển sang tab File Excel -> Bấm `Chọn file Excel (.xlsx)` -> Bấm `Nạp Dữ Liệu Excel`.
- **Dùng File TXT**: Chuyển sang tab File TXT -> Bấm `Chọn file TXT` -> Bấm `Nạp Dữ Liệu TXT`.

### Bước 3: Cài đặt và Bắt đầu kiểm tra
1. Vào tab **Cài Đặt Chạy**:
   - Chọn số luồng kiểm tra (khuyến nghị **1 - 2 luồng** để đảm bảo độ bền của phiên và tránh bị chặn).
   - Đặt khoảng thời gian chờ (Delay ngẫu nhiên giữa các lần check, ví dụ: 2000ms - 4000ms).
2. Nhấn nút xanh **`BẮT ĐẦU`** để hệ thống tiến hành kiểm tra.
3. Theo dõi bảng kết quả trực quan và bảng KPI cập nhật thời gian thực.
4. Bạn có thể nhấn **`Tạm dừng`** hoặc **`Dừng lại`** bất kỳ lúc nào nếu cần.

### Bước 4: Xuất kết quả
- Bấm nút **`Xuất Kết Quả`** và chọn định dạng mong muốn:
  - **Excel (.xlsx)**: File được định dạng màu sắc ô chuẩn quốc tế.
  - **TXT**: Danh sách phân chia theo từng trạng thái.

---

## 📑 Hướng Dẫn Cài Đặt Google Apps Script (30 Giây)

Để kết nối và đồng bộ trực tiếp 2 chiều với Google Sheets:

1. Mở trang Google Sheet của bạn trên trình duyệt.
2. Trên thanh menu, chọn: **Tiện ích mở rộng (Extensions)** $\rightarrow$ **Apps Script**.
3. Xóa toàn bộ nội dung mặc định và dán toàn bộ code từ file [`Scripts/GoogleAppsScript_Code.js`](Scripts/GoogleAppsScript_Code.js) vào trình biên soạn.
4. Nhấn biểu tượng đĩa mềm **Lưu (Ctrl + S)**.
5. Nhấn nút **Triển khai (Deploy)** ở góc trên bên phải $\rightarrow$ Chọn **Triển khai mới (New deployment)**.
6. Nhấp vào biểu tượng bánh răng ⚙️ cạnh "Chọn loại" $\rightarrow$ Chọn **Ứng dụng web (Web app)**:
   - **Mô tả**: `Walmart Checker Pro API`
   - **Thực thi dưới dạng**: `Tôi (Me)`
   - **Ai có quyền truy cập**: `Bất kỳ ai (Anyone)`
7. Nhấn **Triển khai (Deploy)** $\rightarrow$ Chọn tài khoản và **Cấp quyền truy cập (Authorize access)**.
8. Sao chép đường link **URL ứng dụng web** (dạng `https://script.google.com/macros/s/.../exec`) và dán vào ứng dụng C#.

---

## 📂 Cấu Trúc Dự Án (Project Structure)

```text
CheckMailWM2/
├── CheckMailWM2.csproj          # Cấu hình dự án .NET 10 WPF + Dependencies
├── App.xaml / App.xaml.cs       # Quản lý vòng đời ứng dụng và dọn dẹp profile
├── app_icon.ico                 # Icon ứng dụng chuẩn Windows
├── .gitignore                   # Cấu hình loại trừ file build, cache, nhị phân
├── sample_data.txt              # File dữ liệu mẫu thử nghiệm
├── Models/
│   ├── CheckStatus.cs           # Enum trạng thái, badge color converter, extension
│   ├── AccountItem.cs           # Model tài khoản triển khai INotifyPropertyChanged
│   ├── AppSettings.cs           # Model lưu cấu hình ứng dụng
│   └── GoogleSheetDto.cs        # DTO giao tiếp API Google Apps Script
├── Services/
│   ├── PerimeterXSolver.cs      # Engine tự giải Captcha PerimeterX Press & Hold qua CDP
│   ├── EmailExtractor.cs        # Regex trích xuất email & chuẩn hóa dữ liệu
│   ├── WebViewUserDataManager.cs# Quản lý đường dẫn và dọn dẹp UserData_Shared
│   ├── WalmartCheckerEngine.cs  # Engine điều khiển WebView2, phân tích DOM
│   ├── GoogleAppsScriptService.cs# HTTP client gọi API Google Apps Script
│   ├── FileService.cs           # Đọc/ghi dữ liệu TXT và Excel ClosedXML
│   └── WalmartTaskManager.cs    # Bộ điều phối đa luồng (Task Manager)
├── Views/
│   ├── MainWindow.xaml / .cs    # Giao diện chính FluentWindow WPF-UI
│   └── BrowserSessionWindow.xaml/.cs # Cửa sổ WebView2 giải Captcha trực quan
└── Scripts/
    └── GoogleAppsScript_Code.js # Mã nguồn triển khai Web App cho Google Sheets
```

---

## 🔨 Hướng Dẫn Build & Đóng Gói (Build & Publish)

### 1. Clone mã nguồn
```bash
git clone https://github.com/yansuogaming/checkmailwm.git
cd checkmailwm
```

### 2. Biên dịch dự án (Debug / Release)
```bash
dotnet build -c Release
```

### 3. Đóng gói thành 1 file Portable EXE duy nhất (Single-file Self-contained)
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish
```
> File chạy độc lập `CheckMailWM2.exe` sẽ được tạo trong thư mục `publish/`.

---

## ⚠️ Tuyên Bố Miễn Trừ Trách Nhiệm (Disclaimer)

- Công cụ này được phát triển với mục đích **nghiên cứu kỹ thuật, học tập và kiểm thử tự động hóa quy trình quản lý tài khoản cá nhân hợp pháp**.
- Tác giả không chịu trách nhiệm đối với bất kỳ hành vi sử dụng công cụ sai mục đích, vi phạm điều khoản dịch vụ của bên thứ ba hoặc các quy định pháp luật hiện hành. Người sử dụng tự chịu hoàn toàn trách nhiệm đối với hoạt động của mình.

---

## 👤 Tác Giả (Author)

- **Tác giả**: [Yansuo](https://github.com/yansuogaming)
- **Repository**: [https://github.com/yansuogaming/checkmailwm](https://github.com/yansuogaming/checkmailwm)
- **Bản quyền**: © 2026 Yansuo Developer. Giấy phép mã nguồn mở [MIT License](LICENSE).
