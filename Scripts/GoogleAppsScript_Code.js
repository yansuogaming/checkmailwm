/**
 * =========================================================================
 * GOOGLE APPS SCRIPT CHO TOOL CHECK MAIL WALMART PRO
 * =========================================================================
 * 
 * HƯỚNG DẪN CÀI ĐẶT NHANH (Chỉ mất 30 giây):
 * 1. Mở file Google Sheet của bạn trên trình duyệt.
 * 2. Trên thanh menu, chọn: Tiện ích mở rộng (Extensions) -> Apps Script.
 * 3. Xóa hết code mặc định trong trình chỉnh sửa và DÁN toàn bộ nội dung file này vào.
 * 4. Bấm nút "Lưu" (biểu tượng hình đĩa mềm hoặc Ctrl + S).
 * 5. Bấm nút "Triển khai" (Deploy) ở góc trên bên phải -> Chọn "Triển khai mới" (New deployment).
 * 6. Bấm biểu tượng bánh răng cạnh "Chọn loại" -> Chọn "Ứng dụng web" (Web app).
 * 7. Cấu hình:
 *    - Mô tả: Tool Check Mail Walmart Pro
 *    - Thực thi dưới dạng: Tôi (Execute as: Me)
 *    - Ai có quyền truy cập: Bất kỳ ai (Who has access: Anyone)
 * 8. Bấm "Triển khai" (Deploy) -> Cấp quyền truy cập nếu Google hỏi.
 * 9. Copy đường link "URL ứng dụng web" (dạng: https://script.google.com/macros/s/.../exec) và dán vào Tool C#.
 * =========================================================================
 */

function doGet(e) {
  try {
    const action = e.parameter.action;
    const ss = SpreadsheetApp.getActiveSpreadsheet();

    // 1. Lấy danh sách tất cả các Tab (Sheet names)
    if (action === "getSheets") {
      const sheets = ss.getSheets().map(s => s.getName());
      return createJsonResponse({
        success: true,
        sheets: sheets
      });
    }

    // 2. Đọc toàn bộ dữ liệu từ 1 Sheet tab
    if (action === "getData") {
      const sheetName = e.parameter.sheetName;
      const sheet = sheetName ? ss.getSheetByName(sheetName) : ss.getSheets()[0];
      
      if (!sheet) {
        return createJsonResponse({
          success: false,
          error: "Không tìm thấy Sheet: " + sheetName
        });
      }

      const values = sheet.getDataRange().getDisplayValues();
      return createJsonResponse({
        success: true,
        data: values
      });
    }

    return createJsonResponse({
      success: false,
      error: "Action không hợp lệ."
    });

  } catch (error) {
    return createJsonResponse({
      success: false,
      error: error.toString()
    });
  }
}

function doPost(e) {
  try {
    const postData = JSON.parse(e.postData.contents);
    const action = postData.action;
    const ss = SpreadsheetApp.getActiveSpreadsheet();

    // 3. Cập nhật kết quả trạng thái (Registered / Not Registered / Suspended / Error) vào Sheet
    if (action === "updateStatus") {
      const sheetName = postData.sheetName;
      const sheet = sheetName ? ss.getSheetByName(sheetName) : ss.getSheets()[0];

      if (!sheet) {
        return createJsonResponse({
          success: false,
          error: "Không tìm thấy Sheet: " + sheetName
        });
      }

      const updates = postData.updates || [];
      for (let i = 0; i < updates.length; i++) {
        const item = updates[i];
        if (item.row > 0 && item.col > 0) {
          const cell = sheet.getRange(item.row, item.col);
          cell.setValue(item.value);

          // Tô màu ô kết quả theo chuẩn màu sắc
          if (item.color) {
            cell.setFontColor(item.color).setFontWeight("bold");
          } else if (item.value === "Registered") {
            cell.setFontColor("#16a34a").setFontWeight("bold"); // Xanh lá
          } else if (item.value === "Not Registered" || (typeof item.value === "string" && item.value.indexOf("@") !== -1)) {
            cell.setFontColor("#d97706").setFontWeight("bold"); // Cam (Amber) cho Chưa đăng ký
          } else if (item.value === "Suspended") {
            cell.setFontColor("#9333ea").setFontWeight("bold"); // Tím
          } else if (item.value === "Error") {
            cell.setFontColor("#dc2626").setFontWeight("bold"); // Đỏ
          }

          if (item.bgColor) {
            cell.setBackground(item.bgColor);
          }
        }
      }

      SpreadsheetApp.flush();
      return createJsonResponse({
        success: true,
        message: `Đã cập nhật ${updates.length} ô thành công.`
      });
    }

    return createJsonResponse({
      success: false,
      error: "Action POST không hợp lệ."
    });

  } catch (error) {
    return createJsonResponse({
      success: false,
      error: error.toString()
    });
  }
}

function createJsonResponse(data) {
  return ContentService.createTextOutput(JSON.stringify(data))
    .setMimeType(ContentService.MimeType.JSON);
}
