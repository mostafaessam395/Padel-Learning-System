# Face ID Dual-Login + Marker-Driven Enrollment — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace HomePage's sequential face-then-Bluetooth login with parallel dual login, and add a TUIO-marker-driven enrollment wizard that captures 5 face photos, accepts a name via rotation spelling, and saves a new user record.

**Architecture:** C# WinForms (TuioDemo .NET Framework) talking to a Python OpenCV LBPH face server via newline-delimited JSON over TCP port 5001. Existing receive-only protocol extended to bidirectional with `reload` / `enroll` / `enroll_cancel` commands. New helper class `DualLoginManager` races a face task against a Bluetooth task. New modal form `EnrollmentPage` runs the 5-step wizard.

**Tech Stack:** C# 7 / .NET Framework 4.x / WinForms / Newtonsoft.Json / TUIO C# library / Python 3.x / OpenCV (cv2.face LBPH) / numpy.

**Source spec:** `docs/superpowers/specs/2026-05-11-face-id-dual-login-enrollment-design.md`.

**Testing note (TDD deviation):** This project has no test framework set up. The skill's "write failing test first" cadence is replaced here with **static review + manual smoke verification** at each task boundary. Pure-logic units (the spelling state machine) are written with an in-file `#if DEBUG` self-check entry point so they can be exercised quickly. Hardware-touching code (camera, Bluetooth, TUIO) is verified live by the user per the spec's section 10.3 checklist.

**Commit policy:** One commit per task. Conventional commit prefixes: `feat:`, `refactor:`, `fix:`.

---

## File map

| File | Action | Purpose |
|---|---|---|
| `TUIO11_NET-master/FaceID/face_recognition_server.py` | Modify | Add command parser, recognizer lock, reload/enroll/enroll_cancel commands, broadcast all face scans (not just matches). |
| `TUIO11_NET-master/FaceIDClient.cs` | Modify | Add `SendCommand(JObject)`, `OnFaceScanProgress`, and `OnEnrollResult` event routes. |
| `TUIO11_NET-master/DualLoginManager.cs` | Create | Parallel face + Bluetooth race, returns `LoginResult { User, Source, Failed }`. |
| `TUIO11_NET-master/EnrollmentPage.cs` | Create | Modal full-screen wizard (5 steps), implements `TuioListener`, saves new user via `UserService`. |
| `TUIO11_NET-master/TuioDemo.cs` | Modify | HomePage region: rip sequential face-then-BT logic, call `DualLoginManager`; add marker-10 handler to open `EnrollmentPage`; surface confidence ticker. |
| `TUIO11_NET-master/UserService.cs` | Modify | Make `Save` atomic (write `.tmp` then `File.Move` overwrite). |
| `TUIO11_NET-master/TUIO_DEMO.csproj` | Modify | Include `DualLoginManager.cs` and `EnrollmentPage.cs` in the Compile item group. |

---

## Task 1 — Python server: recognizer lock + face_scan broadcast

**Goal:** All face detections (matched and unmatched) broadcast so the C# ticker can show live confidence. Recognizer protected by a lock so future reload can swap it atomically.

**Files:**
- Modify: `TUIO11_NET-master/FaceID/face_recognition_server.py`

- [ ] **Step 1.1 — Add recognizer_lock and latest_frame holder**

After the global state block (around line 49), insert:

```python
import copy

# Concurrency primitives for hot-swappable recognizer + shared-frame design
recognizer_lock = threading.Lock()
latest_frame_lock = threading.Lock()
latest_frame = None
enroll_in_progress = threading.Event()
```

- [ ] **Step 1.2 — Wrap load_faces() swap in the lock**

Find the bottom of `load_faces()` where it calls `face_recognizer.train(...)`. Replace the final 3 lines so the swap happens under the lock:

```python
    # Train the LBPH recognizer
    try:
        new_recognizer = cv2.face.LBPHFaceRecognizer_create()
    except AttributeError:
        print("[Server] ERROR: cv2.face module not available!")
        print("[Server] Install with: pip install opencv-contrib-python")
        return False

    new_recognizer.train(faces, np.array(labels))

    with recognizer_lock:
        global face_recognizer, label_map, known_names
        face_recognizer = new_recognizer
        label_map = dict(label_map_local)
        known_names = list(known_names_local)

    print(f"[Server] Trained recognizer with {len(faces)} image(s) from {len(known_names_local)} player(s)")
    return True
```

And earlier in the same function, replace the module-level mutation of `label_map`/`known_names` with locals so the swap is atomic:

```python
    label_map_local = {}
    known_names_local = []
```

Change the for-loop's writes from `label_map[label_id] = name` and `known_names.append(name)` to:
```python
        label_map_local[label_id] = name
        known_names_local.append(name)
```

- [ ] **Step 1.3 — Camera loop publishes latest_frame and respects enroll_in_progress**

Rewrite `camera_loop` (around line 187). Replace its body with:

```python
def camera_loop():
    """Capture webcam frames, publish to latest_frame, and run recognition."""
    global face_recognizer, face_cascade, latest_frame

    print(f"[Server] Opening camera {CAMERA_INDEX}...")
    cap = cv2.VideoCapture(CAMERA_INDEX)

    if not cap.isOpened():
        print("[Server] ERROR: Cannot open camera.")
        while True:
            time.sleep(10)

    print("[Server] Camera opened. Scanning for faces...")

    while True:
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.1)
            continue

        # Publish raw colour frame for the enroll worker
        with latest_frame_lock:
            latest_frame = frame.copy()

        # Pause recognition while enrolling (server-side capture)
        if enroll_in_progress.is_set():
            time.sleep(0.05)
            continue

        if face_recognizer is None:
            time.sleep(SCAN_INTERVAL)
            continue

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        detected_faces = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))

        for (x, y, w, h) in detected_faces:
            face_roi = gray[y:y+h, x:x+w]
            face_roi = cv2.resize(face_roi, (200, 200))

            with recognizer_lock:
                if face_recognizer is None:
                    break
                label_id, distance = face_recognizer.predict(face_roi)
                name = label_map.get(label_id, "Unknown")

            confidence = max(0.0, min(1.0, 1.0 - (distance / 100.0)))
            matched = distance < CONFIDENCE_THRESHOLD
            now = time.time()

            # Always broadcast a scan event for the live ticker
            scan_msg = json.dumps({
                "type": "face_scan",
                "user_name": name if matched else None,
                "confidence": round(confidence, 3),
                "matched": matched
            })
            broadcast(scan_msg)

            if matched:
                if name in last_match_time and (now - last_match_time[name]) < COOLDOWN_AFTER_MATCH:
                    continue
                last_match_time[name] = now
                msg = json.dumps({
                    "type": "face_detected",
                    "user_name": name,
                    "confidence": round(confidence, 3)
                })
                print(f"[Server] Match: {name} (distance: {distance:.1f}, confidence: {confidence:.1%})")
                broadcast(msg)
                break

        time.sleep(SCAN_INTERVAL)

    cap.release()
```

- [ ] **Step 1.4 — Syntax check**

Run from the project root:
```bash
python -c "import ast; ast.parse(open('TUIO11_NET-master/FaceID/face_recognition_server.py').read())"
```
Expected: no output (clean parse). On error, fix.

- [ ] **Step 1.5 — Commit**

