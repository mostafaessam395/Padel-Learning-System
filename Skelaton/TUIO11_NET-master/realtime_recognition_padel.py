import cv2
import time
import json
import socket
import mediapipe as mp
from dollarpy import Recognizer, Template, Point

mp_pose = mp.solutions.pose
drawing_utils = mp.solutions.drawing_utils

# ── TCP Sender ─────────────────────────────────────────────────
class GestureSender:
    def __init__(self, host='127.0.0.1', port=5000):
        self.sock = None
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((host, port))
            print(f"[TCP] Connected to {host}:{port}")
        except:
            print(f"[TCP] Could not connect")

    def send(self, gesture, action, score):
        if self.sock is None: return
        try:
            msg = json.dumps({
                "type": "gesture",
                "gesture": gesture,
                "action": action,
                "score": round(score, 3)
            }) + "\n"
            self.sock.sendall(msg.encode())
        except:
            pass

sender = GestureSender()

# ── Pose ───────────────────────────────────────────────────────
pose = mp_pose.Pose(min_detection_confidence=0.5, min_tracking_confidence=0.5)

# ── Only 7 keypoints: head, wrists, ankles ────────────────────
TRACKED_LANDMARKS = [
    mp_pose.PoseLandmark.NOSE,
    mp_pose.PoseLandmark.LEFT_WRIST,
    mp_pose.PoseLandmark.RIGHT_WRIST,
    mp_pose.PoseLandmark.LEFT_ANKLE,
    mp_pose.PoseLandmark.RIGHT_ANKLE,
    mp_pose.PoseLandmark.LEFT_ELBOW,
    mp_pose.PoseLandmark.RIGHT_ELBOW,
]
POINTS_PER_FRAME = len(TRACKED_LANDMARKS)  # 7

# ── Merge variants ─────────────────────────────────────────────
GESTURE_MAP = {
    "Backhand":        "Backhand",
    "Backhand2":       "Backhand",
    "Forehand":        "Forehand",
    "Forehand2":       "Forehand",
    "BackhandVolley":  "BackhandVolley",
    "BackhandVolley2": "BackhandVolley",
    "ForehandVolley":  "ForehandVolley",
    "ForehandVolley2": "ForehandVolley",
}

# ── Import templates ───────────────────────────────────────────
import sys
sys.path.insert(0, r"C:\Users\Yahya\Desktop\Padel-Learning-System-main\TUIO11_NET-master\DynamicPatternsPadel")
from TestDollar2 import recognizer

print("\n" + "=" * 60)
print("REAL-TIME PADEL SHOT RECOGNITION")
print("4 Shots: Backhand | Forehand | BackhandVolley | ForehandVolley")
print("=" * 60)
print("\nStand in front of the camera and perform a padel shot")
print("Press 'q' to quit\n")

# ── Camera ─────────────────────────────────────────────────────
cap = cv2.VideoCapture(0)
frame_count = 0
FRAMES_PER_WINDOW = 23
WINDOW_SIZE = FRAMES_PER_WINDOW * POINTS_PER_FRAME  # 23 × 7 = 161 points
SHIFT_BY = POINTS_PER_FRAME * 2                      # remove 2 frames worth (14 points)
all_points = []
last_result = "Waiting..."
last_score = 0.0
last_time = time.time()

while cap.isOpened():
    ret, frame = cap.read()
    if not ret:
        print("Camera error")
        break

    frame = cv2.resize(frame, (640, 480))
    frame_count += 1

    try:
        RGB = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        results = pose.process(RGB)

        if results.pose_landmarks:
            h, w, _ = frame.shape
            for landmark in TRACKED_LANDMARKS:
                x = int(results.pose_landmarks.landmark[landmark].x * w)
                y = int(results.pose_landmarks.landmark[landmark].y * h)
                all_points.append(Point(x, y, 1))

            drawing_utils.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)

        # Every 23 frames, recognize
        if frame_count % FRAMES_PER_WINDOW == 0:
            if len(all_points) >= WINDOW_SIZE:
                window = all_points[-WINDOW_SIZE:]

                result = recognizer.recognize(window)
                template_name = result[0]
                score = result[1]
                action = GESTURE_MAP.get(template_name, "Unknown")

                last_result = f"{action} ({score:.2f})"
                last_score = score
                last_time = time.time()

                print(f"[{time.strftime('%H:%M:%S')}] Template: {template_name} | Shot: {action} | Score: {score:.3f}")

                sender.send(template_name, action, score)

                all_points = all_points[SHIFT_BY:]

            frame_count = 0

        # Display
        elapsed = time.time() - last_time
        if elapsed < 2.0 and last_score > 0.3:
            color = (0, 255, 0) if last_score > 0.5 else (0, 200, 255)
            cv2.putText(frame, last_result, (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, color, 3)
        else:
            cv2.putText(frame, "Perform a padel shot...", (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)

        cv2.putText(frame, f"Points: {len(all_points)}", (10, 460), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        cv2.imshow('Padel Shot Recognition', frame)

    except Exception as e:
        pass

    if cv2.waitKey(1) == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()
print("\nDone.")