using System.Collections.Generic;
using Newtonsoft.Json;

namespace CheckMailWM2.Models
{
    public class SheetMetaResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("sheets")]
        public List<string> Sheets { get; set; } = new();

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    public class SheetDataResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("data")]
        public List<List<string>> Data { get; set; } = new();

        [JsonProperty("error")]
        public string? Error { get; set; }
    }

    public class UpdateStatusRequest
    {
        [JsonProperty("action")]
        public string Action { get; set; } = "updateStatus";

        [JsonProperty("sheetName")]
        public string SheetName { get; set; } = string.Empty;

        [JsonProperty("updates")]
        public List<CellUpdateDto> Updates { get; set; } = new();
    }

    public class CellUpdateDto
    {
        [JsonProperty("row")]
        public int Row { get; set; } // 1-based row index in Google Sheets

        [JsonProperty("col")]
        public int Col { get; set; } // 1-based column index in Google Sheets

        [JsonProperty("value")]
        public string Value { get; set; } = string.Empty;

        [JsonProperty("color", NullValueHandling = NullValueHandling.Ignore)]
        public string? Color { get; set; }

        [JsonProperty("bgColor", NullValueHandling = NullValueHandling.Ignore)]
        public string? BgColor { get; set; }
    }

    public class CommonResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("error")]
        public string? Error { get; set; }
    }
}
