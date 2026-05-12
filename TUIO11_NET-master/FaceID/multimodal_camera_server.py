"""
Multimodal Camera Server — face recognition + hand gestures from ONE webcam.
============================================================================

Single-cam machines can only run one cv2.VideoCapture at a time, so the
separate face_recognition_server (5001) and gesture_recognition_server
(5000) used to be mutually exclusive. This file fuses both into one
process: one camera read loop feeds both the LBPH face recogniser AND
the MediaPipe-Pose + dollarpy gesture recogniser, and spins up TWO TCP
listeners (one per port) so the C# client doesn't have to change anything.

Run instead of starting face/gesture servers separately:
    pip install dollarpy "mediapipe==0.10.13" opencv-python opencv-contrib-python numpy
    python multimodal_camera_server.py
"""

import json
import os
import socket
import sys
import threading
import time
from collections import deque

# ─── Configuration ────────────────────────────────────────────────────
HOST = "127.0.0.1"
FACE_PORT = 5001
GESTURE_PORT = 5000
CAMERA_INDEX = 0

# Face recognition
FACE_DIST_THRESHOLD = 70.0     # LBPH: lower distance = better match
FACE_SCAN_INTERVAL = 0.8       # seconds between face scans
FACE_COOLDOWN = 5.0            # do not re-fire same match for N seconds

# Gesture recognition — thresholds tuned permissive so demo bodies/lighting pass
STROKE_WINDOW_FRAMES = 60
MIN_MOTION_PX = 50.0           # was 80 — allow smaller arm movements
MIN_GESTURE_SCORE = 0.35       # was 0.55 — allow looser shape matches
GESTURE_COOLDOWN_S = 1.6
GESTURE_SCAN_HZ = 4.0

# Auto-enrolment — if a face is detected but doesn't match any known
# player for this many consecutive face scans, the server enrols them
# automatically (5 photos, fresh UserId, default profile, users.json append).
AUTO_ENROLL_UNKNOWN_THRESHOLD_DIST = 85.0   # LBPH distance above which we consider it "definitely unknown"
AUTO_ENROLL_CONSECUTIVE_SCANS     = 5      # ~4 seconds at 0.8s scan interval
AUTO_ENROLL_COOLDOWN_S            = 30.0   # after one auto-enrol, ignore "unknown" for N seconds

CANONICAL = {
    "LACheckmark":  "Checkmark",
    "RACheckmark":  "Checkmark",
    "LASwipeLeft":  "SwipeLeft",
    "RASwipeLeft":  "SwipeLeft",
    "LASwipeRight": "SwipeRight",
    "RASwipeRight": "SwipeRight",
    "Circle":       "Circle",
}

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
FACES_DIR = os.path.join(SCRIPT_DIR, "..", "Data", "face_images")
USERS_JSON_PATH = os.path.join(SCRIPT_DIR, "..", "Data", "users.json")
USERS_JSON_BIN_DEBUG = os.path.join(SCRIPT_DIR, "..", "bin", "Debug", "Data", "users.json")

# ─── Deps ─────────────────────────────────────────────────────────────
try:
    import cv2
    import numpy as np
except ImportError as e:
    print(f"[Multi] missing dep: {e} — pip install opencv-python opencv-contrib-python numpy")
    sys.exit(1)

try:
    from mediapipe.python.solutions import pose as mp_pose
except ImportError as e:
    print(f"[Multi] mediapipe legacy solutions.pose missing: {e}")
    print("[Multi] Pin: pip install \"mediapipe==0.10.13\" --force-reinstall")
    sys.exit(1)

try:
    from dollarpy import Recognizer, Template, Point
except ImportError:
    print("[Multi] missing dollarpy — pip install dollarpy")
    sys.exit(1)

sys.path.insert(0, SCRIPT_DIR)
try:
    import gesture_templates as gtpl
    gesture_recognizer = gtpl.recognizer
    print(f"[Multi] gesture templates loaded: {len(gesture_recognizer.templates)}")
except Exception as e:
    print(f"[Multi] gesture_templates load failed: {e}")
    sys.exit(1)

# ─── Shared camera state ──────────────────────────────────────────────
latest_frame_lock = threading.Lock()
latest_frame = None
enroll_in_progress = threading.Event()

# ─── Face recognition state ───────────────────────────────────────────
face_lock = threading.Lock()
face_recognizer = None
label_map = {}
known_names = []
face_cascade = None
last_match_time = {}

# Auto-enrolment state
auto_enroll_lock = threading.Lock()
auto_enroll_in_progress = False
unknown_consecutive = 0
auto_enroll_cooldown_until = 0.0  # epoch seconds; ignore unknowns before this

