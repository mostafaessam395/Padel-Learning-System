"""
AI Vision Coach - YOLO Object Tracking Server (OpenCV DNN version)
===================================================================
Uses OpenCV's built-in DNN module with YOLOv4-tiny weights.
NO torch / ultralytics required — works with just opencv-python.

Port: 5003
Endpoints: /status  /frame  /start  /stop

Setup (already installed):
  pip install flask opencv-python numpy

Model files are downloaded automatically on first run to:
  FaceID/models/yolov4-tiny.cfg
  FaceID/models/yolov4-tiny.weights
  FaceID/models/coco.names
"""

import threading
import time
import os
import urllib.request
import numpy as np
import cv2
from flask import Flask, Response, jsonify

# ─── Configuration ────────────────────────────────────────────────────────────
PORT          = 5003
CAMERA_INDEX  = 0
CONF_THRESH   = 0.40
NMS_THRESH    = 0.45
INPUT_SIZE    = (416, 416)

# Model files directory (next to this script)
SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR   = os.path.join(SCRIPT_DIR, "models")
CFG_PATH    = os.path.join(MODEL_DIR, "yolov4-tiny.cfg")
WEIGHTS_PATH= os.path.join(MODEL_DIR, "yolov4-tiny.weights")
NAMES_PATH  = os.path.join(MODEL_DIR, "coco.names")

# COCO class IDs we care about
TARGET_CLASSES = {"person": "Player", "sports ball": "Ball", "tennis racket": "Racket"}

# Model download URLs
URLS = {
    CFG_PATH:     "https://raw.githubusercontent.com/AlexeyAB/darknet/master/cfg/yolov4-tiny.cfg",
    WEIGHTS_PATH: "https://github.com/AlexeyAB/darknet/releases/download/darknet_yolo_v4_pre/yolov4-tiny.weights",
    NAMES_PATH:   "https://raw.githubusercontent.com/AlexeyAB/darknet/master/data/coco.names",
}

# ─── Shared state ─────────────────────────────────────────────────────────────
_lock  = threading.Lock()
_state = {
    "running":     False,
    "objects":     [],
    "player_zone": "Unknown",
    "ball_zone":   "Unknown",
    "frame_bytes": None,
    "error":       None,
    "status_msg":  "Not started",
}

app           = Flask(__name__)
_stop_event   = threading.Event()
_user_stopped = threading.Event()   # set when user explicitly calls /stop
_thread       = None


# ─── Helpers ──────────────────────────────────────────────────────────────────
def ensure_models():
    """Download model files if missing."""
    os.makedirs(MODEL_DIR, exist_ok=True)
    for path, url in URLS.items():
        if not os.path.exists(path):
            fname = os.path.basename(path)
            print(f"[YOLO] Downloading {fname} ...")
            try:
                urllib.request.urlretrieve(url, path)
                print(f"[YOLO] {fname} downloaded.")
            except Exception as e:
                raise RuntimeError(f"Failed to download {fname}: {e}")


def get_zone(cx, cy, w, h):
    col = "Left"   if cx < w / 3 else ("Right" if cx > 2 * w / 3 else "Center")
    row = "Net Zone" if cy < h / 3 else ("Center Zone" if cy < 2 * h / 3 else "Back Court")
    return row if col == "Center" else f"{row} ({col})"


def make_placeholder(msg="No frame yet"):
    img = np.zeros((240, 420, 3), dtype=np.uint8)
    cv2.putText(img, msg, (20, 120), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 2)
    _, buf = cv2.imencode(".jpg", img)
    return buf.tobytes()


