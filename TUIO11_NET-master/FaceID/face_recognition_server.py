"""
Face Recognition Server for Smart Padel Coaching System
========================================================
Uses pure OpenCV (no TensorFlow, no dlib, no heavy AI).

Strategy: OpenCV DNN face detector + LBPH face recognizer.
Works on ALL Python versions including 3.14.

Enrollment: Save a photo in Data/face_images/<PlayerName>/ref.jpg
The server compares webcam faces against these reference photos.

Usage:
    pip install opencv-python opencv-contrib-python numpy
    python face_recognition_server.py

Protocol (TCP, newline-delimited JSON):
    {"type": "face_detected", "user_name": "Shahd", "confidence": 0.92}
"""

import os
import sys
import json
import socket
import threading
import time

try:
    import cv2
    import numpy as np
except ImportError as e:
    print(f"ERROR: Missing dependency: {e}")
    print("Run:  pip install opencv-python opencv-contrib-python numpy")
    sys.exit(1)

# ── Configuration ────────────────────────────────────────────────
HOST = "127.0.0.1"
PORT = 5001
FACES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Data", "face_images")
CONFIDENCE_THRESHOLD = 70.0     # LBPH: lower distance = better match. < 70 = good match
SCAN_INTERVAL = 0.8             # seconds between scans
COOLDOWN_AFTER_MATCH = 5.0      # don't re-send same person for N seconds
CAMERA_INDEX = 0                # default webcam

# ── Global state ─────────────────────────────────────────────────
clients = []
clients_lock = threading.Lock()
known_names = []                # list of player names
label_map = {}                  # label_id -> name
face_recognizer = None
face_cascade = None

# Concurrency primitives for hot-swappable recognizer + shared-frame design
recognizer_lock = threading.Lock()
latest_frame_lock = threading.Lock()
latest_frame = None
enroll_in_progress = threading.Event()