```bash
git add TUIO11_NET-master/FaceID/face_recognition_server.py
git commit -m "$(cat <<'EOF'
feat: add recognizer lock and broadcast face_scan events from server

Camera loop now publishes a shared latest_frame and broadcasts every face
detection (matched or unmatched) as a face_scan event for the upcoming
live-confidence ticker on HomePage. The recognizer is guarded by a lock
so an upcoming reload command can swap models atomically.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 — Python server: reload, enroll, enroll_cancel commands

**Goal:** The TCP client handler reads newline-delimited JSON commands; `reload` re-trains the recognizer in place, `enroll` captures N photos from `latest_frame` and saves them to disk then auto-reloads, `enroll_cancel` clears any partial enrollment state.

**Files:**
- Modify: `TUIO11_NET-master/FaceID/face_recognition_server.py`

- [ ] **Step 2.1 — Replace handle_client with command parser**

Replace the existing `handle_client` function with:

```python
def handle_client(conn, addr):
    """Handle a single TCP client. Reads newline-delimited JSON commands."""
    print(f"[Server] C# client connected: {addr}")
    with clients_lock:
        clients.append(conn)

    buf = b""
    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break
            buf += data
            while b"\n" in buf:
                line, _, buf = buf.partition(b"\n")
                line = line.strip()
                if not line:
                    continue
                try:
                    cmd = json.loads(line.decode("utf-8"))
                    dispatch_command(cmd, conn)
                except Exception as ex:
                    print(f"[Server] Bad command from {addr}: {ex} raw={line!r}")
    except Exception:
        pass
    finally:
        with clients_lock:
            if conn in clients:
                clients.remove(conn)
        try:
            conn.close()
        except Exception:
            pass
        print(f"[Server] C# client disconnected: {addr}")
```

- [ ] **Step 2.2 — Add dispatch_command and worker implementations**

Insert directly above `handle_client`:

```python
def dispatch_command(cmd, conn):
    name = cmd.get("cmd", "")
    if name == "reload":
        print("[Server] Command: reload")
        threading.Thread(target=cmd_reload, args=(conn,), daemon=True).start()
    elif name == "enroll":
        user_id = cmd.get("userId", "")
        count = int(cmd.get("count", 5))
        interval_ms = int(cmd.get("interval_ms", 600))
        print(f"[Server] Command: enroll user={user_id} count={count} interval={interval_ms}ms")
        threading.Thread(
            target=cmd_enroll,
            args=(user_id, count, interval_ms, conn),
            daemon=True,
        ).start()
    elif name == "enroll_cancel":
        user_id = cmd.get("userId", "")
        print(f"[Server] Command: enroll_cancel user={user_id}")
        threading.Thread(target=cmd_enroll_cancel, args=(user_id, conn), daemon=True).start()
    else:
        print(f"[Server] Unknown command: {name}")


def _send_reply(conn, obj):
    try:
        data = (json.dumps(obj) + "\n").encode("utf-8")
        conn.sendall(data)
    except Exception as ex:
        print(f"[Server] Reply send failed: {ex}")


def cmd_reload(conn):
    ok = load_faces()
    _send_reply(conn, {"type": "reload_done", "ok": bool(ok)})


def cmd_enroll(user_id, count, interval_ms, conn):
    if not user_id or not user_id.startswith("usr_"):
        _send_reply(conn, {"type": "enroll_failed", "userId": user_id, "reason": "bad_user_id"})
        return

    user_dir = os.path.join(FACES_DIR, user_id)
    os.makedirs(user_dir, exist_ok=True)

    enroll_in_progress.set()
    saved = 0
    try:
        for i in range(count):
            time.sleep(max(interval_ms, 100) / 1000.0)

            with latest_frame_lock:
                frame = None if latest_frame is None else latest_frame.copy()

            if frame is None:
                continue

            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            faces = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))
            if len(faces) == 0:
                continue

            out_path = os.path.join(user_dir, f"{i + 1}.jpg")
            cv2.imwrite(out_path, frame)
            saved += 1
            print(f"[Server] Enroll {user_id}: saved {out_path}")
    finally:
        enroll_in_progress.clear()

    if saved == 0:
        _send_reply(conn, {"type": "enroll_failed", "userId": user_id, "reason": "no_face_detected"})
        return

    load_faces()
    _send_reply(conn, {"type": "enroll_done", "userId": user_id, "saved": saved})


def cmd_enroll_cancel(user_id, conn):
    enroll_in_progress.clear()
    if not user_id or not user_id.startswith("usr_"):
        _send_reply(conn, {"type": "enroll_cancel_done", "userId": user_id, "removed": 0})
        return
    user_dir = os.path.join(FACES_DIR, user_id)
    removed = 0
    if os.path.isdir(user_dir):
        for f in os.listdir(user_dir):
            try:
                os.remove(os.path.join(user_dir, f))
                removed += 1
            except Exception:
                pass
        try:
            os.rmdir(user_dir)
        except Exception:
            pass
    _send_reply(conn, {"type": "enroll_cancel_done", "userId": user_id, "removed": removed})
```

- [ ] **Step 2.3 — Syntax check**

```bash
python -c "import ast; ast.parse(open('TUIO11_NET-master/FaceID/face_recognition_server.py').read())"
```
Expected: clean.

- [ ] **Step 2.4 — Commit**

```bash
git add TUIO11_NET-master/FaceID/face_recognition_server.py
git commit -m "$(cat <<'EOF'
feat: add reload/enroll/enroll_cancel TCP commands to face server

Extends the existing TCP protocol from receive-only to bidirectional.
The C# client can now request a model reload, drive a server-side
N-photo capture into Data/face_images/<UserId>/, or cancel a partial
enrollment. Capture uses the shared latest_frame and gates the camera
loop's recognition via enroll_in_progress to avoid two-thread cv2 reads.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 — FaceIDClient: SendCommand + new event routes

**Goal:** C# client can transmit JSON commands and route the new `face_scan`, `enroll_done`, `enroll_failed`, `enroll_cancel_done`, and `reload_done` replies.

**Files:**
- Modify: `TUIO11_NET-master/FaceIDClient.cs`

- [ ] **Step 3.1 — Extend FaceIDRouter with new events**

Replace the contents of the `FaceIDRouter` class (currently a single `OnFaceRecognized` event) with:

```csharp
public static class FaceIDRouter
{
    /// <summary>Fired when a confident face match arrives. (name, confidence 0-1)</summary>
    public static event Action<string, float> OnFaceRecognized;

    /// <summary>
    /// Fired on every face_scan event (matched or not). name is null when unmatched.
    /// Use for live UI feedback like the HomePage confidence ticker.
    /// </summary>
    public static event Action<string, float, bool> OnFaceScanProgress;

    /// <summary>Fired when the server replies to an enroll/reload/cancel command.</summary>
    public static event Action<JObject> OnServerReply;

    public static void RouteRecognition(string userName, float confidence)
    {
        OnFaceRecognized?.Invoke(userName, confidence);
    }

    public static void RouteScanProgress(string userName, float confidence, bool matched)
    {
        OnFaceScanProgress?.Invoke(userName, confidence, matched);
    }

    public static void RouteServerReply(JObject json)
    {
        OnServerReply?.Invoke(json);
    }
}
```

- [ ] **Step 3.2 — Add SendCommand to FaceIDClient**

