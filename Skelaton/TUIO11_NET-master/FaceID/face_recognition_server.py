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


def load_faces():
    """Load enrolled faces and train the LBPH recognizer."""
    global known_names, label_map, face_recognizer, face_cascade

    # Load OpenCV's built-in face detector
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

    faces = []
    labels = []
    label_id = 0

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

            # Detect face in the reference image
            detected = face_cascade.detectMultiScale(img, 1.1, 5, minSize=(80, 80))
            if len(detected) == 0:
                # Try with the whole image
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

        label_map[label_id] = name
        known_names.append(name)
        label_id += 1

    if not faces:
        print("[Server] No face images found to train on.")
        return False

    # Train the LBPH recognizer
    try:
        face_recognizer = cv2.face.LBPHFaceRecognizer_create()
    except AttributeError:
        print("[Server] ERROR: cv2.face module not available!")
        print("[Server] Install with: pip install opencv-contrib-python")
        return False

    face_recognizer.train(faces, np.array(labels))
    print(f"[Server] Trained recognizer with {len(faces)} image(s) from {len(known_names)} player(s)")
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


def handle_client(conn, addr):
    """Handle a single TCP client connection."""
    print(f"[Server] C# client connected: {addr}")
    with clients_lock:
        clients.append(conn)
    try:
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
    """Capture webcam frames and perform face recognition."""
    global face_recognizer, face_cascade

    if face_recognizer is None:
        print("[Server] No trained model — camera loop waiting.")
        print(f"[Server] Enroll faces first: python enroll_face.py --name \"PlayerName\"")
        while True:
            time.sleep(10)

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

        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        detected_faces = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))

        for (x, y, w, h) in detected_faces:
            face_roi = gray[y:y+h, x:x+w]
            face_roi = cv2.resize(face_roi, (200, 200))

            label_id, distance = face_recognizer.predict(face_roi)

            if distance < CONFIDENCE_THRESHOLD:
                name = label_map.get(label_id, "Unknown")
                now = time.time()

                # Cooldown
                if name in last_match_time and (now - last_match_time[name]) < COOLDOWN_AFTER_MATCH:
                    continue

                # Convert distance to 0-1 confidence (lower distance = higher confidence)
                confidence = max(0.0, min(1.0, 1.0 - (distance / 100.0)))

                last_match_time[name] = now
                msg = json.dumps({
                    "type": "face_detected",
                    "user_name": name,
                    "confidence": round(confidence, 3)
                })
                print(f"[Server] Match: {name} (distance: {distance:.1f}, confidence: {confidence:.1%})")
                broadcast(msg)
                break  # One match per frame

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