# ─── Gesture state ────────────────────────────────────────────────────
pose = mp_pose.Pose(min_detection_confidence=0.5, min_tracking_confidence=0.5)
TRACKED = [
    mp_pose.PoseLandmark.NOSE,
    mp_pose.PoseLandmark.LEFT_SHOULDER, mp_pose.PoseLandmark.RIGHT_SHOULDER,
    mp_pose.PoseLandmark.LEFT_ELBOW, mp_pose.PoseLandmark.RIGHT_ELBOW,
    mp_pose.PoseLandmark.LEFT_WRIST, mp_pose.PoseLandmark.RIGHT_WRIST,
    mp_pose.PoseLandmark.LEFT_HIP, mp_pose.PoseLandmark.RIGHT_HIP,
    mp_pose.PoseLandmark.LEFT_KNEE, mp_pose.PoseLandmark.RIGHT_KNEE,
    mp_pose.PoseLandmark.LEFT_ANKLE, mp_pose.PoseLandmark.RIGHT_ANKLE,
]

# ─── TCP plumbing — separate client lists per port ────────────────────
clients_face = []; clients_face_lock = threading.Lock()
clients_gest = []; clients_gest_lock = threading.Lock()


def _broadcast(client_list, lock, obj):
    line = (json.dumps(obj) + "\n").encode("utf-8")
    with lock:
        dead = []
        for sock in client_list:
            try: sock.sendall(line)
            except: dead.append(sock)
        for sock in dead:
            client_list.remove(sock)
            try: sock.close()
            except: pass


def broadcast_face(obj):    _broadcast(clients_face, clients_face_lock, obj)
def broadcast_gesture(obj): _broadcast(clients_gest, clients_gest_lock, obj)


# ─── Face server — handles enroll/reload/cancel commands ──────────────
def load_faces():
    global face_recognizer, label_map, known_names, face_cascade
    if face_cascade is None or face_cascade.empty():
        face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + "haarcascade_frontalface_default.xml")

    if not os.path.isdir(FACES_DIR):
        os.makedirs(FACES_DIR, exist_ok=True)

    faces, labels, lid = [], [], 0
    new_map, new_names = {}, []
    for name in sorted(os.listdir(FACES_DIR)):
        d = os.path.join(FACES_DIR, name)
        if not os.path.isdir(d): continue
        imgs = [f for f in os.listdir(d) if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp'))]
        if not imgs: continue
        for fn in imgs:
            img = cv2.imread(os.path.join(d, fn), cv2.IMREAD_GRAYSCALE)
            if img is None: continue
            det = face_cascade.detectMultiScale(img, 1.1, 5, minSize=(80, 80))
            if len(det) == 0:
                faces.append(cv2.resize(img, (200, 200))); labels.append(lid)
            else:
                for (x, y, w, h) in det:
                    faces.append(cv2.resize(img[y:y+h, x:x+w], (200, 200))); labels.append(lid)
        new_map[lid] = name; new_names.append(name); lid += 1

    if not faces:
        with face_lock:
            face_recognizer = None; label_map = {}; known_names = []
        print("[Multi] No face images to train on.")
        return False
    try:
        new_rec = cv2.face.LBPHFaceRecognizer_create()
    except AttributeError:
        print("[Multi] cv2.face missing — pip install opencv-contrib-python")
        return False
    new_rec.train(faces, np.array(labels))
    with face_lock:
        face_recognizer = new_rec
        label_map = dict(new_map)
        known_names = list(new_names)
    print(f"[Multi] Trained LBPH with {len(faces)} image(s) from {len(new_names)} player(s)")
    return True


def cmd_face_reload(conn):
    ok = load_faces()
    try: conn.sendall((json.dumps({"type": "reload_done", "ok": bool(ok)}) + "\n").encode())
    except: pass


def _append_user_to_json(user_id, display_name, level="Primary"):
    """Append a minimal player record to both copies of users.json
    (source tree + bin/Debug runtime), so the C# app sees the new user on
    its next LoadUsersFromJson() call."""
    record = {
        "UserId": user_id,
        "BluetoothId": "",
        "FaceId": user_id,
        "Name": display_name,
        "Gender": "",
        "Age": 0,
        "Level": level,
        "Role": "Player",
        "IsActive": True,
        "GazeProfile": {
            "Strokes_Score": 50, "Rules_Score": 50, "Practice_Score": 50,
            "Quiz_Score": 50, "Spelling_Score": 50, "Competition_Score": 50,
        },
    }
    for path in (USERS_JSON_PATH, USERS_JSON_BIN_DEBUG):
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            if os.path.exists(path):
                with open(path, "r", encoding="utf-8") as f:
                    txt = f.read().strip()
                lst = json.loads(txt) if txt and txt != "[]" else []
            else:
                lst = []
            lst.append(record)
            tmp = path + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(lst, f, indent=2)
            os.replace(tmp, path)
            print(f"[Multi] users.json updated: {path}")
        except Exception as ex:
            print(f"[Multi] users.json write FAILED at {path}: {ex}")


def do_auto_enroll():
    """Called when an unknown face has been detected for AUTO_ENROLL_CONSECUTIVE_SCANS
    consecutive scans. Captures 5 photos, generates a user, appends to users.json,
    retrains LBPH, then broadcasts a face_detected so the C# dual-login claims
    the new user as logged-in."""
    global auto_enroll_in_progress, unknown_consecutive, auto_enroll_cooldown_until

    with auto_enroll_lock:
        if auto_enroll_in_progress:
            return
        auto_enroll_in_progress = True

    import uuid
    user_id = "usr_" + uuid.uuid4().hex[:8]
    display = "Player " + user_id[-4:].upper()
    udir = os.path.join(FACES_DIR, user_id)
    os.makedirs(udir, exist_ok=True)

    print(f"[Multi] AUTO-ENROL starting: {user_id} ({display})")
    enroll_in_progress.set()
    saved = 0
    try:
        for i in range(5):
            time.sleep(0.4)  # short gap for slight pose variation
            with latest_frame_lock:
                frame = None if latest_frame is None else latest_frame.copy()
            if frame is None: continue
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            if len(face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))) == 0:
                continue
            cv2.imwrite(os.path.join(udir, f"{i + 1}.jpg"), frame)
            saved += 1
            print(f"[Multi] auto-enrol {user_id}: saved {i+1}.jpg")
    finally:
        enroll_in_progress.clear()

    if saved == 0:
        print(f"[Multi] auto-enrol {user_id} FAILED — no face captured. Rolling back.")
        try:
            for f in os.listdir(udir): os.remove(os.path.join(udir, f))
            os.rmdir(udir)
        except: pass
        with auto_enroll_lock:
            auto_enroll_in_progress = False
        return

    _append_user_to_json(user_id, display)
    load_faces()  # retrain so the next face_detected matches the new user

    # Tell the C# app "this user just logged in" so dual-login completes.
    broadcast_face({"type": "face_detected", "user_name": user_id, "confidence": 0.95})
    broadcast_face({"type": "auto_enrolled", "userId": user_id, "name": display, "saved": saved})
    print(f"[Multi] AUTO-ENROL complete: {user_id} ({display}) {saved} photos")

    auto_enroll_cooldown_until = time.time() + AUTO_ENROLL_COOLDOWN_S
    unknown_consecutive = 0
    with auto_enroll_lock:
        auto_enroll_in_progress = False


