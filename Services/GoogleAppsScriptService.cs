using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CheckMailWM2.Models;
using Newtonsoft.Json;

namespace CheckMailWM2.Services
{
    public class GoogleAppsScriptService
    {
        private readonly HttpClient _httpClient;

        public GoogleAppsScriptService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
        }

        /// <summary>
        /// Lấy danh sách tên tất cả các tabs trong file Google Sheet
        /// </summary>
        public async Task<List<string>> GetSheetNamesAsync(string scriptUrl)
        {
            try
            {
                string url = $"{scriptUrl.TrimEnd('/')}?action=getSheets";
                var response = await _httpClient.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<SheetMetaResponse>(response);

                if (result != null && result.Success)
                {
                    return result.Sheets;
                }
                else
                {
                    throw new Exception(result?.Error ?? "Không thể lấy danh sách Sheets từ Apps Script.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi kết nối Google Apps Script: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Đọc toàn bộ dữ liệu từ Sheet tab được chọn
        /// </summary>
        public async Task<List<List<string>>> GetSheetDataAsync(string scriptUrl, string sheetName)
        {
            try
            {
                string url = $"{scriptUrl.TrimEnd('/')}?action=getData&sheetName={Uri.EscapeDataString(sheetName)}";
                var response = await _httpClient.GetStringAsync(url);
                var result = JsonConvert.DeserializeObject<SheetDataResponse>(response);

                if (result != null && result.Success)
                {
                    return result.Data;
                }
                else
                {
                    throw new Exception(result?.Error ?? "Không thể đọc dữ liệu Sheet.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Lỗi tải dữ liệu Sheet '{sheetName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Cập nhật kết quả (Registered / Not Registered / Suspended / Error) và màu sắc cho một ô trên Google Sheet
        /// </summary>
        public async Task<bool> UpdateCellStatusAsync(string scriptUrl, string sheetName, int row, int col, string value, string? color = null)
        {
            try
            {
                var requestObj = new UpdateStatusRequest
                {
                    Action = "updateStatus",
                    SheetName = sheetName,
                    Updates = new List<CellUpdateDto>
                    {
                        new CellUpdateDto { Row = row, Col = col, Value = value, Color = color }
                    }
                };

                string json = JsonConvert.SerializeObject(requestObj);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(scriptUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<CommonResponse>(responseBody);

                return result != null && result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cập nhật hàng loạt kết quả lên Google Sheet
        /// </summary>
        public async Task<bool> UpdateBatchStatusAsync(string scriptUrl, string sheetName, List<CellUpdateDto> updates)
        {
            if (updates == null || updates.Count == 0) return true;

            try
            {
                var requestObj = new UpdateStatusRequest
                {
                    Action = "updateStatus",
                    SheetName = sheetName,
                    Updates = updates
                };

                string json = JsonConvert.SerializeObject(requestObj);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(scriptUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<CommonResponse>(responseBody);

                return result != null && result.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