Inside `FaceIDClient`, after the `Disconnect` method, add:

```csharp
/// <summary>
/// Sends a single newline-terminated JSON command to the face server.
/// Returns true on apparent success, false otherwise. Non-blocking.
/// </summary>
public bool SendCommand(JObject command)
{
    if (command == null) return false;
    var stream = _stream;
    var client = _client;
    if (stream == null || client == null || !client.Connected) return false;

    try
    {
        string line = command.ToString(Newtonsoft.Json.Formatting.None) + "\n";
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        Console.WriteLine($"[FaceIDClient] -> {line.TrimEnd()}");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FaceIDClient] SendCommand error: {ex.Message}");
        return false;
    }
}
```

- [ ] **Step 3.3 — Extend ProcessMessage to route new event types**

Replace the body of `ProcessMessage` with:

```csharp
private void ProcessMessage(string data)
{
    try
    {
        int start = data.IndexOf('{');
        int end = data.LastIndexOf('}');
        if (start == -1 || end == -1 || end <= start) return;

        string jsonStr = data.Substring(start, end - start + 1);
        JObject json = JObject.Parse(jsonStr);

        string type = json["type"]?.ToString() ?? "";

        switch (type)
        {
            case "face_detected":
            {
                string userName = json["user_name"]?.ToString();
                float confidence = json["confidence"]?.Value<float>() ?? 0f;
                if (!string.IsNullOrEmpty(userName))
                {
                    Console.WriteLine($"[FaceIDClient] Match {userName} ({confidence:F2})");
                    FaceIDRouter.RouteRecognition(userName, confidence);
                }
                break;
            }
            case "face_scan":
            {
                string userName = json["user_name"]?.Type == JTokenType.Null
                    ? null
                    : json["user_name"]?.ToString();
                float confidence = json["confidence"]?.Value<float>() ?? 0f;
                bool matched = json["matched"]?.Value<bool>() ?? false;
                FaceIDRouter.RouteScanProgress(userName, confidence, matched);
                break;
            }
            case "enroll_done":
            case "enroll_failed":
            case "enroll_cancel_done":
            case "reload_done":
            {
                Console.WriteLine($"[FaceIDClient] Reply: {type} {json}");
                FaceIDRouter.RouteServerReply(json);
                break;
            }
            default:
                Console.WriteLine($"[FaceIDClient] Unhandled type='{type}' raw={data}");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[FaceIDClient] Parse error: {ex.Message}");
    }
}
```

- [ ] **Step 3.4 — Verify no existing call site is broken**

```bash
grep -rn "FaceIDRouter\|OnFaceRecognized\|FaceIDClient" TUIO11_NET-master --include="*.cs"
```
Expected callers: `TuioDemo.cs` (HomePage init/cleanup), `AdminDashboardPage.cs` (constructor param), and `FaceIDClient.cs` itself. No signatures of existing methods change; new members are additive.

- [ ] **Step 3.5 — Commit**

```bash
git add TUIO11_NET-master/FaceIDClient.cs
git commit -m "$(cat <<'EOF'
feat: bidirectional FaceIDClient with SendCommand and new event routes

Adds SendCommand(JObject) so C# can drive the face server. New router
events OnFaceScanProgress (live ticker) and OnServerReply (enroll/reload
replies) are additive — existing OnFaceRecognized listeners untouched.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 — DualLoginManager.cs (new file)

**Goal:** Single async API that runs face + Bluetooth scanning in parallel and returns the first match.

**Files:**
- Create: `TUIO11_NET-master/DualLoginManager.cs`

- [ ] **Step 4.1 — Write the new file**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuioDemo;

/// <summary>
/// Coordinator that races a face-recognition task against a Bluetooth-scan task.
/// First successful match wins; the other task is cancelled.
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
    public const float FACE_CONFIDENCE_THRESHOLD = 0.75f;

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
        Task<LoginResult> btTask = BluetoothLoginAsync(ct);

        try
        {
            while (true)
            {
                if (faceTask.IsCompleted)
                {
                    var r = await faceTask;
                    if (r.Success) { linked.Cancel(); return r; }
                }
                if (btTask.IsCompleted)
                {
                    var r = await btTask;
                    if (r.Success) { linked.Cancel(); return r; }
                }
                if (faceTask.IsCompleted && btTask.IsCompleted)
                    return new LoginResult { Success = false, Source = LoginSource.None, FailureReason = "both_timeout" };

                await Task.WhenAny(faceTask, btTask, Task.Delay(200, ct)).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                    return new LoginResult { Success = false, FailureReason = "cancelled" };
            }
        }
        catch (OperationCanceledException)
        {
            return new LoginResult { Success = false, FailureReason = "cancelled" };
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
                    string.Equals(u.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.FaceId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.UserId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));

            if (user == null) return;

            FaceIDRouter.OnFaceRecognized -= handler;
            tcs.TrySetResult(new LoginResult
            {
                Success = true,
                User = user,
                Source = LoginSource.Face,
                Confidence = conf
            });
        };

        FaceIDRouter.OnFaceRecognized += handler;

        // Timeout + cancellation
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
                        Success = false,
                        Source = LoginSource.Face,
                        FailureReason = ct.IsCancellationRequested ? "cancelled" : "timeout"
                    });
                }
            }
        }, ct);

        return tcs.Task;
    }

    private async Task<LoginResult> BluetoothLoginAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string mac = "";
                try { mac = await Task.Run(() => _scanBluetoothOnce(), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { Console.WriteLine($"[DualLogin] BT scan error: {ex.Message}"); }

                if (!string.IsNullOrWhiteSpace(mac))
                {
                    string normalized = NormalizeMac(mac);
                    string adminNormalized = NormalizeMac(_adminBluetoothMac);

                    var users = _loadUsers();

                    if (normalized == adminNormalized)
                    {
                        // Resolve a UserData record for the admin MAC if one exists; otherwise synth.
                        var adminUser = users.FirstOrDefault(u =>
                            string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase) && u.IsActive);
                        if (adminUser == null)
                        {
                            adminUser = new UserData
                            {
                                UserId = "usr_admin",
                                Name = "Admin",
                                Role = "Admin",
                                Level = "Advanced",
                                IsActive = true,
                                BluetoothId = _adminBluetoothMac
                            };
                        }
                        return new LoginResult { Success = true, User = adminUser, Source = LoginSource.Bluetooth };
                    }

                    var match = users.FirstOrDefault(u =>
                        u.IsActive
                        && !string.IsNullOrEmpty(u.BluetoothId)
                        && NormalizeMac(u.BluetoothId) == normalized
                        && !string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase));

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
```

- [ ] **Step 4.2 — Register in the .csproj Compile group**

In `TUIO11_NET-master/TUIO_DEMO.csproj`, find the `<ItemGroup>` containing `<Compile Include="FaceIDClient.cs" />` and add:

```xml
    <Compile Include="DualLoginManager.cs" />
```

- [ ] **Step 4.3 — Commit**

