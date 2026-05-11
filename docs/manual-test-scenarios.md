# Manual Test Scenarios — Face ID + Gaze + Hand Gestures over TUIO

How to verify the dual-login race, marker-driven enrollment, gaze-session adaptation, **and the new hand-gesture parallel input path** end-to-end using a real TUIO surface (reacTIVision + webcam).

Hand gestures are an **additional** input — TUIO markers always work too. Use whichever you want (or both) and compare the experience at the end.

---

## Prerequisites

Run these once before any scenario.

| Component | Command | Port |
|---|---|---|
| reacTIVision (or the TUIO simulator) | `reacTIVision.exe` (Windows) — sends fiducial markers via OSC | 3333 → 127.0.0.1 |
| Face / gaze / gesture webcam | Plug in + close any app that may be holding the camera | n/a |
| Face server | `python TUIO11_NET-master\FaceID\face_recognition_server.py` | 5001 |
| Gaze server | `python TUIO11_NET-master\FaceID\gaze_tracking_server.py` | 5002 |
| **Gesture server** (new) | `python TUIO11_NET-master\FaceID\gesture_recognition_server.py` | **5000** |
| YOLO vision server (optional) | `python TUIO11_NET-master\FaceID\yolo_tracking_server.py` | 5003 |
| Emotion server (optional) | `python TUIO11_NET-master\mock_emotion_server.py` | 5005 |
| Main app | `TUIO11_NET-master\bin\Debug\TuioDemo.exe` | listens on TUIO 3333 |

**Gesture server first-time install (verified working on Python 3.12):**
```
pip install dollarpy "mediapipe==0.10.13" opencv-python numpy
```
**Why the version pin:** MediaPipe ≥ 0.10.30 removed the legacy `solutions.pose` API that the Skelaton trainer (and this server) use. MediaPipe is officially Python 3.9–3.12; on 3.13/3.14 there are no wheels at all.

**⚠ Camera conflict — only one camera-using server can run at a time on a single webcam:**

All four Python servers (`face_recognition_server`, `gaze_tracking_server`, `gesture_recognition_server`, `yolo_tracking_server`) try to open camera index `0`. On Windows only one process holds the webcam at a time, so simultaneously running them with a single camera will leave all but the first with "cannot open camera" errors.

Workarounds:
- For a demo, pick the two you need most (face + gesture is the common pair) and skip the others.
- With two physical webcams: edit `CAMERA_INDEX = 0` to `1` in one of the server files.
- Sharing camera between servers would need a separate frame-broker — out of scope here.