def cmd_face_enroll(conn, user_id, count, interval_ms):
    if not user_id or not user_id.startswith("usr_"):
        try: conn.sendall((json.dumps({"type": "enroll_failed", "userId": user_id, "reason": "bad_user_id"}) + "\n").encode())
        except: pass
        return
    udir = os.path.join(FACES_DIR, user_id)
    os.makedirs(udir, exist_ok=True)
    enroll_in_progress.set()
    saved = 0
    try:
        for i in range(int(count)):
            time.sleep(max(int(interval_ms), 100) / 1000.0)
            with latest_frame_lock:
                frame = None if latest_frame is None else latest_frame.copy()
            if frame is None: continue
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            if len(face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))) == 0:
                continue
            cv2.imwrite(os.path.join(udir, f"{i + 1}.jpg"), frame)
            saved += 1
            print(f"[Multi] Enroll {user_id}: saved {i+1}.jpg")
    finally:
        enroll_in_progress.clear()

    if saved == 0:
        try: conn.sendall((json.dumps({"type": "enroll_failed", "userId": user_id, "reason": "no_face_detected"}) + "\n").encode())
        except: pass
        return
    load_faces()
    try: conn.sendall((json.dumps({"type": "enroll_done", "userId": user_id, "saved": saved}) + "\n").encode())
    except: pass


def cmd_face_cancel(conn, user_id):
    enroll_in_progress.clear()
    removed = 0
    if user_id and user_id.startswith("usr_"):
        udir = os.path.join(FACES_DIR, user_id)
        if os.path.isdir(udir):
            for f in os.listdir(udir):
                try: os.remove(os.path.join(udir, f)); removed += 1
                except: pass
            try: os.rmdir(udir)
            except: pass
    try: conn.sendall((json.dumps({"type": "enroll_cancel_done", "userId": user_id, "removed": removed}) + "\n").encode())
    except: pass