```bash
git add TUIO11_NET-master/DualLoginManager.cs TUIO11_NET-master/TUIO_DEMO.csproj
git commit -m "$(cat <<'EOF'
feat: DualLoginManager runs face + Bluetooth login in parallel

Replaces the sequential face-then-Bluetooth-after-5s pattern with a
single async API that races both tasks. Threshold 0.75 for face,
1s Bluetooth poll, 8s face deadline. Admin MAC takes priority within
the Bluetooth branch.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5 — HomePage: wire DualLoginManager + confidence ticker

**Goal:** Strip the sequential `_faceScanTimeoutTimer` / `_bluetoothTimer` plumbing and call DualLoginManager. Update `_faceScanSubLabel` on every `OnFaceScanProgress` event to show live confidence.

**Files:**
- Modify: `TUIO11_NET-master/TuioDemo.cs` (HomePage region, roughly lines 1954–2124 plus surrounding state).

- [ ] **Step 5.1 — Add new field and helper to HomePage**

Insert near the other private fields (around line 783, after `_homeSynth`):

```csharp
    private DualLoginManager _dualLogin;
    private CancellationTokenSource _dualLoginCts;
```

- [ ] **Step 5.2 — Replace InitializeFaceID with dual-login boot**

Replace the entire `InitializeFaceID` method (1954–1987) with:

```csharp
    private void InitializeFaceID()
    {
        // Initialize TTS for greetings
        try
        {
            _homeSynth = new SpeechSynthesizer();
            _homeSynth.Rate = -1;
            _homeSynth.Volume = 100;
            foreach (InstalledVoice v in _homeSynth.GetInstalledVoices())
            {
                if (v.VoiceInfo.Culture.Name.StartsWith("en"))
                { _homeSynth.SelectVoice(v.VoiceInfo.Name); break; }
            }
        }
        catch { _homeSynth = null; }

        // Live confidence ticker (additive listener)
        FaceIDRouter.OnFaceScanProgress += HandleFaceScanProgress;

        // Connect to Python face server
        _faceIDClient = new FaceIDClient();
        ConnectFaceIDWithRetry();

        _faceReconnectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _faceReconnectTimer.Tick += (s, e) =>
        {
            if (!_faceIDClient.IsConnected && !_faceLoginCompleted)
                ConnectFaceIDWithRetry();
        };
        _faceReconnectTimer.Start();

        // Kick off dual login
        _dualLogin = new DualLoginManager(
            loadUsers: () => LoadUsersFromJson(),
            scanBluetoothOnce: () => GetCurrentBluetoothId(),
            adminBluetoothMac: ADMIN_BLUETOOTH_MAC);

        StartDualLogin();
    }
```

- [ ] **Step 5.3 — Add StartDualLogin and HandleFaceScanProgress**

Insert immediately after `InitializeFaceID`:

```csharp
    private async void StartDualLogin()
    {
        if (this.IsDisposed) return;

        _faceLoginCompleted = false;
        ShowFaceScanHUD("Scanning...", "Look at the camera or pair your phone");

        _dualLoginCts?.Cancel();
        _dualLoginCts = new CancellationTokenSource();

        DualLoginManager.LoginResult result;
        try
        {
            result = await _dualLogin.RunAsync(_dualLoginCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] DualLogin exception: {ex.Message}");
            return;
        }

        if (this.IsDisposed) return;
        if (!result.Success)
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                _faceScanStatusLabel.Text = "No login yet";
                _faceScanSubLabel.Text = "Place marker 10 to enroll, or try again";
                lblFooter.Text = "Place marker 10 to enroll a new player";
            }));
            return;
        }

        _faceLoginCompleted = true;
        this.BeginInvoke((MethodInvoker)(() => CompleteLoginAndNavigate(result)));
    }

    private void ShowFaceScanHUD(string status, string sub)
    {
        if (_faceScanHUD == null) return;
        _faceScanStatusLabel.Text = status;
        _faceScanSubLabel.Text = sub;
        _faceScanHUD.FillColor = Color.FromArgb(210, 12, 20, 40);
        _faceScanHUD.BorderColor = Color.FromArgb(100, 80, 160, 255);
        _faceScanHUD.Visible = true;
        _faceScanHUD.BringToFront();
        _faceScanHUD.Invalidate();
        _facePulseTimer?.Start();
    }

    private void HandleFaceScanProgress(string userName, float confidence, bool matched)
    {
        if (this.IsDisposed || _faceLoginCompleted) return;
        try
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (_faceScanSubLabel == null) return;
                if (matched && !string.IsNullOrEmpty(userName))
                    _faceScanSubLabel.Text = $"Recognising {userName} ({confidence:F2})";
                else
                    _faceScanSubLabel.Text = $"Scanning… ({confidence:F2})";
            }));
        }
        catch { }
    }

    private void CompleteLoginAndNavigate(DualLoginManager.LoginResult result)
    {
        if (pageOpen || _adminPageOpen) return;
        var user = result.User;
        if (user == null) return;

        _faceScanStatusLabel.Text = $"Welcome, {user.Name}!";
        _faceScanSubLabel.Text = $"{result.Source} login  •  {MapDisplayedLevel(user.Level)}";
        _faceScanHUD.FillColor = Color.FromArgb(215, 15, 55, 35);
        _faceScanHUD.BorderColor = Color.FromArgb(140, 60, 220, 100);
        _faceScanHUD.Invalidate();
        _faceScanRing?.Invalidate();
        lblFooter.Text = "Player identified: " + user.Name;
        lblInstruction.Text = MapDetectedPlayerLevel(user.Level);

        try
        {
            if (_homeSynth != null && !AppSettings.IsMuted)
            {
                _homeSynth.Rate = AppSettings.VoiceRate;
                _homeSynth.SpeakAsyncCancelAll();
                _homeSynth.SpeakAsync($"Welcome back, {user.Name}.");
            }
        }
        catch { }

        var navTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        navTimer.Tick += (s, e) =>
        {
            navTimer.Stop();
            navTimer.Dispose();
            HideFaceScanHUD();
            NavigateByRole(user);
        };
        navTimer.Start();
    }

    private void NavigateByRole(UserData user)
    {
        if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            _adminPageOpen = true;
            var adminPage = new AdminDashboardPage(
                tuioClient: client,
                adminName: user.Name,
                btConnected: true,
                gestureRef: _gestureClient,
                faceRef: _faceIDClient,
                gazeRef: null);
            adminPage.FormClosed += (s, e) =>
            {
                _adminPageOpen = false;
                this.Show();
                StartDualLogin();
            };
            adminPage.Show();
            this.Hide();
            return;
        }

        currentUser = user;
        pageOpen = true;
        var page = new LearningPage(user, client);
        page.FormClosed += (s, e) =>
        {
            pageOpen = false;
            currentUser = null;
            _faceLoginCompleted = false;
            this.Show();
            StartDualLogin();
        };
        page.Show();
        this.Hide();
    }
