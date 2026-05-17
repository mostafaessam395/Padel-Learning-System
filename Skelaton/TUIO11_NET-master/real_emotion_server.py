import cv2
import socket
import json
import time
import threading
from deepface import DeepFace

# ─── Shared state ────────────────────────────────────────────────────
latest_raw_emotion  = "neutral"   # updated every frame by camera thread
confirmed_emotion   = "neutral"   # stable for 5s — what we actually send

face_cascade = cv2.CascadeClassifier(
    cv2.data.haarcascades + 'haarcascade_frontalface_default.xml'
)

# ─── Camera / detection thread ───────────────────────────────────────
def emotion_detector_thread():
    global latest_raw_emotion
    cap = cv2.VideoCapture(0)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
    print("[Camera] Webcam opened. Starting expression detection...")

    while True:
        ret, frame = cap.read()
        if not ret:
            break

        gray  = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(60, 60))

        label = "No face"
        if len(faces) > 0:
            x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
            cv2.rectangle(frame, (x, y), (x + w, y + h), (0, 220, 0), 2)
            face_img = frame[y:y+h, x:x+w]

            try:
                result = DeepFace.analyze(
                    face_img,
                    actions=['emotion'],
                    enforce_detection=False,
                    detector_backend='skip',
                    silent=True
                )
                if isinstance(result, list):
                    result = result[0]
                dom = result.get('dominant_emotion', 'neutral')

                if dom in ['sad', 'angry', 'fear', 'disgust']:
                    latest_raw_emotion = 'sad'
                elif dom == 'neutral':
                    latest_raw_emotion = 'bored'
                elif dom in ['happy', 'surprise']:
                    latest_raw_emotion = 'happy'
                else:
                    latest_raw_emotion = 'bored'

                label = f"{dom} -> {latest_raw_emotion}"
            except KeyboardInterrupt:
                raise
            except Exception:
                pass  # keep last raw emotion

            cv2.putText(frame, label, (x, y - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 220, 0), 2)
        else:
            cv2.putText(frame, "No face detected", (30, 40),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 100, 255), 2)

        # Also show the confirmed emotion (what was actually sent)
        cv2.putText(frame, f"Sent: {confirmed_emotion}", (30, 450),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 0), 2)

        cv2.imshow('Padel AI - Emotion Camera', frame)
        if cv2.waitKey(1) & 0xFF == ord('q'):
            break

    cap.release()
    cv2.destroyAllWindows()


# ─── Socket / sender thread ──────────────────────────────────────────
def socket_server_thread():
    global confirmed_emotion
    host = '127.0.0.1'
    port = 5005

    STABLE_SECONDS = 5.0   # must hold the same emotion for 5s to confirm
    CHECK_INTERVAL = 0.5   # check twice per second

    last_sent      = "neutral"
    tracked        = "neutral"
    stable_since   = time.time()

    while True:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
                s.bind((host, port))
                s.listen()
                print(f"[Socket] Listening for C# app on port {port}...")
                conn, addr = s.accept()
                print(f"[Socket] Connected by {addr}")
                with conn:
                    while True:
                        now     = time.time()
                        current = latest_raw_emotion

                        # Reset stability timer if emotion changed
                        if current != tracked:
                            tracked      = current
                            stable_since = now
                            print(f"[Debounce] Emotion changed to '{current}', starting 5s timer...")

                        stable_duration = now - stable_since

                        # Only send if stable for 5 seconds AND different from last sent
                        if stable_duration >= STABLE_SECONDS and current != last_sent:
                            confirmed_emotion = current
                            data = json.dumps({"type": "emotion", "emotion": current}) + "\n"
                            conn.sendall(data.encode('utf-8'))
                            print(f"[Confirmed -> C#] {current}  (stable {stable_duration:.1f}s)")
                            last_sent = current

                        time.sleep(CHECK_INTERVAL)

        except KeyboardInterrupt:
            raise
        except Exception as e:
            print(f"[Socket] Disconnected: {e}. Waiting...")
            time.sleep(2)
            # Reset on reconnect
            last_sent    = "neutral"
            tracked      = "neutral"
            stable_since = time.time()


if __name__ == "__main__":
    threading.Thread(target=socket_server_thread, daemon=True).start()
    emotion_detector_thread()