# ─── Tracking loop ────────────────────────────────────────────────────────────
def tracking_loop():
    try:
        with _lock:
            _state["status_msg"] = "Downloading model files..."

        ensure_models()

        with _lock:
            _state["status_msg"] = "Loading YOLO model..."

        # Load COCO class names
        with open(NAMES_PATH) as f:
            class_names = [l.strip() for l in f.readlines()]

        # Build target class id → display label map
        target_ids = {}
        for i, name in enumerate(class_names):
            if name in TARGET_CLASSES:
                target_ids[i] = TARGET_CLASSES[name]

        # Load network
        net = cv2.dnn.readNetFromDarknet(CFG_PATH, WEIGHTS_PATH)
        net.setPreferableBackend(cv2.dnn.DNN_BACKEND_OPENCV)
        net.setPreferableTarget(cv2.dnn.DNN_TARGET_CPU)
        out_layers = [net.getLayerNames()[i - 1]
                      for i in net.getUnconnectedOutLayers().flatten()]

        print("[YOLO] Model loaded. Opening camera...")

        # Try multiple backends/indexes to find a working camera
        cap = None
        backends = [cv2.CAP_DSHOW, cv2.CAP_MSMF, cv2.CAP_ANY]
        indexes  = [0, 1, 2]
        for idx in indexes:
            for backend in backends:
                attempt = cv2.VideoCapture(idx + backend)
                if attempt.isOpened():
                    # verify we can actually grab a frame
                    ok, _ = attempt.read()
                    if ok:
                        cap = attempt
                        print(f"[YOLO] Camera opened: index={idx} backend={backend}")
                        break
                    attempt.release()
            if cap:
                break

        if cap is None or not cap.isOpened():
            with _lock:
                _state["error"]      = "Cannot open camera."
                _state["status_msg"] = "Camera error"
                _state["running"]    = False
            return

        with _lock:
            _state["running"]    = True
            _state["error"]      = None
            _state["status_msg"] = "Running"

        print(f"[YOLO] Tracking started on port {PORT}.")

        BOX_COLORS = {"Player": (0, 200, 80), "Ball": (0, 140, 255), "Racket": (200, 60, 200)}

        _fail_count = 0
        while not _stop_event.is_set():
            ret, frame = cap.read()
            if not ret:
                _fail_count += 1
                if _fail_count > 30:
                    print("[YOLO] Too many frame failures, reopening camera...")
                    cap.release()
                    time.sleep(1)
                    # try to reopen
                    for idx in indexes:
                        for backend in backends:
                            attempt = cv2.VideoCapture(idx + backend)
                            if attempt.isOpened():
                                ok, _ = attempt.read()
                                if ok:
                                    cap = attempt
                                    _fail_count = 0
                                    print(f"[YOLO] Camera reopened: index={idx}")
                                    break
                                attempt.release()
                        if _fail_count == 0:
                            break
                    if _fail_count > 0:
                        break  # give up, watchdog will restart
                time.sleep(0.05)
                continue
            _fail_count = 0

            fh, fw = frame.shape[:2]

            # Run inference
            blob = cv2.dnn.blobFromImage(frame, 1/255.0, INPUT_SIZE,
                                         swapRB=True, crop=False)
            net.setInput(blob)
            outputs = net.forward(out_layers)

            boxes, confs, class_ids = [], [], []
            for out in outputs:
                for det in out:
                    scores = det[5:]
                    cid    = int(np.argmax(scores))
                    conf   = float(scores[cid])
                    if cid not in target_ids or conf < CONF_THRESH:
                        continue
                    cx, cy, bw, bh = det[:4]
                    x = int((cx - bw / 2) * fw)
                    y = int((cy - bh / 2) * fh)
                    w = int(bw * fw)
                    h = int(bh * fh)
                    boxes.append([x, y, w, h])
                    confs.append(conf)
                    class_ids.append(cid)

            # NMS
            indices = cv2.dnn.NMSBoxes(boxes, confs, CONF_THRESH, NMS_THRESH)
            indices = indices.flatten() if len(indices) > 0 else []

            objects      = []
            player_zone  = "Unknown"
            ball_zone    = "Unknown"
            annotated    = frame.copy()

            for i in indices:
                x, y, w, h = boxes[i]
                conf        = confs[i]
                label       = target_ids[class_ids[i]]
                color       = BOX_COLORS.get(label, (200, 200, 200))
                cx, cy      = x + w // 2, y + h // 2
                zone        = get_zone(cx, cy, fw, fh)

                if label == "Player":
                    player_zone = zone
                elif label == "Ball":
                    ball_zone = zone

                objects.append({"label": label, "confidence": round(conf, 2),
                                 "x": x, "y": y, "w": w, "h": h, "zone": zone})

                # Draw box + label
                cv2.rectangle(annotated, (x, y), (x + w, y + h), color, 2)
                txt = f"{label} {conf:.2f}"
                (tw, th), _ = cv2.getTextSize(txt, cv2.FONT_HERSHEY_SIMPLEX, 0.6, 2)
                cv2.rectangle(annotated, (x, y - th - 8), (x + tw + 4, y), color, -1)
                cv2.putText(annotated, txt, (x + 2, y - 4),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)

            # Zone grid overlay
            gc = (180, 180, 180)
            cv2.line(annotated, (fw // 3, 0),     (fw // 3, fh),     gc, 1)
            cv2.line(annotated, (2*fw//3, 0),     (2*fw//3, fh),     gc, 1)
            cv2.line(annotated, (0, fh // 3),     (fw, fh // 3),     gc, 1)
            cv2.line(annotated, (0, 2*fh//3),     (fw, 2*fh//3),     gc, 1)
            cv2.putText(annotated, "Net Zone",    (5, fh//6),        cv2.FONT_HERSHEY_SIMPLEX, 0.5, gc, 1)
            cv2.putText(annotated, "Center Zone", (5, fh//2),        cv2.FONT_HERSHEY_SIMPLEX, 0.5, gc, 1)
            cv2.putText(annotated, "Back Court",  (5, 5*fh//6),      cv2.FONT_HERSHEY_SIMPLEX, 0.5, gc, 1)

            _, buf = cv2.imencode(".jpg", annotated, [cv2.IMWRITE_JPEG_QUALITY, 75])

            with _lock:
                _state["objects"]      = objects
                _state["player_zone"]  = player_zone
                _state["ball_zone"]    = ball_zone
                _state["frame_bytes"]  = buf.tobytes()

            time.sleep(0.1)

    except Exception as e:
        print(f"[YOLO] Error: {e}")
        with _lock:
            _state["error"]      = str(e)
            _state["status_msg"] = f"Error: {e}"
            _state["running"]    = False
    finally:
        try:
            cap.release()
        except Exception:
            pass
        with _lock:
            _state["running"]    = False
            _state["status_msg"] = "Stopped"
        print("[YOLO] Tracking stopped.")


# ─── Flask routes ─────────────────────────────────────────────────────────────
@app.route("/status")
def status():
    with _lock:
        d = dict(_state)
        d.pop("frame_bytes", None)
    return jsonify(d)


@app.route("/frame")
def frame():
    with _lock:
        fb = _state.get("frame_bytes")
    if not fb:
        fb = make_placeholder("Waiting for camera...")
    return Response(fb, mimetype="image/jpeg")


@app.route("/start")
def start():
    global _thread, _stop_event
    with _lock:
        already = _state.get("running", False)
    if already:
        return jsonify({"status": "already running"})
    _user_stopped.clear()   # user explicitly wants to start
    _stop_event.clear()
    _thread = threading.Thread(target=tracking_loop, daemon=True)
    _thread.start()
    return jsonify({"status": "started"})


@app.route("/stop")
def stop():
    _user_stopped.set()   # prevent watchdog from restarting
    _stop_event.set()
    with _lock:
        _state["running"]    = False
        _state["status_msg"] = "Stopped"
    return jsonify({"status": "stopped"})
    return jsonify({"status": "stopped"})


# ─── Watchdog: auto-restart tracking if it dies ───────────────────────────────
def watchdog_loop():
    """Restart the tracking thread automatically if it stops unexpectedly."""
    global _thread
    while True:
        time.sleep(5)
        if _stop_event.is_set() or _user_stopped.is_set():
            continue  # intentionally stopped, don't restart
        if _thread is None or not _thread.is_alive():
            with _lock:
                was_running = _state.get("running", False)
                err = _state.get("error")
            print(f"[WATCHDOG] Tracking thread died (error={err}). Restarting in 3s...")
            time.sleep(3)
            _stop_event.clear()
            _thread = threading.Thread(target=tracking_loop, daemon=True)
            _thread.start()
            print("[WATCHDOG] Tracking thread restarted.")


# ─── Entry point ──────────────────────────────────────────────────────────────
if __name__ == "__main__":
    print("=" * 55)
    print("  AI Vision Coach - YOLO Tracking Server")
    print(f"  Port : {PORT}")
    print("  Model: YOLOv4-tiny (OpenCV DNN, no torch needed)")
    print("  Endpoints: /status  /frame  /start  /stop")
    print("=" * 55)

    # Auto-start tracking
    _stop_event.clear()
    _thread = threading.Thread(target=tracking_loop, daemon=True)
    _thread.start()

    # Start watchdog to auto-restart if tracking crashes
    _watchdog = threading.Thread(target=watchdog_loop, daemon=True)
    _watchdog.start()

    app.run(host="0.0.0.0", port=PORT, threaded=True)
