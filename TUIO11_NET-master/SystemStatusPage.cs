using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin-only system status page — animated pills, gradient header.
/// Navigation: Marker 35 = Refresh, Marker 20 = Back.
/// </summary>
public class SystemStatusPage : Form, TuioListener
{
    private readonly bool _btConnected;
    private readonly bool _tuioConnected;
    private readonly GestureClient _gestureRef;
    private readonly FaceIDClient  _faceRef;
    private readonly GazeClient    _gazeRef;
    private readonly TuioClient    _tuioClient;

    private readonly ContentService _svc = new ContentService();

    private bool _refreshCooldown = false;

    private GradientHeader _header;
    private Panel _scroller;

    public SystemStatusPage(
        bool btConnected,
        bool tuioConnected,
        GestureClient gestureRef  = null,
        FaceIDClient  faceRef     = null,
        GazeClient    gazeRef     = null,
        TuioClient    tuioClient  = null)
    {
        _btConnected   = btConnected;
        _tuioConnected = tuioConnected;
        _gestureRef    = gestureRef;
        _faceRef       = faceRef;
        _gazeRef       = gazeRef;
        _tuioClient    = tuioClient;

        this.Text           = "System Status — Admin";
        this.Size           = new Size(820, 720);
        this.StartPosition  = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;
        this.BackColor      = PadelTheme.BgDeep;
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BuildUI();

        Shown += (s, e) =>
        {
            if (_tuioClient != null) _tuioClient.addTuioListener(this);
            GestureRouter.ClaimFocus(this);
            GestureRouter.OnGestureMarker += HandleGestureMarker;
        };
        FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= HandleGestureMarker;
            GestureRouter.ReleaseFocus(this);
            if (_tuioClient != null) _tuioClient.removeTuioListener(this);
        };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PadelTheme.PaintAppBackdrop(this, e);
    }

    private void BuildUI()
    {
        this.Controls.Clear();

        _header = new GradientHeader
        {
            Title        = "System Status",
            Subtitle     = "Live snapshot of all subsystems · Marker 35 to refresh · Marker 20 to go back",
            Icon         = "📊",
            Height       = 118,
            GradientFrom = PadelTheme.PrimaryDeep,
            GradientTo   = PadelTheme.Hot,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        this.Controls.Add(_header);

        _scroller = new Panel
        {
            Dock      = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding   = new Padding(28, 22, 28, 24),
        };
        this.Controls.Add(_scroller);
        _scroller.BringToFront();

        var items = _svc.LoadAll();
        int activeCount = 0;
        foreach (var i in items) if (i.IsActive) activeCount++;
        string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "padel_content.json");
        bool jsonExists = File.Exists(jsonPath);

        int y = 0;
        AddDivider(ref y, "Connectivity");
        AddPill(ref y, "Bluetooth",         _btConnected,                       _btConnected ? "Admin device detected" : "Not connected");
        AddPill(ref y, "Admin Device",       _btConnected,                      _btConnected ? "E8:3A:12:40:1A:70 ✔" : "Not detected");
        AddPill(ref y, "TUIO Server",        _tuioConnected,                    _tuioConnected ? "Connected · port 3333" : "Not connected");

        y += 10;
        AddDivider(ref y, "AI Services");
        AddPill(ref y, "YOLO Tracking",      false,                             "Verify localhost:5003 manually");
        AddPill(ref y, "Face Recognition",   _faceRef != null && _faceRef.IsConnected,    _faceRef != null && _faceRef.IsConnected    ? "Connected · port 5001" : "Not connected");
        AddPill(ref y, "Gaze Tracking",      _gazeRef != null && _gazeRef.IsConnected,    _gazeRef != null && _gazeRef.IsConnected    ? "Connected · port 5002" : "Not connected");
        AddPill(ref y, "Gesture Tracking",   _gestureRef != null && _gestureRef.IsConnected, _gestureRef != null && _gestureRef.IsConnected ? "Connected · port 5000" : "Not connected");

        y += 10;
        AddDivider(ref y, "Content");
        AddPill(ref y, "Padel content JSON", jsonExists,                        jsonExists ? (items.Count + " items total") : "File missing — will be created on first use");
        AddPill(ref y, "Active items",       activeCount > 0,                   activeCount + " active drills/tactics");

        y += 18;

        // Action hint cards
        var refreshCard = new GlassCard
        {
            AccentTop = PadelTheme.Primary,
            AccentBot = PadelTheme.Accent,
            Size      = new Size(360, 80),
            Location  = new Point(0, y),
            Badge     = "Marker 35",
        };
        AddCardLabels(refreshCard, "🔄", "Refresh status", "Re-poll all subsystems live");
        _scroller.Controls.Add(refreshCard);

        var backCard = new GlassCard
        {
            AccentTop = PadelTheme.HotDeep,
            AccentBot = PadelTheme.Hot,
            Size      = new Size(360, 80),
            Location  = new Point(380, y),
            Badge     = "Marker 20",
        };
        AddCardLabels(backCard, "◀", "Back to admin", "Return to the admin dashboard");
        _scroller.Controls.Add(backCard);
    }

    private void AddDivider(ref int y, string text)
    {
        var d = new SectionDivider
        {
            Text     = text,
            Size     = new Size(_scroller.ClientSize.Width - 56, 26),
            Location = new Point(0, y),
            AccentColor = PadelTheme.Accent,
            Anchor   = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _scroller.Controls.Add(d);
        y += 32;
    }

    private void AddPill(ref int y, string label, bool ok, string detail)
    {
        var pill = new StatusPill
        {
            Label     = label,
            Value     = (ok ? "● " : "○ ") + detail,
            PillColor = ok ? PadelTheme.Ok : PadelTheme.Err,
            Pulse     = ok,
            Size      = new Size(_scroller.ClientSize.Width - 56, 46),
            Location  = new Point(0, y),
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _scroller.Controls.Add(pill);
        y += 52;
    }

    private void AddCardLabels(GlassCard card, string icon, string title, string sub)
    {
        var lblIcon = new Label
        {
            Text      = icon,
            Font      = new Font(PadelTheme.DisplayFamily, 22, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(48, 60),
            Location  = new Point(22, 10),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        card.Controls.Add(lblIcon);

        var lblTitle = new Label
        {
            Text      = title,
            Font      = new Font(PadelTheme.DisplayFamily, 13, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(280, 22),
            Location  = new Point(74, 14),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        card.Controls.Add(lblTitle);

        var lblSub = new Label
        {
            Text      = sub,
            Font      = new Font(PadelTheme.TextFamily, 9.5f, FontStyle.Regular),
            ForeColor = PadelTheme.TextMid,
            BackColor = Color.Transparent,
            AutoSize  = false,
            Size      = new Size(280, 36),
            Location  = new Point(74, 38),
            TextAlign = ContentAlignment.TopLeft,
        };
        card.Controls.Add(lblSub);
    }

    // ── Marker / Gesture handling ─────────────────────────────────────────

    private void HandleGestureMarker(int id)
    {
        if (!Visible || IsDisposed) return;
        if (!GestureRouter.HasFocus(this)) return;
        BeginInvoke((MethodInvoker)(() => Dispatch(id)));
    }

    private void Dispatch(int id)
    {
        if (id == 20)
        {
            Console.WriteLine("[SystemStatus] Marker 20 → Back");
            Close();
            return;
        }

        if (id == 35)
        {
            if (_refreshCooldown) return;
            _refreshCooldown = true;
            Console.WriteLine("[SystemStatus] Marker 35 → Refresh");
            BuildUI();

            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); _refreshCooldown = false; };
            t.Start();
        }
    }

    public void addTuioObject(TuioObject o)
    {
        if (!GestureRouter.HasFocus(this)) return;
        BeginInvoke((MethodInvoker)(() => Dispatch(o.SymbolID)));
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
}