def face_dispatch(cmd, conn):
    name = cmd.get("cmd", "")
    if name == "reload":
        threading.Thread(target=cmd_face_reload, args=(conn,), daemon=True).start()
    elif name == "enroll":
        threading.Thread(
            target=cmd_face_enroll,
            args=(conn, cmd.get("userId", ""), int(cmd.get("count", 5)), int(cmd.get("interval_ms", 600))),
            daemon=True
        ).start()
    elif name == "enroll_cancel":
        threading.Thread(target=cmd_face_cancel, args=(conn, cmd.get("userId", "")), daemon=True).start()


def handle_face_client(conn, addr):
    print(f"[Multi-face] client {addr} connected")
    with clients_face_lock: clients_face.append(conn)
    buf = b""
    try:
        while True:
            data = conn.recv(4096)
            if not data: break
            buf += data
            while b"\n" in buf:
                line, _, buf = buf.partition(b"\n")
                line = line.strip()
                if not line: continue
                try:
                    face_dispatch(json.loads(line.decode("utf-8")), conn)
                except Exception as ex:
                    print(f"[Multi-face] bad cmd {ex}")
    except: pass
    finally:
        with clients_face_lock:
            if conn in clients_face: clients_face.remove(conn)
        try: conn.close()
        except: pass


def handle_gest_client(conn, addr):
    print(f"[Multi-gest] client {addr} connected")
    with clients_gest_lock: clients_gest.append(conn)
    try:
        while True:
            d = conn.recv(1024)
            if not d: break
    except: pass
    finally:
        with clients_gest_lock:
            if conn in clients_gest: clients_gest.remove(conn)
        try: conn.close()
        except: pass


def tcp_listener(port, handler):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((HOST, port))
    srv.listen(5)
    print(f"[Multi] TCP listening on {HOST}:{port}")
    while True:
        conn, addr = srv.accept()
        threading.Thread(target=handler, args=(conn, addr), daemon=True).start()


# ─── Per-frame pipelines ──────────────────────────────────────────────
def run_face_recognition(frame):
    """LBPH face matching, broadcasts face_scan + face_detected.

    Also drives auto-enrolment: when a face is detected but doesn't match
    any known player (or no model exists yet) for AUTO_ENROLL_CONSECUTIVE_SCANS
    scans in a row, it kicks off do_auto_enroll() in a worker thread.
    """
    global unknown_consecutive
    if face_cascade is None or face_cascade.empty():
        return

    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    det = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))

    if len(det) == 0:
        # No face in frame — reset unknown counter (don't enrol empty rooms).
        unknown_consecutive = 0
        return

    matched_anyone = False
    best_dist = 999.0

    for (x, y, w, h) in det:
        roi = cv2.resize(gray[y:y+h, x:x+w], (200, 200))
        name = "Unknown"
        dist = 999.0
        with face_lock:
            if face_recognizer is not None:
                lid, dist = face_recognizer.predict(roi)
                name = label_map.get(lid, "Unknown")
        best_dist = min(best_dist, dist)

        conf = max(0.0, min(1.0, 1.0 - (dist / 100.0)))
        matched = dist < FACE_DIST_THRESHOLD
        broadcast_face({"type": "face_scan", "user_name": name if matched else None,
                        "confidence": round(conf, 3), "matched": bool(matched)})
        if matched:
            matched_anyone = True
            now = time.time()
            if name in last_match_time and (now - last_match_time[name]) < FACE_COOLDOWN: continue
            last_match_time[name] = now
            broadcast_face({"type": "face_detected", "user_name": name, "confidence": round(conf, 3)})
            print(f"[Multi] face match: {name} (conf {conf:.1%})")
            break

    # ── Auto-enrolment trigger ───────────────────────────────────────
    # Conditions:
    #   - At least one face was detected this scan.
    #   - It did NOT match any known user.
    #   - Either: no LBPH model exists yet (first-ever user), OR
    #             the best distance is >= AUTO_ENROLL_UNKNOWN_THRESHOLD_DIST
    #             (so we don't over-trigger on a known face with bad lighting).
    #   - We aren't inside the post-enrolment cooldown window.
    now = time.time()
    no_model = face_recognizer is None
    definitely_unknown = (not matched_anyone) and (no_model or best_dist >= AUTO_ENROLL_UNKNOWN_THRESHOLD_DIST)

    if matched_anyone or now < auto_enroll_cooldown_until:
        unknown_consecutive = 0
        return

    if definitely_unknown:
        unknown_consecutive += 1
        print(f"[Multi] unknown face seen — streak={unknown_consecutive}/{AUTO_ENROLL_CONSECUTIVE_SCANS} (best dist {best_dist:.1f}, model={'yes' if not no_model else 'no'})")
        if unknown_consecutive >= AUTO_ENROLL_CONSECUTIVE_SCANS:
            with auto_enroll_lock:
                already = auto_enroll_in_progress
            if not already:
                threading.Thread(target=do_auto_enroll, daemon=True).start()
    else:
        # Face seen but ambiguous (best_dist between FACE_DIST_THRESHOLD and the
        # auto-enrol threshold) — don't count it as unknown nor as known.
        unknown_consecutive = max(0, unknown_consecutive - 1)