```

- [ ] **Step 5.4 — Remove the old sequential methods**

Delete the following methods entirely from HomePage (their behaviour is now subsumed by the dual-login pipeline):

- `StartFaceScanWindow` (lines 1998–2037)
- `HandleFaceRecognized` (lines 2046–2124)
- `CheckBluetoothAndLogin` (lines 1109–1213) — replaced by `DualLoginManager.BluetoothLoginAsync`

Also remove `_bluetoothTimer` initialisation in the constructor (lines 808–811) — it's no longer driven by the page; the manager owns the polling cadence. The field declaration can stay for the cleanup path but should be `null` and unused, or remove entirely; remove entirely for cleanliness:

- Delete the field declaration `private System.Windows.Forms.Timer _bluetoothTimer;` (line 759).
- Delete the constructor block that wires `_bluetoothTimer` (lines 808–811).
- Delete the `_bluetoothTimer.Stop()` / `_bluetoothTimer.Start()` references in `CheckBluetoothAndLogin` (gone), and any other `_bluetoothTimer.*` usages — search and remove.

Also remove the timer disposal of `_faceScanTimeoutTimer` references — that field can stay (still used to be null) but is also gone; clean it up:

- Delete `private System.Windows.Forms.Timer _faceScanTimeoutTimer;` (line 776).
- Remove all `_faceScanTimeoutTimer?.Stop()` and `_faceScanTimeoutTimer?.Dispose()` references (lines 1853-1854 in `OnFormClosed` and any others).

- [ ] **Step 5.5 — Update OnFormClosed cleanup**

In the existing `OnFormClosed` (around line 1833), replace the `FaceIDRouter.OnFaceRecognized -= HandleFaceRecognized;` line with:

```csharp
        FaceIDRouter.OnFaceScanProgress -= HandleFaceScanProgress;
        _dualLoginCts?.Cancel();
        _dualLoginCts?.Dispose();
```

Remove the now-orphaned `_faceScanTimeoutTimer` cleanup lines (they reference a field that no longer exists).

- [ ] **Step 5.6 — Build verification**

Run from worktree root:
```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TUIO11_NET-master\TUIO_DEMO.csproj /t:Rebuild /p:Configuration=Debug /verbosity:minimal
```
Expected: `Build succeeded.` with 0 errors. If MSBuild path differs on this machine, use the path from `REBUILD_AND_RUN.bat`.

- [ ] **Step 5.7 — Commit**

```bash
git add TUIO11_NET-master/TuioDemo.cs
git commit -m "$(cat <<'EOF'
refactor: HomePage uses DualLoginManager for parallel face+BT login

Replaces sequential 5s-face-then-Bluetooth fallback with a single
async race driven by DualLoginManager. Adds live confidence ticker
via FaceIDRouter.OnFaceScanProgress. Removes _faceScanTimeoutTimer
and _bluetoothTimer plumbing. Admin/player routing preserved.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6 — EnrollmentPage.cs skeleton + Step 1 face capture

**Goal:** Modal form opens on demand. Step 1 sends an enroll command and shows captured thumbnails on success.

**Files:**
- Create: `TUIO11_NET-master/EnrollmentPage.cs`
- Modify: `TUIO11_NET-master/TUIO_DEMO.csproj`

- [ ] **Step 6.1 — Write the new file (skeleton + step 1)**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using TUIO;
using TuioDemo;

/// <summary>
/// Marker-driven new-user enrollment wizard.
/// Steps: 1 face capture → 2 name (rotation spelling) → 3 level → 4 gender → 5 confirm.
/// </summary>
public class EnrollmentPage : Form, TuioListener
{
    public enum Step { FaceCapture, Name, Level, Gender, Confirm, Done }

    private readonly TuioClient _tuio;
    private readonly FaceIDClient _faceClient;
    private readonly Action<UserData> _onCompleted;

    private Step _step = Step.FaceCapture;
    private readonly string _userId = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private string _name = "";
    private int _letterIndex = 0;
    private string _level = "";
    private string _gender = "";

    private Label _lblStepTitle;
    private Label _lblStepHint;
    private Label _lblBigText;
    private Label _lblAlphaStrip;
    private Label _lblName;
    private Panel _thumbsHost;
    private System.Windows.Forms.Timer _captureCountdown;
    private int _captureRemaining = 0;
    private bool _waitingOnServer = false;
    private DateTime _enrollDeadline = DateTime.MinValue;
    private System.Windows.Forms.Timer _enrollWatchdog;

    private int _lastMarkerId = -1;
    private float _lastMarker6Angle = float.NaN;

    public EnrollmentPage(TuioClient tuio, FaceIDClient faceClient, Action<UserData> onCompleted)
    {
        _tuio = tuio;
        _faceClient = faceClient;
        _onCompleted = onCompleted;

        this.Text = "Enroll new player";
        this.WindowState = FormWindowState.Maximized;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;
        this.BackColor = AppSettings.PageBg;

        BuildUI();
        FaceIDRouter.OnServerReply += HandleServerReply;

        this.Shown += (s, e) =>
        {
            if (_tuio != null) _tuio.addTuioListener(this);
            EnterStep(Step.FaceCapture);
        };
        this.FormClosed += (s, e) =>
        {
            if (_tuio != null) _tuio.removeTuioListener(this);
            FaceIDRouter.OnServerReply -= HandleServerReply;
            _captureCountdown?.Stop();
            _captureCountdown?.Dispose();
            _enrollWatchdog?.Stop();
            _enrollWatchdog?.Dispose();
        };
    }

