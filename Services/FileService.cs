using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CheckMailWM2.Models;
using ClosedXML.Excel;

namespace CheckMailWM2.Services
{
    public class FileService
    {
        /// <summary>
        /// Đọc file TXT và phân tích danh sách tài khoản
        /// </summary>
        public async Task<List<AccountItem>> ReadTxtFileAsync(string filePath)
        {
            var items = new List<AccountItem>();
            if (!File.Exists(filePath)) return items;

            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
            int id = 1;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string email = EmailExtractor.ExtractEmail(line);
                // Bỏ qua dòng không có email hợp lệ
                if (string.IsNullOrWhiteSpace(email))
                {
                    continue;
                }

                items.Add(new AccountItem
                {
                    Id = id++,
                    RowIndex = i + 1,
                    RawData = line,
                    ExtractedEmail = email,
                    Status = CheckStatus.Pending,
                    Note = ""
                });
            }

            return items;
        }

        /// <summary>
        /// Đọc file Excel (.xlsx, .csv) và phân tích các dòng
        /// </summary>
        public List<AccountItem> ReadExcelFile(string filePath, bool skipChecked = true)
        {
            var items = new List<AccountItem>();
            if (!File.Exists(filePath)) return items;

            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) return items;

            var rows = worksheet.RangeUsed()?.RowsUsed()?.ToList();
            if (rows == null || rows.Count == 0) return items;

            int id = 1;
            int startRow = 1;
            var firstRowCells = rows[0].Cells().Select(c => c.GetString().Trim()).ToList();
            bool hasHeader = firstRowCells.Any(c => c.Equals("Email", StringComparison.OrdinalIgnoreCase) || 
                                                    c.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("Trạng thái", StringComparison.OrdinalIgnoreCase) ||
                                                    c.Equals("STT", StringComparison.OrdinalIgnoreCase));
            if (hasHeader) startRow = 2;

            int lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            int lastColIndex = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;

            for (int r = startRow; r <= lastRowNumber; r++)
            {
                var row = worksheet.Row(r);
                if (row.IsEmpty()) continue;

                var cellValues = row.Cells().Select(c => c.GetString().Trim()).ToList();
                string fullRowText = string.Join(" | ", cellValues);
                string email = EmailExtractor.ExtractEmail(fullRowText);

                // Bỏ qua dòng không có email hợp lệ
                if (string.IsNullOrWhiteSpace(email))
                {
                    continue;
                }

                int emailColIndex = -1;
                for (int c = 1; c <= lastColIndex; c++)
                {
                    if (EmailExtractor.ExtractEmail(row.Cell(c).GetString()).Equals(email, StringComparison.OrdinalIgnoreCase))
                    {
                        emailColIndex = c;
                        break;
                    }
                }

                int targetCol = lastColIndex + 1;
                bool alreadyChecked = false;
                for (int c = 1; c <= lastColIndex; c++)
                {
                    if (c == emailColIndex) continue; // Bỏ qua cột chứa email chính

                    string val = row.Cell(c).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    if (EmailExtractor.IsAlreadyCheckedStatus(val))
                    {
                        alreadyChecked = true;
                        targetCol = c;
                        break;
                    }

                    if (c != emailColIndex && c > emailColIndex && EmailExtractor.ContainsEmail(val))
                    {
                        alreadyChecked = true;
                        targetCol = c;
                        break;
                    }
                }

                if (skipChecked && alreadyChecked)
                {
                    continue;
                }

                items.Add(new AccountItem
                {
                    Id = id++,
                    RowIndex = r,
                    RawData = fullRowText,
                    ExtractedEmail = email,
                    Status = CheckStatus.Pending,
                    Note = "",
                    TargetStatusColumnIndex = targetCol
                });
            }

            return items;
        }

        /// <summary>
        /// Xuất kết quả ra file TXT (chuẩn tiếng Anh cho Status)
        /// </summary>
        public async Task ExportToTxtAsync(string outputPath, IEnumerable<AccountItem> items)
        {
            var lines = new List<string>();
            foreach (var item in items)
            {
                string statusText = item.Status.ToOutputString(!string.IsNullOrWhiteSpace(item.ExtractedEmail) ? item.ExtractedEmail : item.RawData);
                lines.Add($"{item.RawData} | {statusText} | {item.CheckedTime:yyyy-MM-dd HH:mm:ss}");
            }
            await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// Cập nhật kết quả vào file Excel nguồn hoặc file Excel mới (chuẩn tiếng Anh và tô màu cho Status)
        /// </summary>
        public void SaveResultsToExcel(string? sourceFilePath, string outputFilePath, IEnumerable<AccountItem> items)
        {
            using var workbook = (!string.IsNullOrWhiteSpace(sourceFilePath) && File.Exists(sourceFilePath)) 
                ? new XLWorkbook(sourceFilePath) 
                : new XLWorkbook();
            
            var worksheet = workbook.Worksheets.FirstOrDefault() ?? workbook.Worksheets.Add("Walmart Check Results");

            int maxCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            int statusCol = maxCol == 0 ? 3 : maxCol + 1;

            if (maxCol == 0)
            {
                worksheet.Cell(1, 1).Value = "Raw Data";
                worksheet.Cell(1, 2).Value = "Extracted Email";
                worksheet.Cell(1, 3).Value = "Status";
                worksheet.Cell(1, 4).Value = "Checked Time";
                worksheet.Row(1).Style.Font.Bold = true;
            }
            else
            {
                worksheet.Cell(1, statusCol).Value = "Status";
                worksheet.Cell(1, statusCol + 1).Value = "Checked Time";
                worksheet.Cell(1, statusCol).Style.Font.Bold = true;
                worksheet.Cell(1, statusCol + 1).Style.Font.Bold = true;
            }

            foreach (var item in items)
            {
                int r = item.RowIndex > 0 ? item.RowIndex : (item.Id + 1);
                int targetCol = item.TargetStatusColumnIndex > 0 ? item.TargetStatusColumnIndex : statusCol;
                string statusText = item.Status.ToOutputString(!string.IsNullOrWhiteSpace(item.ExtractedEmail) ? item.ExtractedEmail : item.RawData);

                if (maxCol == 0)
                {
                    worksheet.Cell(r, 1).Value = item.RawData;
                    worksheet.Cell(r, 2).Value = item.ExtractedEmail;
                    worksheet.Cell(r, 3).Value = statusText;
                    worksheet.Cell(r, 4).Value = item.CheckedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                }
                else
                {
                    worksheet.Cell(r, targetCol).Value = statusText;
                    worksheet.Cell(r, targetCol + 1).Value = item.CheckedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                }

                // Tô màu theo trạng thái
                var cell = worksheet.Cell(r, targetCol);
                if (item.Status == CheckStatus.Registered)
                {
                    cell.Style.Font.FontColor = XLColor.Green;
                    cell.Style.Font.Bold = true;
                }
                else if (item.Status == CheckStatus.NotRegistered)
                {
                    cell.Style.Font.FontColor = XLColor.DarkOrange;
                    cell.Style.Font.Bold = true;
                }
                else if (item.Status == CheckStatus.Suspended)
                {
                    cell.Style.Font.FontColor = XLColor.Purple;
                    cell.Style.Font.Bold = true;
                }
                else if (item.Status == CheckStatus.Error)
                {
                    cell.Style.Font.FontColor = XLColor.Red;
                }
            }

            worksheet.Columns().AdjustToContents();
            workbook.SaveAs(outputFilePath);
        }
    }
}
