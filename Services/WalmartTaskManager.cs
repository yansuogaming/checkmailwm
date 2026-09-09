using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using CheckMailWM2.Models;

namespace CheckMailWM2.Services
{
    public class WalmartTaskManager
    {
        private readonly GoogleAppsScriptService _gasService;
        private CancellationTokenSource? _cts;
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private bool _isRunning = false;
        private bool _isPaused = false;

        // Cơ chế đồng bộ hóa giải Captcha tập trung cho tất cả các luồng
        private readonly object _captchaLock = new();
        private TaskCompletionSource<bool>? _captchaSolvingTcs;

        public event Action<AccountItem>? OnItemUpdated;
        public event Action<int, int, int, int, int, int>? OnProgressChanged; // total, checked, registered, notRegistered, suspended, errors
        public event Action? OnCompleted;
        public event Action<string>? OnLog;

        // Sự kiện yêu cầu UI hiển thị cửa sổ giải Captcha thủ công
        public event Func<Task<bool>>? OnRequestManualCaptcha;

        public bool IsRunning => _isRunning;
        public bool IsPaused => _isPaused;

        public WalmartTaskManager()
        {
            _gasService = new GoogleAppsScriptService();
        }

        public void Pause()
        {
            if (_isRunning && !_isPaused)
            {
                _isPaused = true;
                _pauseEvent.Reset();
                OnLog?.Invoke("⏸️ Đã tạm dừng xử lý.");
            }
        }

        public void Resume()
        {
            if (_isRunning && _isPaused)
            {
                _isPaused = false;
                _pauseEvent.Set();
                OnLog?.Invoke("▶️ Tiếp tục xử lý.");
            }
        }

        public void Stop()
        {
            if (_isRunning)
            {
                _cts?.Cancel();
                _pauseEvent.Set();
                lock (_captchaLock)
                {
                    _captchaSolvingTcs?.TrySetResult(false);
                }
                _isRunning = false;
                _isPaused = false;
                OnLog?.Invoke("⏹️ Đã gửi yêu cầu dừng xử lý.");
            }
        }

