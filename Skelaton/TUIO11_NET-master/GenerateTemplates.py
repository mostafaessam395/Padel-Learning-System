import os
import cv2
from mediapipe.python.solutions import drawing_utils
from mediapipe.python.solutions import pose as mp_pose

pose = mp_pose.Pose(
    min_detection_confidence=0.5,
    min_tracking_confidence=0.5)

# ── Landmarks to track (full body) ─────────────────────────────
TRACKED_LANDMARKS = [
    mp_pose.PoseLandmark.NOSE,
    mp_pose.PoseLandmark.LEFT_SHOULDER,
    mp_pose.PoseLandmark.RIGHT_SHOULDER,
    mp_pose.PoseLandmark.LEFT_ELBOW,
    mp_pose.PoseLandmark.RIGHT_ELBOW,
    mp_pose.PoseLandmark.LEFT_WRIST,
    mp_pose.PoseLandmark.RIGHT_WRIST,
    mp_pose.PoseLandmark.LEFT_HIP,
    mp_pose.PoseLandmark.RIGHT_HIP,
    mp_pose.PoseLandmark.LEFT_KNEE,
    mp_pose.PoseLandmark.RIGHT_KNEE,
    mp_pose.PoseLandmark.LEFT_ANKLE,
    mp_pose.PoseLandmark.RIGHT_ANKLE,
]

def StartTest(directory):
    f = open("TemplatesDollar.Py", "a")
    for file_name in os.listdir(directory):
        if os.path.isfile(os.path.join(directory, file_name)):
            if file_name.endswith(".mp4"):
                f.write("\nresult = recognizer.recognize([")
                print(file_name)
                cap = cv2.VideoCapture(directory+"/"+file_name)
                cv2.namedWindow('Output', cv2.WINDOW_NORMAL)
                framecnt=0
                while cap.isOpened():
                    ret, frame = cap.read()
                    if not ret:
                        print("Can't receive frame (stream end?). Exiting ...")
                        break
                    frame = cv2.resize(frame, (480, 320))
                    framecnt+=1
                    try:
                        RGB = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                        results = pose.process(RGB)
                        image_hight, image_width, _ = frame.shape
                        for landmark in TRACKED_LANDMARKS:
                            x = str(int(results.pose_landmarks.landmark[landmark].x * image_width))
                            y = str(int(results.pose_landmarks.landmark[landmark].y * image_hight))
                            f.write("Point("+x+","+y+", 1),\n")
                        drawing_utils.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)
                        cv2.imshow('Output', frame)
                    except:
                        break
                    if cv2.waitKey(30) == ord('q'):
                        break
                f.write("])\n")   
                f.write("print(result)\n") 
                cap.release()
                cv2.destroyAllWindows()
    f.close()

def loop_files(directory):
    f = open(directory+"/TestDollar2.Py", "w")
    f.write("from dollarpy import Recognizer, Template, Point\n")
    recstring=""
    for file_name in os.listdir(directory):
        if os.path.isfile(os.path.join(directory, file_name)):
            if file_name.endswith(".mp4"):
                print(file_name)
                foo = file_name[:-4]
                recstring+=foo+","
                f.write(foo+" = Template('"+foo+"', [\n")
                cap = cv2.VideoCapture(directory+"/"+file_name)
                cv2.namedWindow('Output', cv2.WINDOW_NORMAL)
                framecnt=0
                skipped=0
                while cap.isOpened():
                    ret, frame = cap.read()
                    if not ret:
                        print("Video ended")
                        break
                    frame = cv2.resize(frame, (480, 320))
                    framecnt+=1
                    RGB = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                    results = pose.process(RGB)
                    
                    if results.pose_landmarks:
                        image_hight, image_width, _ = frame.shape
                        for landmark in TRACKED_LANDMARKS:
                            x = str(int(results.pose_landmarks.landmark[landmark].x * image_width))
                            y = str(int(results.pose_landmarks.landmark[landmark].y * image_hight))
                            f.write("Point("+x+","+y+", 1),\n")
                        drawing_utils.draw_landmarks(frame, results.pose_landmarks, mp_pose.POSE_CONNECTIONS)
                    else:
                        skipped+=1
                    
                    cv2.imshow('Output', frame)
                    if cv2.waitKey(30) == ord('q'):
                        break
                f.write("])\n")    
                cap.release()
                cv2.destroyAllWindows()
                print(f"  Done: {framecnt} frames ({skipped} skipped)")
    recstring = recstring[:-1]
    f.write("recognizer = Recognizer(["+recstring+"])\n")    
    f.close()
    print("\nDONE! TestDollar2.Py created.")

directory_path = r"C:\Users\Yahya\Desktop\Padel-Learning-System-main\TUIO11_NET-master\DynamicPatternsPadel"
loop_files(directory_path)