def load_faces():
    """Load enrolled faces and train the LBPH recognizer.
    Builds the new recognizer in locals, then atomically swaps it in
    under recognizer_lock so concurrent predicts never see a half-state.
    Returns True if a model was trained, False otherwise.
    """
    global face_cascade, face_recognizer, label_map, known_names

    # Load OpenCV's built-in face detector (once)
    if face_cascade is None or face_cascade.empty():
        cascade_path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
        face_cascade = cv2.CascadeClassifier(cascade_path)
        if face_cascade.empty():
            print("[Server] ERROR: Could not load face cascade classifier!")
            sys.exit(1)

    print(f"[Server] Looking for enrolled faces in: {FACES_DIR}")

    if not os.path.isdir(FACES_DIR):
        os.makedirs(FACES_DIR, exist_ok=True)
        print(f"[Server] Created faces dir: {FACES_DIR}")
        print("[Server] No enrolled faces yet.")
        print(f"[Server] To enroll: python enroll_face.py --name \"Shahd\"")
        return False

    # Build everything into locals first
    faces = []
    labels = []
    label_id = 0
    label_map_local = {}
    known_names_local = []

    for name in sorted(os.listdir(FACES_DIR)):
        player_dir = os.path.join(FACES_DIR, name)
        if not os.path.isdir(player_dir):
            continue

        images = [f for f in os.listdir(player_dir)
                  if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp'))]

        if not images:
            print(f"[Server] Warning: {name}/ has no images, skipping")
            continue

        for img_file in images:
            img_path = os.path.join(player_dir, img_file)
            img = cv2.imread(img_path, cv2.IMREAD_GRAYSCALE)
            if img is None:
                continue

            detected = face_cascade.detectMultiScale(img, 1.1, 5, minSize=(80, 80))
            if len(detected) == 0:
                resized = cv2.resize(img, (200, 200))
                faces.append(resized)
                labels.append(label_id)
                print(f"[Server] Loaded: {name}/{img_file} (full image)")
            else:
                for (x, y, w, h) in detected:
                    face_roi = img[y:y+h, x:x+w]
                    face_roi = cv2.resize(face_roi, (200, 200))
                    faces.append(face_roi)
                    labels.append(label_id)
                    print(f"[Server] Loaded: {name}/{img_file} (face detected)")

        label_map_local[label_id] = name
        known_names_local.append(name)
        label_id += 1

    if not faces:
        print("[Server] No face images found to train on.")
        # Clear the global recognizer so stale matches never fire after a
        # user is deleted off disk.
        with recognizer_lock:
            face_recognizer = None
            label_map = {}
            known_names = []
        return False

    try:
        new_recognizer = cv2.face.LBPHFaceRecognizer_create()
    except AttributeError:
        print("[Server] ERROR: cv2.face module not available!")
        print("[Server] Install with: pip install opencv-contrib-python")
        return False

    new_recognizer.train(faces, np.array(labels))

    with recognizer_lock:
        face_recognizer = new_recognizer
        label_map = dict(label_map_local)
        known_names = list(known_names_local)

    print(f"[Server] Trained recognizer with {len(faces)} image(s) from {len(known_names_local)} player(s)")
    return True


def broadcast(message: str):
    """Send a message to all connected TCP clients."""
    data = (message.strip() + "\n").encode("utf-8")
    with clients_lock:
        dead = []
        for sock in clients:
            try:
                sock.sendall(data)
            except Exception:
                dead.append(sock)
        for sock in dead:
            clients.remove(sock)
            try:
                sock.close()
            except Exception:
                pass


def _send_reply(conn, obj):
    """Send one JSON object as a newline-terminated line to a single client."""
    try:
        data = (json.dumps(obj) + "\n").encode("utf-8")
        conn.sendall(data)
    except Exception as ex:
        print(f"[Server] Reply send failed: {ex}")


def cmd_reload(conn):
    ok = load_faces()
    _send_reply(conn, {"type": "reload_done", "ok": bool(ok)})


def cmd_enroll(user_id, count, interval_ms, conn):
    """Capture N frames from latest_frame, save them to disk, retrain.
    Sends enroll_done on success or enroll_failed with a reason on failure.
    """
    if not user_id or not user_id.startswith("usr_"):
        _send_reply(conn, {"type": "enroll_failed", "userId": user_id, "reason": "bad_user_id"})
        return

    user_dir = os.path.join(FACES_DIR, user_id)
    os.makedirs(user_dir, exist_ok=True)

    enroll_in_progress.set()
    saved = 0
    try:
        for i in range(int(count)):
            time.sleep(max(int(interval_ms), 100) / 1000.0)

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

    # Retrain so the newly enrolled user is recognised immediately
    load_faces()
    _send_reply(conn, {"type": "enroll_done", "userId": user_id, "saved": saved})


def cmd_enroll_cancel(user_id, conn):
    """Discard any partially enrolled photos for this user_id."""
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


def handle_client(conn, addr):
    """Handle one TCP client connection. Reads newline-delimited JSON commands."""
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


def tcp_server():
    """Run the TCP server accepting C# client connections."""
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(5)
    print(f"[Server] TCP server listening on {HOST}:{PORT}")

    while True:
        conn, addr = server.accept()
        t = threading.Thread(target=handle_client, args=(conn, addr), daemon=True)
        t.start()


def camera_loop():
    """Capture webcam frames, publish latest_frame, and run recognition.

    The camera is opened unconditionally (even with no trained model) so
    enrollment can capture frames before any face is registered. The
    enroll_in_progress flag pauses recognition while server-side enrollment
    captures photos so two threads never read from cv2.VideoCapture at once.
    """
    global latest_frame

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

        # Publish the latest frame for the enroll worker
        with latest_frame_lock:
            latest_frame = frame.copy()

        # Pause recognition while server-side enrollment is capturing
        if enroll_in_progress.is_set():
            time.sleep(0.05)
            continue

        # Need a trained model to recognise — but the camera keeps running.
        if face_recognizer is None:
            time.sleep(SCAN_INTERVAL)
            continue

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        detected_faces = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))

        for (x, y, w, h) in detected_faces:
            face_roi = gray[y:y+h, x:x+w]
            face_roi = cv2.resize(face_roi, (200, 200))

            # Acquire the lock for the duration of one predict so a
            # mid-flight reload can't swap models under us.
            with recognizer_lock:
                if face_recognizer is None:
                    break
                label_id, distance = face_recognizer.predict(face_roi)
                name = label_map.get(label_id, "Unknown")

            # Convert LBPH distance to 0-1 confidence
            confidence = max(0.0, min(1.0, 1.0 - (distance / 100.0)))
            matched = distance < CONFIDENCE_THRESHOLD
            now = time.time()

            # Broadcast every scan for live UI feedback (ticker)
            scan_msg = json.dumps({
                "type": "face_scan",
                "user_name": name if matched else None,
                "confidence": round(confidence, 3),
                "matched": bool(matched)
            })
            broadcast(scan_msg)

            if matched:
                # Cooldown: don't re-fire a confident match too fast
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
                break  # One confident match per frame

        time.sleep(SCAN_INTERVAL)

    cap.release()


last_match_time = {}


def main():
    print("=" * 56)
    print("  Smart Padel Coaching - Face Recognition Server")
    print("  (OpenCV LBPH - Pure Python, No Heavy AI)")
    print("=" * 56)

    ready = load_faces()

    # Start TCP server in background thread
    tcp_thread = threading.Thread(target=tcp_server, daemon=True)
    tcp_thread.start()

    # Run camera loop on main thread
    try:
        camera_loop()
    except KeyboardInterrupt:
        print("\n[Server] Shutting down...")


if __name__ == "__main__":
    main()
