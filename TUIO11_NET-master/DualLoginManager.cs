using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuioDemo;

/// <summary>
/// Coordinator that races a face-recognition task against a Bluetooth-scan task.
/// First successful match wins; the loser is cancelled.
/// </summary>
public class DualLoginManager
{
    public enum LoginSource { None, Face, Bluetooth }

    public class LoginResult
    {
        public bool Success;
        public UserData User;
        public LoginSource Source;
        public string FailureReason;
        public float Confidence;     // face only
    }

    /// <summary>Confidence floor for trusting a face match.</summary>
    public const float FACE_CONFIDENCE_THRESHOLD = 0.60f;

    /// <summary>How long the face task waits for a confident match before giving up.</summary>
    public TimeSpan FaceTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Bluetooth poll interval.</summary>
    public TimeSpan BluetoothPollInterval { get; set; } = TimeSpan.FromSeconds(1);

    private readonly Func<List<UserData>> _loadUsers;
    private readonly Func<string> _scanBluetoothOnce;
    private readonly string _adminBluetoothMac;

    public DualLoginManager(
        Func<List<UserData>> loadUsers,
        Func<string> scanBluetoothOnce,
        string adminBluetoothMac)
    {
        _loadUsers = loadUsers;
        _scanBluetoothOnce = scanBluetoothOnce;
        _adminBluetoothMac = adminBluetoothMac;
    }

    /// <summary>
    /// Runs face + Bluetooth tasks in parallel and returns the first success.
    /// If both finish without a match the result has Success=false.
    /// </summary>
    public async Task<LoginResult> RunAsync(CancellationToken externalToken)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var ct = linked.Token;

        Task<LoginResult> faceTask = FaceLoginAsync(ct);
        Task<LoginResult> btTask   = BluetoothLoginAsync(ct);

        try
        {
            while (true)
            {
                if (faceTask.IsCompleted)
                {
                    var r = await faceTask.ConfigureAwait(false);
                    if (r.Success) { linked.Cancel(); return r; }
                }
                if (btTask.IsCompleted)
                {
                    var r = await btTask.ConfigureAwait(false);
                    if (r.Success) { linked.Cancel(); return r; }
                }
                if (faceTask.IsCompleted && btTask.IsCompleted)
                    return new LoginResult { Success = false, Source = LoginSource.None, FailureReason = "both_timeout" };

                try
                {
                    await Task.WhenAny(faceTask, btTask, Task.Delay(200, ct)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* re-checked below */ }

                if (ct.IsCancellationRequested)
                    return new LoginResult { Success = false, FailureReason = "cancelled" };
            }
        }
        catch (OperationCanceledException)
        {
            return new LoginResult { Success = false, FailureReason = "cancelled" };
        }
        finally
        {
            try { linked.Cancel(); } catch { }
            linked.Dispose();
        }
    }

    private Task<LoginResult> FaceLoginAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<LoginResult>();
        DateTime deadline = DateTime.UtcNow.Add(FaceTimeout);

        Action<string, float> handler = null;
        handler = (name, conf) =>
        {
            if (tcs.Task.IsCompleted) return;
            if (string.IsNullOrEmpty(name)) return;
            if (conf < FACE_CONFIDENCE_THRESHOLD) return;

            var users = _loadUsers();
            var user = users.FirstOrDefault(u =>
                u.IsActive && (
                    string.Equals(u.Name?.Trim(),   name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.FaceId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.UserId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (user == null) return;

            FaceIDRouter.OnFaceRecognized -= handler;
            tcs.TrySetResult(new LoginResult
            {
                Success    = true,
                User       = user,
                Source     = LoginSource.Face,
                Confidence = conf
            });
        };

        FaceIDRouter.OnFaceRecognized += handler;

        // Timeout + cancellation watchdog
        Task.Run(async () =>
        {
            try
            {
                while (!tcs.Task.IsCompleted)
                {
                    if (ct.IsCancellationRequested || DateTime.UtcNow >= deadline) break;
                    await Task.Delay(150, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                FaceIDRouter.OnFaceRecognized -= handler;
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetResult(new LoginResult
                    {
                        Success       = false,
                        Source        = LoginSource.Face,
                        FailureReason = ct.IsCancellationRequested ? "cancelled" : "timeout"
                    });
                }
            }
        });

        return tcs.Task;
    }

    private async Task<LoginResult> BluetoothLoginAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string mac = "";
                try
                {
                    mac = await Task.Run(() => _scanBluetoothOnce(), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DualLogin] BT scan error: {ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(mac))
                {
                    string normalized = NormalizeMac(mac);
                    var users = _loadUsers();

                    // Match purely by MAC + Role from users.json — no hardcoded admin MAC
                    var match = users.FirstOrDefault(u =>
                        u.IsActive
                        && !string.IsNullOrEmpty(u.BluetoothId)
                        && NormalizeMac(u.BluetoothId) == normalized);

                    if (match != null)
                        return new LoginResult { Success = true, User = match, Source = LoginSource.Bluetooth };
                }

                try { await Task.Delay(BluetoothPollInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (OperationCanceledException) { }

        return new LoginResult { Success = false, Source = LoginSource.Bluetooth, FailureReason = "cancelled_or_no_match" };
    }

    private static string NormalizeMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return "";
        return mac.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
    }
}
