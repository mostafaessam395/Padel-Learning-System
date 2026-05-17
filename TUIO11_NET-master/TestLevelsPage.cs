using System;
using System.Drawing;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Test Levels page — marker-only navigation, no mouse/click interaction.
/// Marker 21 = Beginner, 22 = Intermediate, 23 = Advanced, 20 = Back.
/// </summary>
public class TestLevelsPage : Form, TuioListener
{
    private readonly TuioClient _tuioClient;
    private bool _pageOpen = false;

    public TestLevelsPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        Text           = "Test Levels — Admin";
        WindowState    = FormWindowState.Maximized;
        StartPosition  = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor      = PadelTheme.BgDeep;
        MinimumSize    = new Size(900, 600);
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
        var header = new GradientHeader
        {
            Title        = "Choose a Level",
            Subtitle     = "Marker-only navigation · Pick Beginner, Intermediate or Advanced to start",
            Icon         = "🎾",
            Height       = 118,
            GradientFrom = PadelTheme.PrimaryDeep,
            GradientTo   = PadelTheme.Accent,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        Controls.Add(header);

        var contentWrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        Controls.Add(contentWrapper);
        contentWrapper.BringToFront();

        var cardHolder = new Panel { BackColor = Color.Transparent };
        contentWrapper.Controls.Add(cardHolder);

        const int CARD_W = 300;
        const int CARD_H = 260;
        const int GAP    = 30;

        var defs = new[]
        {
            new LvlDef("🟢", "Beginner",     "Basic strokes and simple padel training for new players.",
                       Color.FromArgb(60, 200, 130), Color.FromArgb(110, 230, 170), "Marker 21", 21),
            new LvlDef("🔵", "Intermediate", "Faster reactions and advanced drills for developing players.",
                       PadelTheme.Primary,           PadelTheme.PrimarySoft,         "Marker 22", 22),
            new LvlDef("🔴", "Advanced",     "High-speed challenges and competition mode for experts.",
                       PadelTheme.Hot,               Color.FromArgb(255, 150, 170),  "Marker 23", 23),
        };

        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            int mid = d.Marker;
            var tile = new NavTile
            {
                Icon = d.Icon, Title = d.Title, Subtitle = d.Sub, Hint = d.Hint,
                AccentA = d.A, AccentB = d.B,
                Size = new Size(CARD_W, CARD_H),
                Location = new Point(i * (CARD_W + GAP), 0),
            };
            tile.Activated += (s, e) => Dispatch(mid);
            cardHolder.Controls.Add(tile);
        }

        int row1W = defs.Length * CARD_W + (defs.Length - 1) * GAP;

        var backTile = new NavTile
        {
            Icon = "🏠", Title = "Back to Dashboard", Subtitle = "Return to the Admin Dashboard.",
            Hint = "Marker 20",
            AccentA = PadelTheme.Hot, AccentB = PadelTheme.HotDeep,
            Size = new Size(CARD_W, 150),
            Location = new Point((row1W - CARD_W) / 2, CARD_H + GAP),
        };
        backTile.Activated += (s, e) => Dispatch(20);
        cardHolder.Controls.Add(backTile);

        cardHolder.Size = new Size(row1W, CARD_H + GAP + 150);

        Action center = () => {
            int x = Math.Max(20, (contentWrapper.ClientSize.Width  - cardHolder.Width)  / 2);
            int y = Math.Max(20, (contentWrapper.ClientSize.Height - cardHolder.Height) / 2);
            cardHolder.Location = new Point(x, y);
        };
        contentWrapper.Resize += (s, e) => center();
        Shown  += (s, e) => center();
        Resize += (s, e) => center();
    }

    private struct LvlDef
    {
        public string Icon, Title, Sub, Hint;
        public Color A, B;
        public int Marker;
        public LvlDef(string icon, string title, string sub, Color a, Color b, string hint, int marker)
        { Icon = icon; Title = title; Sub = sub; A = a; B = b; Hint = hint; Marker = marker; }
    }

    private void OpenLevel(string level)
    {
        if (_pageOpen || IsDisposed) return;
        _pageOpen = true;
        var dummyUser = new UserData { Name = "Admin Test", Level = level,
            GazeProfile = new GazeProfile() };
        var page = new LearningPage(dummyUser, _tuioClient);
        page.Shown      += (s, e) => GestureRouter.ClaimFocus(page);
        page.FormClosed += (s, e) => { _pageOpen = false; GestureRouter.ClaimFocus(this); };
        page.Show();
    }

    private void Dispatch(int id)
    {
        Console.WriteLine("[TestLevels] Marker detected: " + id);
        switch (id)
        {
            case 21: OpenLevel("Primary");    break;
            case 22: OpenLevel("Secondary");  break;
            case 23: OpenLevel("HighSchool"); break;
            case 20:
                Console.WriteLine("[TestLevels] Marker 20 → Back to Admin Dashboard");
                if (!IsDisposed) Close();
                break;
        }
    }

    private void HandleGestureMarker(int id)
    {
        if (!Visible || IsDisposed) return;
        if (!GestureRouter.HasFocus(this)) return;
        BeginInvoke((MethodInvoker)(() => Dispatch(id)));
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
