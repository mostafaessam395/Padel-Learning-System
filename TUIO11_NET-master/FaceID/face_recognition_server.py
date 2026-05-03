"""
Face Recognition Server for Smart Padel Coaching System
========================================================
Captures webcam frames, compares faces against enrolled embeddings,
and sends recognized identities to the C# client over TCP (port 5001).

Usage:
    pip install -r requirements.txt
    python face_recognition_server.py

Protocol (TCP, newline-delimited JSON):
    {"type": "face_detected", "user_name": "MOSTAFA_ESSAM", "confidence": 0.92}
"""

import os
import sys
import json
import pickle
import socket
import threading
import time
import numpy as np

try:
    import cv2
    import face_recognition
except ImportError:
    print("ERROR: Missing dependencies. Run:  pip install -r requirements.txt")
    sys.exit(1)

# ── Configuration ────────────────────────────────────────────────
HOST = "127.0.0.1"
PORT = 5001
EMBEDDINGS_DIR = os.path.join(os.path.dirname(__file__), "..", "Data", "face_embeddings")
CONFIDENCE_THRESHOLD = 0.85          # minimum match confidence (1 - distance)
SCAN_INTERVAL = 0.5                  # seconds between scans
COOLDOWN_AFTER_MATCH = 5.0           # don't re-send same person for N seconds
CAMERA_INDEX = 0                     # default webcam

# ── Global state ─────────────────────────────────────────────────
clients = []                         # connected TCP sockets
clients_lock = threading.Lock()
known_encodings = []                 # list of numpy arrays
known_names = []                     # parallel list of names
last_match_time = {}                 # name -> timestamp (cooldown tracking)


def load_embeddings():
    """Load all .pkl face embeddings from Data/face_embeddings/."""
    global known_encodings, known_names
    known_encodings.clear()
    known_names.clear()

    if not os.path.isdir(EMBEDDINGS_DIR):
        os.makedirs(EMBEDDINGS_DIR, exist_ok=True)
        print(f"[Server] Created embeddings dir: {EMBEDDINGS_DIR}")
        print("[Server] No enrolled faces yet. Run enroll_face.py to add players.")
        return

    count = 0
    for fname in os.listdir(EMBEDDINGS_DIR):
        if not fname.endswith(".pkl"):
            continue
        path = os.path.join(EMBEDDINGS_DIR, fname)
        try:
            with open(path, "rb") as f:
                data = pickle.load(f)
            name = data["name"]
            encoding = data["encoding"]
            known_names.append(name)
            known_encodings.append(encoding)
            count += 1
            print(f"[Server] Loaded face: {name}")
        except Exception as e:
            print(f"[Server] Failed to load {fname}: {e}")

    print(f"[Server] {count} face(s) loaded from {EMBEDDINGS_DIR}")


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
    print(f"[Server] Client connected: {addr}")
    with clients_lock:
        clients.append(conn)
    try:
        while True:
            # Keep connection alive; we don't expect data from C# client
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
        print(f"[Server] Client disconnected: {addr}")


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
    print(f"[Server] Opening camera {CAMERA_INDEX}...")
    cap = cv2.VideoCapture(CAMERA_INDEX)

    if not cap.isOpened():
        print("[Server] ERROR: Cannot open camera. Face recognition disabled.")
        return

    print("[Server] Camera opened. Scanning for faces...")

    while True:
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.1)
            continue

        # Downscale for speed
        small = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
        rgb_small = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)

        # Detect faces
        locations = face_recognition.face_locations(rgb_small)
        if not locations:
            time.sleep(SCAN_INTERVAL)
            continue

        # Encode detected faces
        encodings = face_recognition.face_encodings(rgb_small, locations)

        for enc in encodings:
            if not known_encodings:
                break

            # Compare against all known faces
            distances = face_recognition.face_distance(known_encodings, enc)
            best_idx = int(np.argmin(distances))
            best_distance = distances[best_idx]
            confidence = 1.0 - best_distance

            if confidence >= CONFIDENCE_THRESHOLD:
                name = known_names[best_idx]
                now = time.time()

                # Cooldown: don't spam the same person
                if name in last_match_time and (now - last_match_time[name]) < COOLDOWN_AFTER_MATCH:
                    continue

                last_match_time[name] = now
                msg = json.dumps({
                    "type": "face_detected",
                    "user_name": name,
                    "confidence": round(confidence, 3)
                })
                print(f"[Server] Match: {name} ({confidence:.2%})")
                broadcast(msg)

        time.sleep(SCAN_INTERVAL)

    cap.release()


def main():
    print("=" * 56)
    print("  Smart Padel Coaching — Face Recognition Server")
    print("=" * 56)

    load_embeddings()

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
