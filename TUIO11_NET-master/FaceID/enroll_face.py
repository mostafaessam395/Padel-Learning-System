"""
Face Enrollment Script for Smart Padel Coaching System
======================================================
Captures a face from the webcam and saves its embedding to
Data/face_embeddings/<name>.pkl

Usage:
    python enroll_face.py --name "MOSTAFA_ESSAM"
    python enroll_face.py --name "Ahmed" --camera 1

Privacy Note:
    No images are saved — only the mathematical face embedding (128-d vector).
"""

import os
import sys
import pickle
import argparse

try:
    import cv2
    import face_recognition
except ImportError:
    print("ERROR: Missing dependencies. Run:  pip install -r requirements.txt")
    sys.exit(1)

EMBEDDINGS_DIR = os.path.join(os.path.dirname(__file__), "..", "Data", "face_embeddings")


def enroll(name: str, camera_index: int = 0):
    os.makedirs(EMBEDDINGS_DIR, exist_ok=True)
    out_path = os.path.join(EMBEDDINGS_DIR, f"{name}.pkl")

    if os.path.exists(out_path):
        resp = input(f"Embedding for '{name}' already exists. Overwrite? (y/N): ")
        if resp.lower() != "y":
            print("Aborted.")
            return

    print(f"Opening camera {camera_index}... Look at the camera and press SPACE to capture.")
    cap = cv2.VideoCapture(camera_index)

    if not cap.isOpened():
        print("ERROR: Cannot open camera.")
        return

    captured = False
    while True:
        ret, frame = cap.read()
        if not ret:
            continue

        # Draw a guide rectangle
        h, w = frame.shape[:2]
        cx, cy = w // 2, h // 2
        box_size = min(w, h) // 3
        cv2.rectangle(
            frame,
            (cx - box_size, cy - box_size),
            (cx + box_size, cy + box_size),
            (0, 255, 0), 2
        )
        cv2.putText(
            frame, f"Enrolling: {name}", (20, 30),
            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2
        )
        cv2.putText(
            frame, "Press SPACE to capture, ESC to cancel", (20, h - 20),
            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1
        )

        cv2.imshow("Face Enrollment", frame)
        key = cv2.waitKey(1) & 0xFF

        if key == 27:  # ESC
            print("Cancelled.")
            break
        elif key == 32:  # SPACE
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            locations = face_recognition.face_locations(rgb)

            if not locations:
                print("No face detected! Try again — make sure your face is visible.")
                continue

            if len(locations) > 1:
                print(f"Multiple faces detected ({len(locations)}). Only one person should be in frame.")
                continue

            encodings = face_recognition.face_encodings(rgb, locations)
            if not encodings:
                print("Could not compute face embedding. Try again.")
                continue

            # Save embedding only (no image — privacy)
            data = {
                "name": name,
                "encoding": encodings[0]
            }
            with open(out_path, "wb") as f:
                pickle.dump(data, f)

            print(f"SUCCESS: Face embedding saved to {out_path}")
            print(f"  Name     : {name}")
            print(f"  Embedding: 128-dimensional vector (no image stored)")
            captured = True
            break

    cap.release()
    cv2.destroyAllWindows()

    if not captured:
        print("No face was enrolled.")


def main():
    parser = argparse.ArgumentParser(description="Enroll a player's face for the Padel Coaching System")
    parser.add_argument("--name", required=True, help="Player name (must match users.json)")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default: 0)")
    args = parser.parse_args()

    print("=" * 50)
    print("  Smart Padel — Face Enrollment")
    print("=" * 50)
    enroll(args.name, args.camera)


if __name__ == "__main__":
    main()
