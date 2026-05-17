"""
Gaze Tracking Server for Smart Padel Coaching System
=====================================================
Uses OpenCV to track pupil position and estimate gaze direction.
Sends normalized (x, y) coordinates to the C# client over TCP (port 5002).

Usage:
    pip install opencv-python opencv-contrib-python numpy
    python gaze_tracking_server.py

Protocol (TCP, newline-delimited JSON):
    {"type": "gaze", "x": 0.45, "y": 0.62}
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
    print("Run:  pip install opencv-python numpy")
    sys.exit(1)

# ── Configuration ────────────────────────────────────────────────
HOST = "127.0.0.1"
PORT = 5002
CAMERA_INDEX = 0
SCAN_INTERVAL = 0.066           # ~15 Hz gaze sampling
SMOOTHING_FACTOR = 0.3          # exponential smoothing for gaze coords

# ── Global state ─────────────────────────────────────────────────
clients = []
clients_lock = threading.Lock()
smooth_x, smooth_y = 0.5, 0.5   # smoothed gaze position


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
    """Handle a TCP client."""
    print(f"[GazeServer] Client connected: {addr}")
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
        print(f"[GazeServer] Client disconnected: {addr}")


def tcp_server():
    """Accept TCP client connections."""
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(5)
    print(f"[GazeServer] TCP listening on {HOST}:{PORT}")

    while True:
        conn, addr = server.accept()
        threading.Thread(target=handle_client, args=(conn, addr), daemon=True).start()


def detect_gaze(frame, face_cascade, eye_cascade):
    """
    Detect gaze direction using face → eye → pupil tracking.
    Returns (gaze_x, gaze_y) normalized 0-1, or None if no face detected.
    """
    h, w = frame.shape[:2]
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)

    # Detect face
    faces = face_cascade.detectMultiScale(gray, 1.3, 5, minSize=(100, 100))
    if len(faces) == 0:
        return None

    # Use the largest face
    fx, fy, fw, fh = max(faces, key=lambda f: f[2] * f[3])
    face_roi_gray = gray[fy:fy+fh, fx:fx+fw]

    # Detect eyes within face
    eyes = eye_cascade.detectMultiScale(face_roi_gray, 1.1, 5, minSize=(25, 25))

    if len(eyes) < 1:
        # Fallback: use face center position as approximate gaze
        gaze_x = (fx + fw / 2) / w
        gaze_y = (fy + fh / 2) / h
        return (gaze_x, gaze_y)

    # Analyze each eye for pupil position
    pupil_offsets_x = []
    pupil_offsets_y = []

    for (ex, ey, ew, eh) in eyes[:2]:  # max 2 eyes
        eye_roi = face_roi_gray[ey:ey+eh, ex:ex+ew]

        # Threshold to find dark pupil
        _, thresh = cv2.threshold(eye_roi, 45, 255, cv2.THRESH_BINARY_INV)

        # Find contours
        contours, _ = cv2.findContours(thresh, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        if not contours:
            continue

        # Largest contour = pupil
        largest = max(contours, key=cv2.contourArea)
        M = cv2.moments(largest)
        if M["m00"] == 0:
            continue

        # Pupil center within eye ROI
        px = int(M["m10"] / M["m00"])
        py = int(M["m01"] / M["m00"])

        # Offset from eye center (normalized -0.5 to 0.5)
        offset_x = (px / ew) - 0.5
        offset_y = (py / eh) - 0.5
        pupil_offsets_x.append(offset_x)
        pupil_offsets_y.append(offset_y)

    if not pupil_offsets_x:
        # No pupil detected — use face position
        gaze_x = (fx + fw / 2) / w
        gaze_y = (fy + fh / 2) / h
        return (gaze_x, gaze_y)

    # Average pupil offset across both eyes
    avg_offset_x = sum(pupil_offsets_x) / len(pupil_offsets_x)
    avg_offset_y = sum(pupil_offsets_y) / len(pupil_offsets_y)

    # Map face position + pupil offset to screen gaze
    # Face position gives rough direction, pupil offset refines it
    face_norm_x = (fx + fw / 2) / w
    face_norm_y = (fy + fh / 2) / h

    # Combine: face position + amplified pupil offset
    gaze_x = max(0.0, min(1.0, face_norm_x + avg_offset_x * 2.0))
    gaze_y = max(0.0, min(1.0, face_norm_y + avg_offset_y * 2.0))

    return (gaze_x, gaze_y)


def camera_loop():
    """Main camera loop for gaze tracking."""
    global smooth_x, smooth_y

    face_cascade = cv2.CascadeClassifier(
        cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    eye_cascade = cv2.CascadeClassifier(
        cv2.data.haarcascades + "haarcascade_eye.xml")

    if face_cascade.empty() or eye_cascade.empty():
        print("[GazeServer] ERROR: Could not load cascade classifiers!")
        return

    print(f"[GazeServer] Opening camera {CAMERA_INDEX}...")
    cap = cv2.VideoCapture(CAMERA_INDEX)

    if not cap.isOpened():
        print("[GazeServer] ERROR: Cannot open camera.")
        while True:
            time.sleep(10)
        return

    print("[GazeServer] Camera opened. Tracking gaze...")

    while True:
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.05)
            continue

        # Mirror horizontally for natural mapping
        frame = cv2.flip(frame, 1)

        result = detect_gaze(frame, face_cascade, eye_cascade)

        if result:
            raw_x, raw_y = result

            # Exponential smoothing
            smooth_x = smooth_x * (1 - SMOOTHING_FACTOR) + raw_x * SMOOTHING_FACTOR
            smooth_y = smooth_y * (1 - SMOOTHING_FACTOR) + raw_y * SMOOTHING_FACTOR

            msg = json.dumps({
                "type": "gaze",
                "x": round(smooth_x, 4),
                "y": round(smooth_y, 4)
            })
            broadcast(msg)

        time.sleep(SCAN_INTERVAL)

    cap.release()


def main():
    print("=" * 56)
    print("  Smart Padel Coaching — Gaze Tracking Server")
    print("  (OpenCV Haar + Pupil Detection)")
    print("=" * 56)

    tcp_thread = threading.Thread(target=tcp_server, daemon=True)
    tcp_thread.start()

    try:
        camera_loop()
    except KeyboardInterrupt:
        print("\n[GazeServer] Shutting down...")


if __name__ == "__main__":
    main()
