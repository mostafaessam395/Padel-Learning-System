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
    private GradientHeader _header;
    private StepIndicator  _stepDots;
    private GlassCard      _mainCard;
    private Label _lblBigText;
    private Label _lblAlphaStrip;
    private Label _lblName;
    private Panel _thumbsHost;
    private ProgressRing _ring;
    private GlassCard    _ringFrame;

    // Capture state
    private System.Windows.Forms.Timer _captureCountdown;
    private int _captureRemaining = 0;
    private bool _waitingOnServer = false;
    private DateTime _enrollDeadline = DateTime.MinValue;
    private System.Windows.Forms.Timer _enrollWatchdog;
    private bool _step1AcceptReady = false;

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
        this.BackColor = PadelTheme.BgDeep;
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BuildUI();
        FaceIDRouter.OnServerReply += HandleServerReply;
        GestureRouter.OnGestureRecognized += HandleGestureRecognized;

        this.Shown += (s, e) =>
        {
            if (_tuio != null) _tuio.addTuioListener(this);
            EnterStep(Step.FaceCapture);
            LayoutCard();
        };
        this.Resize += (s, e) => LayoutCard();
        this.FormClosed += (s, e) =>
        {
            try { if (_tuio != null) _tuio.removeTuioListener(this); } catch { }
            FaceIDRouter.OnServerReply -= HandleServerReply;
            GestureRouter.OnGestureRecognized -= HandleGestureRecognized;
            if (_captureCountdown != null) { _captureCountdown.Stop(); _captureCountdown.Dispose(); }
            if (_enrollWatchdog   != null) { _enrollWatchdog.Stop();   _enrollWatchdog.Dispose();   }

            if (!_completed)
            {
                _completed = true;
                SendEnrollCancel();
                if (_onCompleted != null) _onCompleted(null);
            }
        };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PadelTheme.PaintAppBackdrop(this, e);
    }

    // ─────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        _header = new GradientHeader
        {
            Title        = "New Player Enrollment",
            Subtitle     = "Marker-driven wizard · Marker 20 to cancel at any time",
            Icon         = "👤",
            Height       = 118,
            GradientFrom = PadelTheme.PrimaryDeep,
            GradientTo   = PadelTheme.Accent,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        this.Controls.Add(_header);

        _stepDots = new StepIndicator
        {
            StepCount = 5,
            Current   = 0,
            Size      = new Size(420, 36),
        };
        this.Controls.Add(_stepDots);

        _mainCard = new GlassCard
        {
            AccentTop = PadelTheme.Primary,
            AccentBot = PadelTheme.Accent,
            Hoverable = false,
            Size      = new Size(960, 520),
        };
        this.Controls.Add(_mainCard);

        _lblBigText = new Label
        {
            Text = "",
            Font = new Font(PadelTheme.DisplayFamily, 22, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Dock      = DockStyle.None,
        };
        _mainCard.Controls.Add(_lblBigText);

        _lblName = new Label
        {
            Text = "",
            Font = new Font(PadelTheme.MonoFamily, 36, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Visible = false,
        };
        _mainCard.Controls.Add(_lblName);

        _lblAlphaStrip = new Label
        {
            Text = "",
            Font = new Font(PadelTheme.MonoFamily, 20, FontStyle.Regular),
            ForeColor = PadelTheme.TextLo,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Visible = false,
        };
        _mainCard.Controls.Add(_lblAlphaStrip);

        _thumbsHost = new Panel
        {
            BackColor = Color.Transparent,
            Visible = false,
        };
        _mainCard.Controls.Add(_thumbsHost);

        _ringFrame = new GlassCard
        {
            AccentTop = PadelTheme.Accent,
            AccentBot = PadelTheme.AccentDeep,
            ShowAccent = false,
            Hoverable  = false,
            Size       = new Size(220, 220),
            Visible    = false,
        };
        _mainCard.Controls.Add(_ringFrame);

        _ring = new ProgressRing
        {
            ArcColor      = PadelTheme.Accent,
            Indeterminate = true,
            Caption       = "Capturing…",
            Thickness     = 12,
            Dock          = DockStyle.Fill,
        };
        _ringFrame.Controls.Add(_ring);

        NavHelper.AddNavBar(this, "Enrollment", canGoBack: true);
    }

    private void LayoutCard()
    {
        if (_mainCard == null) return;

        int top = _header.Bottom + 12;
        int stepW = 420, stepH = 36;
        _stepDots.Location = new Point((this.ClientSize.Width - stepW) / 2, top);

        int cardW = Math.Min(1100, this.ClientSize.Width - 60);
        int cardH = Math.Min(580, this.ClientSize.Height - top - stepH - 60);
        _mainCard.Size     = new Size(cardW, cardH);
        _mainCard.Location = new Point((this.ClientSize.Width - cardW) / 2, top + stepH + 12);

        // Lay out children inside the card
        int padX = 40, padY = 36;
        _lblBigText.Bounds    = new Rectangle(padX, padY, cardW - padX * 2, 100);
        _lblName.Bounds       = new Rectangle(padX, padY + 110, cardW - padX * 2, 80);
        _lblAlphaStrip.Bounds = new Rectangle(padX, padY + 200, cardW - padX * 2, 50);
        _thumbsHost.Bounds    = new Rectangle(padX, padY + 270, cardW - padX * 2, cardH - padY - 290);
        _ringFrame.Bounds     = new Rectangle((cardW - 220) / 2, padY + 130, 220, 220);
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
        _ringFrame.Visible = false;
        if (_ring != null) { _ring.Indeterminate = true; _ring.Caption = ""; }

        switch (s)
        {
            case Step.FaceCapture:
                _header.Title    = "Face Capture";
                _header.Subtitle = "Step 1 / 5 — turn your head slightly between each photo";
                _header.GradientFrom = PadelTheme.PrimaryDeep;
                _header.GradientTo   = PadelTheme.Primary;
                _mainCard.AccentTop  = PadelTheme.Primary;
                _mainCard.AccentBot  = PadelTheme.PrimarySoft;
                _stepDots.Current    = 0;
                _lblBigText.Text     = "Get ready…";
                _step1AcceptReady    = false;
                StartCaptureCountdown();
                break;

            case Step.Name:
                _header.Title    = "Your Name";
                _header.Subtitle = "Step 2 / 5 — rotate marker 6 = next letter · 4 pick · 5 backspace · 7 done";
                _header.GradientFrom = PadelTheme.Accent;
                _header.GradientTo   = PadelTheme.Primary;
                _mainCard.AccentTop  = PadelTheme.Accent;
                _mainCard.AccentBot  = PadelTheme.AccentSoft;
                _stepDots.Current    = 1;
                _lblBigText.Text     = "Spell your name";
                _lblName.Visible     = true;
                _lblAlphaStrip.Visible = true;
                _name = "";
                _letterIndex = 0;
                _lastMarker6Angle = float.NaN;
                RenderSpelling();
                break;

            case Step.Level:
                _header.Title    = "Skill Level";
                _header.Subtitle = "Step 3 / 5 — Marker 3 Beginner · 4 Intermediate · 5 Advanced · 20 back";
                _header.GradientFrom = PadelTheme.Gold;
                _header.GradientTo   = PadelTheme.Hot;
                _mainCard.AccentTop  = PadelTheme.Gold;
                _mainCard.AccentBot  = PadelTheme.Hot;
                _stepDots.Current    = 2;
                _lblBigText.Text     = "Choose your level";
                break;

            case Step.Gender:
                _header.Title    = "Gender (optional)";
                _header.Subtitle = "Step 4 / 5 — Marker 3 Male · 4 Female · 5 Skip · 20 back";
                _header.GradientFrom = PadelTheme.Hot;
                _header.GradientTo   = PadelTheme.HotDeep;
                _mainCard.AccentTop  = PadelTheme.Hot;
                _mainCard.AccentBot  = PadelTheme.HotDeep;
                _stepDots.Current    = 3;
                _lblBigText.Text     = "Choose gender (optional)";
                break;

            case Step.Confirm:
                _header.Title    = "Confirm";
                _header.Subtitle = "Step 5 / 5 — Marker 7 save · 5 start over · 20 cancel";
                _header.GradientFrom = PadelTheme.AccentDeep;
                _header.GradientTo   = PadelTheme.Accent;
                _mainCard.AccentTop  = PadelTheme.Accent;
                _mainCard.AccentBot  = PadelTheme.AccentSoft;
                _stepDots.Current    = 4;
                _lblBigText.Text     = _name + "\n" + MapLevelDisplay(_level) + "  ·  " + (string.IsNullOrEmpty(_gender) ? "—" : _gender);
                break;

            case Step.Done:
                _header.Title    = "Welcome aboard!";
                _header.Subtitle = "Profile saved — loading your dashboard…";
                _header.GradientFrom = PadelTheme.Accent;
                _header.GradientTo   = PadelTheme.AccentDeep;
                _stepDots.Current    = 4;
                _lblBigText.Text     = _name;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Step 1: face capture
    // ─────────────────────────────────────────────────────────────────
    private void StartCaptureCountdown()
    {
        _captureRemaining = 3;
        if (_captureCountdown != null) _captureCountdown.Stop();
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
                _lblBigText.Text   = "Capturing 5 photos…";
                _ringFrame.Visible = true;
                _ring.Indeterminate = true;
                _ring.Caption       = "📸";
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
            _ringFrame.Visible = false;
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
            _ringFrame.Visible = false;
            return;
        }

        _waitingOnServer = true;
        _enrollDeadline = DateTime.UtcNow.AddSeconds(15);
        if (_enrollWatchdog != null) _enrollWatchdog.Stop();
        _enrollWatchdog = new System.Windows.Forms.Timer { Interval = 1000 };
        _enrollWatchdog.Tick += (s, e) =>
        {
            if (!_waitingOnServer) { _enrollWatchdog.Stop(); return; }
            if (DateTime.UtcNow >= _enrollDeadline)
            {
                _enrollWatchdog.Stop();
                _waitingOnServer = false;
                _ringFrame.Visible = false;
                _lblBigText.Text  = "Camera timed out.\nMarker 5 = retake · 20 = cancel.";
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
                _ringFrame.Visible = false;
                _lblBigText.Text = "Capture failed: " + reason + ".\nMarker 5 = retake · 20 = cancel.";
            }));
        }
        else if (type == "enroll_cancel_done")
        {
            // No-op
        }
    }

    private void OnEnrollSucceeded(int saved)
    {
        _waitingOnServer = false;
        if (_enrollWatchdog != null) _enrollWatchdog.Stop();
        _ringFrame.Visible = false;
        _lblBigText.Text  = "Captured " + saved + " photos.\nMarker 4 = keep  ·  5 = retake  ·  20 = cancel.";
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

        int gap = 16;
        int thumbW = (_thumbsHost.Width - gap * (files.Length + 1)) / Math.Max(files.Length, 1);
        int thumbH = _thumbsHost.Height - gap;
        for (int i = 0; i < files.Length; i++)
        {
            var frame = new GlassCard
            {
                AccentTop = PadelTheme.Accent,
                AccentBot = PadelTheme.AccentDeep,
                ShowAccent = false,
                Hoverable  = false,
                Size       = new Size(thumbW, thumbH),
                Location   = new Point(gap + i * (thumbW + gap), 0),
            };
            var pb = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                Dock = DockStyle.Fill,
            };
            try
            {
                using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read))
                using (var img = Image.FromStream(fs))
                    pb.Image = new Bitmap(img);
            }
            catch { }
            frame.Controls.Add(pb);
            pb.Dock = DockStyle.Fill;
            frame.Padding = new Padding(10);
            _thumbsHost.Controls.Add(frame);
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
                _header.Subtitle = "Name cannot be empty — keep picking letters.";
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
    private void HandleGestureRecognized(string name, float score)
    {
        if (this.IsDisposed || _completed) return;
        if (string.IsNullOrEmpty(name)) return;

        this.BeginInvoke((MethodInvoker)(() =>
        {
            if (this.IsDisposed || _completed) return;

            switch (_step)
            {
                case Step.FaceCapture:
                    if (!_step1AcceptReady && name != "Circle" && name != "SwipeLeft") return;
                    if (name == "Checkmark")        EnterStep(Step.Name);
                    else if (name == "SwipeRight")  { SendEnrollCancel(); EnterStep(Step.FaceCapture); }
                    else if (name == "SwipeLeft" || name == "Circle") CancelWizard();
                    break;

                case Step.Name:
                    if (name == "SwipeRight")
                    {
                        _letterIndex = (_letterIndex + 1) % 26;
                        RenderSpelling();
                    }
                    else if (name == "SwipeLeft")  HandleNameMarker(5);
                    else if (name == "Checkmark")  HandleNameMarker(4);
                    else if (name == "Circle")
                    {
                        if (string.IsNullOrEmpty(_name)) CancelWizard();
                        else HandleNameMarker(7);
                    }
                    break;

                case Step.Level:
                    if (name == "SwipeLeft")       HandleLevelMarker(3);
                    else if (name == "Checkmark")  HandleLevelMarker(4);
                    else if (name == "SwipeRight") HandleLevelMarker(5);
                    else if (name == "Circle")     CancelWizard();
                    break;

                case Step.Gender:
                    if (name == "SwipeLeft")       HandleGenderMarker(3);
                    else if (name == "Checkmark")  HandleGenderMarker(4);
                    else if (name == "SwipeRight") HandleGenderMarker(5);
                    else if (name == "Circle")     CancelWizard();
                    break;

                case Step.Confirm:
                    if (name == "Checkmark")       HandleConfirmMarker(7);
                    else if (name == "SwipeLeft")  HandleConfirmMarker(5);
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
        try { if (_onCompleted != null) _onCompleted(null); } catch { }
        this.Close();
    }

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

    private void HandleConfirmMarker(int id)
    {
        if (id == 5)
        {
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

        try { if (_faceClient != null) _faceClient.SendCommand(new JObject { ["cmd"] = "reload" }); }
        catch { }

        EnterStep(Step.Done);

        _completed = true;
        var navTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        navTimer.Tick += (s, e) =>
        {
            navTimer.Stop();
            navTimer.Dispose();
            try { if (_onCompleted != null) _onCompleted(user); } catch { }
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
            _completed = true;
            SendEnrollCancel();
            try { if (_onCompleted != null) _onCompleted(null); } catch { }
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
            case Step.Name:    HandleNameMarker(id);    break;
            case Step.Level:   HandleLevelMarker(id);   break;
            case Step.Gender:  HandleGenderMarker(id);  break;
            case Step.Confirm: HandleConfirmMarker(id); break;
        }
    }
}

namespace TuioDemo
{
    // 5-dot step indicator with smooth fill transition.
    public class StepIndicator : Control
    {
        private int _count   = 5;
        private int _current = 0;
        private float _animValue;
        private readonly System.Windows.Forms.Timer _t;

        public int StepCount { get { return _count; } set { _count = Math.Max(1, value); Invalidate(); } }
        public int Current
        {
            get { return _current; }
            set
            {
                int v = Math.Max(0, Math.Min(_count - 1, value));
                if (v == _current) return;
                _current = v;
                _t.Start();
            }
        }

        public StepIndicator()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(420, 36);

            _t = new System.Windows.Forms.Timer { Interval = 16 };
            _t.Tick += (s, e) =>
            {
                float target = _current;
                float d = (target - _animValue) * 0.2f;
                if (Math.Abs(d) < 0.005f) { _animValue = target; _t.Stop(); }
                else _animValue += d;
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);

            int dotR = 9;
            int spacing = (Width - 40) / Math.Max(1, _count - 1);
            int y = Height / 2;
            int startX = 20;

            // line behind
            using (var pen = new Pen(Color.FromArgb(60, 80, 110, 160), 2))
                g.DrawLine(pen, startX, y, startX + spacing * (_count - 1), y);

            // animated progress line
            int progEnd = startX + (int)(_animValue * spacing);
            using (var br = new LinearGradientBrush(
                new Rectangle(startX, y - 2, Math.Max(1, progEnd - startX), 4),
                PadelTheme.Accent, PadelTheme.Primary, LinearGradientMode.Horizontal))
                g.FillRectangle(br, startX, y - 2, Math.Max(1, progEnd - startX), 4);

            for (int i = 0; i < _count; i++)
            {
                int cx = startX + i * spacing;
                bool active = i <= _current;
                bool current = i == _current;
                Color fill = active ? PadelTheme.Accent : PadelTheme.BgPanelAlt;
                Color stroke = active ? PadelTheme.AccentSoft : Color.FromArgb(140, 80, 100, 140);

                int r = current ? dotR + 4 : dotR;
                using (var br = new SolidBrush(fill))
                    g.FillEllipse(br, cx - r, y - r, r * 2, r * 2);
                using (var pen = new Pen(stroke, current ? 2.2f : 1.2f))
                    g.DrawEllipse(pen, cx - r, y - r, r * 2, r * 2);

                using (var f = new Font(PadelTheme.TextFamily, 8.5f, FontStyle.Bold))
                using (var br = new SolidBrush(active ? Color.White : PadelTheme.TextMuted))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString((i + 1).ToString(), f, br, new Rectangle(cx - r, y - r, r * 2, r * 2), sf);
            }
        }
    }
}