**Before each scenario:**
- Back up `Data\users.json` and `Data\gaze_reports\` (in case you want to roll back).
- Make sure at least one user exists in `Data\users.json` with a face folder under `Data\face_images\<userId>\`.

---

## The two input vocabularies, side by side

### TUIO markers (physical fiducials on the surface)

| Marker | Where | Action |
|---|---|---|
| 10 | HomePage | Open enrollment wizard |
| 20 | Almost every page | Cancel / Back / Close |
| 3 / 4 / 5 | Various | Step-specific picks (e.g. Beginner / Intermediate / Advanced) |
| 6 | Enrollment step 2 + LessonPage | Rotation = cycle items |
| 7 | Enrollment step 2 + step 5 | Done / Save / Advance |
| 21–30 | HomePage | Circular settings menu (Display/Audio/System) |

### Hand gestures (recognised by `gesture_recognition_server.py` via MediaPipe + dollarpy)

| Gesture | How to perform it in front of the webcam |
|---|---|
| **Circle** | Draw a full circle in the air with one arm raised; both LA (left-arm) and RA (right-arm) variants are trained, either works |
| **Checkmark** | Trace a check (✓): short down-stroke then a longer up-stroke to the right |
| **SwipeLeft** | Sweep one arm across your body, right → left |
| **SwipeRight** | Sweep one arm across your body, left → right |

The recogniser fires at most once every ~1.6 seconds per gesture (cooldown). It expects a clear ~2-second stroke; jittering won't trigger.

### Universal gesture → action mapping

When a page hasn't subscribed for context-specific gesture handling, gestures fall back to a simulated TUIO marker:

| Gesture | Default marker | Effect (legacy fallback) |
|---|---|---|
| **Circle** | marker 10 | Triggers the HomePage enroll wizard (only meaningful on HomePage) |
| **Checkmark** | marker 4 | Universal "confirm / pick / keep" |
| **SwipeRight** | marker 7 | Universal "next / advance / done" |
| **SwipeLeft** | marker 20 | Universal "back / cancel" |

Page-specific contextual mappings (EnrollmentPage, LessonPage, Quiz, Spelling) are listed inside each scenario below.

---

## Scenario A — Dual login: face wins

**Goal:** verify face login completes within ~8s and the HUD confidence ticker updates live.

1. Start the face server. Watch its console — you should see `[Server] Trained recognizer with N image(s) from M player(s)`.
2. Start the app. Within 1s the HomePage HUD shows the pulsing scan ring with status "Scanning...".
3. Sit facing the camera. The HUD sub-label should change frame by frame:
   ```
   Scanning... (0.34)         ← first sub-threshold scan
   Scanning... (0.61)
   Recognising Shahd (0.81)   ← matched=true
   ```
4. Within ~8s, when confidence ≥ 0.75, the HUD turns green: `Welcome, <Name>!`. TTS speaks the greeting.
5. After 2s the app navigates to LearningPage.

This scenario uses **no markers and no gestures** — login is passive.

**Pass:** confidence ticks up, log-in completes in <8s, navigation to LearningPage (Players) or AdminDashboardPage (Admin role).

---

## Scenario B — Dual login: Bluetooth wins

**Goal:** verify a paired phone logs the user in before the face task finishes.

1. Pair your phone to Windows Bluetooth. Its MAC must match a `BluetoothId` in `users.json` (or admin MAC `E8:3A:12:40:1A:70` to land on AdminDashboard).
2. Start the app. Bluetooth task runs in parallel with face task.
3. Within ~1–2s the HUD jumps to `Welcome, <Name>!` with `Bluetooth login` in the sub-label.
4. App navigates to LearningPage (or AdminDashboard).

Again no markers/gestures — this is automatic.

**Pass:** Bluetooth wins without waiting for face timeout. HUD says `Bluetooth`, not `Face`.

---

## Scenario C — New player enrolment (TUIO **and** gesture paths)

**Goal:** verify the full 5-step wizard saves a usable new player, via either input method.

The wizard accepts both inputs concurrently — you can mix and match within one run (e.g. open the wizard with a gesture, type the name via swipes, save with a marker).

### Step 0 — open the wizard
- **TUIO:** place **marker 10** on the surface.
- **Gesture:** perform **Circle**.

HomePage hides, the wizard opens at "1 / 5 — Face Capture".

### Step 1 — Face Capture
1. 3-2-1 countdown → `Capturing 5 photos…` → ~3.5s → `Captured 5 photos. Marker 4 = keep • 5 = retake • 20 = cancel.` Five thumbnails appear.
2. Choose:

| Action | TUIO | Gesture |
|---|---|---|
| Keep photos, advance to step 2 | marker **4** | **Checkmark** |
| Retake all 5 photos | marker **5** | **SwipeRight** |
| Cancel the whole wizard | marker **20** | **SwipeLeft** *or* **Circle** |

### Step 2 — Name (rotation spelling)
A `_` placeholder over an A–Z strip with `[A]` highlighted.

| Action | TUIO | Gesture |
|---|---|---|
| Cycle highlight to next letter | rotate **marker 6** clockwise (~18°/letter) | **SwipeRight** |
| Cycle highlight to previous letter | rotate **marker 6** counter-clockwise | **SwipeLeft** |
| Commit the highlighted letter to the name | place **marker 4** | **Checkmark** |
| Backspace last letter | place **marker 5** | *(no gesture — use marker 5)* |
| Done — name is complete, advance to step 3 | place **marker 7** | **Circle** |
| Cancel the whole wizard | place **marker 20** | *(no gesture in this step — use marker 20)* |

Spelling "TESTER" with gestures: SwipeRight×19 (A→T), Checkmark, SwipeRight×... etc. Slow but works.

### Step 3 — Level

| Action | TUIO | Gesture |
|---|---|---|
| Beginner | marker **3** | **SwipeLeft** |
| Intermediate | marker **4** | **Checkmark** |
| Advanced | marker **5** | **SwipeRight** |
| Cancel | marker **20** | **Circle** |

### Step 4 — Gender (skippable)

| Action | TUIO | Gesture |
|---|---|---|
| Male | marker **3** | **SwipeLeft** |
| Female | marker **4** | **Checkmark** |
| Skip | marker **5** | **SwipeRight** |
| Cancel | marker **20** | **Circle** |

### Step 5 — Confirm
Summary shows name + level + gender.

| Action | TUIO | Gesture |
|---|---|---|
| Save and auto-login the new user | marker **7** | **Checkmark** |
| Start over (clear all, back to step 1) | marker **5** | **SwipeLeft** |
| Cancel without saving | marker **20** | **Circle** |

### Verify on disk
- `Data\users.json` has a new entry with `UserId == usr_<8 hex>`, plus correct `Name` / `Level` / `Gender`.
- `Data\face_images\<UserId>\1.jpg … 5.jpg` exist.
- `Data\users.json.bak` exists (atomic save's previous version).
- Restart the app → log in as the new user via face → succeeds without restarting the Python servers.

---

## Scenario D — Cancel mid-enrolment

1. Open the wizard (marker 10 or Circle gesture). Capture photos through step 1.
2. At any step, cancel:
   - **TUIO:** marker **20**
   - **Gesture:** **SwipeLeft** at step 1 or **Circle** at any other step
3. Wizard closes immediately. HomePage shows again and dual-login restarts.

**Pass:**
- `Data\face_images\<UserId>\` is deleted (no orphan photos).
- `Data\users.json` is unchanged.
- Server console shows `[Server] Command: enroll_cancel user=usr_<id>`.

---

## Scenario E — Gaze session report is written

**Goal:** verify the gaze session lands in `Data\gaze_reports\<userId>_history.json` when LearningPage closes.

1. Log in as any user (face or BT).
2. On LearningPage, stare at each of the six cards for 5–10s. Cards are arranged:
   ```
   ┌────────────┬────────────┬────────────┐
   │  Strokes   │   Rules    │  Practice  │
   ├────────────┼────────────┼────────────┤
   │   Quiz     │  Spelling  │ Competition│
   └────────────┴────────────┴────────────┘
   ```
3. Stare deliberately longer at **one** specific card (say `Rules`) for 20+s.
4. Close LearningPage:
   - **TUIO:** marker **20**
   - **Gesture:** **SwipeLeft** (Circle does NOT close LearningPage — Circle on HomePage opens enroll; LearningPage's cancel is SwipeLeft via universal-marker-20 mapping)
5. Open `Data\gaze_reports\<UserId>_history.json`.

**Pass:**
- New entry timestamped now.
- `DurationSeconds` ≈ wall-clock session time.
- `CardDwellTimes` non-zero for the cards you looked at.
- `DominantCategory` is whichever card you stared at most.

---

## Scenario F — "Picked up from last time" highlight cycle

**Goal:** verify each session reads the previous one and highlights the most-used card.

1. Run Scenario E once, focusing on `Rules`. Confirm the report ends with `"DominantCategory": "Rules"`.
2. Log in again **as the exact same user** (face or BT).
3. Within ~1s of LearningPage opening, the `Rules` card glows gold with a "Picked up from last time!" ribbon.
4. Close (marker 20 or SwipeLeft) and re-run focusing on `Quiz`. Next session, `Quiz` is highlighted instead.

**Pass:** the highlighted card matches the latest report's `DominantCategory` and changes every session.

---

## Scenario G — Per-user isolation

1. Log in as **User A**, focus on `Strokes`, close.
2. Log in as **User B**, focus on `Competition`, close.
3. Log back in as A → `Strokes` glows. Log back in as B → `Competition` glows.

**Pass:** `Data\gaze_reports\` has two files, each per-user. No cross-contamination.

---

## Scenario H — LessonPage navigation (cycling padel terms)

**Goal:** verify the lesson page accepts both rotation and swipes for cycling through padel terms.

1. From LearningPage, open Padel Shots (Beginner level):
   - **TUIO:** place **marker 3** to open Beginner level → place **marker 3** again to open Padel Shots.
   - **Gesture:** the universal map fires marker 4 on Checkmark — opens Padel Rules instead. (No gesture for "marker 3" specifically. Use TUIO for level/category selection here.)
2. Once a LessonPage is open, cycle terms:

| Action | TUIO | Gesture |
|---|---|---|
| Next padel term | rotate **marker 6** clockwise | **SwipeRight** |
| Previous padel term | rotate **marker 6** counter-clockwise | **SwipeLeft** |
| Replay the term's voice | (use keyboard Space, or wait for auto-replay) | **Checkmark** |
| Close LessonPage | place **marker 20** | **Circle** |

**Pass:** swipes advance terms one at a time matching the rotation behaviour.

---

## Scenario I — Quiz / Speed Mode

**Goal:** verify the quiz pages let you pick A / B / C answers via gesture.

1. From LearningPage open Quick Challenge (Quiz) or Speed Mode (Spelling) via markers 6 / 7 (or gesture **SwipeRight** which fires marker 7 = Speed Mode).
2. When a question shows three options (A, B, C):

| Action | TUIO | Gesture |
|---|---|---|
| Pick option **A** (leftmost) | marker **10** | **SwipeLeft** |
| Pick option **B** (middle) | marker **11** | **Checkmark** |
| Pick option **C** (rightmost) | marker **12** | **SwipeRight** |
| Close the quiz | marker **20** | **Circle** |

**Pass:** the picked option is highlighted and scored exactly as with markers.

---

## Scenario J — Live confidence ticker (HCI rubric, face only)

While on HomePage, watch the HUD sub-label as you:
- Look directly at the camera → ticker climbs to 0.65–0.85.
- Cover your face → ticker freezes / drops.
- Show a non-enrolled face → 0.30–0.55 (sub-threshold, no login).
- Show your enrolled face → climbs >0.75 and you log in.

**Pass:** number visibly updates each frame.

---

## Scenario K — Gesture recogniser sanity check (do this once before C / H / I)

**Goal:** verify the gesture server is actually firing events.

1. Start `gesture_recognition_server.py`. Console should print:
   ```
   [GestureServer] Loaded recogniser with 7 templates
   [GestureServer] TCP listening on 127.0.0.1:5000
   [GestureServer] Camera open. Watching for gestures...
   ```
2. Start the app. App console should print `[GestureClient] Connected!`.
3. Stand back from the webcam so your full upper body is visible. Perform each of the four gestures slowly and deliberately.
4. The app console should print one line per recognition:
   ```
   [GestureClient] Circle (0.78) -> marker 10
   [GestureClient] Checkmark (0.81) -> marker 4
   [GestureClient] SwipeRight (0.72) -> marker 7
   [GestureClient] SwipeLeft (0.69) -> marker 20
   ```
5. The server console should print the raw recognition + canonicalisation:
   ```
   [GestureServer] LACheckmark -> Checkmark  score=0.81
   ```

**Pass:** each gesture lands on the correct canonical name with score ≥ 0.55. Misfires (e.g. random arm movement triggering a Circle) are rare.

**Tune if needed:** open `gesture_recognition_server.py` and adjust:
- `MIN_SCORE = 0.55` — raise to be stricter (fewer false positives, more genuine gestures rejected); lower to be looser.
- `MIN_MOTION_PX = 80.0` — raise so a still pose doesn't trigger; lower for smaller gestures.
- `COOLDOWN_SECONDS = 1.6` — raise to debounce more aggressively.

---

## Cleanup after testing

1. Restore `Data\users.json` from backup if you don't want to keep the test user.
2. Delete `Data\face_images\<test-UserId>\` and the matching `Data\gaze_reports\<test-UserId>_history.json`.
3. Optional: delete `Data\users.json.bak`.

---

## TUIO vs Hand Gestures — what to compare

Once you've run the same scenario both ways, the comparison axes are:

| Axis | TUIO markers | Hand gestures |
|---|---|---|
| **Latency** | ~100ms (marker placement → event) | ~1.5–2s (full stroke + recognition + cooldown) |
| **Precision** | exact, deterministic — marker 4 is always marker 4 | probabilistic — score is 0–1, some misfires |
| **Setup cost** | print fiducial sheet + reacTIVision running | none (just webcam) |
| **Continuous input** | rotation is natural (marker 6) | discrete only — must swipe N times to cycle N steps |
| **Range of vocabulary** | 30+ unique markers possible | only the 4 trained classes |
| **Hands-free** | requires hands on the surface | requires hands but no contact |
| **Discoverability** | each marker is a separate physical object | learner has to know each gesture |
| **Failure mode** | marker not seen by camera = no event | wrong gesture matched = wrong action |

The gesture path is best for **simple yes/no/next/back** decisions and for users who don't have the marker set physically with them. TUIO remains better for precision, continuous input (rotation), and rich vocabulary (>4 actions on the same page).

---

## Known limitations

- LBPH face confidence is conservative. Dim lighting or pose changes can hold a known face below the 0.75 threshold — re-enrol if needed.
- Gaze tracking accuracy depends on the gaze server's calibration.
- The face server pauses recognition for ~3s during an `enroll` command — a second user trying to face-login mid-enrolment won't get a response until enrolment finishes.
- Hand gestures require ~2-second strokes with clear arm motion; tiny hand-only gestures don't trigger (MediaPipe Pose tracks the whole body, including shoulder + elbow + wrist).
- Only the four canonical gestures are recognised; anything outside that set (a wave, a fist, etc.) is silently ignored.
- The `Skelaton/DynamicPatternsPadel/` set (Forehand / Backhand / Volleys) is **not** wired into this UI — it's coaching content for the AI Vision Coach page, not navigation.