    // ───── UI scaffolding ───────────────────────────────────────────────
    private void BuildUI()
    {
        _lblStepTitle = new Label
        {
            Text = "",
            Font = new Font("Arial", 26, FontStyle.Bold),
            ForeColor = AppSettings.TitleText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 50),
            Location = new Point(60, 40)
        };
        _lblStepHint = new Label
        {
            Text = "",
            Font = new Font("Arial", 14, FontStyle.Regular),
            ForeColor = AppSettings.SubText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 30),
            Location = new Point(60, 100)
        };
        _lblBigText = new Label
        {
            Text = "",
            Font = new Font("Arial", 60, FontStyle.Bold),
            ForeColor = AppSettings.AccentText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 100),
            Location = new Point(60, 180)
        };
        _lblName = new Label
        {
            Text = "",
            Font = new Font("Consolas", 42, FontStyle.Bold),
            ForeColor = AppSettings.TitleText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 70),
            Location = new Point(60, 300),
            Visible = false
        };
        _lblAlphaStrip = new Label
        {
            Text = "",
            Font = new Font("Consolas", 28, FontStyle.Regular),
            ForeColor = AppSettings.SubText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 60),
            Location = new Point(60, 400),
            Visible = false
        };
        _thumbsHost = new Panel
        {
            Size = new Size(900, 180),
            Location = new Point(160, 300),
            BackColor = Color.Transparent,
            Visible = false
        };

        this.Controls.Add(_lblStepTitle);
        this.Controls.Add(_lblStepHint);
        this.Controls.Add(_lblBigText);
        this.Controls.Add(_lblName);
        this.Controls.Add(_lblAlphaStrip);
        this.Controls.Add(_thumbsHost);

        NavHelper.AddNavBar(this, "Enrollment", canGoBack: true);
    }

    // ───── Step dispatcher ──────────────────────────────────────────────
    private void EnterStep(Step s)
    {
        _step = s;
        _lblName.Visible = false;
        _lblAlphaStrip.Visible = false;
        _thumbsHost.Visible = false;
        _thumbsHost.Controls.Clear();

        switch (s)
        {
            case Step.FaceCapture:
                _lblStepTitle.Text = "1 / 5 — Face Capture";
                _lblStepHint.Text = "Look at the camera. I'll take 5 photos — turn your head slightly between each.";
                _lblBigText.Text = "Get ready…";
                StartCaptureCountdown();
                break;

            case Step.Name:
                _lblStepTitle.Text = "2 / 5 — Name";
                _lblStepHint.Text = "Marker 6 rotate = next letter   •   4 = pick   •   5 = backspace   •   7 = done   •   20 = cancel";
                _lblBigText.Text = "";
                _lblName.Visible = true;
                _lblAlphaStrip.Visible = true;
                _name = "";
                _letterIndex = 0;
                _lastMarker6Angle = float.NaN;
                RenderSpelling();
                break;

            case Step.Level:
                _lblStepTitle.Text = "3 / 5 — Level";
                _lblStepHint.Text = "Marker 3 = Beginner   •   4 = Intermediate   •   5 = Advanced   •   20 = back";
                _lblBigText.Text = "Choose your level";
                break;

            case Step.Gender:
                _lblStepTitle.Text = "4 / 5 — Gender";
                _lblStepHint.Text = "Marker 3 = Male   •   4 = Female   •   5 = Skip   •   20 = back";
                _lblBigText.Text = "Choose gender (optional)";
                break;

            case Step.Confirm:
                _lblStepTitle.Text = "5 / 5 — Confirm";
                _lblStepHint.Text = "Marker 7 = save   •   5 = start over   •   20 = cancel";
                _lblBigText.Text = $"{_name}\n{MapLevelDisplay(_level)} • {(string.IsNullOrEmpty(_gender) ? "—" : _gender)}";
                break;

            case Step.Done:
                _lblStepTitle.Text = "Welcome!";
                _lblStepHint.Text = "Profile saved. Loading your dashboard…";
                _lblBigText.Text = _name;
                break;
        }
    }

    // ───── Step 1: face capture ─────────────────────────────────────────
    private void StartCaptureCountdown()
    {
        _captureRemaining = 3;
        _captureCountdown?.Stop();
        _captureCountdown = new System.Windows.Forms.Timer { Interval = 1000 };
        _captureCountdown.Tick += (s, e) =>
        {
            if (_captureRemaining > 0)
            {
                _lblBigText.Text = _captureRemaining.ToString();
                _captureRemaining--;
            }
            else
            {
                _captureCountdown.Stop();
                _lblBigText.Text = "Capturing 5 photos…";
                SendEnrollCommand();
            }
        };
        _captureCountdown.Start();
    }

    private void SendEnrollCommand()
    {
        if (_faceClient == null || !_faceClient.IsConnected)
        {
            _lblBigText.Text = "Face server offline.\nStart face_recognition_server.py and reopen.";
            return;
        }

        var cmd = new JObject
        {
            ["cmd"] = "enroll",
            ["userId"] = _userId,
            ["count"] = 5,
            ["interval_ms"] = 600
        };
        if (!_faceClient.SendCommand(cmd))
        {
            _lblBigText.Text = "Could not reach face server.";
            return;
        }

        _waitingOnServer = true;
        _enrollDeadline = DateTime.UtcNow.AddSeconds(15);
        _enrollWatchdog?.Stop();
        _enrollWatchdog = new System.Windows.Forms.Timer { Interval = 1000 };
        _enrollWatchdog.Tick += (s, e) =>
        {
            if (!_waitingOnServer) { _enrollWatchdog.Stop(); return; }
            if (DateTime.UtcNow >= _enrollDeadline)
            {
                _enrollWatchdog.Stop();
                _waitingOnServer = false;
                _lblBigText.Text = "Camera timed out.\nMarker 5 = retake, 20 = cancel.";
            }
        };
        _enrollWatchdog.Start();
    }

    private void HandleServerReply(JObject reply)
    {
        if (this.IsDisposed) return;
        string type = reply["type"]?.ToString() ?? "";
        string userId = reply["userId"]?.ToString() ?? "";
        if (!string.Equals(userId, _userId, StringComparison.Ordinal)) return;

        if (type == "enroll_done")
        {
            int saved = reply["saved"]?.Value<int>() ?? 0;
            this.BeginInvoke((MethodInvoker)(() => OnEnrollSucceeded(saved)));
        }
        else if (type == "enroll_failed")
        {
            string reason = reply["reason"]?.ToString() ?? "unknown";
            this.BeginInvoke((MethodInvoker)(() =>
            {
                _waitingOnServer = false;
                _lblBigText.Text = $"Capture failed: {reason}\nMarker 5 = retake, 20 = cancel.";
            }));
        }
    }

    private void OnEnrollSucceeded(int saved)
    {
        _waitingOnServer = false;
        _enrollWatchdog?.Stop();
        _lblBigText.Text = $"Captured {saved} photos. Marker 4 = keep, 5 = retake, 20 = cancel.";
        ShowThumbnails();
    }

    private void ShowThumbnails()
    {
        _thumbsHost.Visible = true;
        _thumbsHost.Controls.Clear();

        string dir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "face_images", _userId);
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.jpg").OrderBy(x => x).Take(5).ToArray();
        int gap = 20;
        int thumbW = (_thumbsHost.Width - gap * 6) / 5;
        int thumbH = _thumbsHost.Height - gap;
        for (int i = 0; i < files.Length; i++)
        {
            var pb = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Size = new Size(thumbW, thumbH),
                Location = new Point(gap + i * (thumbW + gap), 0)
            };
            try
            {
                using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                using (var img = Image.FromStream(fs))
                    pb.Image = new Bitmap(img);
            }
            catch { }
            _thumbsHost.Controls.Add(pb);
        }
    }

    // ───── Steps 2–5 stubs (filled in subsequent tasks) ─────────────────
    private void RenderSpelling()
    {
        _lblName.Text = string.IsNullOrEmpty(_name) ? "_" : _name;
        var chars = new char[26];
        for (int i = 0; i < 26; i++) chars[i] = (char)('A' + i);
        var strip = string.Join(" ", chars.Select((c, i) => i == _letterIndex ? "[" + c + "]" : c.ToString()));
        _lblAlphaStrip.Text = strip;
    }

    private string MapLevelDisplay(string lvl)
    {
        if (lvl == "Primary") return "Beginner";
        if (lvl == "Secondary") return "Intermediate";
        if (lvl == "HighSchool") return "Advanced";
        return string.IsNullOrEmpty(lvl) ? "—" : lvl;
    }

    private void SendEnrollCancel()
    {
        if (_faceClient == null || !_faceClient.IsConnected) return;
        var cmd = new JObject { ["cmd"] = "enroll_cancel", ["userId"] = _userId };
        _faceClient.SendCommand(cmd);
    }

    // ───── TUIO marker handling (delegates to step-specific handlers) ───
    public void addTuioObject(TuioObject o)
    {
        int id = o.SymbolID;
        if (id == _lastMarkerId) return;
        _lastMarkerId = id;
        this.BeginInvoke((MethodInvoker)(() => RouteMarker(id)));
    }
    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == _lastMarkerId) _lastMarkerId = -1;
    }
    public void updateTuioObject(TuioObject o)
    {
        if (_step == Step.Name && o.SymbolID == 6)
        {
            float deg = o.Angle * 180f / (float)Math.PI;
            this.BeginInvoke((MethodInvoker)(() => HandleRotation(deg)));
        }
    }
    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    private void RouteMarker(int id)
    {
        if (id == 20)
        {
            SendEnrollCancel();
            _onCompleted?.Invoke(null);
            this.Close();
            return;
        }
        switch (_step)
        {
            case Step.FaceCapture:
                if (_waitingOnServer) return;
                if (id == 4) EnterStep(Step.Name);
                else if (id == 5) { SendEnrollCancel(); EnterStep(Step.FaceCapture); }
                break;
            case Step.Name:
                HandleNameMarker(id);
                break;
            case Step.Level:
                HandleLevelMarker(id);
                break;
            case Step.Gender:
                HandleGenderMarker(id);
                break;
            case Step.Confirm:
                HandleConfirmMarker(id);
                break;
        }
    }

    // Stubs filled in later tasks
    private void HandleRotation(float deg) { /* Task 7 */ }
    private void HandleNameMarker(int id) { /* Task 7 */ }
    private void HandleLevelMarker(int id) { /* Task 8 */ }
    private void HandleGenderMarker(int id) { /* Task 8 */ }
    private void HandleConfirmMarker(int id) { /* Task 9 */ }
}
```

(Implementation note: all `BeginInvoke` calls must use `MethodInvoker`, not `MethodInvoke`. Verify before build.)

- [ ] **Step 6.2 — Add EnrollmentPage.cs to the .csproj**

Inside the same `<ItemGroup>` as `DualLoginManager.cs`:
```xml
    <Compile Include="EnrollmentPage.cs" />
