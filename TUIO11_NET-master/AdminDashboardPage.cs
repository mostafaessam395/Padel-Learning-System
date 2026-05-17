using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Dashboard — full-screen, modern glass UI.
/// Opened automatically when admin Bluetooth MAC E8:3A:12:40:1A:70 is detected.
/// </summary>
public class AdminDashboardPage : Form, TuioListener
{
    private readonly TuioClient   _tuioClient;
    private readonly string       _adminName;
    private readonly bool         _btConnected;
    private readonly GestureClient _gestureRef;
    private readonly FaceIDClient  _faceRef;
    private readonly GazeClient    _gazeRef;

    // Gesture-focus cursor over the 4 nav tiles (markers 31..34).
    private readonly List<NavTile> _navTiles = new List<NavTile>();
    private int _gestureFocus = -1;
    private bool _gestureFocusActive = false;

    private GradientHeader _header;
    private Panel _contentWrapper;
    private Panel _cardHolder;
    private NavTile _backTile;

    public AdminDashboardPage(
        TuioClient    tuioClient,
        string        adminName   = "Admin",
        bool          btConnected = false,
        GestureClient gestureRef  = null,
        FaceIDClient  faceRef     = null,
        GazeClient    gazeRef     = null)
    {
        _tuioClient  = tuioClient;
        _adminName   = adminName;
        _btConnected = btConnected;
        _gestureRef  = gestureRef;
        _faceRef     = faceRef;
        _gazeRef     = gazeRef;

        this.Text            = "Admin Dashboard — Smart Padel Coaching System";
        this.WindowState     = FormWindowState.Maximized;
        this.StartPosition   = FormStartPosition.CenterScreen;
        this.DoubleBuffered  = true;
        this.BackColor       = PadelTheme.BgDeep;
        this.MinimumSize     = new Size(900, 600);
        this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        BuildUI();

        this.Shown += (s, e) =>
        {
            if (_tuioClient != null) _tuioClient.addTuioListener(this);
            GestureRouter.ClaimFocus(this);
            GestureRouter.OnGestureMarker += HandleGestureMarker;
            GestureRouter.OnGestureRecognized += HandleGestureName;
        };
        this.FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= HandleGestureMarker;
            GestureRouter.OnGestureRecognized -= HandleGestureName;
            GestureRouter.ReleaseFocus(this);
            if (_tuioClient != null) _tuioClient.removeTuioListener(this);
        };
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        PadelTheme.PaintAppBackdrop(this, e);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UI construction
    // ─────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Header ───────────────────────────────────────────────────────
        _header = new GradientHeader
        {
            Title        = "Admin Dashboard  ·  Welcome, " + _adminName,
            Subtitle     = "Marker-driven control · Bluetooth verified · No mouse required",
            Icon         = "🛡",
            Height       = 124,
            GradientFrom = PadelTheme.PrimaryDeep,
            GradientTo   = PadelTheme.Accent,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        this.Controls.Add(_header);

        // MAC badge floats over the header (top-right)
        var macBadge = new StatusPill
        {
            Label      = "Admin BT verified",
            Value      = "E8:3A:12:40:1A:70",
            PillColor  = PadelTheme.Ok,
            Height     = 38,
            Width      = 320,
            Pulse      = true,
        };
        this.Controls.Add(macBadge);
        macBadge.BringToFront();
        this.Resize += (s, e) =>
            macBadge.Location = new Point(this.ClientSize.Width - macBadge.Width - 28, 18);
        macBadge.Location = new Point(this.ClientSize.Width - macBadge.Width - 28, 18);

        // ── Content area ─────────────────────────────────────────────────
        _contentWrapper = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        this.Controls.Add(_contentWrapper);
        _contentWrapper.BringToFront();
        macBadge.BringToFront();

        _cardHolder = new Panel
        {
            BackColor = Color.Transparent,
            Anchor    = AnchorStyles.None,
        };
        _contentWrapper.Controls.Add(_cardHolder);

        // 4 primary nav tiles
        var tileDefs = new[]
        {
            new TileDef("📋", "Manage Content",
                "Add, edit and deactivate training content by level, marker and activity.",
                PadelTheme.Primary,    PadelTheme.PrimarySoft, "Marker 31"),

            new TileDef("🎾", "Test Levels",
                "Open Beginner, Intermediate and Advanced training pages for demo testing.",
                PadelTheme.Accent,     PadelTheme.AccentSoft,  "Marker 32"),

            new TileDef("🎯", "Test Markers",
                "Quickly open any marker page to verify navigation and content.",
                PadelTheme.Gold,       Color.FromArgb(255, 220, 130), "Marker 33"),

            new TileDef("📊", "System Status",
                "View Bluetooth, TUIO, YOLO, Face ID, Gaze and Gesture status live.",
                PadelTheme.Hot,        Color.FromArgb(255, 140, 170), "Marker 34"),
        };

        const int CARD_W   = 290;
        const int CARD_H   = 240;
        const int CARD_GAP = 26;

        for (int i = 0; i < tileDefs.Length; i++)
        {
            var d = tileDefs[i];
            int markerId = 31 + i;
            var tile = new NavTile
            {
                Icon     = d.Icon,
                Title    = d.Title,
                Subtitle = d.Sub,
                Hint     = d.Hint,
                AccentA  = d.AccentA,
                AccentB  = d.AccentB,
                Size     = new Size(CARD_W, CARD_H),
                Location = new Point(i * (CARD_W + CARD_GAP), 0),
            };
            tile.Activated += (s, e) => DispatchMarker(markerId);
            _cardHolder.Controls.Add(tile);
            _navTiles.Add(tile);
        }

        int row1Width = tileDefs.Length * CARD_W + (tileDefs.Length - 1) * CARD_GAP;

        // Back tile, centred on row 2 (smaller)
        _backTile = new NavTile
        {
            Icon     = "🏠",
            Title    = "Back to System",
            Subtitle = "Return to the main player home screen.",
            Hint     = "Marker 20",
            AccentA  = PadelTheme.Hot,
            AccentB  = PadelTheme.HotDeep,
            Size     = new Size(CARD_W, 150),
            Location = new Point((row1Width - CARD_W) / 2, CARD_H + CARD_GAP),
        };
        _backTile.Activated += (s, e) => GoBack();
        _cardHolder.Controls.Add(_backTile);

        _cardHolder.Size = new Size(row1Width, CARD_H + CARD_GAP + 150);

        _contentWrapper.Resize += (s, e) => CenterControl(_contentWrapper, _cardHolder);
        this.Shown            += (s, e) => CenterControl(_contentWrapper, _cardHolder);
        this.Resize           += (s, e) => CenterControl(_contentWrapper, _cardHolder);
    }

