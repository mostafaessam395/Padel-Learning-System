import cv2
from mediapipe.python.solutions import drawing_utils
from mediapipe.python.solutions import pose as mp_pose
from dollarpy import Recognizer, Template, Point
import time
import json
import socket

class GestureSender:
    def __init__(self, host='127.0.0.1', port=5000):
        self.sock = None
        try:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.sock.connect((host, port))
            print(f"[TCP] Connected to {host}:{port}")
        except:
            print(f"[TCP] Could not connect — C# app may not be running")

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

# ── Initialize Pose ────────────────────────────────────────────
pose = mp_pose.Pose(
    min_detection_confidence=0.15,
    min_tracking_confidence=0.15 )

# ── GESTURE TO ACTION MAP ──────────────────────────────────────
GESTURE_MAP = {
    "RASwipeRight": "SwipeRight",
    "LASwipeRight": "SwipeRight",
    "RASwipeLeft":  "SwipeLeft",
    "LASwipeLeft":  "SwipeLeft",
    "Circle":       "Circle",
    "RACheckmark":  "Checkmark",
    "LACheckmark":  "Checkmark",
}

# ── Import all templates from TestDollar.Py ────────────────────
import sys
sys.path.insert(0, r"C:\Users\Yahya\Desktop\Padel-Learning-System-main\TUIO11_NET-master\DynamicPatterns")

from TestDollar import recognizer

print("\n" + "="*60)
print("REAL-TIME GESTURE RECOGNITION")
print("4 Actions: SwipeRight | SwipeLeft | Circle | Checkmark")
print("="*60)
print("\nStand in front of the camera and perform a gesture")
print("Press 'q' to quit\n")

# ── Camera Setup ───────────────────────────────────────────────
cap = cv2.VideoCapture(0)
frame_count = 0
WINDOW_SIZE = 46          # 23 frames × 2 wrists = 46 points
SHIFT_BY = 4              # remove 4 points (2 frames) from the front each recognition
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
            
            # Right wrist
            rx = int(results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].x * w)
            ry = int(results.pose_landmarks.landmark[mp_pose.PoseLandmark.RIGHT_WRIST].y * h)
            all_points.append(Point(rx, ry, 1))
            
            # Left wrist
            lx = int(results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].x * w)
            ly = int(results.pose_landmarks.landmark[mp_pose.PoseLandmark.LEFT_WRIST].y * h)
            all_points.append(Point(lx, ly, 1))
            
            # Draw skeleton
            drawing_utils.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)
        
        # Every 23 frames, recognize using current window
        if frame_count % 23 == 0:
            if len(all_points) >= WINDOW_SIZE:
                # Take the latest WINDOW_SIZE points
                window = all_points[-WINDOW_SIZE:]
                
                result = recognizer.recognize(window)
                template_name = result[0]
                score = result[1]
                action = GESTURE_MAP.get(template_name, "Unknown")
                
                last_result = f"{action} ({score:.2f})"
                last_score = score
                last_time = time.time()
                
                print(f"[{time.strftime('%H:%M:%S')}] Template: {template_name} | Action: {action} | Score: {score:.3f}")
                
                # Send to C# app
                sender.send(template_name, action, score)
                
                # Shift: remove oldest SHIFT_BY points
                all_points = all_points[SHIFT_BY:]
            
            frame_count = 0
        
        # Display result on screen
        elapsed = time.time() - last_time
        if elapsed < 2.0 and last_score > 0.3:
            color = (0, 255, 0) if last_score > 0.5 else (0, 200, 255)
            cv2.putText(frame, last_result, (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1.2, color, 3)
        else:
            cv2.putText(frame, "Perform a gesture...", (10, 40), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
        
        cv2.putText(frame, f"Points: {len(all_points)}", (10, 460), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        cv2.imshow('Padel Gesture Recognition', frame)
        
    except Exception as e:
        pass
    
    if cv2.waitKey(1) == ord('q'):
        break

cap.release()
cv2.destroyAllWindows()
print("\nDone.")