def stroke_motion(points):
    if len(points) < 2: return 0.0
    total = 0.0
    for i in range(1, len(points)):
        dx = points[i].x - points[i - 1].x
        dy = points[i].y - points[i - 1].y
        total += (dx * dx + dy * dy) ** 0.5
    return total


# Gesture rolling buffers
_window = deque(maxlen=STROKE_WINDOW_FRAMES * len(TRACKED))
_wrist_path = deque(maxlen=STROKE_WINDOW_FRAMES)
_last_gest_emit_ts = 0.0
_last_gest_label = None
_next_gesture_scan = 0.0


def run_gesture(frame):
    global _last_gest_emit_ts, _last_gest_label, _next_gesture_scan
    rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
    res = pose.process(rgb)
    if res.pose_landmarks:
        h, w, _ = frame.shape
        for lm in TRACKED:
            p = res.pose_landmarks.landmark[lm]
            _window.append(Point(int(p.x * w), int(p.y * h), 1))
        rw = res.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST]
        _wrist_path.append(Point(int(rw.x * w), int(rw.y * h), 1))

    now = time.time()
    if now < _next_gesture_scan: return
    _next_gesture_scan = now + (1.0 / GESTURE_SCAN_HZ)

    if (len(_window) < STROKE_WINDOW_FRAMES * len(TRACKED) - len(TRACKED)
        or stroke_motion(_wrist_path) < MIN_MOTION_PX):
        return

    try:
        label, score = gesture_recognizer.recognize(list(_window))
    except Exception as ex:
        print(f"[Multi] gesture recognizer error: {ex}")
        return
    if label is None or score is None or score < MIN_GESTURE_SCORE: return

    canonical = CANONICAL.get(label, label)
    cooldown_ok = (now - _last_gest_emit_ts) >= GESTURE_COOLDOWN_S
    if not cooldown_ok: return

    _last_gest_emit_ts = now; _last_gest_label = canonical
    print(f"[Multi] gesture: {label} -> {canonical}  score={score:.2f}")
    broadcast_gesture({"type": "gesture", "gesture": canonical,
                       "raw_label": label, "score": round(float(score), 3)})
    broadcast_gesture({"gesture": canonical, "score": round(float(score), 3)})
    _window.clear(); _wrist_path.clear()


# ─── Single camera loop driving both pipelines ───────────────────────
def camera_loop():
    global latest_frame
    print(f"[Multi] Opening camera index {CAMERA_INDEX}...")
    cap = cv2.VideoCapture(CAMERA_INDEX)
    if not cap.isOpened():
        print("[Multi] ERROR: cannot open camera");
        while True: time.sleep(10)
    print("[Multi] Camera open. Running face + gesture pipelines on every frame.")

    last_face_scan = 0.0
    while True:
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.05); continue
        frame = cv2.resize(frame, (480, 320))

        with latest_frame_lock:
            latest_frame = frame.copy()

        if enroll_in_progress.is_set():
            time.sleep(0.05); continue

        # Face recognition every FACE_SCAN_INTERVAL seconds
        now = time.time()
        if now - last_face_scan >= FACE_SCAN_INTERVAL:
            last_face_scan = now
            try: run_face_recognition(frame)
            except Exception as ex: print(f"[Multi] face err {ex}")

        # Gesture pipeline every frame (it has its own internal scan rate)
        try: run_gesture(frame)
        except Exception as ex: print(f"[Multi] gesture err {ex}")

        time.sleep(0.005)


def main():
    print("=" * 60)
    print("  Smart Padel - Multimodal Camera Server (face + gesture)")
    print("=" * 60)
    load_faces()
    threading.Thread(target=tcp_listener, args=(FACE_PORT, handle_face_client), daemon=True).start()
    threading.Thread(target=tcp_listener, args=(GESTURE_PORT, handle_gest_client), daemon=True).start()
    try: camera_loop()
    except KeyboardInterrupt: print("\n[Multi] shutting down")


if __name__ == "__main__":
    main()
