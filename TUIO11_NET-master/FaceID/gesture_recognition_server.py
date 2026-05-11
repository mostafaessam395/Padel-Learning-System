"""
Hand-Gesture Recognition Server for Smart Padel Coaching System
================================================================
Pairs with GestureClient.cs (TCP port 5000). Lets the user drive the
TUIO-marker UI with body gestures recognised by the $1 algorithm
(dollarpy) over MediaPipe Pose landmarks.

Four gesture classes (LA/RA variants collapse into one canonical name):
    Circle, Checkmark, SwipeLeft, SwipeRight

Templates were trained from videos in Skelaton/DynamicPatterns/ and are
inlined in gesture_templates.py — load order is the dollarpy recognizer
the Skelaton author already produced.

Setup once:
    pip install dollarpy mediapipe opencv-python numpy

Run:
    python gesture_recognition_server.py

Protocol (TCP, newline-delimited JSON, broadcast to every client):
    {"type":"gesture","gesture":"Circle","score":0.78}

Also supports the legacy "{"gesture":"..."}" envelope that the existing
C# client used to expect — both shapes are sent on every match so older
binaries still work.
"""

import json
import os
import socket
import sys
import threading
import time
from collections import deque

# ─── Camera + recogniser config ───────────────────────────────────
HOST = "127.0.0.1"
PORT = 5000
CAMERA_INDEX = 0

# How many frames of pose data we feed into the recogniser per attempt.
# Around 30 fps × 2s = 60 frames covers a slow checkmark or swipe.
STROKE_WINDOW_FRAMES = 60

# Don't bother running the recogniser unless the user actually moved.
# Motion = total wrist travel across the window in pixels (480x320 frame).
MIN_MOTION_PX = 80.0

# dollarpy returns a score in 0..1 — accept matches above this threshold.
MIN_SCORE = 0.55

# After a successful recognition, ignore further gestures for this many
# seconds so a single swipe doesn't fire three times.
COOLDOWN_SECONDS = 1.6

# How often the camera worker tries to recognise the rolling window.
SCAN_HZ = 4.0  # recognise four times per second

# Collapse LA/RA template variants into a single canonical class name so
# the C# side can stay simple. Anything not in this map is passed through.
CANONICAL_MAP = {
    "LACheckmark":  "Checkmark",
    "RACheckmark":  "Checkmark",
    "LASwipeLeft":  "SwipeLeft",
    "RASwipeLeft":  "SwipeLeft",
    "LASwipeRight": "SwipeRight",
    "RASwipeRight": "SwipeRight",
    "Circle":       "Circle",
}

# ─── Dependency imports with clear error messages ─────────────────
try:
    import cv2
    import numpy as np
except ImportError as e:
    print(f"[GestureServer] Missing dep: {e}")
    print("[GestureServer] Run: pip install opencv-python numpy")
    sys.exit(1)

try:
    from mediapipe.python.solutions import pose as mp_pose
except ImportError as e:
    print(f"[GestureServer] Missing dep: {e}")
    print("[GestureServer] Run: pip install mediapipe")
    print("[GestureServer] (MediaPipe officially supports Python 3.9-3.12.)")
    sys.exit(1)

try:
    from dollarpy import Recognizer, Template, Point
except ImportError as e:
    print(f"[GestureServer] Missing dep: {e}")
    print("[GestureServer] Run: pip install dollarpy")
    sys.exit(1)

# Load the bundled templates (a sibling file produced by GenerateTemplates.py)
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)
try:
    import gesture_templates as templates_module
    recognizer = templates_module.recognizer
    print(f"[GestureServer] Loaded recogniser with {len(recognizer.templates)} templates")
except Exception as e:
    print(f"[GestureServer] Failed to load gesture_templates.py: {e}")
    sys.exit(1)


# ─── MediaPipe Pose ───────────────────────────────────────────────
pose = mp_pose.Pose(min_detection_confidence=0.5, min_tracking_confidence=0.5)

# Same 13 landmarks the Skelaton trainer used. Order matters — dollarpy
# compares point sequences as flat arrays, so the order has to match.
TRACKED = [
    mp_pose.PoseLandmark.NOSE,
    mp_pose.PoseLandmark.LEFT_SHOULDER,
    mp_pose.PoseLandmark.RIGHT_SHOULDER,
    mp_pose.PoseLandmark.LEFT_ELBOW,
    mp_pose.PoseLandmark.RIGHT_ELBOW,
    mp_pose.PoseLandmark.LEFT_WRIST,
    mp_pose.PoseLandmark.RIGHT_WRIST,
    mp_pose.PoseLandmark.LEFT_HIP,
    mp_pose.PoseLandmark.RIGHT_HIP,
    mp_pose.PoseLandmark.LEFT_KNEE,
    mp_pose.PoseLandmark.RIGHT_KNEE,
    mp_pose.PoseLandmark.LEFT_ANKLE,
    mp_pose.PoseLandmark.RIGHT_ANKLE,
]


