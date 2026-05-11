# Face ID Dual-Login + Marker-Driven Enrollment — Design

**Date:** 2026-05-11
**Scope:** Stages 1–3 of the voice-driven face enrollment scenario. Voice STT (Stage 4) is out of scope for v1; name entry uses marker rotation-spelling instead.
**Status:** Approved by user, awaiting spec review before plan/implementation.

---

## 1. Goals

1. Make face ID a first-class login path, not a fallback. Replace the current sequential "face-first, Bluetooth-after-5s-timeout" flow with parallel scanning, first-match-wins.
2. Add a fully marker- and camera-driven enrollment flow so new players can self-onboard without keyboard or mouse.
3. Keep the existing TUIO + Python-server + WinForms architecture; introduce no new heavyweight dependencies (no Whisper, no AForge, no OpenCVSharp on the C# side).

## 2. Non-goals

- Voice-driven name capture (Whisper / Vosk / Speech SDK). The hooks remain available for v2 but no Python STT server is added.
- Admin enrollment via the wizard. Admins continue to be managed via `users.json` or `ContentManagerPage`.
- Bluetooth pairing during enrollment. New users start with empty `BluetoothId`; admin can populate later.
- Replacing the existing OpenCV LBPH recognizer with something stronger (e.g. dlib / FaceNet). Out of scope.
- Multi-camera support. Single webcam at index 0, same as today.

## 3. Architecture

### 3.1 New files

| File | Purpose | Approx LOC |
|---|---|---|
| `TUIO11_NET-master/EnrollmentPage.cs` | Full-screen WinForms wizard. 5 steps: face capture → name → level → gender → confirm. Implements `TuioListener`. | ~500 |
| `TUIO11_NET-master/DualLoginManager.cs` | Coordinates parallel face + Bluetooth scan with a single `Task<LoginResult> RunAsync(CancellationToken)` API. | ~200 |

### 3.2 Edited files

| File | Change |
|---|---|
| `TUIO11_NET-master/TuioDemo.cs` (HomePage region) | Remove sequential face-then-Bluetooth logic. Call `DualLoginManager.RunAsync` on load. Add marker-10 handler to launch `EnrollmentPage`. |
| `TUIO11_NET-master/FaceIDClient.cs` | Add `SendCommand(JObject)` so C# can issue `reload`, `enroll`, `enroll_cancel`. Receive loop unchanged; new event `OnEnrollResult(JObject)` for enroll replies. |
| `TUIO11_NET-master/FaceID/face_recognition_server.py` | In `handle_client`, parse newline-delimited JSON commands under a `recognizer_lock`. Implement `reload`, `enroll`, `enroll_cancel`. Camera loop takes the lock around `predict`. |
| `TUIO11_NET-master/UserService.cs` | Add `SaveNewUser(UserData)` if not present; ensure atomic write (write `users.json.tmp` then `File.Move(... overwrite:true)`). |

### 3.3 Data layout

- Face images: `Data/face_images/<UserId>/1.jpg` … `5.jpg`. Folder is `UserId` (not display name) so a rename does not orphan images.
- New user record appended to `Data/users.json` with the existing schema. `FaceId` is set to `UserId` (the convention already used for "Shahd"). `BluetoothId` left empty. Default `GazeProfile` all 50.
- `UserId` generation: `usr_<8 random hex chars>`, matching existing pattern.

## 4. Stage 1 — Dual login

### 4.1 Flow

```
HomePage.OnLoad
    ↓
DualLoginManager.RunAsync(ct)
    ├─ Task A: FaceLoginAsync(ct, timeout=8s)
    │     subscribes to FaceIDRouter.OnFaceRecognized
    │     accepts first event with confidence >= 0.75
    │     resolves a UserData via UserService.GetByFaceId
    │
    └─ Task B: BluetoothLoginAsync(ct)
          loops GetCurrentBluetoothId() every 1s
          first non-null known MAC wins
          admin MAC check has priority
    ↓
Task.WhenAny picks winner → cancels the other via shared CTS
    ↓
HUD shows "Welcome, <Name>" + TTS speaks + NavigateByRole(user)
    (admin → AdminDashboardPage; player → LearningPage)
```

### 4.2 Failure modes

- Both tasks finish without a match: HUD displays "Look at the camera or bring your phone closer — or place marker 10 to enroll." Bluetooth task keeps polling silently (user may walk away and back); face task remains idle until marker 10 is placed or the form is closed.
- Face confidence < 0.75 for the full 8s window: face task returns `Failed`. Bluetooth task continues.
- Face server offline: `FaceIDClient.IsConnected == false` → face task returns `Failed` immediately. The HUD warning surfaces this to the user.

### 4.3 Live confidence ticker

While the face task is active, a label below the HomePage hero text shows the most recent confidence reading: `"Scanning... 0.62 → 0.81 ✓ Sara recognized"`. The label updates on every `OnFaceRecognized` callback (even sub-threshold ones — we route those too, see 4.4).

### 4.4 Protocol changes for sub-threshold events

Today the Python server only broadcasts when `distance < CONFIDENCE_THRESHOLD`. For the live ticker we extend the broadcast to **all** face detections, with the server's existing 0-1 confidence value, but add `"matched": true/false`. The C# `FaceIDRouter` invokes:

- `OnFaceRecognized(name, conf)` — when `matched == true` (login decision).
- `OnFaceScanProgress(name_or_null, conf)` — always (UI feedback).

This is additive and doesn't break any existing listener.

## 5. Stage 2 — Face capture

### 5.1 Trigger

Marker 10 placed on HomePage opens `EnrollmentPage` as a modal full-screen form. HomePage continues running in the background; on EnrollmentPage close it resumes dual login. (Marker 10 was chosen because 9–19 is unused on HomePage; marker 21 — the doc's original suggestion — overlaps the existing circular settings menu (21–30).)

### 5.2 UX

1. Wizard opens. Step indicator "1 / 5 — Face Capture" at top.
2. Center: live face-guide rectangle. Instructional text: "Look at the camera. I'll take 5 photos. Turn your head slightly between each."
3. 3-2-1 countdown.
4. C# sends:
   ```json
   {"cmd":"enroll","userId":"usr_<new-8-hex>","count":5,"interval_ms":600}
   ```
5. While waiting, UI shows progress beeps and "Capturing 1/5… 2/5…" (driven by a local timer, not a server status — server reply comes only at the end).
6. Server replies:
   ```json
   {"type":"enroll_done","userId":"usr_…","saved":5}
   ```
   or
   ```json
   {"type":"enroll_failed","userId":"usr_…","reason":"no_face_detected"|"camera_unavailable"|"timeout"}
   ```
7. On `enroll_done`, C# reads the 5 JPGs from disk and displays them as thumbnails.
8. Markers:
   - Marker 4 → accept, advance to Step 2 (name).
   - Marker 5 → retake all (sends `enroll_cancel`, deletes folder, restarts step).
   - Marker 20 → cancel entire wizard (sends `enroll_cancel`, deletes folder, returns to HomePage).

### 5.3 Why server-side capture

Only one process can hold the webcam. The Python server already owns it. C#-side capture would either steal the camera or require running the server with the camera closed during enrollment — both fragile. Server-side capture is the simpler architecture.

## 6. Stage 3 — Marker-driven fields

### 6.1 Name (rotation spelling)

UI: an A–Z strip across the center with one letter highlighted; current accumulated name shown large above it.

Markers (active only while step 2 is current):

| Marker | Action |
|---|---|
| 6 | Rotate to cycle highlight. Clockwise = next letter (A→B→…→Z→A); counter-clockwise = previous. Throttled to one step per ~150° rotation. |
| 4 | Append highlighted letter to name. |
| 5 | Backspace last letter. |
| 7 | Done. Commit name and advance to step 3 (level). Refuses if name is empty. |
| 20 | Cancel wizard (with confirm). |

Max name length 16. Letters are uppercase, matching the existing display style in `users.json`.

### 6.2 Level

Three card buttons. Markers:

| Marker | Level (UI) | Stored in users.json |
|---|---|---|
| 3 | Beginner | `Primary` |
| 4 | Intermediate | `Secondary` |
| 5 | Advanced | `HighSchool` |
| 20 | Back to name step |

### 6.3 Gender

| Marker | Value |
|---|---|
| 3 | `Male` |
| 4 | `Female` |
| 5 | Skip (stored as empty string) |
| 20 | Back to level step |

### 6.4 Confirm

Summary card with first thumbnail, name, level, gender. Markers:

| Marker | Action |
|---|---|
| 7 | Save → atomic write `users.json` → send `reload` → auto-login → navigate to `LearningPage`. |
| 5 | Start over (returns to step 1, deletes face images). |
| 20 | Cancel (deletes face images, returns to HomePage). |

## 7. Server protocol extension

### 7.1 Commands C# → Python (newline-delimited JSON)

```json
{"cmd":"reload"}
{"cmd":"enroll","userId":"usr_…","count":5,"interval_ms":600}
{"cmd":"enroll_cancel","userId":"usr_…"}
```

### 7.2 Replies Python → C# (already newline-delimited JSON)

Existing:
```json
{"type":"face_detected","user_name":"Sara","confidence":0.92}
```

New (also broadcast on every detect for the ticker):
```json
{"type":"face_scan","user_name":"Sara","confidence":0.62,"matched":false}
```

New enroll replies:
```json
{"type":"enroll_done","userId":"usr_…","saved":5}
{"type":"enroll_failed","userId":"usr_…","reason":"no_face_detected"}
```

### 7.3 Concurrency

A `threading.Lock` (`recognizer_lock`) guards `face_recognizer` and `label_map`. The camera loop acquires it for the duration of one predict call. The command handler acquires it only when swapping in a new recognizer after `load_faces()`. Enroll capture acquires the camera by reading from the same `cv2.VideoCapture` already owned by the camera loop — to avoid two-thread contention, the loop yields the camera during enrollment by polling an `enroll_in_progress` flag and sleeping while it's set. Capture frames are saved, then the flag clears and the loop resumes.

## 8. Auto-login & users.json write

1. C# `UserService.SaveNewUser(UserData)` writes `users.json.tmp` (full file rewrite) then `File.Move(tmp, real, overwrite: true)`.
2. C# sends `{"cmd":"reload"}` as a safety net (server may already have reloaded after enroll).
3. EnrollmentPage closes.
4. HomePage `DualLoginManager` is bypassed for this turn — we already have the new user object — and `NavigateByRole(newUser)` runs directly.

## 9. Error handling

| Failure mode | Behavior |
|---|---|
| Face server not connected when marker 10 placed | Toast: "Face server not running. Start face_recognition_server.py and try again." Wizard does not open. |
| `enroll_done` not received within 15 s | Treat as `enroll_failed` with `reason: timeout`. Offer retake. |
| `enroll_failed` reason `no_face_detected` | "I couldn't see your face. Step closer to the camera and try again." Retake. |
| `enroll_failed` reason `camera_unavailable` | "Camera is busy or unplugged." Cancel back to HomePage. |
| Display-name collision (another user shares the name) | "There's already a Sara — continue anyway? Marker 4 yes, 5 retype name." UserId differs regardless so this is purely a UX disambiguation. |
| `users.json` write failure (IO error) | Face images remain on disk. Show error, back to confirm step so user can retry. |
| Mid-wizard cancel (marker 20) | Send `enroll_cancel`, delete `Data/face_images/<UserId>/`, no users.json change. |
| Confidence < 0.75 for 8 s in dual login | Face task returns Failed. Bluetooth keeps polling. HUD updates instructions. |
| Bluetooth scan exception | Logged, BT task returns Failed. Face task continues independently. |

## 10. Testing & verification

### 10.1 What I (Claude) will verify

- C# code compiles under MSBuild against `TUIO_DEMO.csproj`.
- Python module imports cleanly under Python 3.x with `opencv-python opencv-contrib-python numpy`.
- Static review: no obvious null derefs, no unawaited tasks in dual login, atomic write path correct.
- Marker IDs do not collide with existing handlers.

### 10.2 What you (user) will need to verify live

- Face server starts, camera opens, existing face login still works (regression).
- HomePage shows dual-login UI; face match and Bluetooth match both succeed.
- Confidence ticker updates as expected.
- Enrollment wizard launches on marker 10.
- All 5 photos captured and saved under the new UserId folder.
- Name spelling, level, gender steps each respond to the assigned markers.
- New user appears in `users.json` with all fields populated.
- Server `reload` command triggers retraining; new user is recognized on the next scan.
- Marker 20 cancel cleanly deletes partial state.

### 10.3 Manual smoke checklist for the deliverable

1. Backup `users.json` before testing.
2. Start `python face_recognition_server.py`.
3. Run `bin/Debug/TuioDemo.exe`.
4. Confirm dual-login HUD appears.
5. Confirm existing user (Shahd) still recognized.
6. Place marker 10 → wizard opens.
7. Walk through all 5 steps; create user "TEST".
8. Confirm `users.json` contains "TEST" with new UserId.
9. Confirm `Data/face_images/<UserId>/1.jpg…5.jpg` exist.
10. Close app, restart, log in as TEST via face → success.
11. Place marker 10 again → wizard opens, cancel via marker 20 → no residue.

## 11. Open questions / known limitations

- LBPH confidence is approximate; the 0.75 threshold may need empirical tuning after live testing.
- During enrollment capture, the live face detection loop pauses for ~3 seconds. A second user trying to log in at exactly that moment would see no response. Acceptable for a single-station demo.
- The wizard does not collect a Bluetooth pairing. A new user is face-login-only until an admin adds their MAC.
- Name field is uppercase ASCII A–Z only. Diacritics, spaces, hyphens not supported in v1.
- Marker 10 is hardcoded as the enrollment trigger. If a future change reassigns 10 elsewhere, this must be updated.
