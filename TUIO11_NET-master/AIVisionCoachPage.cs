using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using TUIO;

/// <summary>
/// AI Vision Coach Page — full-screen responsive dashboard layout.
/// All YOLO integration, polling, feedback, and score logic is unchanged.
/// Only the UI layout and visual design have been improved.
/// </summary>
public class AIVisionCoachPage : Form, TuioListener
{
    // ── Constants ──────────────────────────────────────────────────────────
    private const string SERVER_BASE = "http://localhost:5003";
    private const int    POLL_MS     = 600;

    // ── Level / activity state ─────────────────────────────────────────────
    private readonly string _level;
    private string _activity;

    // ── TUIO ───────────────────────────────────────────────────────────────
    private readonly TuioClient _tuioClient;

    // ── HTTP client ────────────────────────────────────────────────────────
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    // ── Timers ─────────────────────────────────────────────────────────────
    private Timer _pollTimer;
    private bool  _polling      = false;
    private int   _pollFailCount = 0;

    // ── UI controls ────────────────────────────────────────────────────────
    private PictureBox picFeed;
    private Label      lblServerStatus;
    private Label      lblLevelVal;
    private Label      lblObjectsVal;
    private Label      lblPlayerZoneVal;
    private Label      lblBallZoneVal;
    private Label      lblFeedbackVal;
    private Label      lblScoreVal;
    private Panel      offlinePanel;
    // Activity selector (replaces ComboBox)
    private Panel[]    _activityCards;
    private int        _activityIndex = 0;
    // Marker debounce
    private DateTime   _lastMarkerAction = DateTime.MinValue;
    private const int  MARKER_COOLDOWN_MS = 1200;

    // ─────────────────────────────────────────────────────────────────────
    //  Constructor
    // ─────────────────────────────────────────────────────────────────────
    public AIVisionCoachPage(string level, TuioClient tuioClient = null)
    {
        _level      = level ?? "Beginner";
        _activity   = DefaultActivity(_level);
        _tuioClient = tuioClient;

        InitForm();
        BuildUI();
        SetupTimer();

        NavHelper.AddNavBar(this, "AI Vision Coach", true);

        // Register TUIO listener and GestureRouter only when the form is actually shown,
        // so we don't intercept events while LearningPage is still active.
        this.Shown += (s, e) =>
        {
            Console.WriteLine("[AIVisionCoach] Page shown — registering TUIO listener and gesture handler.");
            if (_tuioClient != null)
                _tuioClient.addTuioListener(this);
            GestureRouter.OnGestureMarker += HandleGestureMarker;
        };

        this.FormClosed += (s, e) =>
        {
            Console.WriteLine("[AIVisionCoach] Page closed — unregistering TUIO listener and gesture handler.");
            GestureRouter.OnGestureMarker -= HandleGestureMarker;
            if (_tuioClient != null)
                _tuioClient.removeTuioListener(this);
            _pollTimer?.Stop();
            _http?.Dispose();
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Gesture handler (mirrors every other page in the project)
    // ─────────────────────────────────────────────────────────────────────
    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed) return;
        Console.WriteLine($"[AIVisionCoach] Gesture marker detected: {markerId}");
        SafeInvoke(() => DispatchMarker(markerId));
    }

    private void DispatchMarker(int id)
    {
        if (id == 20) { Console.WriteLine("[AIVisionCoach] Marker 20 → Back"); this.Close(); return; }

        // Debounce for action markers
        if ((DateTime.Now - _lastMarkerAction).TotalMilliseconds < MARKER_COOLDOWN_MS) return;
        _lastMarkerAction = DateTime.Now;

        if (id == 31) { Console.WriteLine("[AIVisionCoach] Marker 31 → Start Tracking"); _ = OnStartTracking(); }
        if (id == 32) { Console.WriteLine("[AIVisionCoach] Marker 32 → Stop Tracking");  _ = OnStopTracking(); }
        if (id == 33) { Console.WriteLine("[AIVisionCoach] Marker 33 → Next Activity");  SelectActivity(+1); }
        if (id == 34) { Console.WriteLine("[AIVisionCoach] Marker 34 → Prev Activity");  SelectActivity(-1); }
    }