# ─── TCP broadcast plumbing ───────────────────────────────────────
clients = []
clients_lock = threading.Lock()


def broadcast(obj):
    line = (json.dumps(obj) + "\n").encode("utf-8")
    with clients_lock:
        dead = []
        for sock in clients:
            try:
                sock.sendall(line)
            except Exception:
                dead.append(sock)
        for sock in dead:
            clients.remove(sock)
            try: sock.close()
            except: pass


def handle_client(conn, addr):
    print(f"[GestureServer] Client connected: {addr}")
    with clients_lock:
        clients.append(conn)
    try:
        # We don't expect inbound traffic; just keep the socket alive
        while True:
            data = conn.recv(1024)
            if not data:
                break
    except Exception:
        pass
    finally:
        with clients_lock:
            if conn in clients:
                clients.remove(conn)
        try: conn.close()
        except: pass
        print(f"[GestureServer] Client disconnected: {addr}")


def tcp_server():
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((HOST, PORT))
    srv.listen(5)
    print(f"[GestureServer] TCP listening on {HOST}:{PORT}")
    while True:
        conn, addr = srv.accept()
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


# ─── Recognition loop ─────────────────────────────────────────────
def stroke_motion(points):
    """Total wrist travel inside the current window (px)."""
    if len(points) < 2:
        return 0.0
    total = 0.0
    for i in range(1, len(points)):
        dx = points[i].x - points[i - 1].x
        dy = points[i].y - points[i - 1].y
        total += (dx * dx + dy * dy) ** 0.5
    return total


def collapse(label):
    return CANONICAL_MAP.get(label, label)


def camera_loop():
    print(f"[GestureServer] Opening camera index {CAMERA_INDEX}...")
    cap = cv2.VideoCapture(CAMERA_INDEX)
    if not cap.isOpened():
        print("[GestureServer] ERROR: Cannot open camera.")
        while True:
            time.sleep(10)

    # Rolling window — every TRACKED landmark from each frame appended in
    # the same order the trainer used.
    window = deque(maxlen=STROKE_WINDOW_FRAMES * len(TRACKED))
    wrist_path = deque(maxlen=STROKE_WINDOW_FRAMES)  # just for motion gating

    last_emit_ts = 0.0
    last_emit_label = None
    scan_interval = 1.0 / SCAN_HZ
    next_scan_ts = time.time() + scan_interval

    print("[GestureServer] Camera open. Watching for gestures...")
    print(f"[GestureServer] Window={STROKE_WINDOW_FRAMES}f, motion>={MIN_MOTION_PX}px, score>={MIN_SCORE}")

    while True:
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.05)
            continue

        frame = cv2.resize(frame, (480, 320))
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        result = pose.process(rgb)

        if result.pose_landmarks:
            h, w, _ = frame.shape
            # Append all tracked landmarks for this frame
            for lm in TRACKED:
                p = result.pose_landmarks.landmark[lm]
                window.append(Point(int(p.x * w), int(p.y * h), 1))

            # Track right-wrist path separately for motion gating
            rw = result.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST]
            wrist_path.append(Point(int(rw.x * w), int(rw.y * h), 1))

        now = time.time()
        if now >= next_scan_ts:
            next_scan_ts = now + scan_interval

            # Need a near-full window + meaningful motion before bothering
            if (len(window) >= STROKE_WINDOW_FRAMES * len(TRACKED) - len(TRACKED)
                and stroke_motion(wrist_path) >= MIN_MOTION_PX):

                try:
                    label, score = recognizer.recognize(list(window))
                except Exception as ex:
                    label, score = None, 0.0
                    print(f"[GestureServer] recogniser error: {ex}")

                if label and score is not None and score >= MIN_SCORE:
                    canonical = collapse(label)
                    cooldown_ok = (now - last_emit_ts) >= COOLDOWN_SECONDS
                    not_same_burst = canonical != last_emit_label or cooldown_ok
                    if cooldown_ok and not_same_burst:
                        last_emit_ts = now
                        last_emit_label = canonical
                        print(f"[GestureServer] {label} -> {canonical}  score={score:.2f}")

                        # New-style envelope (typed)
                        broadcast({
                            "type": "gesture",
                            "gesture": canonical,
                            "raw_label": label,
                            "score": round(float(score), 3),
                        })
                        # Legacy envelope for any older listener
                        broadcast({
                            "gesture": canonical,
                            "score": round(float(score), 3),
                        })

                        # Clear the window so the same gesture isn't fired
                        # again from the same trailing points.
                        window.clear()
                        wrist_path.clear()

        # Throttle CPU a bit — pose.process is the heavy part
        time.sleep(0.005)


def main():
    print("=" * 56)
    print("  Smart Padel - Hand-Gesture Recognition Server")
    print("  (MediaPipe Pose + dollarpy)")
    print("=" * 56)

    threading.Thread(target=tcp_server, daemon=True).start()
    try:
        camera_loop()
    except KeyboardInterrupt:
        print("\n[GestureServer] Shutting down.")


if __name__ == "__main__":
    main()
