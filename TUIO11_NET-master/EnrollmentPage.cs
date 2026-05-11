using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using TUIO;
using TuioDemo;

/// <summary>
/// Marker-driven new-user enrollment wizard.
/// Steps: 1 face capture → 2 name (rotation spelling) → 3 level → 4 gender → 5 confirm.
/// Cancellation: marker 20 at any step.
/// </summary>
public class EnrollmentPage : Form, TuioListener
{
    public enum Step { FaceCapture, Name, Level, Gender, Confirm, Done }

    private readonly TuioClient _tuio;
    private readonly FaceIDClient _faceClient;
    private readonly Action<UserData> _onCompleted;

    private Step _step = Step.FaceCapture;
    private readonly string _userId = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 8);
    private string _name = "";
    private int _letterIndex = 0;
    private string _level = "";
    private string _gender = "";

    // UI
    private Label _lblStepTitle;
    private Label _lblStepHint;
    private Label _lblBigText;
    private Label _lblAlphaStrip;
    private Label _lblName;
    private Panel _thumbsHost;

    // Capture state
    private System.Windows.Forms.Timer _captureCountdown;
    private int _captureRemaining = 0;
    private bool _waitingOnServer = false;
    private DateTime _enrollDeadline = DateTime.MinValue;
    private System.Windows.Forms.Timer _enrollWatchdog;
    private bool _step1AcceptReady = false;  // gate marker 4 until thumbnails appear

    // Marker debounce
    private int _lastMarkerId = -1;
    private float _lastMarker6Angle = float.NaN;
    private bool _completed = false;

    public EnrollmentPage(TuioClient tuio, FaceIDClient faceClient, Action<UserData> onCompleted)
    {
        _tuio = tuio;
        _faceClient = faceClient;
        _onCompleted = onCompleted;

        this.Text = "Enroll new player";
        this.WindowState = FormWindowState.Maximized;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;
        this.BackColor = AppSettings.PageBg;

        BuildUI();
        FaceIDRouter.OnServerReply += HandleServerReply;
        GestureRouter.OnGestureRecognized += HandleGestureRecognized;

        this.Shown += (s, e) =>
        {
            if (_tuio != null) _tuio.addTuioListener(this);
            EnterStep(Step.FaceCapture);
        };
        this.FormClosed += (s, e) =>
        {
            try { if (_tuio != null) _tuio.removeTuioListener(this); } catch { }
            FaceIDRouter.OnServerReply -= HandleServerReply;
            GestureRouter.OnGestureRecognized -= HandleGestureRecognized;
            _captureCountdown?.Stop();
            _captureCountdown?.Dispose();
            _enrollWatchdog?.Stop();
            _enrollWatchdog?.Dispose();

            // If the user closed via the window X without going through a marker path,
            // make sure the parent gets a "cancelled" callback exactly once.
            if (!_completed)
            {
                _completed = true;
                SendEnrollCancel();
                _onCompleted?.Invoke(null);
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────
    //  UI scaffolding
    // ─────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        _lblStepTitle = new Label
        {
            Text = "",
            Font = new Font("Arial", 26, FontStyle.Bold),
            ForeColor = AppSettings.TitleText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 50),
            Location = new Point(60, 70)
        };
        _lblStepHint = new Label
        {
            Text = "",
            Font = new Font("Arial", 13, FontStyle.Regular),
            ForeColor = AppSettings.SubText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 30),
            Location = new Point(60, 130)
        };
        _lblBigText = new Label
        {
            Text = "",
            Font = new Font("Arial", 36, FontStyle.Bold),
            ForeColor = AppSettings.AccentText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 180),
            Location = new Point(60, 180)
        };
        _lblName = new Label
        {
            Text = "",
            Font = new Font("Consolas", 42, FontStyle.Bold),
            ForeColor = AppSettings.TitleText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 70),
            Location = new Point(60, 220),
            Visible = false
        };
        _lblAlphaStrip = new Label
        {
            Text = "",
            Font = new Font("Consolas", 22, FontStyle.Regular),
            ForeColor = AppSettings.SubText,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Size = new Size(1100, 60),
            Location = new Point(60, 320),
            Visible = false
        };
        _thumbsHost = new Panel
        {
            Size = new Size(900, 180),
            Location = new Point(160, 400),
            BackColor = Color.Transparent,
            Visible = false
        };

        this.Controls.Add(_lblStepTitle);
        this.Controls.Add(_lblStepHint);
        this.Controls.Add(_lblBigText);
        this.Controls.Add(_lblName);
        this.Controls.Add(_lblAlphaStrip);
        this.Controls.Add(_thumbsHost);

        NavHelper.AddNavBar(this, "Enrollment", canGoBack: true);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Step dispatcher
    // ─────────────────────────────────────────────────────────────────
    private void EnterStep(Step s)
    {
        _step = s;
        _lblName.Visible = false;
        _lblAlphaStrip.Visible = false;
        _thumbsHost.Visible = false;
        _thumbsHost.Controls.Clear();

        switch (s)
        {
            case Step.FaceCapture:
                _lblStepTitle.Text = "1 / 5  —  Face Capture";
                _lblStepHint.Text  = "Look at the camera. I'll take 5 photos — turn your head slightly between each.";
                _lblBigText.Text   = "Get ready...";
                _step1AcceptReady  = false;
                StartCaptureCountdown();
                break;

            case Step.Name:
                _lblStepTitle.Text = "2 / 5  —  Name";
                _lblStepHint.Text  = "Marker 6 rotate = next letter   •   4 = pick   •   5 = backspace   •   7 = done   •   20 = cancel";
                _lblBigText.Text   = "";
                _lblName.Visible   = true;
                _lblAlphaStrip.Visible = true;
                _name = "";
                _letterIndex = 0;
                _lastMarker6Angle = float.NaN;
                RenderSpelling();
                break;

            case Step.Level:
                _lblStepTitle.Text = "3 / 5  —  Level";
                _lblStepHint.Text  = "Marker 3 = Beginner   •   4 = Intermediate   •   5 = Advanced   •   20 = back";
                _lblBigText.Text   = "Choose your level";
                break;

            case Step.Gender:
                _lblStepTitle.Text = "4 / 5  —  Gender";
                _lblStepHint.Text  = "Marker 3 = Male   •   4 = Female   •   5 = Skip   •   20 = back";
                _lblBigText.Text   = "Choose gender (optional)";
                break;

            case Step.Confirm:
                _lblStepTitle.Text = "5 / 5  —  Confirm";
                _lblStepHint.Text  = "Marker 7 = save   •   5 = start over   •   20 = cancel";
                _lblBigText.Text   = $"{_name}\n{MapLevelDisplay(_level)}  •  {(string.IsNullOrEmpty(_gender) ? "—" : _gender)}";
                break;

            case Step.Done:
                _lblStepTitle.Text = "Welcome!";
                _lblStepHint.Text  = "Profile saved. Loading your dashboard...";
                _lblBigText.Text   = _name;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Step 1: face capture
    // ─────────────────────────────────────────────────────────────────
    private void StartCaptureCountdown()
    {
        _captureRemaining = 3;
        _captureCountdown?.Stop();
        _captureCountdown = new System.Windows.Forms.Timer { Interval = 1000 };
        _captureCountdown.Tick += (s, e) =>
        {
            if (_captureRemaining > 0)
            {
                _lblBigText.Text = _captureRemaining.ToString();
                _captureRemaining--;
            }
            else
            {
                _captureCountdown.Stop();
                _lblBigText.Text = "Capturing 5 photos…";
                SendEnrollCommand();
            }
        };
        _captureCountdown.Start();
    }

    private void SendEnrollCommand()
    {
        if (_faceClient == null || !_faceClient.IsConnected)
        {
            _lblBigText.Text = "Face server offline.\nStart face_recognition_server.py and reopen.";
            return;
        }

        var cmd = new JObject
        {
            ["cmd"] = "enroll",
            ["userId"] = _userId,
            ["count"] = 5,
            ["interval_ms"] = 600
        };
        if (!_faceClient.SendCommand(cmd))
        {
            _lblBigText.Text = "Could not reach face server.";
            return;
        }

        _waitingOnServer = true;
        _enrollDeadline = DateTime.UtcNow.AddSeconds(15);
        _enrollWatchdog?.Stop();
        _enrollWatchdog = new System.Windows.Forms.Timer { Interval = 1000 };
        _enrollWatchdog.Tick += (s, e) =>
        {
            if (!_waitingOnServer) { _enrollWatchdog.Stop(); return; }
            if (DateTime.UtcNow >= _enrollDeadline)
            {
                _enrollWatchdog.Stop();
                _waitingOnServer = false;
                _lblBigText.Text = "Camera timed out.\nMarker 5 = retake, 20 = cancel.";
                _step1AcceptReady = false;
            }
        };
        _enrollWatchdog.Start();
    }

    private void HandleServerReply(JObject reply)
    {
        if (this.IsDisposed) return;
        string type   = reply["type"]?.ToString() ?? "";
        string userId = reply["userId"]?.ToString() ?? "";

        // Replies for enroll* are user-scoped; reload_done has no userId
        if (type == "reload_done") return;
        if (!string.Equals(userId, _userId, StringComparison.Ordinal)) return;

        if (type == "enroll_done")
        {
            int saved = reply["saved"]?.Value<int>() ?? 0;
            this.BeginInvoke((MethodInvoker)(() => OnEnrollSucceeded(saved)));
        }
        else if (type == "enroll_failed")
        {
            string reason = reply["reason"]?.ToString() ?? "unknown";
            this.BeginInvoke((MethodInvoker)(() =>
            {
                _waitingOnServer = false;
                _step1AcceptReady = false;
                _lblBigText.Text = $"Capture failed: {reason}.\nMarker 5 = retake, 20 = cancel.";
            }));
        }
        else if (type == "enroll_cancel_done")
        {
            // No-op; the parent has already moved on or restarted the step.
        }
    }

    private void OnEnrollSucceeded(int saved)
    {
        _waitingOnServer = false;
        _enrollWatchdog?.Stop();
        _lblBigText.Text = $"Captured {saved} photos.\nMarker 4 = keep  •  5 = retake  •  20 = cancel.";
        _step1AcceptReady = true;
        ShowThumbnails();
    }

    private void ShowThumbnails()
    {
        _thumbsHost.Visible = true;
        _thumbsHost.Controls.Clear();

        string dir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "face_images", _userId);
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.jpg").OrderBy(x => x).Take(5).ToArray();
        if (files.Length == 0) return;

        int gap = 20;
        int thumbW = (_thumbsHost.Width - gap * (files.Length + 1)) / Math.Max(files.Length, 1);
        int thumbH = _thumbsHost.Height - gap;
        for (int i = 0; i < files.Length; i++)
        {
            var pb = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Size = new Size(thumbW, thumbH),
                Location = new Point(gap + i * (thumbW + gap), 0)
            };
            try
            {
                using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                using (var img = Image.FromStream(fs))
                    pb.Image = new Bitmap(img);
            }
            catch { }
            _thumbsHost.Controls.Add(pb);
        }
    }

    private void SendEnrollCancel()
    {
        if (_faceClient == null || !_faceClient.IsConnected) return;
        try
        {
            var cmd = new JObject { ["cmd"] = "enroll_cancel", ["userId"] = _userId };
            _faceClient.SendCommand(cmd);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Step 2: name rotation-spelling
    // ─────────────────────────────────────────────────────────────────
    private void HandleRotation(float deg)
    {
        if (float.IsNaN(_lastMarker6Angle)) { _lastMarker6Angle = deg; return; }
        float delta = deg - _lastMarker6Angle;
        while (delta > 180f) delta -= 360f;
        while (delta < -180f) delta += 360f;
        const float STEP_DEG = 18f;
        if (Math.Abs(delta) < STEP_DEG) return;

        int steps = (int)(delta / STEP_DEG);
        _letterIndex = (((_letterIndex + steps) % 26) + 26) % 26;
        _lastMarker6Angle = deg;
        RenderSpelling();
    }

    private void HandleNameMarker(int id)
    {
        if (id == 4)
        {
            if (_name.Length >= 16) return;
            _name += (char)('A' + _letterIndex);
            RenderSpelling();
        }
        else if (id == 5)
        {
            if (_name.Length == 0) return;
            _name = _name.Substring(0, _name.Length - 1);
            RenderSpelling();
        }
        else if (id == 7)
        {
            if (string.IsNullOrWhiteSpace(_name))
            {
                _lblStepHint.Text = "Name cannot be empty — keep picking letters.";
                return;
            }
            EnterStep(Step.Level);
        }
    }

    private void RenderSpelling()
    {
        _lblName.Text = string.IsNullOrEmpty(_name) ? "_" : _name;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 26; i++)
        {
            if (i > 0) sb.Append(' ');
            char c = (char)('A' + i);
            if (i == _letterIndex) sb.Append('[').Append(c).Append(']');
            else                   sb.Append(c);
        }
        _lblAlphaStrip.Text = sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Hand-gesture handler — maps Circle/Checkmark/SwipeLeft/SwipeRight
    //  into step-appropriate actions. The universal fallback in
    //  GestureClient already covers HomePage and the trivial cases; this
    //  is for the things markers do that no single gesture maps onto
    //  (letter cycling, asymmetric level/gender picks).
    // ─────────────────────────────────────────────────────────────────
    private void HandleGestureRecognized(string name, float score)
    {
        if (this.IsDisposed || _completed) return;
        if (string.IsNullOrEmpty(name)) return;

        this.BeginInvoke((MethodInvoker)(() =>
        {
            if (this.IsDisposed || _completed) return;

            // Universal cancel — any step, Circle when we're in a step
            // that benefits from a quick exit (handled by individual cases
            // for nuance).
            switch (_step)
            {
                case Step.FaceCapture:
                    if (!_step1AcceptReady && name != "Circle" && name != "SwipeLeft") return;
                    if (name == "Checkmark")        EnterStep(Step.Name);                // keep
                    else if (name == "SwipeRight")  { SendEnrollCancel(); EnterStep(Step.FaceCapture); } // retake
                    else if (name == "SwipeLeft" || name == "Circle") CancelWizard();
                    break;

                case Step.Name:
                    if (name == "SwipeRight")
                    {
                        _letterIndex = (_letterIndex + 1) % 26;
                        RenderSpelling();
                    }
                    else if (name == "SwipeLeft")
                    {
                        _letterIndex = (((_letterIndex - 1) % 26) + 26) % 26;
                        RenderSpelling();
                    }
                    else if (name == "Checkmark") HandleNameMarker(4);  // commit
                    else if (name == "Circle")    HandleNameMarker(7);  // done
                    break;

                case Step.Level:
                    if (name == "SwipeLeft")       HandleLevelMarker(3); // Beginner
                    else if (name == "Checkmark")  HandleLevelMarker(4); // Intermediate
                    else if (name == "SwipeRight") HandleLevelMarker(5); // Advanced
                    else if (name == "Circle")     CancelWizard();
                    break;

                case Step.Gender:
                    if (name == "SwipeLeft")       HandleGenderMarker(3); // Male
                    else if (name == "Checkmark")  HandleGenderMarker(4); // Female
                    else if (name == "SwipeRight") HandleGenderMarker(5); // Skip
                    else if (name == "Circle")     CancelWizard();
                    break;

                case Step.Confirm:
                    if (name == "Checkmark")       HandleConfirmMarker(7); // save
                    else if (name == "SwipeLeft")  HandleConfirmMarker(5); // start over
                    else if (name == "Circle")     CancelWizard();
                    break;
            }
        }));
    }

    private void CancelWizard()
    {
        if (_completed) return;
        _completed = true;
        SendEnrollCancel();
        try { _onCompleted?.Invoke(null); } catch { }
        this.Close();
    }

    // ─────────────────────────────────────────────────────────────────
    //  Steps 3 + 4: level + gender
    // ─────────────────────────────────────────────────────────────────
    private void HandleLevelMarker(int id)
    {
        switch (id)
        {
            case 3: _level = "Primary";    break;
            case 4: _level = "Secondary";  break;
            case 5: _level = "HighSchool"; break;
            default: return;
        }
        EnterStep(Step.Gender);
    }

    private void HandleGenderMarker(int id)
    {
        switch (id)
        {
            case 3: _gender = "Male";   break;
            case 4: _gender = "Female"; break;
            case 5: _gender = "";       break;
            default: return;
        }
        EnterStep(Step.Confirm);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Step 5: confirm + save + auto-login
    // ─────────────────────────────────────────────────────────────────
    private void HandleConfirmMarker(int id)
    {
        if (id == 5)
        {
            // Start over: discard captured photos and restart wizard
            SendEnrollCancel();
            _name = "";
            _level = "";
            _gender = "";
            EnterStep(Step.FaceCapture);
            return;
        }
        if (id != 7) return;

        var user = new UserData
        {
            UserId      = _userId,
            BluetoothId = "",
            FaceId      = _userId,
            Name        = _name,
            Gender      = _gender ?? "",
            Age         = 0,
            Level       = _level,
            Role        = "Player",
            IsActive    = true,
            GazeProfile = new GazeProfile()
        };

        try
        {
            new UserService().AddUser(user);
        }
        catch (Exception ex)
        {
            _lblBigText.Text = "Save failed:\n" + ex.Message;
            return;
        }

        // Safety-net reload; the server already reloaded after enroll_done.
        try { _faceClient?.SendCommand(new JObject { ["cmd"] = "reload" }); }
        catch { }

        EnterStep(Step.Done);

        _completed = true;  // suppress the FormClosed "cancelled" callback
        var navTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        navTimer.Tick += (s, e) =>
        {
            navTimer.Stop();
            navTimer.Dispose();
            try { _onCompleted?.Invoke(user); } catch { }
            this.Close();
        };
        navTimer.Start();
    }

    private string MapLevelDisplay(string lvl)
    {
        if (lvl == "Primary")    return "Beginner";
        if (lvl == "Secondary")  return "Intermediate";
        if (lvl == "HighSchool") return "Advanced";
        return string.IsNullOrEmpty(lvl) ? "—" : lvl;
    }

    // ─────────────────────────────────────────────────────────────────
    //  TUIO listener — routes markers per current step
    // ─────────────────────────────────────────────────────────────────
    public void addTuioObject(TuioObject o)
    {
        int id = o.SymbolID;
        if (id == _lastMarkerId) return;
        _lastMarkerId = id;
        this.BeginInvoke((MethodInvoker)(() => RouteMarker(id)));
    }

    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == _lastMarkerId) _lastMarkerId = -1;
    }

    public void updateTuioObject(TuioObject o)
    {
        if (_step == Step.Name && o.SymbolID == 6)
        {
            float deg = o.Angle * 180f / (float)Math.PI;
            this.BeginInvoke((MethodInvoker)(() => HandleRotation(deg)));
        }
    }

    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    private void RouteMarker(int id)
    {
        if (_completed) return;

        if (id == 20)
        {
            // Cancel from anywhere
            _completed = true;
            SendEnrollCancel();
            try { _onCompleted?.Invoke(null); } catch { }
            this.Close();
            return;
        }

        switch (_step)
        {
            case Step.FaceCapture:
                if (_waitingOnServer) return;
                if (id == 4 && _step1AcceptReady) EnterStep(Step.Name);
                else if (id == 5) { SendEnrollCancel(); EnterStep(Step.FaceCapture); }
                break;
            case Step.Name:
                HandleNameMarker(id);
                break;
            case Step.Level:
                HandleLevelMarker(id);
                break;
            case Step.Gender:
                HandleGenderMarker(id);
                break;
            case Step.Confirm:
                HandleConfirmMarker(id);
                break;
        }
    }
}
