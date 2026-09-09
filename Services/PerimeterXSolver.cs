using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace CheckMailWM2.Services
{
    /// <summary>
    /// Bộ giải tự động Captcha PerimeterX ("Press & Hold" / "Robot or human")
    /// Mô phỏng hoàn toàn hành vi chuột của con người (Human-like Mouse Trajectory):
    /// - Quỹ đạo cong Cubic Bezier tự nhiên, không đi thẳng hay dịch chuyển tức thời
    /// - Tăng tốc và giảm tốc phi tuyến tính (Easing / Fitts's Law)
    /// - Rung lắc vi mô (Micro-tremors 10-12Hz)
    /// - Dừng ngập ngừng quan sát (Hesitation / Reaction time) trước khi nhấn
    /// - Duy trì độ trôi tay tự nhiên khi nhấn giữ trong 15s (hoặc nhả ngay khi qua)
    /// - Con trỏ ảo hiển thị trực quan (Visual Cursor) trên giao diện trình duyệt
    /// </summary>
    public static class PerimeterXSolver
    {
        private static double _lastMouseX = 200;
        private static double _lastMouseY = 150;

        /// <summary>
        /// Kiểm tra nhanh xem trang hiện tại có xuất hiện thử thách Captcha PerimeterX hay không
        /// </summary>
        public static async Task<bool> IsCaptchaPresentAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string checkScript = @"
                (function() {
                    const body = document.body ? (document.body.innerText || document.body.textContent || '') : '';
                    const url = window.location.href || '';
                    const hasCaptcha = body.includes('Robot or human') || 
                                       body.includes('Press & Hold') || 
                                       body.includes('Press and Hold') || 
                                       body.includes('Verify your identity') ||
                                       body.includes('Please verify you are a human') ||
                                       body.includes('PerimeterX') ||
                                       body.includes('Access Denied') ||
                                       url.includes('/blocked') ||
                                       url.includes('/challenge') ||
                                       url.includes('/captcha') ||
                                       document.querySelector(""#px-captcha, [id*='px-captcha'], [id*='captcha' i], iframe[src*='captcha' i], iframe[src*='perimeter' i], iframe[title*='challenge' i], div.px-modal"") !== null;
                    return hasCaptcha ? 'YES' : 'NO';
                })();
            ";

            string res = await ExecuteScriptAsync(webView, checkScript);
            res = res?.Trim('"', ' ') ?? "";
            return res == "YES";
        }

        /// <summary>
        /// Tự động định vị nút "Press & Hold", di chuột uốn lượn như người thật và nhấn giữ chuột liên tục trong tối đa maxHoldSeconds (mặc định 15s).
        /// Khi nhận diện Captcha đã vượt qua thành công, hệ thống tự động nhả nút và kết thúc sớm.
        /// </summary>
        public static async Task<bool> AutoSolvePressAndHoldAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 webView,
            Action<string>? onLog = null,
            CancellationToken ct = default,
            int maxHoldSeconds = 15)
        {
            if (webView == null) return false;

            try
            {
                // 1. Kiểm tra ban đầu xem có Captcha không
                bool hasCaptcha = await IsCaptchaPresentAsync(webView);
                if (!hasCaptcha)
                {
                    onLog?.Invoke("ℹ️ Không phát hiện Captcha PerimeterX trên trang.");
                    return true;
                }

                onLog?.Invoke($"🤖 [PerimeterX] Bắt đầu quy trình di chuột người thật & nhấn giữ 'Press & Hold' (tối đa {maxHoldSeconds}s)...");

                // Đợi 600ms để DOM và canvas/iframe Captcha ổn định tọa độ
                await Task.Delay(600, ct);

                // Khởi tạo con trỏ ảo trực quan (Visual Cursor) để người dùng có thể thấy chuột di chuyển
                await InjectVisualCursorAsync(webView);

                // 2. Tìm tọa độ chính xác của nút hoặc vùng Captcha
                string locateScript = @"
                    (function() {
                        function getCenter(el) {
                            if (!el) return null;
                            const r = el.getBoundingClientRect();
                            if (r.width > 15 && r.height > 15 && r.top < window.innerHeight && r.bottom > 0) {
                                return {
                                    x: Math.round(r.left + r.width / 2),
                                    y: Math.round(r.top + r.height / 2),
                                    w: Math.round(r.width),
                                    h: Math.round(r.height)
                                };
                            }
                            return null;
                        }

                        // Ưu tiên 1: Nút hoặc phần tử có text/aria-label 'Press & Hold'
                        const all = Array.from(document.querySelectorAll('*'));
                        for (const el of all) {
                            const aria = (el.getAttribute('aria-label') || '').toLowerCase();
                            const txt = (el.innerText || el.textContent || '').trim().toLowerCase();
                            if (aria.includes('press & hold') || aria.includes('press and hold') ||
                                (txt.includes('press & hold') && txt.length < 50) ||
                                (txt.includes('press and hold') && txt.length < 50)) {
                                const c = getCenter(el);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: 'text_match' });
                            }
                        }

                        // Ưu tiên 2: Container #px-captcha và các biến thể
                        const pxSelectors = [
                            '#px-captcha',
                            'div[id*=""px-captcha""]',
                            '#px-captcha-wrapper',
                            '#px-captcha-box',
                            'div.px-captcha-container',
                            'div[role=""button""][aria-label*=""Hold"" i]',
                            'div.px-modal'
                        ];
                        for (const sel of pxSelectors) {
                            const el = document.querySelector(sel);
                            if (el) {
                                const btn = el.querySelector('button, [role=""button""], div') || el;
                                const c = getCenter(btn) || getCenter(el);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: sel });
                            }
                        }

                        // Ưu tiên 3: Iframe chứa Captcha
                        const iframes = document.querySelectorAll('iframe');
                        for (const ifr of iframes) {
                            const src = (ifr.src || '') + ' ' + (ifr.title || '');
                            if (src.includes('captcha') || src.includes('perimeter') || src.includes('challenge')) {
                                const c = getCenter(ifr);
                                if (c) return JSON.stringify({ found: true, x: c.x, y: c.y, w: c.w, h: c.h, source: 'iframe' });
                            }
                        }

                        // Ưu tiên 4: Trung tâm màn hình (Vị trí mặc định của box PerimeterX trên Walmart)
                        const bodyText = document.body ? (document.body.innerText || '') : '';
                        if (bodyText.includes('Robot or human') || bodyText.includes('PerimeterX') || bodyText.includes('Press & Hold')) {
                            return JSON.stringify({
                                found: true,
                                x: Math.round(window.innerWidth / 2),
                                y: Math.round(window.innerHeight / 2),
                                w: 320,
                                h: 90,
                                source: 'viewport_center'
                            });
                        }

                        return JSON.stringify({ found: false });
                    })();
                ";

                string locJson = await ExecuteScriptAsync(webView, locateScript);
                locJson = locJson?.StartsWith("\"") == true ? JsonSerializer.Deserialize<string>(locJson) ?? "{}" : locJson ?? "{}";

                double centerX = 640;
                double centerY = 400;
                double elemW = 200;
                double elemH = 60;

                try
                {
                    using var doc = JsonDocument.Parse(locJson);
                    if (doc.RootElement.TryGetProperty("found", out var pFound) && pFound.GetBoolean())
                    {
                        centerX = doc.RootElement.GetProperty("x").GetDouble();
                        centerY = doc.RootElement.GetProperty("y").GetDouble();
                        if (doc.RootElement.TryGetProperty("w", out var pW)) elemW = pW.GetDouble();
                        if (doc.RootElement.TryGetProperty("h", out var pH)) elemH = pH.GetDouble();
                        string src = doc.RootElement.TryGetProperty("source", out var pSrc) ? pSrc.GetString() ?? "" : "";
                        onLog?.Invoke($"🎯 [PerimeterX] Đã định vị nút Captcha tại ({centerX:0}, {centerY:0}) [Nguồn: {src}].");
                    }
                    else
                    {
                        onLog?.Invoke($"⚠️ [PerimeterX] Không tìm thấy phần tử trực tiếp, sử dụng tọa độ trung tâm ({centerX}, {centerY}).");
                    }
                }
                catch
                {
                    onLog?.Invoke($"⚠️ [PerimeterX] Lỗi đọc tọa độ, sử dụng vị trí mặc định ({centerX}, {centerY}).");
                }

                // Điểm bấm ngẫu nhiên trong vùng nút (tránh bấm ngay tâm điểm của bot)
                double targetX = centerX + (Random.Shared.NextDouble() * 0.5 - 0.25) * elemW;
                double targetY = centerY + (Random.Shared.NextDouble() * 0.4 - 0.2) * elemH;

                // Chọn điểm xuất phát chuột tự nhiên nếu chưa có
                double startX = _lastMouseX > 0 && _lastMouseX < 1200 ? _lastMouseX : Random.Shared.Next(120, 350);
                double startY = _lastMouseY > 0 && _lastMouseY < 700 ? _lastMouseY : Random.Shared.Next(80, 220);

                // 3. DI CHUỘT THEO QUỸ ĐẠO CONG CUBIC BEZIER (Mô phỏng 100% người thật)
                onLog?.Invoke($"🖱️ [PerimeterX] Đang di chuột uốn lượn như người thật từ ({startX:0}, {startY:0}) đến nút ({targetX:0}, {targetY:0})...");
                await MoveMouseHumanLikeAsync(webView, startX, startY, targetX, targetY, ct);

                // Cập nhật vị trí lưu trữ
                _lastMouseX = targetX;
                _lastMouseY = targetY;

                // 4. NGẬP NGỪNG QUAN SÁT (Reaction Time: 120ms - 260ms) như người thật trước khi nhấn
                int reactionTime = Random.Shared.Next(140, 260);
                await Task.Delay(reactionTime, ct);

                // 5. Gửi sự kiện nhấn chuột xuống (mousePressed) qua CDP
                string pressEvent = FormattableString.Invariant($"{{\"type\":\"mousePressed\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"button\":\"left\",\"buttons\":1,\"clickCount\":1,\"modifiers\":0}}");
                await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", pressEvent);

                // Cập nhật con trỏ ảo sang trạng thái kích hoạt nhấn giữ
                await UpdateVisualCursorAsync(webView, targetX, targetY, isDown: true);

                // Kích hoạt thêm sự kiện DOM dự phòng
                await ExecuteScriptAsync(webView, @"
                    (function() {
                        const el = document.querySelector('#px-captcha') || document.querySelector('[aria-label*=""Hold"" i]');
                        if (el) {
                            const opt = { bubbles: true, cancelable: true, view: window, buttons: 1 };
                            try { el.dispatchEvent(new PointerEvent('pointerdown', opt)); } catch(e){}
                            try { el.dispatchEvent(new MouseEvent('mousedown', opt)); } catch(e){}
                        }
                    })();
                ");

                onLog?.Invoke($"⏳ [PerimeterX] Đang nhấn giữ nút... Giữ tối đa {maxHoldSeconds}s (hoặc thả ngay khi qua).");

                var sw = Stopwatch.StartNew();
                bool passed = false;

                // 6. Vòng lặp nhấn giữ với độ rung tay sinh học (Muscle Tremor)
                try
                {
                    double currentHoldX = targetX;
                    double currentHoldY = targetY;

                    while (sw.ElapsedMilliseconds < maxHoldSeconds * 1000)
                    {
                        if (ct.IsCancellationRequested) break;

                        // Rung lắc tự nhiên khi ngón tay đè lên chuột (jitter ±1.2px)
                        currentHoldX = targetX + (Random.Shared.NextDouble() * 2.4 - 1.2);
                        currentHoldY = targetY + (Random.Shared.NextDouble() * 2.4 - 1.2);

                        string jitterMove = FormattableString.Invariant($"{{\"type\":\"mouseMoved\",\"x\":{currentHoldX:0.00},\"y\":{currentHoldY:0.00},\"buttons\":1}}");
                        await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", jitterMove);
                        await UpdateVisualCursorAsync(webView, currentHoldX, currentHoldY, isDown: true);

                        // Kiểm tra xem Captcha đã giải xong chưa
                        string statusCheckScript = @"
                            (function() {
                                const body = document.body ? (document.body.innerText || document.body.textContent || '') : '';
                                const url = window.location.href || '';

                                // 1. Form nhập email đã xuất hiện -> ĐÃ VƯỢT THÀNH CÔNG
                                const emailInput = document.querySelector(""input[autocomplete='email'], input[name*='email' i], input[type='email'], input[data-automation-id='email-input']"");
                                if (emailInput) return 'PASSED';

                                // 2. Kiểm tra các dấu hiệu của Captcha
                                const hasCaptcha = body.includes('Robot or human') || 
                                                   body.includes('Press & Hold') || 
                                                   body.includes('Press and Hold') || 
                                                   document.querySelector('#px-captcha') !== null;

                                if (!hasCaptcha) {
                                    return 'PASSED';
                                }

                                return 'HOLDING';
                            })();
                        ";

                        string statusRes = await ExecuteScriptAsync(webView, statusCheckScript);
                        statusRes = statusRes?.Trim('"', ' ') ?? "";

                        if (statusRes == "PASSED")
                        {
                            passed = true;
                            double elapsedSec = sw.ElapsedMilliseconds / 1000.0;
                            onLog?.Invoke($"🎉 [PerimeterX] Nhận diện Captcha đã mở sau {elapsedSec:0.1}s! Đang nhả nút...");
                            break;
                        }

                        // Đợi 220 - 280ms ngẫu nhiên cho chu kỳ tiếp theo
                        await Task.Delay(Random.Shared.Next(200, 260), ct);
                    }
                }
                finally
                {
                    // 7. Nhả chuột (mouseReleased) ngay lập tức
                    string releaseEvent = FormattableString.Invariant($"{{\"type\":\"mouseReleased\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"button\":\"left\",\"buttons\":0,\"clickCount\":1}}");
                    await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", releaseEvent);
                    await UpdateVisualCursorAsync(webView, targetX, targetY, isDown: false);

                    await ExecuteScriptAsync(webView, @"
                        (function() {
                            const el = document.querySelector('#px-captcha') || document.querySelector('[aria-label*=""Hold"" i]');
                            if (el) {
                                const opt = { bubbles: true, cancelable: true, view: window, buttons: 0 };
                                try { el.dispatchEvent(new PointerEvent('pointerup', opt)); } catch(e){}
                                try { el.dispatchEvent(new MouseEvent('mouseup', opt)); } catch(e){}
                            }
                        })();
                    ");
                }

                // 8. Di chuột nhẹ rời khỏi nút sau khi bấm (Hành vi tự nhiên sau click của người)
                await Task.Delay(Random.Shared.Next(120, 250), ct);
                double driftAwayX = targetX + Random.Shared.Next(25, 60);
                double driftAwayY = targetY + Random.Shared.Next(15, 45);
                await MoveMouseHumanLikeAsync(webView, targetX, targetY, driftAwayX, driftAwayY, ct, stepCount: 15);
                _lastMouseX = driftAwayX;
                _lastMouseY = driftAwayY;

                // Tắt con trỏ ảo
                await RemoveVisualCursorAsync(webView);

                // 9. Chờ và kiểm tra xác thực từ máy chủ Walmart
                onLog?.Invoke("🔄 [PerimeterX] Đã nhả nút, đang chờ máy chủ Walmart xác thực phiên...");
                await Task.Delay(1200, ct);

                for (int i = 0; i < 4; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    bool stillCaptcha = await IsCaptchaPresentAsync(webView);
                    if (!stillCaptcha)
                    {
                        passed = true;
                        onLog?.Invoke("✅ [PerimeterX] XÁC NHẬN: Đã vượt qua thử thách Captcha thành công!");
                        return true;
                    }
                    await Task.Delay(600, ct);
                }

                if (passed)
                {
                    onLog?.Invoke("✅ [PerimeterX] Captcha đã giải thành công!");
                    return true;
                }
                else
                {
                    onLog?.Invoke("⚠️ [PerimeterX] Đã giữ đủ thời gian nhưng Captcha chưa vượt qua.");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                onLog?.Invoke("🛑 [PerimeterX] Quá trình giải Captcha bị hủy.");
                return false;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"❌ [PerimeterX] Lỗi trong quá trình giải Captcha: {ex.Message}");
                return false;
            }
            finally
            {
                await RemoveVisualCursorAsync(webView);
            }
        }

        /// <summary>
        /// Di chuyển con trỏ chuột theo đường cong Bezier Cubic kết hợp Easing phi tuyến tính mô phỏng 100% người thật
        /// </summary>
        public static async Task MoveMouseHumanLikeAsync(
            Microsoft.Web.WebView2.Wpf.WebView2 webView,
            double startX,
            double startY,
            double targetX,
            double targetY,
            CancellationToken ct = default,
            int? stepCount = null)
        {
            double dx = targetX - startX;
            double dy = targetY - startY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            // Số bước di chuyển tỉ lệ với khoảng cách (30 - 60 bước)
            int steps = stepCount ?? Math.Clamp((int)(distance / 12), 28, 55);

            // Tạo điểm điều khiển Cubic Bezier (P1, P2) uốn cong tự nhiên
            // Vector pháp tuyến vuông góc với hướng di chuyển
            double normalX = -dy / (distance > 0 ? distance : 1);
            double normalY = dx / (distance > 0 ? distance : 1);

            // Hướng cong ngẫu nhiên sang trái hoặc phải
            double curveSign = Random.Shared.Next(0, 2) == 0 ? 1.0 : -1.0;
            double maxArc = Math.Clamp(distance * 0.25, 20, 80);

            double p1X = startX + dx * 0.25 + normalX * maxArc * curveSign * (0.6 + Random.Shared.NextDouble() * 0.4);
            double p1Y = startY + dy * 0.25 + normalY * maxArc * curveSign * (0.6 + Random.Shared.NextDouble() * 0.4);

            double p2X = startX + dx * 0.75 + normalX * (maxArc * 0.6) * curveSign * (0.4 + Random.Shared.NextDouble() * 0.6);
            double p2Y = startY + dy * 0.75 + normalY * (maxArc * 0.6) * curveSign * (0.4 + Random.Shared.NextDouble() * 0.6);

            for (int i = 1; i <= steps; i++)
            {
                if (ct.IsCancellationRequested) break;

                double u = (double)i / steps;

                // Easing Cubic Ease-In-Out: Bắt đầu chậm, lướt nhanh ở giữa, giảm tốc êm ái khi đến gần nút
                double t = u < 0.5 ? 4.0 * u * u * u : 1.0 - Math.Pow(-2.0 * u + 2.0, 3) / 2.0;

                // Công thức Cubic Bezier: B(t) = (1-t)^3 * P0 + 3(1-t)^2*t * P1 + 3(1-t)*t^2 * P2 + t^3 * P3
                double oneMinusT = 1.0 - t;
                double bx = oneMinusT * oneMinusT * oneMinusT * startX
                          + 3.0 * oneMinusT * oneMinusT * t * p1X
                          + 3.0 * oneMinusT * t * t * p2X
                          + t * t * t * targetX;

                double by = oneMinusT * oneMinusT * oneMinusT * startY
                          + 3.0 * oneMinusT * oneMinusT * t * p1Y
                          + 3.0 * oneMinusT * t * t * p2Y
                          + t * t * t * targetY;

                // Rung lắc vi mô sinh học (Micro-noise)
                if (i < steps)
                {
                    bx += (Random.Shared.NextDouble() * 1.6 - 0.8);
                    by += (Random.Shared.NextDouble() * 1.6 - 0.8);
                }

                // Gửi sự kiện mouseMoved qua CDP
                string moveEvent = FormattableString.Invariant($"{{\"type\":\"mouseMoved\",\"x\":{bx:0.00},\"y\":{by:0.00},\"modifiers\":0}}");
                await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", moveEvent);

                // Cập nhật vị trí con trỏ ảo trên màn hình
                await UpdateVisualCursorAsync(webView, bx, by, isDown: false);

                // Độ trễ vi mô biến thiên giữa các bước (10ms - 18ms)
                int delayMs = Random.Shared.Next(10, 18);
                await Task.Delay(delayMs, ct);
            }

            // Đảm bảo dừng chính xác ở vị trí mục tiêu
            string finalMove = FormattableString.Invariant($"{{\"type\":\"mouseMoved\",\"x\":{targetX:0.00},\"y\":{targetY:0.00},\"modifiers\":0}}");
            await CallCdpMethodAsync(webView, "Input.dispatchMouseEvent", finalMove);
            await UpdateVisualCursorAsync(webView, targetX, targetY, isDown: false);
        }

        #region Visual Ghost Cursor (Con Trỏ Ảo Trực Quan)

        private static async Task InjectVisualCursorAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string injectCursorScript = @"
                (function() {
                    if (document.getElementById('human-virtual-cursor')) return;

                    const cursor = document.createElement('div');
                    cursor.id = 'human-virtual-cursor';
                    cursor.style.cssText = `
                        position: fixed;
                        top: 0;
                        left: 0;
                        width: 22px;
                        height: 22px;
                        background: radial-gradient(circle, rgba(239, 68, 68, 0.95) 40%, rgba(220, 38, 38, 0.6) 100%);
                        border: 2px solid #ffffff;
                        border-radius: 50%;
                        pointer-events: none;
                        z-index: 2147483647;
                        transform: translate(-50%, -50%);
                        transition: width 0.15s ease, height 0.15s ease, background 0.2s ease, box-shadow 0.2s ease;
                        box-shadow: 0 0 12px rgba(239, 68, 68, 0.8), 0 2px 8px rgba(0, 0, 0, 0.5);
                        display: none;
                    `;

                    // Điểm tâm định vị
                    const centerDot = document.createElement('div');
                    centerDot.style.cssText = `
                        width: 4px;
                        height: 4px;
                        background: #ffffff;
                        border-radius: 50%;
                        position: absolute;
                        top: 50%;
                        left: 50%;
                        transform: translate(-50%, -50%);
                    `;
                    cursor.appendChild(centerDot);

                    document.documentElement.appendChild(cursor);

                    window.__updateHumanCursor = function(x, y, isDown) {
                        const c = document.getElementById('human-virtual-cursor');
                        if (!c) return;
                        c.style.display = 'block';
                        c.style.left = x + 'px';
                        c.style.top = y + 'px';
                        if (isDown) {
                            c.style.background = 'radial-gradient(circle, rgba(16, 185, 129, 0.95) 40%, rgba(5, 150, 105, 0.6) 100%)';
                            c.style.width = '28px';
                            c.style.height = '28px';
                            c.style.boxShadow = '0 0 16px rgba(16, 185, 129, 0.9), 0 2px 8px rgba(0, 0, 0, 0.6)';
                        } else {
                            c.style.background = 'radial-gradient(circle, rgba(239, 68, 68, 0.95) 40%, rgba(220, 38, 38, 0.6) 100%)';
                            c.style.width = '22px';
                            c.style.height = '22px';
                            c.style.boxShadow = '0 0 12px rgba(239, 68, 68, 0.8), 0 2px 8px rgba(0, 0, 0, 0.5)';
                        }
                    };

                    window.__removeHumanCursor = function() {
                        const c = document.getElementById('human-virtual-cursor');
                        if (c) c.style.display = 'none';
                    };
                })();
            ";
            await ExecuteScriptAsync(webView, injectCursorScript);
        }

        private static async Task UpdateVisualCursorAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, double x, double y, bool isDown)
        {
            string script = FormattableString.Invariant($"if(window.__updateHumanCursor) window.__updateHumanCursor({x:0.0}, {y:0.0}, {(isDown ? "true" : "false")});");
            await ExecuteScriptAsync(webView, script);
        }

        private static async Task RemoveVisualCursorAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            string script = "if(window.__removeHumanCursor) window.__removeHumanCursor();";
            await ExecuteScriptAsync(webView, script);
        }

        #endregion

        #region WebView2 Execution Helpers

        private static async Task<string> ExecuteScriptAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string script)
        {
            try
            {
                if (webView.Dispatcher.CheckAccess())
                {
                    if (webView.CoreWebView2 != null)
                    {
                        return await webView.ExecuteScriptAsync(script);
                    }
                }
                else
                {
                    return await webView.Dispatcher.InvokeAsync(async () =>
                    {
                        if (webView.CoreWebView2 != null)
                        {
                            return await webView.ExecuteScriptAsync(script);
                        }
                        return string.Empty;
                    }).Task.Unwrap();
                }
            }
            catch
            {
                // Bỏ qua lỗi hủy hoặc script context
            }
            return string.Empty;
        }

        private static async Task<string> CallCdpMethodAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView, string method, string jsonParams)
        {
            try
            {
                if (webView.Dispatcher.CheckAccess())
                {
                    if (webView.CoreWebView2 != null)
                    {
                        return await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, jsonParams);
                    }
                }
                else
                {
                    return await webView.Dispatcher.InvokeAsync(async () =>
                    {
                        if (webView.CoreWebView2 != null)
                        {
                            return await webView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, jsonParams);
                        }
                        return string.Empty;
                    }).Task.Unwrap();
                }
            }
            catch
            {
                // Bỏ qua lỗi giao thức khi trang đang chuyển tiếp
            }
            return string.Empty;
        }

        #endregion
    }
}
