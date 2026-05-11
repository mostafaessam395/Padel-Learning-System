import socket
import json
import time

def send_emotion(conn, emotion):
    try:
        data = json.dumps({"type": "emotion", "emotion": emotion}) + "\n"
        conn.sendall(data.encode('utf-8'))
        print(f"Sent: {emotion}")
    except:
        pass

host = '127.0.0.1'
port = 5005

while True:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            s.bind((host, port))
            s.listen()
            print(f"Mock Expression Server listening on {port}")
            conn, addr = s.accept()
            with conn:
                print(f"Connected by {addr}")
                while True:
                    print("Simulating happy user...")
                    send_emotion(conn, "happy")
                    time.sleep(5)
                    
                    print("Simulating bored/sad user...")
                    send_emotion(conn, "bored")
                    time.sleep(5)
    except Exception as e:
        print(f"Disconnected, restarting server... {e}")
        time.sleep(2)

