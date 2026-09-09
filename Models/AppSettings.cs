namespace CheckMailWM2.Models
{
    public class AppSettings
    {
        public int ThreadCount { get; set; } = 2;
        public int DelayMs { get; set; } = 1500;
        public int TimeoutSeconds { get; set; } = 30;
        public bool SkipCheckedRows { get; set; } = true;
        public bool AutoSaveToGoogleSheet { get; set; } = true;
        public string LastScriptUrl { get; set; } = string.Empty;
        public string LastTxtInputPath { get; set; } = string.Empty;
        public string LastExcelInputPath { get; set; } = string.Empty;
        public int SelectedSourceType { get; set; } = 0; // 0: Google Sheet, 1: TXT, 2: Excel
        public bool IsDarkMode { get; set; } = true;
    }
}
