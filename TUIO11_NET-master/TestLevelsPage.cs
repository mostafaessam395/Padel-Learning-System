using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private bool _pageOpen = false;   // debounce: prevent opening multiple pages

    public TestLevelsPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        Text           = "Test Levels — Admin";
        WindowState    = FormWindowState.Maximized;
        StartPosition  = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BackColor      = Color.FromArgb(10, 16, 36);
        MinimumSize    = new Size(900, 600);

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

    // ─────────────────────────────────────────────────────────────────────
    private void BuildUI()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = Color.Transparent, Padding = new Padding(0), Margin = new Padding(0),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));  // header
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // content
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(outer);

        // ── Header ────────────────────────────────────────────────────────
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 24, 52) };
        header.Paint += (s, e) =>
        {
            using (var b = new LinearGradientBrush(header.ClientRectangle,
                Color.FromArgb(20, 36, 72), Color.FromArgb(10, 18, 44), 90f))
                e.Graphics.FillRectangle(b, header.ClientRectangle);
            using (var p = new Pen(Color.FromArgb(55, 100, 200), 2))
                e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.Controls.Add(new Label {
            Text = "🎾  Test Levels", Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false, Size = new Size(500, 44),
            Location = new Point(28, 12), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft });
        header.Controls.Add(new Label {
            Text = "Use TUIO Markers to open a level  •  Marker-Only Interaction  •  No mouse required",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(120, 165, 240), AutoSize = false,
            Size = new Size(800, 24), Location = new Point(30, 62),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });
        outer.Controls.Add(header, 0, 0);

        // ── Content: card holder centered ─────────────────────────────────
        var contentWrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        outer.Controls.Add(contentWrapper, 0, 1);

        var cardHolder = new Panel { BackColor = Color.Transparent, Anchor = AnchorStyles.None };
        contentWrapper.Controls.Add(cardHolder);

        const int CARD_W = 280;
        const int CARD_H = 260;
        const int GAP    = 28;

        var cards = new (string Icon, string Title, string Sub, Color Accent, string MarkerText)[]
        {
            ("🟢", "Beginner",
             "Basic strokes and\nsimple padel training\nfor new players",
             Color.FromArgb(60, 190, 110), "Place Marker 21 to open"),

            ("🔵", "Intermediate",
             "Faster reactions and\nadvanced drills for\ndeveloping players",
             Color.FromArgb(60, 130, 255), "Place Marker 22 to open"),

            ("🔴", "Advanced",
             "High-speed challenges\nand competition mode\nfor expert players",
             Color.FromArgb(220, 80, 60), "Place Marker 23 to open"),
        };

        for (int i = 0; i < cards.Length; i++)
        {
            var c = cards[i];
            var card = BuildCard(c.Icon, c.Title, c.Sub, c.Accent, c.MarkerText, CARD_W, CARD_H);
            card.Location = new Point(i * (CARD_W + GAP), 0);
            cardHolder.Controls.Add(card);
        }

        int row1W = cards.Length * CARD_W + (cards.Length - 1) * GAP;

        // Back card centered below
        var backCard = BuildCard("🏠", "Back to Dashboard",
            "Return to the\nAdmin Dashboard",
            Color.FromArgb(190, 55, 75), "Place Marker 20 to go back",
            CARD_W, 140);
        backCard.Location = new Point((row1W - CARD_W) / 2, CARD_H + GAP);
        cardHolder.Controls.Add(backCard);

        cardHolder.Size = new Size(row1W, CARD_H + GAP + 140);

        void Center() {
            int x = Math.Max(20, (contentWrapper.ClientSize.Width  - cardHolder.Width)  / 2);
            int y = Math.Max(20, (contentWrapper.ClientSize.Height - cardHolder.Height) / 2);
            cardHolder.Location = new Point(x, y);
        }
        contentWrapper.Resize += (s, e) => Center();
        Shown  += (s, e) => Center();
        Resize += (s, e) => Center();
    }

    // ─────────────────────────────────────────────────────────────────────
    private static Panel BuildCard(string icon, string title, string sub,
                                   Color accent, string markerText, int w, int h)
    {
        var card = new Panel { Size = new Size(w, h), BackColor = Color.Transparent,
            Cursor = Cursors.Default };

        var inner = new Panel { Size = new Size(w - 4, h - 4), Location = new Point(2, 2),
            BackColor = Color.FromArgb(22, 34, 66) };

        inner.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rc = new Rectangle(0, 0, inner.Width - 1, inner.Height - 1);
            using (var b = new LinearGradientBrush(rc,
                Color.FromArgb(28, 42, 80), Color.FromArgb(16, 26, 54), 90f))
            using (var path = RoundedRect(rc, 16))
                g.FillPath(b, path);
            using (var ab = new SolidBrush(accent))
            using (var path = RoundedRect(new Rectangle(0, 0, inner.Width - 1, 5), 3))
                g.FillPath(ab, path);
            using (var bp = new Pen(Color.FromArgb(50, accent.R, accent.G, accent.B), 1.5f))
            using (var path = RoundedRect(rc, 16))
                g.DrawPath(bp, path);
        };
        card.Controls.Add(inner);

        int pad = 18;
        inner.Controls.Add(new Label {
            Text = icon, Font = new Font("Segoe UI Emoji", 26),
            ForeColor = Color.White, AutoSize = false, Size = new Size(50, 50),
            Location = new Point(pad, 18), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter });
        inner.Controls.Add(new Label {
            Text = title, Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false,
            Size = new Size(w - pad * 2 - 60, 28), Location = new Point(pad + 56, 28),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });
        inner.Controls.Add(new Label {
            Text = sub, Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(155, 180, 225), AutoSize = false,
            Size = new Size(w - pad * 2, h - 130), Location = new Point(pad, 80),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.TopLeft });
        inner.Controls.Add(new Label {
            Text = "▶  " + markerText, Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = accent, AutoSize = false,
            Size = new Size(w - pad * 2, 30), Location = new Point(pad, h - 46),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });

        return card;
    }

    // ─────────────────────────────────────────────────────────────────────
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
        Console.WriteLine($"[TestLevels] Marker detected: {id}");
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

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X,         r.Y,          d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
        path.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
        path.CloseFigure();
        return path;
    }
}