        /// <summary>
        /// Xử lý mở cửa sổ giải Captcha tập trung: chỉ cho 1 luồng mở UI, các luồng khác chờ đợi
        /// </summary>
        private async Task<bool> HandleCaptchaResolutionAsync(int threadId)
        {
            TaskCompletionSource<bool> tcs;
            bool isInitiator = false;

            lock (_captchaLock)
            {
                if (_captchaSolvingTcs == null || _captchaSolvingTcs.Task.IsCompleted)
                {
                    _captchaSolvingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    isInitiator = true;
                }
                tcs = _captchaSolvingTcs;
            }

            if (isInitiator)
            {
                OnLog?.Invoke($"[Luồng {threadId}] ⚠️ Phát hiện Captcha trên trang đăng nhập! Tạm dừng các luồng khác và tự động mở cửa sổ giải Captcha...");
                try
                {
                    bool solved = false;
                    if (OnRequestManualCaptcha != null)
                    {
                        solved = await OnRequestManualCaptcha.Invoke();
                    }
                    tcs.TrySetResult(solved);
                    return solved;
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Luồng {threadId}] ❌ Lỗi khi mở cửa sổ giải Captcha: {ex.Message}");
                    tcs.TrySetResult(false);
                    return false;
                }
                finally
                {
                    // Đợi một khoảng ngắn rồi giải phóng để lần gặp captcha tiếp theo có thể kích hoạt mới
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        lock (_captchaLock)
                        {
                            if (_captchaSolvingTcs == tcs)
                            {
                                _captchaSolvingTcs = null;
                            }
                        }
                    });
                }
            }
            else
            {
                OnLog?.Invoke($"[Luồng {threadId}] Đang tạm dừng chờ bạn giải Captcha trên cửa sổ trình duyệt...");
                try
                {
                    bool result = await tcs.Task;
                    // Giãn cách một chút giữa các luồng sau khi vượt Captcha để không gửi đồng loạt
                    await Task.Delay(1500);
                    return result;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Bắt đầu xử lý danh sách tài khoản bằng đa luồng
        /// </summary>
        public async Task StartProcessingAsync(
            Panel hostParent,
            List<AccountItem> items,
            int threadCount,
            int delayMs,
            int timeoutSeconds,
            string? googleScriptUrl = null,
            string? googleSheetName = null,
            bool autoSaveToGoogleSheet = true)
        {
            if (_isRunning) return;

            _isRunning = true;
            _isPaused = false;
            _pauseEvent.Set();
            _cts = new CancellationTokenSource();

            var pendingItems = items.Where(i => i.Status == CheckStatus.Pending).ToList();
            int total = pendingItems.Count;

            if (total == 0)
            {
                OnLog?.Invoke("ℹ️ Không có email nào ở trạng thái 'Chờ duyệt' để kiểm tra.");
                _isRunning = false;
                OnCompleted?.Invoke();
                return;
            }

            int checkedCount = 0;
            int registeredCount = items.Count(i => i.Status == CheckStatus.Registered);
            int notRegisteredCount = items.Count(i => i.Status == CheckStatus.NotRegistered);
            int suspendedCount = items.Count(i => i.Status == CheckStatus.Suspended);
            int errorCount = items.Count(i => i.Status == CheckStatus.Error);

            var itemQueue = new ConcurrentQueue<AccountItem>(pendingItems);
            var activeEngines = new List<WalmartCheckerEngine>();

            OnLog?.Invoke($"🚀 Bắt đầu kiểm tra {total} email với {threadCount} luồng song song...");

            try
            {
                for (int t = 0; t < threadCount; t++)
                {
                    int threadId = t + 1;
                    var engine = new WalmartCheckerEngine(threadId, timeoutSeconds);
                    engine.OnLog += (msg) => OnLog?.Invoke(msg);
                    activeEngines.Add(engine);
                }

                var workerTasks = activeEngines.Select(async (engine, index) =>
                {
                    int threadId = index + 1;
                    try
                    {
                        if (index > 0)
                        {
                            await Task.Delay(index * 2500, _cts.Token);
                        }

                        await engine.InitializeAsync(hostParent);

                        while (!_cts.Token.IsCancellationRequested)
                        {
                            // 1. Chờ nếu tiến trình đang bấm Tạm dừng
                            while (_isPaused && !_cts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(300, _cts.Token);
                            }

                            // 2. Chờ nếu đang có phiên giải Captcha thủ công từ luồng khác
                            Task<bool>? pendingCaptchaTask = null;
                            lock (_captchaLock)
                            {
                                if (_captchaSolvingTcs != null && !_captchaSolvingTcs.Task.IsCompleted)
                                {
                                    pendingCaptchaTask = _captchaSolvingTcs.Task;
                                }
                            }

                            if (pendingCaptchaTask != null)
                            {
                                await pendingCaptchaTask;
                                // Giãn cách luồng sau khi vừa hoàn thành giải Captcha
                                await Task.Delay(index * 1200 + 1000, _cts.Token);
                            }

                            if (_cts.Token.IsCancellationRequested) break;

                            // 3. Lấy email tiếp theo trong hàng đợi
                            if (!itemQueue.TryDequeue(out var currentItem))
                            {
                                break; // Đã hết email cần kiểm tra
                            }

                            currentItem.Status = CheckStatus.Checking;
                            OnItemUpdated?.Invoke(currentItem);

                            var result = await engine.CheckEmailAsync(currentItem.ExtractedEmail, hostParent, _cts.Token);

                            // 4. Nếu gặp Captcha trên trang đăng nhập
                            if (result.IsCaptcha)
                            {
                                currentItem.Note = "Chờ giải Captcha...";
                                OnItemUpdated?.Invoke(currentItem);

                                bool solved = await HandleCaptchaResolutionAsync(threadId);

                                if (solved)
                                {
                                    OnLog?.Invoke($"[Luồng {threadId}] ✅ Đã giải xong Captcha! Tiến hành kiểm tra lại: {currentItem.ExtractedEmail}...");
                                    await Task.Delay(1500, _cts.Token);

                                    // Thử kiểm tra lại chính email này với phiên vừa giải
                                    result = await engine.CheckEmailAsync(currentItem.ExtractedEmail, hostParent, _cts.Token);

                                    // Nếu sau khi thử lại mà vẫn tiếp tục dính Captcha
                                    if (result.IsCaptcha)
                                    {
                                        OnLog?.Invoke($"[Luồng {threadId}] ⚠️ Vẫn phát hiện Captcha sau khi giải. Tự động tạm dừng tiến trình để bảo vệ danh sách còn lại.");
                                        Pause();
                                    }
                                }
                                else
                                {
                                    OnLog?.Invoke($"[Luồng {threadId}] ⚠️ Cửa sổ giải Captcha đã đóng mà chưa hoàn tất. Tự động tạm dừng tiến trình để bảo vệ các tài khoản còn lại.");
                                    Pause();
                                }
                            }

                            currentItem.Status = result.Status;
                            currentItem.Note = result.Note;
                            currentItem.CheckedTime = DateTime.Now;

                            Interlocked.Increment(ref checkedCount);
                            if (currentItem.Status == CheckStatus.Registered)
                                Interlocked.Increment(ref registeredCount);
                            else if (currentItem.Status == CheckStatus.NotRegistered)
                                Interlocked.Increment(ref notRegisteredCount);
                            else if (currentItem.Status == CheckStatus.Suspended)
                                Interlocked.Increment(ref suspendedCount);
                            else if (currentItem.Status == CheckStatus.Error)
                                Interlocked.Increment(ref errorCount);

                            OnItemUpdated?.Invoke(currentItem);
                            OnProgressChanged?.Invoke(total, checkedCount, registeredCount, notRegisteredCount, suspendedCount, errorCount);

                            // Nếu là Google Sheet và có bật lưu tự động, cập nhật trạng thái tiếng Anh và mã màu
                            if (autoSaveToGoogleSheet && !string.IsNullOrWhiteSpace(googleScriptUrl) && !string.IsNullOrWhiteSpace(googleSheetName) && currentItem.RowIndex > 0)
                            {
                                int statusCol = currentItem.TargetStatusColumnIndex > 0 ? currentItem.TargetStatusColumnIndex : 2;
                                string englishStatus = currentItem.Status.ToEnglishString();
                                string hexColor = currentItem.Status.ToHexColor();
                                _ = _gasService.UpdateCellStatusAsync(googleScriptUrl, googleSheetName, currentItem.RowIndex, statusCol, englishStatus, hexColor);
                            }

                            if (delayMs > 0 && !_cts.Token.IsCancellationRequested)
                            {
                                await Task.Delay(delayMs, _cts.Token);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"[Luồng {threadId}] Lỗi luồng: {ex.Message}");
                    }
                    finally
                    {
                        engine.Dispose();
                    }
                }).ToList();

                await Task.WhenAll(workerTasks);
                OnLog?.Invoke("✅ Hoàn thành toàn bộ quá trình kiểm tra!");
            }
            catch (OperationCanceledException)
            {
                OnLog?.Invoke("⏹️ Đã dừng quá trình kiểm tra.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"❌ Lỗi tiến trình: {ex.Message}");
            }
            finally
            {
                foreach (var engine in activeEngines)
                {
                    engine.Dispose();
                }

                _isRunning = false;
                _isPaused = false;
                OnCompleted?.Invoke();
            }
        }
    }
}