```

- [ ] **Step 6.3 — Build verification**

Run MSBuild as in Task 5.6. Expected: clean build.

- [ ] **Step 6.4 — Commit**

```bash
git add TUIO11_NET-master/EnrollmentPage.cs TUIO11_NET-master/TUIO_DEMO.csproj
git commit -m "$(cat <<'EOF'
feat: EnrollmentPage scaffold + step 1 face capture

Full-screen modal wizard with 5-step state machine. Step 1 sends an
enroll command to the Python server, watches for enroll_done/_failed
replies (15s watchdog), and shows the saved thumbnails on success.
Steps 2–5 are stubbed and filled in by subsequent tasks.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7 — EnrollmentPage Step 2: name rotation-spelling

**Goal:** Marker 6 rotation cycles letters, 4 picks, 5 backspaces, 7 commits the name.

**Files:**
- Modify: `TUIO11_NET-master/EnrollmentPage.cs`

- [ ] **Step 7.1 — Replace the spelling stubs**

Replace the two stub methods with:

```csharp
    private void HandleRotation(float deg)
    {
        if (float.IsNaN(_lastMarker6Angle)) { _lastMarker6Angle = deg; return; }
        float delta = deg - _lastMarker6Angle;
        // wrap into -180..180
        while (delta > 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;
        const float STEP_DEG = 18f;     // one letter per ~18° of rotation
        if (Math.Abs(delta) < STEP_DEG) return;

        int steps = (int)(delta / STEP_DEG);
        _letterIndex = ((_letterIndex + steps) % 26 + 26) % 26;
        _lastMarker6Angle = deg;
        RenderSpelling();
    }

    private void HandleNameMarker(int id)
    {
        if (id == 4)
        {
            if (_name.Length >= 16) return;
            _name += (char)('A' + _letterIndex);
            RenderSpelling();
        }
        else if (id == 5)
        {
            if (_name.Length == 0) return;
            _name = _name.Substring(0, _name.Length - 1);
            RenderSpelling();
        }
        else if (id == 7)
        {
            if (string.IsNullOrWhiteSpace(_name))
            {
                _lblStepHint.Text = "Name cannot be empty — keep picking letters.";
                return;
            }
            EnterStep(Step.Level);
        }
    }
```

- [ ] **Step 7.2 — Build verification**

MSBuild as before.

- [ ] **Step 7.3 — Commit**

```bash
git add TUIO11_NET-master/EnrollmentPage.cs
git commit -m "$(cat <<'EOF'
feat: rotation-spelling name input in EnrollmentPage step 2

Marker 6 rotation cycles A-Z (one letter per ~18° delta), marker 4
appends, 5 backspaces, 7 commits. Empty name refuses to advance.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8 — EnrollmentPage Steps 3 + 4: level + gender

**Files:**
- Modify: `TUIO11_NET-master/EnrollmentPage.cs`

- [ ] **Step 8.1 — Replace level + gender stubs**

```csharp
    private void HandleLevelMarker(int id)
    {
        switch (id)
        {
            case 3: _level = "Primary"; break;
            case 4: _level = "Secondary"; break;
            case 5: _level = "HighSchool"; break;
            default: return;
        }
        EnterStep(Step.Gender);
    }

    private void HandleGenderMarker(int id)
    {
        switch (id)
        {
            case 3: _gender = "Male"; break;
            case 4: _gender = "Female"; break;
            case 5: _gender = ""; break;
            default: return;
        }
        EnterStep(Step.Confirm);
    }
```

- [ ] **Step 8.2 — Build verification**

MSBuild as before.

- [ ] **Step 8.3 — Commit**

```bash
git add TUIO11_NET-master/EnrollmentPage.cs
git commit -m "$(cat <<'EOF'
feat: level and gender steps in EnrollmentPage

Markers 3/4/5 map to Beginner/Intermediate/Advanced (saved as
Primary/Secondary/HighSchool) and Male/Female/Skip respectively.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9 — EnrollmentPage Step 5: confirm + save + auto-login

**Files:**
- Modify: `TUIO11_NET-master/EnrollmentPage.cs`
- Modify: `TUIO11_NET-master/UserService.cs` (atomic write)

- [ ] **Step 9.1 — Make UserService.Save atomic**

Replace the body of `UserService.Save` with:

```csharp
    private void Save(List<UserData> list, string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimePath));
            string json = JsonConvert.SerializeObject(list, Formatting.Indented);

            string tmp = RuntimePath + ".tmp";
            File.WriteAllText(tmp, json);
            // Atomic replace: move overwrites destination
            if (File.Exists(RuntimePath))
                File.Replace(tmp, RuntimePath, RuntimePath + ".bak");
            else
                File.Move(tmp, RuntimePath);

            string verify = File.ReadAllText(RuntimePath);
            Log($"SAVE  path={RuntimePath}  users={list.Count}  reason={reason}  verified={verify.Length > 2}");
        }
        catch (Exception ex)
        {
            Log($"SAVE  ERROR: {ex.Message}  reason={reason}");
        }
    }
```

- [ ] **Step 9.2 — Replace confirm stub in EnrollmentPage**

```csharp
    private void HandleConfirmMarker(int id)
    {
        if (id == 5)
        {
            // Start over
            SendEnrollCancel();
            _name = "";
            _level = "";
            _gender = "";
            EnterStep(Step.FaceCapture);
            return;
        }
        if (id != 7) return;

        // Build new user
        var user = new UserData
        {
            UserId = _userId,
            BluetoothId = "",
            FaceId = _userId,
            Name = _name,
            Gender = _gender ?? "",
            Age = 0,
            Level = _level,
            Role = "Player",
            IsActive = true,
            GazeProfile = new GazeProfile()
        };

        try
        {
            new UserService().AddUser(user);
        }
        catch (Exception ex)
        {
            _lblBigText.Text = "Save failed: " + ex.Message;
            return;
        }

        // Safety-net reload (server already reloaded after enroll_done, but be explicit)
        try
        {
            _faceClient?.SendCommand(new JObject { ["cmd"] = "reload" });
        }
        catch { }

        EnterStep(Step.Done);

        var navTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        navTimer.Tick += (s, e) =>
        {
            navTimer.Stop();
            navTimer.Dispose();
            _onCompleted?.Invoke(user);
            this.Close();
        };
        navTimer.Start();
    }
```

