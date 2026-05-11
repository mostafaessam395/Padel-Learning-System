# Manual Test Scenarios — Face ID + Gaze Tracking

How to verify the dual-login race, marker-driven enrollment, and gaze-session adaptation end-to-end using a real TUIO surface (reacTIVision + webcam).

---

## Prerequisites

Run these once before any scenario.

| Component | Command | Port |
|---|---|---|
| reacTIVision (or the TUIO simulator) | `reacTIVision.exe` (Windows) — sends fiducial markers via OSC | 3333 → 127.0.0.1 |
| Face / gaze webcam | Plug in + close any app that may be holding the camera | n/a |
| Face server | `python TUIO11_NET-master\FaceID\face_recognition_server.py` | 5001 |
| Gaze server | `python TUIO11_NET-master\FaceID\gaze_tracking_server.py` | 5002 |
| Gesture server (optional) | `python TUIO11_NET-master\FaceID\yolo_tracking_server.py` | 5003 |
| Emotion server (optional) | `python TUIO11_NET-master\mock_emotion_server.py` | 5005 |
| Main app | `TUIO11_NET-master\bin\Debug\TuioDemo.exe` | listens on TUIO 3333 |

**Before each scenario:**
- Back up `Data\users.json` and `Data\gaze_reports\` (in case you want to roll back).
- Make sure at least one user exists in `Data\users.json` with a face folder under `Data\face_images\<userId>\`.

**Marker map (the ones you'll actually print):**

| Marker | Where | Action |
|---|---|---|
| 10 | HomePage | Open the enrollment wizard |
| 20 | Any wizard step | Cancel wizard |
| 3 / 4 / 5 | Various | Step-specific picks |
| 6 | Enrollment step 2 + lesson pages | Rotation = cycle |
| 7 | Enrollment step 2 + step 5 | Done / save |
| 21–30 | HomePage | Circular settings menu (Display/Audio/System) — do NOT use for enrollment |

---

## Scenario A — Dual login: face wins

**Goal:** verify that face login completes within ~8s and that the HUD confidence ticker updates live.

1. Start the face server. Watch its console — you should see `[Server] Trained recognizer with N image(s) from M player(s)`.
2. Start the app. Within 1s the HomePage should show:
   - Top-right HUD: status "Scanning..." over the pulsing scan ring.
   - Footer: "Scanning for player...".
3. Sit facing the camera. After 1–2 frames the HUD sub-label should change to:
   ```
   Scanning... (0.34)        ← first sub-threshold scan
   Scanning... (0.61)
   Recognising Shahd (0.81)  ← when matched=true
   ```
4. Within ~8s, when a frame has confidence ≥ 0.75, the HUD turns green and shows `Welcome, <Name>!`. TTS says "Welcome, X. Loading … Padel training."
5. After 2s the app navigates to LearningPage.

**Pass criteria:**
- The numeric confidence appears in the HUD and visibly changes each frame.
- A known face triggers navigation in under 8s.
- An unknown face never triggers navigation (confidence stays below 0.75).

**Failure modes to look for:**
- HUD frozen at `Scanning...` with no ticker → C# isn't getting `face_scan` events → check `[FaceIDClient] Reply: face_scan ...` lines on the app's console; if absent, the server is sending the old protocol or isn't running.
- App jumps to LearningPage with name = "Unknown" → `face_detected` came through but no users.json match by Name/FaceId/UserId.

---

## Scenario B — Dual login: Bluetooth wins

**Goal:** verify that an already-paired phone logs the user in before the face task finishes.

1. Make sure your phone is paired to the Windows Bluetooth stack and its MAC matches a `BluetoothId` in `users.json` (or matches the admin MAC `E8:3A:12:40:1A:70` to land on AdminDashboard).
2. Start the app. While the face HUD is still scanning, the Bluetooth task is running silently in parallel.
3. Within ~1–2s of the BT scan picking up your device, the HUD jumps straight to `Welcome, <Name>!` with `Bluetooth login` in the sub-label.
4. App navigates to LearningPage (or AdminDashboard for the admin MAC).

**Pass criteria:**
- Bluetooth login wins **without waiting** for the face task to time out.
- The HUD's `Source` text says `Bluetooth`, not `Face`.

---

## Scenario C — New player enrolment (marker-driven)

**Goal:** verify the full 5-step wizard saves a usable new player.

1. From HomePage HUD with no login yet, place **marker 10** on the surface.
2. The app should hide HomePage and open the Enrollment wizard ("1 / 5 — Face Capture").
3. **Step 1 (Face Capture):**
   - 3-2-1 countdown then `Capturing 5 photos…`.
   - After ~3.5s the message changes to `Captured 5 photos. Marker 4 = keep • 5 = retake • 20 = cancel.` Five thumbnails appear below.
   - Place **marker 4** → advance to step 2.
4. **Step 2 (Name):**
   - `_` placeholder above an A-Z strip with `[A]` highlighted.
   - **Rotate marker 6** clockwise → highlight cycles A → B → C → …
   - Place **marker 4** to commit the current letter (appears in the name above).
   - Place **marker 5** to backspace if needed.
   - Place **marker 7** when the name has at least one letter → advance to step 3.
5. **Step 3 (Level):** place marker **3** (Beginner), **4** (Intermediate), or **5** (Advanced) → step 4.
6. **Step 4 (Gender):** place marker **3** (Male), **4** (Female), or **5** (Skip) → step 5.
7. **Step 5 (Confirm):** summary shows name + level + gender. Place **marker 7** to save.
8. Within 1.5s the wizard closes and HomePage shows the welcome HUD with the new user, then navigates to LearningPage.

**Pass criteria (verify on disk):**
- `Data\users.json` has a new entry whose `UserId` matches `usr_<8 hex>` and whose `Name`, `Level`, `Gender` match what you entered.
- `Data\face_images\<UserId>\1.jpg ... 5.jpg` exist.
- `Data\users.json.bak` exists (atomic save's previous version).
- Close the app, restart it, the new user should be recognised via face within ~8s (server reloaded after enroll_done).

**Failure modes to check:**
- "Camera timed out" message → server didn't get any face into `latest_frame` within 15s; check the server console for `[Server] Enroll <id>: saved …` lines. If zero saves, your face wasn't detected — try better lighting.
- Wizard saves but the name is empty → bug in step 2 commit (marker 4 not registering); confirm marker IDs on your prints match.

---

## Scenario D — Cancel mid-enrolment

1. Open the wizard (marker 10). Capture photos through step 1.
2. At any point, place **marker 20**.
3. Wizard closes immediately. HomePage shows again and dual-login restarts.

**Pass criteria:**
- `Data\face_images\<UserId>\` directory is deleted (no orphan photos for the abandoned `UserId`).
- `Data\users.json` is unchanged.
- Server console shows `[Server] Command: enroll_cancel user=usr_<id>`.

---

## Scenario E — Gaze session report is written

**Goal:** verify a fresh gaze session lands in `Data\gaze_reports\<userId>_history.json` when LearningPage closes.

1. Log in as any user (face or BT).
2. On LearningPage, deliberately stare at each of the six cards for 5–10 seconds each. Keep gaze inside the card's screen region; the regions are roughly:
   - Top row: Strokes (left) · Rules (middle) · Practice (right)
   - Bottom row: Quiz (left) · Spelling (middle) · Competition (right)
3. Place **marker 20** to close LearningPage and return to HomePage.
4. Open `Data\gaze_reports\<UserId>_history.json` in a text editor.

**Pass criteria:**
- File exists.
- An entry is appended whose `Timestamp` matches the session you just ran.
- `DurationSeconds` ≈ wall-clock time you spent on LearningPage.
- `CardDwellTimes` has non-zero values for whichever cards you looked at.
- `SessionScores` is a six-key dictionary with 0–100 values.
- `DominantCategory` is whichever card you stared at most.

**If `CardDwellTimes` is all zeros:**
- `TotalFixations` likely also zero → no gaze points came in. Check:
  - Gaze server console for `[GazeServer] gaze x=… y=…` style log.
  - C# console for `[GazeClient] Connected!`.
  - If both look healthy but no points arrive, the server may be sending events in a format the C# client doesn't recognise. Expected payload is `{"type":"gaze","x":0.45,"y":0.62}` with coordinates in `[0, 1]`.

---

## Scenario F — Next-session "Picked up from last time" highlight

**Goal:** verify the system actually reads the previous session's report and highlights the card the user used most.

1. Run Scenario E once. Let's say you stared at `Rules` the most → its `DominantCategory` is `Rules`.
2. Close LearningPage. Make sure the report was written (check the JSON timestamp).
3. Log in again **as the same user**. Open LearningPage.
4. Inside ~1s after LearningPage opens, the `Rules` card should:
   - Get a gentle pulsing gold border.
   - Show a **"Picked up from last time!"** ribbon in its top-right corner.
5. Other cards keep their normal classification (Neglected ones still glow teal with "New for you!", UnderFocused ones get the orange "Try this!" outline).

**Pass criteria:**
- The highlighted card matches the `DominantCategory` field in the *last* report file.
- The ribbon text is literally `Picked up from last time!`.
- The highlight changes each session — i.e. if you spend the next session staring at `Quiz` instead, the **following** session highlights `Quiz`, not `Rules`.

**Quick way to force a specific dominant:** stare at one card for the whole 30 seconds, then close. Next session that card gets the gold ribbon regardless of cumulative history.

---

## Scenario G — Per-user isolation

**Goal:** make sure user A's history doesn't bleed into user B.

1. Log in as user A. Run a session focused on `Strokes`. Close.
2. Log in as user B (different face / BT). Run a session focused on `Competition`. Close.
3. Log back in as user A → `Strokes` should be highlighted (not `Competition`).
4. Log in as user B → `Competition` should be highlighted (not `Strokes`).

**Pass criteria:**
- `Data\gaze_reports\` has two separate `<UserId>_history.json` files, one per user.
- Each one only contains that user's sessions.
- The highlighted card matches each user's own last `DominantCategory`.

This is the key test for the by-`UserId` fix in `AnalyticsEngine.PersistUsers`.

---

## Scenario H — Server reload after enrolment

**Goal:** verify a freshly enrolled face is recognised on the very next login, without restarting the Python server.

1. Run Scenario C to enrol a new user "TESTER".
2. The wizard's confirm step sends a `reload` command after save. The server console should print:
   ```
   [Server] Command: reload
   [Server] Trained recognizer with N image(s) from M+1 player(s)
   ```
3. Log out (close LearningPage → marker 20 from inside it).
4. Sit in front of the camera as "TESTER". Within ~8s the HUD should match `TESTER`.

**Pass criteria:**
- No need to restart `face_recognition_server.py` between enrolment and the first auto-login of the new user.

---

## Scenario I — Live confidence ticker (HCI rubric)

**Goal:** the system shows transparent feedback on what it sees.

1. Log out (return to HomePage).
2. Watch the HUD sub-label as you do these things in front of the camera:
   - Look directly at the camera → `Scanning… (0.65–0.85)`.
   - Cover your face → numbers either freeze or drop because no face is detected.
   - Show a face that's NOT in the trained set → `Scanning… (0.30–0.55)` (sub-threshold so no login).
   - Show your own face → climbs toward 0.8+ and you're logged in.

**Pass criteria:**
- The confidence number actually updates between frames.
- The number drops noticeably when you cover or turn away.

---

## Cleanup after testing

1. Restore `Data\users.json` from the backup if you don't want to keep the test user.
2. Delete `Data\face_images\<test-UserId>\` and the corresponding `Data\gaze_reports\<test-UserId>_history.json` if you want a clean slate.
3. Optional: delete `Data\users.json.bak` (created by the atomic save).

---

## Known limitations during testing

- The OpenCV LBPH recogniser is conservative. If your test environment has dim lighting or the original enrolled reference photo was taken in different lighting, confidence may stay just below the 0.75 threshold. Re-enrol if needed.
- Gaze tracking accuracy depends on the Python gaze server's calibration. Wildly off-center gaze points will be detected but mapped to whichever region they fall into, which can throw off `DominantCategory`.
- The face server pauses recognition for ~3s during an `enroll` command — if a second user walks up and tries to face-login mid-enrolment they'll see no response until enrolment finishes.
- During Scenario E, `marker 20` is the only correct way to end the session. Closing via Alt+F4 / window X still triggers `OnFormClosed` and persists the report, but make sure your TUIO surface isn't generating phantom markers during shutdown.
