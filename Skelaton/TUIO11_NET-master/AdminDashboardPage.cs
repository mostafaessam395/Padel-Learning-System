using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Dashboard — full-screen, TableLayoutPanel-based grid.
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
        this.BackColor       = Color.FromArgb(10, 16, 36);
        this.MinimumSize     = new Size(900, 600);

        BuildUI();

        this.Shown += (s, e) =>
        {
            if (_tuioClient != null) _tuioClient.addTuioListener(this);
            GestureRouter.OnGestureMarker += HandleGestureMarker;
        };
        this.FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= HandleGestureMarker;
            if (_tuioClient != null) _tuioClient.removeTuioListener(this);
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    //  UI construction
    // ─────────────────────────────────────────────────────────────────────

    private void BuildUI()
    {
        // ── Outer layout: header on top, content fills the rest ──────────
        var outer = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 2,
            ColumnCount = 1,
            BackColor   = Color.Transparent,
            Padding     = new Padding(0),
            Margin      = new Padding(0),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));   // header
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // content
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.Controls.Add(outer);

        // ── Header ───────────────────────────────────────────────────────
        outer.Controls.Add(BuildHeader(), 0, 0);

        // ── Content area: vertically + horizontally centered ─────────────
        var contentWrapper = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        outer.Controls.Add(contentWrapper, 0, 1);

        // We use a Panel inside that auto-sizes to its children and is
        // centered via Anchor + manual positioning on resize.
        var cardHolder = new Panel
        {
            BackColor = Color.Transparent,
            Anchor    = AnchorStyles.None,   // centered by the resize handler
        };
        contentWrapper.Controls.Add(cardHolder);

        // Build the 5 cards
        var cardDefs = new (string Icon, string Title, string Sub, Color Accent, string Marker)[]
        {
            ("📋", "Manage Content",
             "Add, edit and deactivate\ntraining content by level,\nmarker and activity",
             Color.FromArgb(55, 125, 255), "Place Marker 31 to open"),

            ("🎾", "Test Levels",
             "Open Beginner, Intermediate\nor Advanced training pages\nfor demo testing",
             Color.FromArgb(50, 185, 105), "Place Marker 32 to open"),

            ("🎯", "Test Markers",
             "Quickly open any marker\npage to verify navigation\nand content",
             Color.FromArgb(220, 140, 40), "Place Marker 33 to open"),

            ("📊", "System Status",
             "View Bluetooth, TUIO, YOLO,\nFace ID, Gaze, Gesture\nand content status",
             Color.FromArgb(175, 55, 220), "Place Marker 34 to open"),
        };

        const int CARD_W   = 280;
        const int CARD_H   = 260;
        const int CARD_GAP = 28;

        // Row 1: 4 cards
        for (int i = 0; i < cardDefs.Length; i++)
        {
            var c = cardDefs[i];
            var card = BuildCard(c.Icon, c.Title, c.Sub, c.Accent, c.Marker, CARD_W, CARD_H);
            card.Location = new Point(i * (CARD_W + CARD_GAP), 0);
            cardHolder.Controls.Add(card);
        }

        // Row 2: Back card centered under the 4 cards
        int row1Width = cardDefs.Length * CARD_W + (cardDefs.Length - 1) * CARD_GAP;
        var backCard  = BuildCard("🏠", "Back to System",
            "Return to the main\nplayer home screen",
            Color.FromArgb(190, 55, 75), "Place Marker 20 to go back",
            CARD_W, 140);
        backCard.Location = new Point((row1Width - CARD_W) / 2, CARD_H + CARD_GAP);
        cardHolder.Controls.Add(backCard);

        // Total holder size
        cardHolder.Size = new Size(row1Width, CARD_H + CARD_GAP + 140);

        // Center cardHolder whenever the wrapper resizes
        contentWrapper.Resize += (s, e) => CenterControl(contentWrapper, cardHolder);
        this.Shown            += (s, e) => CenterControl(contentWrapper, cardHolder);
        this.Resize           += (s, e) => CenterControl(contentWrapper, cardHolder);
    }

    private static void CenterControl(Control parent, Control child)
    {
        int x = Math.Max(20, (parent.ClientSize.Width  - child.Width)  / 2);
        int y = Math.Max(20, (parent.ClientSize.Height - child.Height) / 2);
        child.Location = new Point(x, y);
    }

    // ── Header ────────────────────────────────────────────────────────────

    private Panel BuildHeader()
    {
        var header = new Panel
        {
            Dock      = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 24, 52),
        };

        // Gradient + bottom border via Paint
        header.Paint += (s, e) =>
        {
            var g  = e.Graphics;
            var rc = header.ClientRectangle;
            using (var b = new LinearGradientBrush(rc,
                Color.FromArgb(20, 36, 72), Color.FromArgb(10, 18, 44), 90f))
                g.FillRectangle(b, rc);
            using (var p = new Pen(Color.FromArgb(55, 100, 200), 2))
                g.DrawLine(p, 0, rc.Height - 1, rc.Width, rc.Height - 1);
        };

        // Shield icon
        var lblIcon = new Label
        {
            Text      = "🛡",
            Font      = new Font("Segoe UI Emoji", 28, FontStyle.Regular),
            ForeColor = Color.White,
            AutoSize  = false,
            Size      = new Size(56, 80),
            Location  = new Point(28, 10),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        var lblTitle = new Label
        {
            Text      = $"Admin Dashboard  —  Welcome, {_adminName}",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            Size      = new Size(800, 44),
            Location  = new Point(92, 14),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var lblSub = new Label
        {
            Text      = "Use TUIO Markers to navigate  •  Marker-Only Interaction  •  No mouse required",
            Font      = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(120, 165, 240),
            AutoSize  = false,
            Size      = new Size(800, 26),
            Location  = new Point(94, 60),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // MAC badge top-right
        var lblMac = new Label
        {
            Text      = "🔵  E8:3A:12:40:1A:70  ✔",
            Font      = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 210, 100),
            AutoSize  = true,
            BackColor = Color.Transparent,
            Anchor    = AnchorStyles.Top | AnchorStyles.Right,
        };
        header.Controls.Add(lblMac);
        header.Resize += (s, e) =>
            lblMac.Location = new Point(header.Width - lblMac.Width - 24, 38);

        header.Controls.Add(lblIcon);
        header.Controls.Add(lblTitle);
        header.Controls.Add(lblSub);
        return header;
    }

    // ── Card builder ──────────────────────────────────────────────────────

    private Panel BuildCard(string icon, string title, string sub,
                            Color accent, string marker,
                            int width, int height)
    {
        var card = new Panel
        {
            Size      = new Size(width, height),
            BackColor = Color.Transparent,
            Cursor    = Cursors.Default,   // no mouse interaction
        };

        var inner = new Panel
        {
            Size      = new Size(width - 4, height - 4),
            Location  = new Point(2, 2),
            BackColor = Color.FromArgb(22, 34, 66),
        };
        inner.Paint += (s, e) =>
        {
            var g  = e.Graphics;
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

        // Icon
        var lblIcon = new Label
        {
            Text      = icon,
            Font      = new Font("Segoe UI Emoji", 26, FontStyle.Regular),
            ForeColor = Color.White,
            AutoSize  = false,
            Size      = new Size(50, 50),
            Location  = new Point(pad, 18),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
        };

        // Title
        var lblTitle = new Label
        {
            Text      = title,
            Font      = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = false,
            Size      = new Size(width - pad * 2 - 60, 28),
            Location  = new Point(pad + 56, 28),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // Description — taller now that button is gone
        var lblSub = new Label
        {
            Text      = sub,
            Font      = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(155, 180, 225),
            AutoSize  = false,
            Size      = new Size(width - pad * 2, height - 130),
            Location  = new Point(pad, 80),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopLeft,
        };

        // Marker instruction — large, prominent, replaces the button
        var lblMarker = new Label
        {
            Text      = "▶  " + marker,
            Font      = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = accent,
            AutoSize  = false,
            Size      = new Size(width - pad * 2, 30),
            Location  = new Point(pad, height - 46),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        inner.Controls.Add(lblIcon);
        inner.Controls.Add(lblTitle);
        inner.Controls.Add(lblSub);
        inner.Controls.Add(lblMarker);

        return card;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private void OpenContentManager() => new ContentManagerPage(_tuioClient).Show();
    private void OpenTestLevels()     => new TestLevelsPage(_tuioClient).Show();
    private void OpenTestMarkers()    => new TestMarkersPage(_tuioClient).Show();
    private void OpenSystemStatus()   => new SystemStatusPage(
        _btConnected, _tuioClient != null, _gestureRef, _faceRef, _gazeRef, _tuioClient).Show();
    private void GoBack()             => this.Close();

    // ── TUIO ──────────────────────────────────────────────────────────────

    private void HandleGestureMarker(int id)
    {
        if (!this.Visible || this.IsDisposed) return;
        this.BeginInvoke((MethodInvoker)(() => DispatchMarker(id)));
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
        => this.BeginInvoke((MethodInvoker)(() => DispatchMarker(o.SymbolID)));

    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c)    { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b)        { }
    public void updateTuioBlob(TuioBlob b)     { }
    public void removeTuioBlob(TuioBlob b)     { }
    public void refresh(TuioTime t)            { }

    // ── GDI+ helper ───────────────────────────────────────────────────────

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d    = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X,           r.Y,            d, d, 180, 90);
        path.AddArc(r.Right - d,   r.Y,            d, d, 270, 90);
        path.AddArc(r.Right - d,   r.Bottom - d,   d, d,   0, 90);
        path.AddArc(r.X,           r.Bottom - d,   d, d,  90, 90);
        path.CloseFigure();
        return path;
    }
}