    private struct TileDef
    {
        public string Icon, Title, Sub, Hint;
        public Color AccentA, AccentB;
        public TileDef(string icon, string title, string sub, Color a, Color b, string hint)
        { Icon = icon; Title = title; Sub = sub; AccentA = a; AccentB = b; Hint = hint; }
    }

    private static void CenterControl(Control parent, Control child)
    {
        int x = Math.Max(20, (parent.ClientSize.Width  - child.Width)  / 2);
        int y = Math.Max(20, (parent.ClientSize.Height - child.Height) / 2);
        child.Location = new Point(x, y);
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private void OpenContentManager()
    {
        var page = new ContentManagerPage(_tuioClient);
        page.Shown      += (s, e) => GestureRouter.ClaimFocus(page);
        page.FormClosed += (s, e) => GestureRouter.ClaimFocus(this);
        page.Show();
    }
    private void OpenTestLevels()
    {
        var page = new TestLevelsPage(_tuioClient);
        page.Shown      += (s, e) => GestureRouter.ClaimFocus(page);
        page.FormClosed += (s, e) => GestureRouter.ClaimFocus(this);
        page.Show();
    }
    private void OpenTestMarkers()
    {
        var page = new TestMarkersPage(_tuioClient);
        page.Shown      += (s, e) => GestureRouter.ClaimFocus(page);
        page.FormClosed += (s, e) => GestureRouter.ClaimFocus(this);
        page.Show();
    }
    private void OpenSystemStatus()
    {
        var page = new SystemStatusPage(
            _btConnected, _tuioClient != null, _gestureRef, _faceRef, _gazeRef, _tuioClient);
        page.Shown      += (s, e) => GestureRouter.ClaimFocus(page);
        page.FormClosed += (s, e) => GestureRouter.ClaimFocus(this);
        page.Show();
    }
    private void GoBack() { this.Close(); }

    // ── TUIO ──────────────────────────────────────────────────────────────

    private void HandleGestureMarker(int id)
    {
        if (!this.Visible || this.IsDisposed) return;
        if (_gestureFocusActive && id != 20) return;
        this.BeginInvoke((MethodInvoker)(() => DispatchMarker(id)));
    }

    private void HandleGestureName(string name, float score)
    {
        if (!this.Visible || this.IsDisposed) return;
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;

                switch (name)
                {
                    case "SwipeRight":
                        _gestureFocusActive = true;
                        ApplyGestureFocus(_gestureFocus < 0 ? 0 : (_gestureFocus + 1) % _navTiles.Count);
                        break;

                    case "SwipeLeft":
                        if (!_gestureFocusActive)
                        {
                            GoBack();
                            return;
                        }
                        ApplyGestureFocus(((_gestureFocus - 1) % _navTiles.Count + _navTiles.Count) % _navTiles.Count);
                        break;

                    case "Checkmark":
                        if (_gestureFocus < 0) return;
                        DispatchMarker(31 + _gestureFocus);
                        break;

                    case "Circle":
                        GoBack();
                        break;
                }
            });
        }
        catch { }
    }

    private void ApplyGestureFocus(int newIndex)
    {
        if (_navTiles.Count == 0) return;
        if (_gestureFocus >= 0 && _gestureFocus < _navTiles.Count)
            _navTiles[_gestureFocus].SetHover(false);

        _gestureFocus = newIndex;

        if (_gestureFocus >= 0 && _gestureFocus < _navTiles.Count)
            _navTiles[_gestureFocus].SetHover(true);
    }

    private void DispatchMarker(int id)
    {
        switch (id)
        {
            case 31: OpenContentManager(); break;
            case 32: OpenTestLevels();     break;
            case 33: OpenTestMarkers();    break;
            case 34: OpenSystemStatus();   break;
            case 20: GoBack();             break;
        }
    }

    public void addTuioObject(TuioObject o)
    {
        if (!GestureRouter.HasFocus(this)) return;
        this.BeginInvoke((MethodInvoker)(() => DispatchMarker(o.SymbolID)));
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