    /// <summary>
    /// Moves activity selection by +1 (next) or -1 (previous), wraps around.
    /// Updates _activity and repaints the activity cards.
    /// </summary>
    private void SelectActivity(int direction)
    {
        if (_activityCards == null || _activityCards.Length == 0) return;
        string[] activities = ActivitiesForLevel(_level);

        // Deselect current
        _activityCards[_activityIndex].BackColor = Color.FromArgb(225, 235, 250);
        var oldLbl = _activityCards[_activityIndex].Controls.OfType<Label>().FirstOrDefault();
        if (oldLbl != null) { oldLbl.ForeColor = Color.FromArgb(15, 48, 100); oldLbl.Font = new Font("Segoe UI", 10, FontStyle.Regular); }

        // Move
        _activityIndex = (_activityIndex + direction + activities.Length) % activities.Length;
        _activity = activities[_activityIndex];

        // Highlight new
        _activityCards[_activityIndex].BackColor = Color.FromArgb(30, 80, 180);
        var newLbl = _activityCards[_activityIndex].Controls.OfType<Label>().FirstOrDefault();
        if (newLbl != null) { newLbl.ForeColor = Color.White; newLbl.Font = new Font("Segoe UI", 10, FontStyle.Bold); }

        Console.WriteLine($"[AIVisionCoach] Activity selected: {_activity}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  TuioListener implementation
    // ─────────────────────────────────────────────────────────────────────
    public void addTuioObject(TuioObject o)
    {
        if (!this.IsHandleCreated || this.IsDisposed) return;
        Console.WriteLine($"[AIVisionCoach] TUIO marker: {o.SymbolID}");
        this.BeginInvoke((MethodInvoker)delegate { if (!this.IsDisposed) DispatchMarker(o.SymbolID); });
    }

    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c)    { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b)        { }
    public void updateTuioBlob(TuioBlob b)     { }
    public void removeTuioBlob(TuioBlob b)     { }
    public void refresh(TuioTime t)            { }

    // ─────────────────────────────────────────────────────────────────────
    //  Form init
    // ─────────────────────────────────────────────────────────────────────
    private void InitForm()
    {
        this.Text           = "AI Vision Coach";
        this.WindowState    = FormWindowState.Maximized;
        this.BackColor      = Color.FromArgb(224, 237, 252);
        this.DoubleBuffered = true;
        this.StartPosition  = FormStartPosition.CenterScreen;
        this.MinimumSize    = new Size(1100, 700);
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  BuildUI  — full-screen TableLayoutPanel dashboard
    // ─────────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        // ══════════════════════════════════════════════════════════════════
        //  ROOT layout: 3 rows — header | content | buttons
        // ══════════════════════════════════════════════════════════════════
        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 4,   // header | offline banner | content | buttons
            ColumnCount = 1,
            BackColor   = Color.Transparent,
            Padding     = new Padding(0),
            Margin      = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));   // offline banner
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));   // buttons
        this.Controls.Add(root);

        // ── 1. HEADER ─────────────────────────────────────────────────────
        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);

        // ── 2. OFFLINE BANNER ─────────────────────────────────────────────
        offlinePanel = BuildOfflineBanner();
        root.Controls.Add(offlinePanel, 0, 1);

        // ── 3. CONTENT (camera left + info right) ─────────────────────────
        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 1,
            ColumnCount = 2,
            BackColor   = Color.Transparent,
            Padding     = new Padding(12, 8, 12, 4),
            Margin      = new Padding(0),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
        };
        // 63% camera, 37% info
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        content.Controls.Add(BuildCameraCard(), 0, 0);
        content.Controls.Add(BuildInfoCard(),   1, 0);
        root.Controls.Add(content, 0, 2);

        // ── 4. BUTTON ROW ─────────────────────────────────────────────────
        root.Controls.Add(BuildButtonRow(), 0, 3);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Header
    // ─────────────────────────────────────────────────────────────────────
    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 48, 100),
            Padding   = new Padding(24, 0, 24, 0),
        };

        // Title
        var lblTitle = Lbl("🎾  AI Vision Coach", "Segoe UI", 20, FontStyle.Bold, Color.White);
        lblTitle.Location  = new Point(24, 10);
        lblTitle.Size      = new Size(520, 34);
        lblTitle.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(lblTitle);

        // Subtitle
        var lblSub = Lbl("YOLO Object Tracking  •  Real-Time Padel Coaching Feedback",
                         "Segoe UI", 10, FontStyle.Italic, Color.FromArgb(150, 195, 255));
        lblSub.Location  = new Point(28, 46);
        lblSub.Size      = new Size(620, 20);
        lblSub.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(lblSub);

        // Server status — anchored right
        lblServerStatus = Lbl("● Checking server...", "Segoe UI", 11, FontStyle.Bold,
                               Color.FromArgb(255, 200, 60));
        lblServerStatus.Size      = new Size(240, 76);
        lblServerStatus.TextAlign = ContentAlignment.MiddleRight;
        lblServerStatus.Anchor    = AnchorStyles.Top | AnchorStyles.Right;
        lblServerStatus.Location  = new Point(header.Width - 264, 0);
        header.Controls.Add(lblServerStatus);

        // Keep status label anchored on resize
        header.Resize += (s, e) =>
            lblServerStatus.Location = new Point(header.Width - 264, 0);

        return header;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Offline banner
    // ─────────────────────────────────────────────────────────────────────
    private Panel BuildOfflineBanner()
    {
        var panel = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(255, 243, 205),
            Padding   = new Padding(16, 0, 16, 0),
            Visible   = true,
        };

        var lbl = Lbl(
            "⚠   YOLO server is not running.  Place Marker 31 to Start Tracking after launching the server.",
            "Segoe UI", 10, FontStyle.Bold, Color.FromArgb(130, 70, 0));
        lbl.Dock      = DockStyle.Fill;
        lbl.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(lbl);

        return panel;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Camera card (left)
    // ─────────────────────────────────────────────────────────────────────
    private Panel BuildCameraCard()
    {
        var card = new RoundedShadowPanel
        {
            Dock            = DockStyle.Fill,
            CornerRadius    = 18,
            FillColor       = Color.FromArgb(10, 18, 42),
            BorderColor     = Color.FromArgb(40, 80, 140),
            BorderThickness = 1.5f,
            ShadowColor     = Color.FromArgb(40, 0, 0, 0),
            ShadowOffsetX   = 3,
            ShadowOffsetY   = 5,
            DrawGloss       = false,
            Margin          = new Padding(0, 0, 8, 0),
            Padding         = new Padding(10),
        };

        picFeed = new PictureBox
        {
            Dock      = DockStyle.Fill,
            SizeMode  = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(10, 18, 42),
        };

        // Placeholder label
        var lblWait = Lbl(
            "📷  Camera feed will appear here\nonce the YOLO server is running.",
            "Segoe UI", 13, FontStyle.Italic, Color.FromArgb(90, 130, 200));
        lblWait.Dock      = DockStyle.Fill;
        lblWait.TextAlign = ContentAlignment.MiddleCenter;
        picFeed.Controls.Add(lblWait);

        card.Controls.Add(picFeed);
        return card;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Info card (right)
    // ─────────────────────────────────────────────────────────────────────
    private Panel BuildInfoCard()
    {
        var card = new RoundedShadowPanel
        {
            Dock            = DockStyle.Fill,
            CornerRadius    = 18,
            FillColor       = Color.FromArgb(245, 250, 255),
            BorderColor     = Color.FromArgb(190, 215, 245),
            BorderThickness = 1.2f,
            ShadowColor     = Color.FromArgb(30, 0, 0, 0),
            ShadowOffsetX   = 3,
            ShadowOffsetY   = 5,
            DrawGloss       = false,
            Margin          = new Padding(8, 0, 0, 0),
            Padding         = new Padding(20, 16, 20, 16),
            AutoScroll      = true,
        };

        // Use a vertical flow panel inside so content stacks naturally
        var flow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents  = false,
            AutoScroll    = true,
            BackColor     = Color.Transparent,
            Padding       = new Padding(4, 0, 4, 0),
        };
        card.Controls.Add(flow);

        int cardW = 380; // approximate inner width for sizing rows

        // ── Level ─────────────────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Current Level", cardW));
        lblLevelVal = ValueLabel(_level, 16, LevelColor(_level), cardW);
        flow.Controls.Add(lblLevelVal);
        flow.Controls.Add(Spacer(cardW, 10));

        // ── Activity selector — TUIO-driven, no mouse/keyboard ───────────
        flow.Controls.Add(SectionLabel("Current Activity", cardW));
        flow.Controls.Add(Spacer(cardW, 2));

        // Hint label
        var actHint = new Label {
            Text = "Marker 33 = Next  •  Marker 34 = Previous",
            Font = new Font("Segoe UI", 8, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 130, 180),
            BackColor = Color.Transparent,
            AutoSize = false, Size = new Size(cardW, 16),
            Margin = new Padding(0, 0, 0, 4) };
        flow.Controls.Add(actHint);

        // Build one card per activity
        string[] activities = ActivitiesForLevel(_level);
        _activityIndex = Array.IndexOf(activities, _activity);
        if (_activityIndex < 0) _activityIndex = 0;
        _activityCards = new Panel[activities.Length];

        for (int ai = 0; ai < activities.Length; ai++)
        {
            int idx = ai;   // capture
            bool selected = idx == _activityIndex;

            var aCard = new Panel {
                Size = new Size(cardW, 30),
                BackColor = selected
                    ? Color.FromArgb(30, 80, 180)
                    : Color.FromArgb(225, 235, 250),
                Margin = new Padding(0, 0, 0, 3),
                Cursor = Cursors.Default };

            var aLbl = new Label {
                Text = activities[idx],
                Font = new Font("Segoe UI", 10, selected ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = selected ? Color.White : Color.FromArgb(15, 48, 100),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                BackColor = Color.Transparent };
            aCard.Controls.Add(aLbl);
            _activityCards[idx] = aCard;
            flow.Controls.Add(aCard);
        }
        flow.Controls.Add(Spacer(cardW, 8));

        // ── Detected objects ──────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Detected Objects", cardW));
        lblObjectsVal = ValueLabel("—", 13, Color.FromArgb(15, 48, 100), cardW);
        flow.Controls.Add(lblObjectsVal);
        flow.Controls.Add(Spacer(cardW, 10));

        // ── Player zone ───────────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Player Zone", cardW));
        lblPlayerZoneVal = ValueLabel("—", 13, Color.FromArgb(15, 48, 100), cardW);
        flow.Controls.Add(lblPlayerZoneVal);
        flow.Controls.Add(Spacer(cardW, 10));

        // ── Ball zone ─────────────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Ball Zone", cardW));
        lblBallZoneVal = ValueLabel("—", 13, Color.FromArgb(15, 48, 100), cardW);
        flow.Controls.Add(lblBallZoneVal);
        flow.Controls.Add(Spacer(cardW, 14));

        // ── Coach feedback box ────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Coach Feedback", cardW));

        var fbBox = new RoundedShadowPanel
        {
            CornerRadius    = 14,
            FillColor       = Color.FromArgb(220, 238, 255),
            BorderColor     = Color.FromArgb(160, 205, 245),
            BorderThickness = 1f,
            ShadowColor     = Color.FromArgb(0, 0, 0, 0),
            DrawGloss       = false,
            Size            = new Size(cardW, 80),
            Margin          = new Padding(0, 4, 0, 0),
        };
        lblFeedbackVal = Lbl("Waiting for tracking data...", "Segoe UI", 11,
                              FontStyle.Bold, Color.FromArgb(15, 48, 100));
        lblFeedbackVal.Dock      = DockStyle.Fill;
        lblFeedbackVal.TextAlign = ContentAlignment.MiddleCenter;
        fbBox.Controls.Add(lblFeedbackVal);
        flow.Controls.Add(fbBox);
        flow.Controls.Add(Spacer(cardW, 14));

        // ── Positioning score ─────────────────────────────────────────────
        flow.Controls.Add(SectionLabel("Positioning Score", cardW));
        lblScoreVal = ValueLabel("—", 28, Color.FromArgb(18, 130, 80), cardW);
        lblScoreVal.Font = new Font("Segoe UI", 28, FontStyle.Bold);
        flow.Controls.Add(lblScoreVal);

        return card;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Marker instruction row (replaces button row)
    // ─────────────────────────────────────────────────────────────────────
    private Panel BuildButtonRow()
    {
        var row = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 22, 46),
            Padding   = new Padding(20, 0, 20, 0),
        };

        var flow = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = Color.Transparent };

        var markerDefs = new (string Id, string Action, Color Clr)[] {
            ("31", "Start Tracking", Color.FromArgb(20, 135, 75)),
            ("32", "Stop Tracking",  Color.FromArgb(185, 35, 35)),
            ("33", "Next Activity",  Color.FromArgb(55, 125, 255)),
            ("34", "Prev Activity",  Color.FromArgb(175, 55, 220)),
            ("20", "Back",           Color.FromArgb(38, 72, 145)),
        };

        foreach (var (mid, action, clr) in markerDefs)
        {
            var card = new Panel {
                Size = new Size(160, 46), BackColor = Color.FromArgb(18, 28, 54),
                Margin = new Padding(0, 12, 14, 12), Cursor = Cursors.Default };
            card.Paint += (s, e) => {
                using (var pen = new System.Drawing.Pen(clr, 1.2f))
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                using (var b = new System.Drawing.SolidBrush(clr))
                    e.Graphics.FillRectangle(b, 0, 0, card.Width, 4);
            };
            card.Controls.Add(new Label {
                Text = $"▶  Marker {mid}",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = clr, BackColor = Color.Transparent,
                Location = new Point(8, 6), AutoSize = true });
            card.Controls.Add(new Label {
                Text = action,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(200, 215, 240), BackColor = Color.Transparent,
                Location = new Point(8, 24), AutoSize = true });
            flow.Controls.Add(card);
        }

        row.Controls.Add(flow);
        return row;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Timer
    // ─────────────────────────────────────────────────────────────────────
    private void SetupTimer()
    {
        _pollTimer = new Timer { Interval = POLL_MS };
        _pollTimer.Tick += async (s, e) =>
        {
            if (_polling) return;
            _polling = true;
            try   { await PollStatus(); await PollFrame(); }
            finally { _polling = false; }
        };
        _pollTimer.Start();
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Polling — UNCHANGED logic
    // ─────────────────────────────────────────────────────────────────────
    private async Task PollStatus()
    {
        try
        {
            string json = await _http.GetStringAsync(SERVER_BASE + "/status");
            JObject data = JObject.Parse(json);

            bool   running = data["running"]?.Value<bool>() ?? false;
            string pZone   = data["player_zone"]?.ToString() ?? "Unknown";
            string bZone   = data["ball_zone"]?.ToString()   ?? "Unknown";
            JArray objs    = data["objects"] as JArray;

            bool playerFound = false, ballFound = false;
            var parts = new System.Collections.Generic.List<string>();

            if (objs != null)
            {
                foreach (JObject o in objs)
                {
                    string lbl  = o["label"]?.ToString() ?? "";
                    double conf = o["confidence"]?.Value<double>() ?? 0;
                    parts.Add($"{lbl} ({conf:P0})");
                    if (lbl == "Player") playerFound = true;
                    if (lbl == "Ball")   ballFound   = true;
                }
            }

            string objStr   = parts.Count > 0 ? string.Join(", ", parts) : "None detected";
            string feedback = GenerateFeedback(_level, _activity, pZone, bZone, playerFound, ballFound);
            int    score    = GenerateScore(_level, _activity, pZone, playerFound);

            _pollFailCount = 0;

            SafeInvoke(() =>
            {
                offlinePanel.Visible = false;

                lblServerStatus.Text      = running ? "● YOLO Running" : "● Server Idle";
                lblServerStatus.ForeColor = running
                    ? Color.FromArgb(80, 220, 120)
                    : Color.FromArgb(255, 200, 60);

                lblLevelVal.Text      = _level;
                lblObjectsVal.Text    = objStr;
                lblPlayerZoneVal.Text = playerFound ? pZone : "Not detected";
                lblBallZoneVal.Text   = ballFound   ? bZone : "Not detected";
                lblFeedbackVal.Text   = feedback;
                lblFeedbackVal.ForeColor = Color.FromArgb(15, 48, 100);
                lblScoreVal.Text      = playerFound ? score + "%" : "—";
                lblScoreVal.ForeColor = ScoreColor(score);
            });
        }
        catch
        {
            _pollFailCount++;
            if (_pollFailCount < 3) return;

            SafeInvoke(() =>
            {
                offlinePanel.Visible = true;
                lblServerStatus.Text      = "● Server Offline";
                lblServerStatus.ForeColor = Color.FromArgb(255, 80, 80);
                lblFeedbackVal.Text       = "YOLO server is not running.\nClick \"Launch YOLO Server\" then \"Start Tracking\".";
                lblFeedbackVal.ForeColor  = Color.FromArgb(160, 60, 0);
            });
        }
    }

    private async Task PollFrame()
    {
        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(SERVER_BASE + "/frame");
            if (bytes == null || bytes.Length < 100) return;

            Image img;
            using (var ms = new MemoryStream(bytes))
                img = Image.FromStream(ms);

            SafeInvoke(() =>
            {
                var old = picFeed.Image;
                picFeed.Image = img;
                old?.Dispose();
            });
        }
        catch { /* server offline — silently skip */ }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Button handlers — UNCHANGED
    // ─────────────────────────────────────────────────────────────────────
    private async Task OnStartTracking()
    {
        try
        {
            await _http.GetStringAsync(SERVER_BASE + "/start");
            SafeInvoke(() =>
            {
                lblFeedbackVal.Text      = "Tracking started. Stand in front of the camera.";
                lblFeedbackVal.ForeColor = Color.FromArgb(15, 48, 100);
            });
        }
        catch
        {
            SafeInvoke(() =>
            {
                offlinePanel.Visible = true;
                lblFeedbackVal.Text      = "YOLO server is not running.\nPlace Marker 31 after launching the server.";
                lblFeedbackVal.ForeColor = Color.FromArgb(160, 60, 0);
            });
        }
    }

    private async Task OnStopTracking()
    {
        try { await _http.GetStringAsync(SERVER_BASE + "/stop"); }
        catch { /* ignore */ }
    }

    private void OnLaunchServer(object sender, EventArgs e)
    {
        string batPath = Path.Combine(Application.StartupPath, "..", "..", "run_yolo_server.bat");
        batPath = Path.GetFullPath(batPath);
        if (!File.Exists(batPath))
            batPath = Path.Combine(Application.StartupPath, "run_yolo_server.bat");

        if (File.Exists(batPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = batPath, UseShellExecute = true });
                SafeInvoke(() =>
                {
                    lblFeedbackVal.Text      = "YOLO server is starting...\nWait a few seconds, then click Start Tracking.";
                    lblFeedbackVal.ForeColor = Color.FromArgb(15, 48, 100);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not launch server:\n" + ex.Message,
                                "AI Vision Coach", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            MessageBox.Show(
                "run_yolo_server.bat not found.\n\nPlease open a terminal and run:\n" +
                "  pip install flask opencv-python numpy ultralytics\n" +
                "  python FaceID\\yolo_tracking_server.py",
                "AI Vision Coach", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Feedback logic — UNCHANGED
    // ─────────────────────────────────────────────────────────────────────
    private static string GenerateFeedback(string level, string activity,
                                           string playerZone, string ballZone,
                                           bool playerFound, bool ballFound)
    {
        if (!playerFound)
            return ballFound
                ? "Ball detected. Stand in front of the camera."
                : "No player detected. Stand in front of the camera.";

        string pz = playerZone ?? "";
        switch (level)
        {
            case "Beginner":
                switch (activity)
                {
                    case "Serve Practice":    return pz.Contains("Back Court") ? "Good serve position." : "Move behind the service line.";
                    case "Forehand Position": return pz.Contains("Center") ? "Good forehand position." : "Move to the center zone for forehand.";
                    case "Backhand Position": return pz.Contains("Center") ? "Good backhand position." : "Move to the center zone for backhand.";
                    case "Volley Position":   return pz.Contains("Net Zone") ? "Good volley position." : "Move closer to the net.";
                    default:                  return pz.Contains("Back Court") ? "Good beginner position." : "Move to the correct court zone.";
                }
            case "Intermediate":
                switch (activity)
                {
                    case "Wall Practice":      return pz.Contains("Back Court") ? "Prepare for the wall rebound." : "Read the rebound early.";
                    case "Court Positioning":  return pz.Contains("Center") ? "Recover to center." : "Recover to the center after the shot.";
                    case "Reaction Training":
                        if (ballFound && !pz.Contains("Center")) return "Move toward the ball direction.";
                        return pz.Contains("Center") ? "Good reaction position." : "React faster to the ball.";
                    default: return "Recover to center.";
                }
            case "Advanced":
                switch (activity)
                {
                    case "Smash Position":       return pz.Contains("Net Zone") ? "Good smash position." : "Prepare for smash or wall rebound.";
                    case "Net Control":          return pz.Contains("Net Zone") ? "Good attacking position." : "Control the net area.";
                    case "Competition Movement":
                        if (ballFound && !pz.Contains("Center")) return "Return to center after the shot.";
                        return pz.Contains("Net Zone") ? "Good attacking position." : "Return to center after the shot.";
                    default: return pz.Contains("Net Zone") ? "Good attacking position." : "Prepare for smash.";
                }
            default:
                return "Keep your body balanced.";
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Score logic — UNCHANGED
    // ─────────────────────────────────────────────────────────────────────
    private static int GenerateScore(string level, string activity, string playerZone, bool playerFound)
    {
        if (!playerFound) return 0;
        string pz = playerZone ?? "";
        switch (level)
        {
            case "Beginner":
                switch (activity)
                {
                    case "Serve Practice":  return pz.Contains("Back Court") ? 90 : pz.Contains("Center") ? 60 : 30;
                    case "Volley Position": return pz.Contains("Net Zone") ? 90 : pz.Contains("Center") ? 65 : 35;
                    default:                return pz.Contains("Center") ? 80 : 55;
                }
            case "Intermediate":
                switch (activity)
                {
                    case "Wall Practice":     return pz.Contains("Back Court") ? 88 : pz.Contains("Center") ? 65 : 40;
                    case "Court Positioning": return pz.Contains("Center") ? 92 : pz.Contains("Back Court") ? 60 : 45;
                    default:                  return pz.Contains("Center") ? 80 : 55;
                }
            case "Advanced":
                switch (activity)
                {
                    case "Smash Position": return pz.Contains("Net Zone") ? 95 : pz.Contains("Center") ? 65 : 35;
                    case "Net Control":    return pz.Contains("Net Zone") ? 95 : pz.Contains("Center") ? 60 : 30;
                    default:               return pz.Contains("Net Zone") ? 90 : pz.Contains("Center") ? 70 : 45;
                }
            default:
                return 50;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  Activity helpers — UNCHANGED
    // ─────────────────────────────────────────────────────────────────────
    private static string DefaultActivity(string level)
    {
        switch (level)
        {
            case "Intermediate": return "Court Positioning";
            case "Advanced":     return "Net Control";
            default:             return "Serve Practice";
        }
    }

    private static string[] ActivitiesForLevel(string level)
    {
        switch (level)
        {
            case "Intermediate": return new[] { "Wall Practice", "Court Positioning", "Reaction Training" };
            case "Advanced":     return new[] { "Smash Position", "Net Control", "Competition Movement" };
            default:             return new[] { "Serve Practice", "Forehand Position", "Backhand Position", "Volley Position" };
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UI helpers
    // ─────────────────────────────────────────────────────────────────────
    private static Label SectionLabel(string text, int width)
    {
        return new Label
        {
            Text      = text,
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(70, 100, 145),
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(width, 22),
            Margin    = new Padding(0, 6, 0, 2),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Label ValueLabel(string text, int fontSize, Color color, int width)
    {
        return new Label
        {
            Text      = text,
            Font      = new Font("Segoe UI", fontSize, FontStyle.Bold),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(width, fontSize + 14),
            Margin    = new Padding(0, 0, 0, 2),
            TextAlign = ContentAlignment.MiddleLeft,
        };
    }

    private static Panel Spacer(int width, int height)
        => new Panel { Size = new Size(width, height), BackColor = Color.Transparent, Margin = new Padding(0) };

    private static Label Lbl(string text, string font, int size, FontStyle style, Color color)
        => new Label
        {
            Text      = text,
            Font      = new Font(font, size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoSize  = false,
        };

    private static Button DashBtn(string text, Color back, Color fore)
    {
        var b = new Button
        {
            Text      = text,
            Size      = new Size(210, 46),
            Font      = new Font("Segoe UI", 12, FontStyle.Bold),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
        };
        b.FlatAppearance.BorderSize  = 0;
        b.FlatAppearance.BorderColor = back; // match button background — avoids Transparent which is unsupported on ButtonBase
        return b;
    }

    private static Color LevelColor(string level)
    {
        switch (level)
        {
            case "Intermediate": return Color.FromArgb(10, 120, 120);
            case "Advanced":     return Color.FromArgb(140, 80, 10);
            default:             return Color.FromArgb(15, 48, 100);
        }
    }

    private static Color ScoreColor(int score)
    {
        if (score >= 80) return Color.FromArgb(18, 130, 80);
        if (score >= 55) return Color.FromArgb(180, 130, 0);
        return Color.FromArgb(180, 40, 40);
    }

    private void SafeInvoke(Action a)
    {
        if (this.IsDisposed || !this.IsHandleCreated) return;
        try { this.Invoke((MethodInvoker)(() => a())); }
        catch { /* form closing */ }
    }
}
