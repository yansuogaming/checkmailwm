using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CheckMailWM2.Models;
using CheckMailWM2.Services;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace CheckMailWM2.Views
{
    public partial class MainWindow : FluentWindow
    {
        private readonly ObservableCollection<AccountItem> _items = new();
        private ICollectionView? _itemsView;
        private readonly WalmartTaskManager _taskManager = new();
        private readonly GoogleAppsScriptService _gasService = new();
        private readonly FileService _fileService = new();
        private bool _isDarkMode = true;

        public MainWindow()
        {
            InitializeComponent();
            SetupDataView();
            SetupEvents();
        }

        private void SetupDataView()
        {
            dgvResults.ItemsSource = _items;
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            if (_itemsView != null)
            {
                _itemsView.Filter = FilterItem;
            }
        }

        private void SetupEvents()
        {
            _taskManager.OnItemUpdated += TaskManager_OnItemUpdated;
            _taskManager.OnProgressChanged += TaskManager_OnProgressChanged;
            _taskManager.OnCompleted += TaskManager_OnCompleted;
            _taskManager.OnLog += TaskManager_OnLog;
            _taskManager.OnRequestManualCaptcha += TaskManager_OnRequestManualCaptcha;
        }

        private async Task<bool> TaskManager_OnRequestManualCaptcha()
        {
            return await Dispatcher.InvokeAsync(() =>
            {
                lblStatusMsg.Text = "⚠️ Phát hiện Captcha trên trang đăng nhập! Đang mở cửa sổ để bạn giải...";
                var win = new BrowserSessionWindow
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Topmost = true
                };
                bool? result = win.ShowDialog();
                if (result == true)
                {
                    lblStatusMsg.Text = "✅ Đã giải xong Captcha thành công! Hệ thống đang tiếp tục chạy...";
                    return true;
                }
                else
                {
                    lblStatusMsg.Text = "⚠️ Cửa sổ giải Captcha đã đóng. Hệ thống tạm dừng, bấm [Tiếp tục] khi sẵn sàng.";
                    btnPause.Content = "Tiếp tục";
                    btnStart.IsEnabled = true;
                    return false;
                }
            });
        }

        #region Theme Toggle

        private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = !_isDarkMode;
            if (_isDarkMode)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                btnThemeToggle.Icon = new SymbolIcon(SymbolRegular.WeatherMoon20);
            }
            else
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                btnThemeToggle.Icon = new SymbolIcon(SymbolRegular.WeatherSunny20);
            }
        }

        #endregion

        #region Google Sheets

        private async void BtnConnectSheet_Click(object sender, RoutedEventArgs e)
        {
            string url = txtScriptUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("Vui lòng nhập đường link Google Apps Script Web App URL!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            btnConnectSheet.IsEnabled = false;
            lblStatusMsg.Text = "⏳ Đang kết nối tới Google Sheets...";

            try
            {
                var sheetNames = await _gasService.GetSheetNamesAsync(url);
                cboSheets.Items.Clear();
                foreach (var name in sheetNames)
                {
                    cboSheets.Items.Add(name);
                }

                if (cboSheets.Items.Count > 0)
                {
                    cboSheets.SelectedIndex = 0;
                    lblStatusMsg.Text = $"✅ Đã kết nối thành công! Tìm thấy {sheetNames.Count} bảng tính.";
                    System.Windows.MessageBox.Show($"Kết nối thành công! Đã lấy được {sheetNames.Count} Sheet tabs.", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                lblStatusMsg.Text = "❌ Lỗi kết nối Google Sheets.";
                System.Windows.MessageBox.Show($"Lỗi kết nối: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                btnConnectSheet.IsEnabled = true;
            }
        }

        private async void BtnLoadSheetData_Click(object sender, RoutedEventArgs e)
        {
            string url = txtScriptUrl.Text.Trim();
            string selectedSheet = cboSheets.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(selectedSheet))
            {
                System.Windows.MessageBox.Show("Vui lòng nhập URL và chọn một Sheet tab cần nạp!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            btnLoadSheetData.IsEnabled = false;
            lblStatusMsg.Text = $"⏳ Đang tải dữ liệu từ sheet '{selectedSheet}'...";

            try
            {
                var rawData = await _gasService.GetSheetDataAsync(url, selectedSheet);
                if (rawData == null || rawData.Count == 0)
                {
                    System.Windows.MessageBox.Show("Sheet không có dữ liệu!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                _items.Clear();
                bool skipChecked = chkSkipCheckedSheet.IsChecked == true;
                int id = 1;

                // 1. Xác định cột ghi Status trên toàn Sheet (để kết quả được ghi vào đúng 1 cột thẳng hàng)
                int detectedStatusCol = -1;
                int maxColsInSheet = 0;

                for (int r = 0; r < Math.Min(rawData.Count, 5); r++)
                {
                    var row = rawData[r];
                    for (int c = 0; c < row.Count; c++)
                    {
                        string header = row[c].Trim();
                        if (header.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("Trạng thái", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("Trạng Thái", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("Result", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("Kết quả", StringComparison.OrdinalIgnoreCase) ||
                            header.Equals("Walmart", StringComparison.OrdinalIgnoreCase))
                        {
                            detectedStatusCol = c + 1;
                            break;
                        }
                    }
                    if (detectedStatusCol > 0) break;
                }

                foreach (var row in rawData)
                {
                    for (int c = row.Count - 1; c >= 0; c--)
                    {
                        if (!string.IsNullOrWhiteSpace(row[c]))
                        {
                            if (c + 1 > maxColsInSheet)
                                maxColsInSheet = c + 1;
                            break;
                        }
                    }
                }

                int defaultStatusCol = detectedStatusCol > 0 ? detectedStatusCol : (maxColsInSheet > 0 ? maxColsInSheet + 1 : 2);

                int startRowIndex = 0;
                if (rawData.Count > 1)
                {
                    var headerRow = rawData[0];
                    if (headerRow.Any(c => c.Equals("Email", StringComparison.OrdinalIgnoreCase) || 
                                           c.Equals("Status", StringComparison.OrdinalIgnoreCase) ||
                                           c.Equals("Trạng thái", StringComparison.OrdinalIgnoreCase) ||
                                           c.Equals("STT", StringComparison.OrdinalIgnoreCase)))
                    {
                        startRowIndex = 1;
                    }
                }

                for (int r = startRowIndex; r < rawData.Count; r++)
                {
                    var rowCells = rawData[r];
                    if (rowCells.All(string.IsNullOrWhiteSpace)) continue;

                    int actualSheetRowNum = r + 1; // Số thứ tự dòng thực tế trên Google Sheet (1-indexed)
                    string fullLine = string.Join(" | ", rowCells.Where(c => !string.IsNullOrWhiteSpace(c)));
                    string email = EmailExtractor.ExtractEmail(fullLine);

                    // CHỈ NẠP DÒNG NÀO CÓ EMAIL HỢP LỆ, DÒNG KHÔNG CÓ EMAIL SẼ BỎ QUA HOÀN TOÀN
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        continue;
                    }

                    int targetStatusCol = defaultStatusCol;
                    bool alreadyChecked = false;
                    for (int c = 0; c < rowCells.Count; c++)
                    {
                        string val = rowCells[c].Trim();
                        if (EmailExtractor.IsAlreadyCheckedStatus(val))
                        {
                            alreadyChecked = true;
                            targetStatusCol = c + 1;
                            break;
                        }
                    }

                    if (skipChecked && alreadyChecked)
                    {
                        continue;
                    }

                    _items.Add(new AccountItem
                    {
                        Id = id++,
                        RowIndex = actualSheetRowNum,
                        RawData = fullLine,
                        ExtractedEmail = email,
                        Status = CheckStatus.Pending,
                        Note = "",
                        TargetStatusColumnIndex = targetStatusCol
                    });
                }

                UpdateStatsDisplay();
                lblStatusMsg.Text = $"✅ Đã nạp {_items.Count} dòng từ Google Sheet.";
                System.Windows.MessageBox.Show($"Đã nạp {_items.Count} dòng sẵn sàng kiểm tra!", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                lblStatusMsg.Text = "❌ Lỗi nạp dữ liệu Sheet.";
                System.Windows.MessageBox.Show($"Lỗi nạp dữ liệu: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                btnLoadSheetData.IsEnabled = true;
            }
        }

        #endregion

        #region File TXT & Excel

        private void BtnBrowseTxt_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Chọn file TXT chứa danh sách tài khoản"
            };

            if (ofd.ShowDialog() == true)
            {
                txtTxtPath.Text = ofd.FileName;
            }
        }

        private async void BtnLoadTxtData_Click(object sender, RoutedEventArgs e)
        {
            string path = txtTxtPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Windows.MessageBox.Show("Vui lòng chọn file TXT hợp lệ!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            btnLoadTxtData.IsEnabled = false;
            lblStatusMsg.Text = "⏳ Đang đọc file TXT...";

            try
            {
                var items = await _fileService.ReadTxtFileAsync(path);
                _items.Clear();
                foreach (var it in items)
                {
                    _items.Add(it);
                }

                UpdateStatsDisplay();
                lblStatusMsg.Text = $"✅ Đã nạp {_items.Count} dòng từ file TXT.";
                System.Windows.MessageBox.Show($"Đã nạp thành công {_items.Count} dòng từ file TXT!", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi đọc file TXT: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                btnLoadTxtData.IsEnabled = true;
            }
        }

        private void BtnBrowseExcel_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                Title = "Chọn file Excel chứa danh sách tài khoản"
            };

            if (ofd.ShowDialog() == true)
            {
                txtExcelPath.Text = ofd.FileName;
            }
        }

        private void BtnLoadExcelData_Click(object sender, RoutedEventArgs e)
        {
            string path = txtExcelPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Windows.MessageBox.Show("Vui lòng chọn file Excel hợp lệ!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            btnLoadExcelData.IsEnabled = false;
            lblStatusMsg.Text = "⏳ Đang đọc file Excel...";

            try
            {
                bool skipChecked = chkSkipCheckedExcel.IsChecked == true;
                var items = _fileService.ReadExcelFile(path, skipChecked);
                _items.Clear();
                foreach (var it in items)
                {
                    _items.Add(it);
                }

                UpdateStatsDisplay();
                lblStatusMsg.Text = $"✅ Đã nạp {_items.Count} dòng từ file Excel.";
                System.Windows.MessageBox.Show($"Đã nạp thành công {_items.Count} dòng từ file Excel!", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Lỗi đọc file Excel: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                btnLoadExcelData.IsEnabled = true;
            }
        }

        #endregion

        #region Actions Start / Pause / Stop / Export / Captcha

        private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            var win = new BrowserSessionWindow
            {
                Owner = this
            };
            win.ShowDialog();
            lblStatusMsg.Text = "✅ Đã lưu phiên duyệt web & Captcha. Bạn có thể bấm [BẮT ĐẦU] để quét!";
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                System.Windows.MessageBox.Show("Vui lòng nạp dữ liệu trước khi bắt đầu!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (_taskManager.IsPaused)
            {
                _taskManager.Resume();
                btnPause.Content = "Tạm dừng";
                btnStart.IsEnabled = false;
                btnPause.IsEnabled = true;
                btnStop.IsEnabled = true;
                return;
            }

            int threads = cboThreads.SelectedIndex + 1;
            int delay = int.TryParse(txtDelay.Text.Trim(), out var d) ? d : 1500;
            int timeout = int.TryParse(txtTimeout.Text.Trim(), out var t) ? t : 30;

            if (threads > 2)
            {
                var warn = System.Windows.MessageBox.Show(
                    $"⚠️ CẢNH BÁO ĐA LUỒNG ({threads} luồng)\n\n" +
                    "Walmart sử dụng PerimeterX chống bot rất nhạy cảm.\n" +
                    "Khi chạy từ 3 luồng trở lên trên cùng 1 IP:\n" +
                    "  • Walmart có thể bắt giải Captcha sau vài email\n" +
                    "  • Các luồng sẽ báo lỗi 'Captcha / Blocked'\n\n" +
                    "Khuyến nghị: Chạy 1 - 2 luồng để đạt độ ổn định cao nhất.\n\n" +
                    "Bạn có muốn tiếp tục chạy với " + threads + " luồng không?",
                    "Cảnh báo đa luồng",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (warn == System.Windows.MessageBoxResult.No)
                {
                    return;
                }
            }

            string? scriptUrl = null;
            string? sheetName = null;
            bool autoSave = false;

            if (tabSources.SelectedIndex == 0)
            {
                scriptUrl = txtScriptUrl.Text.Trim();
                sheetName = cboSheets.SelectedItem?.ToString();
                autoSave = chkAutoSaveSheet.IsChecked == true;
            }

            btnStart.IsEnabled = false;
            btnPause.IsEnabled = true;
            btnStop.IsEnabled = true;
            btnPause.Content = "Tạm dừng";

            lblStatusMsg.Text = $"🚀 Đang chạy kiểm tra với {threads} luồng...";

            await _taskManager.StartProcessingAsync(
                HiddenHostContainer,
                _items.ToList(),
                threads,
                delay,
                timeout,
                scriptUrl,
                sheetName,
                autoSave
            );
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_taskManager.IsPaused)
            {
                _taskManager.Resume();
                btnPause.Content = "Tạm dừng";
                lblStatusMsg.Text = "▶️ Đang tiếp tục...";
            }
            else
            {
                _taskManager.Pause();
                btnPause.Content = "Tiếp tục";
                lblStatusMsg.Text = "⏸️ Đã tạm dừng.";
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _taskManager.Stop();
            btnStart.IsEnabled = true;
            btnPause.IsEnabled = false;
            btnStop.IsEnabled = false;
            btnPause.Content = "Tạm dừng";
            lblStatusMsg.Text = "⏹️ Đã dừng kiểm tra.";
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0)
            {
                System.Windows.MessageBox.Show("Không có dữ liệu để xuất!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|Text File (*.txt)|*.txt",
                Title = "Lưu kết quả kiểm tra",
                FileName = $"Walmart_Check_Results_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    if (sfd.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        string sourcePath = txtExcelPath.Text.Trim();
                        _fileService.SaveResultsToExcel(sourcePath, sfd.FileName, _items);
                    }
                    else
                    {
                        await _fileService.ExportToTxtAsync(sfd.FileName, _items);
                    }

                    System.Windows.MessageBox.Show("Xuất file kết quả thành công!", "Thành công", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Lỗi xuất file: {ex.Message}", "Lỗi", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (_taskManager.IsRunning)
            {
                System.Windows.MessageBox.Show("Vui lòng dừng tiến trình trước khi xóa bảng!", "Cảnh báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            _items.Clear();
            UpdateStatsDisplay();
            lblStatusMsg.Text = "🧹 Đã xóa danh sách.";
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            txtLogs.Clear();
        }

        #endregion

        #region Progress & Task Events

        private void TaskManager_OnItemUpdated(AccountItem item)
        {
            // ObservableCollection và INotifyPropertyChanged tự động cập nhật UI DataGrid
        }

        private void TaskManager_OnProgressChanged(int total, int @checked, int registered, int notRegistered, int suspended, int errors)
        {
            Dispatcher.InvokeAsync(() =>
            {
                txtStatTotal.Text = total.ToString("N0");
                txtStatChecked.Text = @checked.ToString("N0");
                txtStatRegistered.Text = registered.ToString("N0");
                txtStatNotRegistered.Text = notRegistered.ToString("N0");
                txtStatSuspended.Text = suspended.ToString("N0");
                txtStatError.Text = errors.ToString("N0");

                lblStatusMsg.Text = $"⚡ Tiến độ: {@checked}/{total} ({(@checked * 100.0 / Math.Max(total, 1)):F1}%) | Đã ĐK: {registered} | Chưa ĐK: {notRegistered} | Đình chỉ: {suspended} | Lỗi: {errors}";
            });
        }

        private void TaskManager_OnCompleted()
        {
            Dispatcher.InvokeAsync(() =>
            {
                btnStart.IsEnabled = true;
                btnPause.IsEnabled = false;
                btnStop.IsEnabled = false;
                btnPause.Content = "Tạm dừng";
                lblStatusMsg.Text = "✅ Đã hoàn thành toàn bộ danh sách kiểm tra!";
                System.Windows.MessageBox.Show("Quá trình kiểm tra đã hoàn thành!", "Thông báo", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            });
        }

        private void TaskManager_OnLog(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                txtLogs.AppendText(logLine);
                txtLogs.ScrollToEnd();
            });
        }

        private void UpdateStatsDisplay()
        {
            int total = _items.Count;
            int @checked = _items.Count(i => i.Status != CheckStatus.Pending && i.Status != CheckStatus.Checking);
            int registered = _items.Count(i => i.Status == CheckStatus.Registered);
            int notRegistered = _items.Count(i => i.Status == CheckStatus.NotRegistered);
            int suspended = _items.Count(i => i.Status == CheckStatus.Suspended);
            int error = _items.Count(i => i.Status == CheckStatus.Error);

            txtStatTotal.Text = total.ToString("N0");
            txtStatChecked.Text = @checked.ToString("N0");
            txtStatRegistered.Text = registered.ToString("N0");
            txtStatNotRegistered.Text = notRegistered.ToString("N0");
            txtStatSuspended.Text = suspended.ToString("N0");
            txtStatError.Text = error.ToString("N0");

            UpdateFilterDisplayCount();
        }

        #endregion

        #region Search & Filter

        private bool FilterItem(object obj)
        {
            if (obj is not AccountItem item) return false;

            // Lọc theo từ khóa tìm kiếm
            string filterText = txtSearch?.Text?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrWhiteSpace(filterText))
            {
                bool matches = (item.ExtractedEmail?.ToLowerInvariant().Contains(filterText) == true) ||
                                (item.RawData?.ToLowerInvariant().Contains(filterText) == true) ||
                                (item.Note?.ToLowerInvariant().Contains(filterText) == true);
                if (!matches) return false;
            }

            // Lọc theo trạng thái
            int statusIndex = cboFilterStatus?.SelectedIndex ?? 0;
            return statusIndex switch
            {
                1 => item.Status == CheckStatus.Pending,
                2 => item.Status == CheckStatus.Checking,
                3 => item.Status == CheckStatus.Registered,
                4 => item.Status == CheckStatus.NotRegistered,
                5 => item.Status == CheckStatus.Suspended,
                6 => item.Status == CheckStatus.Error,
                _ => true // Tất cả
            };
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _itemsView?.Refresh();
            UpdateFilterDisplayCount();
        }

        private void CboFilterStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _itemsView?.Refresh();
            UpdateFilterDisplayCount();
        }

        private void UpdateFilterDisplayCount()
        {
            if (_itemsView == null) return;
            int count = _itemsView.Cast<object>().Count();
            txtDisplayCount.Text = $"Hiển thị: {count:N0} / {_items.Count:N0} dòng";
        }

        #endregion
    }
}