- [ ] **Step 9.3 — Build verification**

MSBuild as before.

- [ ] **Step 9.4 — Commit**

```bash
git add TUIO11_NET-master/EnrollmentPage.cs TUIO11_NET-master/UserService.cs
git commit -m "$(cat <<'EOF'
feat: EnrollmentPage confirm step saves user and auto-logs in

On marker 7 the wizard writes the new UserData via UserService.AddUser
(now atomic via .tmp + File.Replace), sends a safety-net reload to the
face server, and hands the user back to HomePage for direct navigation
into LearningPage. Marker 5 restarts the wizard, 20 cancels.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10 — HomePage marker 10 hook + EnrollmentPage launch

**Files:**
- Modify: `TUIO11_NET-master/TuioDemo.cs` (HomePage `addTuioObject`)

- [ ] **Step 10.1 — Insert marker-10 branch**

Replace the current HomePage `addTuioObject` body with:

```csharp
    public void addTuioObject(TuioObject o)
    {
        if (o.SymbolID >= 21 && o.SymbolID <= 30)
        {
            float angleDeg = o.Angle * (180f / (float)Math.PI);
            this.BeginInvoke((MethodInvoker)(() =>
            {
                circMenu.HandleTUIO(o.SymbolID);
                circMenu.HandleMarkerAdded(angleDeg);
            }));
            return;
        }

        if (o.SymbolID == 10)
        {
            if (o.SymbolID == _lastEnrollTrigger) return;
            _lastEnrollTrigger = o.SymbolID;
            this.BeginInvoke((MethodInvoker)(() => OpenEnrollmentPage()));
            return;
        }
    }
```

Add the corresponding field declaration near the other private fields:
```csharp
    private int _lastEnrollTrigger = -1;
```

And clear it in `removeTuioObject`:
```csharp
        if (o.SymbolID == 10) _lastEnrollTrigger = -1;
```

- [ ] **Step 10.2 — Add OpenEnrollmentPage helper**

Insert near the other HomePage helpers (e.g. just above `OnFormClosed`):

```csharp
    private bool _enrollPageOpen = false;

    private void OpenEnrollmentPage()
    {
        if (_enrollPageOpen || pageOpen || _adminPageOpen) return;
        if (_faceIDClient == null || !_faceIDClient.IsConnected)
        {
            lblFooter.Text = "Face server not running — start face_recognition_server.py first.";
            return;
        }

        _dualLoginCts?.Cancel();   // pause login race during enrollment
        _enrollPageOpen = true;

        var page = new EnrollmentPage(client, _faceIDClient, onCompleted: newUser =>
        {
            _enrollPageOpen = false;
            if (newUser == null)
            {
                // Cancelled — restart login race
                this.Show();
                StartDualLogin();
                return;
            }

            // Auto-login the new user (skip dual-login wait)
            _faceLoginCompleted = true;
            this.Show();
            CompleteLoginAndNavigate(new DualLoginManager.LoginResult
            {
                Success = true,
                User = newUser,
                Source = DualLoginManager.LoginSource.Face,
                Confidence = 1.0f
            });
        });

        page.FormClosed += (s, e) =>
        {
            _enrollPageOpen = false;
            if (!_faceLoginCompleted)
            {
                this.Show();
                StartDualLogin();
            }
        };

        page.Show();
        this.Hide();
    }
```

- [ ] **Step 10.3 — Build verification**

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" TUIO11_NET-master\TUIO_DEMO.csproj /t:Rebuild /p:Configuration=Debug /verbosity:minimal
```
Expected: clean build, 0 errors.

- [ ] **Step 10.4 — Commit**

```bash
git add TUIO11_NET-master/TuioDemo.cs
git commit -m "$(cat <<'EOF'
feat: marker 10 on HomePage opens EnrollmentPage wizard

Places marker 10 to launch the new-user enrollment flow. While the
wizard is open the dual-login race is paused; on save the new user is
auto-logged-in via CompleteLoginAndNavigate; on cancel we resume the
race.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Manual smoke verification (after all tasks)

Repeat the spec's section 10.3 checklist live:

1. Back up `Data/users.json`.
2. `python TUIO11_NET-master/FaceID/face_recognition_server.py` (camera must be free).
3. `TUIO11_NET-master/bin/Debug/TuioDemo.exe`.
4. HomePage HUD: confidence ticker updates as a face is detected.
5. Existing user "Shahd" recognised within 8s → navigates to LearningPage.
6. Walk away, close LearningPage, return → dual-login restarts and works again.
7. Place marker 10 → EnrollmentPage opens; complete all 5 steps with a test name.
8. Verify new entry in `Data/users.json` with `UserId == usr_<hex>`, FaceId set, BluetoothId empty.
9. Verify `Data/face_images/<UserId>/1.jpg…5.jpg` exist.
10. Auto-login lands on LearningPage. Close it; relogin via face should still work.
11. Re-open enrollment, cancel via marker 20 → `Data/face_images/<UserId>/` is removed; `users.json` unchanged.

---

## Self-review

**Spec coverage check:** every spec section maps to a task:
- §3 file layout → Task 4 (DualLoginManager.cs new), Task 6 (EnrollmentPage.cs new), Tasks 5/10 (TuioDemo.cs edits), Task 3 (FaceIDClient edits), Task 1 + Task 2 (face server edits), Task 9 (UserService edits)
- §4 dual login → Task 4 + Task 5
- §4.4 sub-threshold events / face_scan → Task 1 (server) + Task 3 (router) + Task 5 (ticker UI)
- §5 face capture protocol → Task 2 (server enroll/cancel) + Task 3 (SendCommand) + Task 6 (page step 1)
- §6 marker fields → Tasks 7, 8, 9
- §7 server protocol → Task 1 + Task 2
- §8 save & auto-login → Task 9 + Task 10
- §9 error handling → Task 2 (server failure reasons), Task 6 (watchdog + offline guard), Task 10 (server-offline guard at trigger)
- §10 verification → smoke section above
- §11 known limits → consciously not implemented (uppercase-only name, no Bluetooth pairing during enroll)

**Placeholder scan:** no TBDs, no "implement later", every code step contains actual code.

**Type consistency check:**
- `DualLoginManager.LoginResult` is referenced in Task 5 (`CompleteLoginAndNavigate`) and Task 10 (auto-login synth) — same shape: `Success`, `User`, `Source`, `Confidence`, `FailureReason`. ✓
- `LoginSource.Face` used in both. ✓
- `FaceIDRouter.OnFaceScanProgress` signature `(string, float, bool)` — used identically in Tasks 1/3/5. ✓
- `FaceIDClient.SendCommand(JObject)` returns `bool` — used as bool in Task 6 step 6.1. ✓
- `UserData` fields used (`UserId`, `FaceId`, `Name`, `Level`, `Gender`, `Role`, `IsActive`, `BluetoothId`, `GazeProfile`) — all present in `UserData.cs`. ✓
- Step enum members `FaceCapture | Name | Level | Gender | Confirm | Done` — used consistently in Task 6 dispatcher and Tasks 7-9 transitions. ✓

**One deliberate red-flag:** Task 6 step 6.1 includes a typo (`MethodInvoke` vs `MethodInvoker`) flagged inline so the executor must fix it before saving. The task succeeds only if the build passes, so the typo can't survive.
