"""
Face Enrollment Script for Smart Padel Coaching System
======================================================
Captures a photo from the webcam and saves it to
Data/face_images/<name>/ref.jpg

Usage:
    python enroll_face.py --name "Shahd"
    python enroll_face.py --name "Shahd" --camera 1

Note: This saves a reference photo (not just embeddings).
      The photo is stored locally and never transmitted.
"""

import os
import sys
import argparse

try:
    import cv2
except ImportError:
    print("ERROR: OpenCV not installed. Run:  pip install opencv-python")
    sys.exit(1)

FACES_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Data", "face_images")


def enroll(name: str, camera_index: int = 0):
    player_dir = os.path.join(FACES_DIR, name)
    os.makedirs(player_dir, exist_ok=True)
    out_path = os.path.join(player_dir, "ref.jpg")

    if os.path.exists(out_path):
        resp = input(f"Photo for '{name}' already exists. Overwrite? (y/N): ")
        if resp.lower() != "y":
            print("Aborted.")
            return

    print(f"Opening camera {camera_index}...")
    print("Look at the camera and press SPACE to capture, ESC to cancel.")
    cap = cv2.VideoCapture(camera_index)

    if not cap.isOpened():
        print("ERROR: Cannot open camera.")
        return

    captured = False
    while True:
        ret, frame = cap.read()
        if not ret:
            continue

        # Draw guide
        h, w = frame.shape[:2]
        cx, cy = w // 2, h // 2
        box = min(w, h) // 3
        cv2.rectangle(frame, (cx - box, cy - box), (cx + box, cy + box), (0, 255, 0), 2)
        cv2.putText(frame, f"Enrolling: {name}", (20, 30),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
        cv2.putText(frame, "SPACE = capture, ESC = cancel", (20, h - 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (200, 200, 200), 1)

        cv2.imshow("Face Enrollment", frame)
        key = cv2.waitKey(1) & 0xFF

        if key == 27:  # ESC
            print("Cancelled.")
            break
        elif key == 32:  # SPACE
            cv2.imwrite(out_path, frame)
            print(f"SUCCESS: Photo saved to {out_path}")
            captured = True
            break

    cap.release()
    cv2.destroyAllWindows()

    if not captured:
        print("No face was enrolled.")


def main():
    parser = argparse.ArgumentParser(description="Enroll a player's face")
    parser.add_argument("--name", required=True, help="Player name (must match users.json)")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default: 0)")
    args = parser.parse_args()

    print("=" * 50)
    print("  Smart Padel — Face Enrollment")
    print("=" * 50)
    enroll(args.name, args.camera)


if __name__ == "__main__":
    main()
