using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Test Markers page — modern marker grid with animated cards.
/// Markers 3-8 open their respective pages; Marker 20 = Back.
/// </summary>
public class TestMarkersPage : Form, TuioListener
{
    private readonly TuioClient _tuioClient;

    private int  _openMarkerId  = -1;
    private bool _pageOpen      = false;

    private readonly Dictionary<int, MarkerRow> _rows = new Dictionary<int, MarkerRow>();

    private static readonly MarkerDef[] Defs = new[]
    {
        new MarkerDef(3,  "🎾", "Padel Shots",       "Primary",     PadelTheme.Primary,         PadelTheme.PrimarySoft),
        new MarkerDef(4,  "📜", "Padel Rules",       "Primary",     PadelTheme.Accent,          PadelTheme.AccentSoft),
        new MarkerDef(5,  "👁",  "AI Vision Coach",   "Advanced",    PadelTheme.Gold,            Color.FromArgb(255,220,130)),
        new MarkerDef(6,  "⚡", "Quick Challenge",   "Secondary",   Color.FromArgb(175,55,220), Color.FromArgb(220,130,255)),
        new MarkerDef(7,  "🏃", "Speed Mode",        "Secondary",   PadelTheme.Hot,             Color.FromArgb(255,150,170)),
        new MarkerDef(8,  "🏆", "Competition",       "HighSchool",  PadelTheme.Lime,            Color.FromArgb(180,255,150)),
        new MarkerDef(20, "🏠", "Back / Home",       "",            PadelTheme.HotDeep,         PadelTheme.Hot),
    };

    public TestMarkersPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        Text           = "Test Markers — Admin";
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
            Title        = "Test Markers",
            Subtitle     = "Place any fiducial marker to open its page · Marker 20 to go back",
            Icon         = "🎯",
            Height       = 118,
            GradientFrom = PadelTheme.Gold,
            GradientTo   = PadelTheme.Primary,
            AccentColor  = PadelTheme.Accent,
            Dock         = DockStyle.Top,
        };
        Controls.Add(header);

        var scroll = new Panel
        {
            Dock      = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding   = new Padding(28, 22, 28, 24),
        };
        Controls.Add(scroll);
        scroll.BringToFront();

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            BackColor   = Color.Transparent,
            AutoSize    = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding     = new Padding(0),
            Margin      = new Padding(0),
            Anchor      = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location    = new Point(0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        scroll.Controls.Add(grid);

        for (int i = 0; i < Defs.Length; i++)
        {
            var d = Defs[i];
            var row = new MarkerRow(d);
            row.Margin = new Padding(8);
            row.Size   = new Size(420, 96);
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            row.Activated += (s, e) => Dispatch(d.Id);
            grid.Controls.Add(row, i % 2, i / 2);
            _rows[d.Id] = row;
        }

        // Resize columns to fill scroll
        Action stretch = () =>
        {
            int colW = Math.Max(360, (scroll.ClientSize.Width - 32) / 2);
            foreach (Control c in grid.Controls)
                c.Width = colW;
            grid.Width = colW * 2 + 16;
        };
        scroll.SizeChanged += (s, e) => stretch();
        Shown              += (s, e) => stretch();
    }

    private void SetStatus(int id, string text, MarkerRow.RowState state)
    {
        if (_rows.TryGetValue(id, out var row))
            row.SetStatus(text, state);
    }

    private void Dispatch(int id)
    {
        Console.WriteLine("[TestMarkers] Marker detected: " + id);

        if (id == 20)
        {
            SetStatus(20, "Going back...", MarkerRow.RowState.Ok);
            if (!IsDisposed) Close();
            return;
        }

        if (_pageOpen) return;

        MarkerDef def = default(MarkerDef);
        bool found = false;
        foreach (var d in Defs) if (d.Id == id) { def = d; found = true; break; }
        if (!found) return;

        _pageOpen     = true;
        _openMarkerId = id;
        SetStatus(id, "Opening…", MarkerRow.RowState.Working);
        Console.WriteLine("[TestMarkers] Opening page=" + def.Name + " level=" + def.Level);

        try
        {
            var dummyUser = new UserData {
                Name  = "Admin Test",
                Level = def.Level,
                GazeProfile = new GazeProfile()
            };
            var page = new LearningPage(dummyUser, _tuioClient);

            page.Shown += (ps, pe) =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 500 };
                t.Tick += (ts, te) =>
                {
                    t.Stop(); t.Dispose();
                    long sid = 900000 + id;
                    page.addTuioObject(new TuioObject(sid, id, 0.5f, 0.5f, 0f));
                };
                t.Start();
            };

            page.FormClosed += (ps, pe) =>
            {
                _pageOpen     = false;
                _openMarkerId = -1;
                SetStatus(id, "Opened successfully", MarkerRow.RowState.Ok);
            };

            page.Show();
            SetStatus(id, "Opened successfully", MarkerRow.RowState.Ok);
        }
        catch (Exception ex)
        {
            _pageOpen     = false;
            _openMarkerId = -1;
            string msg = ex.Message.Length > 40 ? ex.Message.Substring(0, 40) : ex.Message;
            SetStatus(id, "Error: " + msg, MarkerRow.RowState.Err);
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

    // ── Helper types ─────────────────────────────────────────────────────

    private struct MarkerDef
    {
        public int Id;
        public string Icon, Name, Level;
        public Color AccentA, AccentB;
        public MarkerDef(int id, string icon, string name, string level, Color a, Color b)
        { Id = id; Icon = icon; Name = name; Level = level; AccentA = a; AccentB = b; }
    }

    private class MarkerRow : Control
    {
        public enum RowState { Idle, Working, Ok, Err }

        private readonly MarkerDef _def;
        private RowState _state = RowState.Idle;
        private string _status = "Ready";
        private bool _hover;
        private float _hoverAmt;
        private readonly System.Windows.Forms.Timer _t;
        private readonly System.Windows.Forms.Timer _pulse;
        private float _pulsePhase;

        public event EventHandler Activated;

        internal MarkerRow(MarkerDef def)
        {
            _def = def;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor    = Cursors.Hand;
            Size      = new Size(420, 96);

            _t = new System.Windows.Forms.Timer { Interval = 16 };
            _t.Tick += (s, e) =>
            {
                float target = _hover ? 1f : 0f;
                float d = (target - _hoverAmt) * 0.25f;
                if (Math.Abs(d) < 0.003f) { _hoverAmt = target; _t.Stop(); }
                else _hoverAmt += d;
                Invalidate();
            };
            _pulse = new System.Windows.Forms.Timer { Interval = 33 };
            _pulse.Tick += (s, e) => { _pulsePhase += 0.08f; if (_pulsePhase > 6.28f) _pulsePhase -= 6.28f; Invalidate(); };
            _pulse.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); _pulse.Stop(); _pulse.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true;  _t.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _t.Start(); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var h = Activated; if (h != null) h(this, EventArgs.Empty);
        }

        public void SetStatus(string text, RowState state)
        {
            _status = text ?? "";
            _state  = state;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int lift = (int)(_hoverAmt * 3);
            var r = new Rectangle(6, 6 - lift, Width - 12, Height - 14);

            PadelTheme.DrawSoftShadow(g, r, PadelTheme.RadMd, 8 + (int)(_hoverAmt * 4), 70);

            // Body
            using (var path = PadelTheme.RoundedRect(r, PadelTheme.RadMd))
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)),
                Color.FromArgb(240, 28, 38, 70),
                Color.FromArgb(240, 18, 26, 52),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                g.FillPath(br, path);

            // Left accent ribbon
            var ribbon = new Rectangle(r.X, r.Y, 7, r.Height);
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(ribbon, _def.AccentA, _def.AccentB, 90f))
                g.FillRectangle(br, ribbon);

            // Marker ID circle
            int circleR = 24;
            int cx = r.X + 50, cy = r.Y + r.Height / 2;
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(cx - circleR, cy - circleR, circleR * 2, circleR * 2),
                _def.AccentA, _def.AccentB, 45f))
                g.FillEllipse(br, cx - circleR, cy - circleR, circleR * 2, circleR * 2);
            using (var pen = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
                g.DrawEllipse(pen, cx - circleR, cy - circleR, circleR * 2, circleR * 2);
            using (var f = new Font(PadelTheme.DisplayFamily, 13, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(_def.Id.ToString(), f, br, new Rectangle(cx - circleR, cy - circleR, circleR * 2, circleR * 2), sf);

            // Icon + name
            using (var fi = new Font(PadelTheme.DisplayFamily, 20, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
                g.DrawString(_def.Icon, fi, br, r.X + 86, r.Y + 14);

            using (var ft = new Font(PadelTheme.DisplayFamily, 13, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
                g.DrawString(_def.Name, ft, br, r.X + 130, r.Y + 16);

            // Status text
            Color statusColor;
            switch (_state)
            {
                case RowState.Working: statusColor = PadelTheme.Warn; break;
                case RowState.Ok:      statusColor = PadelTheme.Ok;   break;
                case RowState.Err:     statusColor = PadelTheme.Err;  break;
                default:               statusColor = PadelTheme.TextLo; break;
            }

            // Status dot
            int dotR = 5;
            int dx = r.X + 130, dy = r.Y + 50;
            if (_state == RowState.Working || _state == RowState.Ok)
            {
                float p = (float)((Math.Sin(_pulsePhase) + 1) * 0.5);
                int rr = dotR + 2 + (int)(p * 6);
                int a = 110 - (int)(p * 80);
                using (var br = new SolidBrush(Color.FromArgb(Math.Max(20, a), statusColor.R, statusColor.G, statusColor.B)))
                    g.FillEllipse(br, dx - 1 - (rr - dotR), dy - (rr - dotR), rr * 2, rr * 2);
            }
            using (var br = new SolidBrush(statusColor))
                g.FillEllipse(br, dx, dy, dotR * 2, dotR * 2);

            using (var fs = new Font(PadelTheme.TextFamily, 9.5f, FontStyle.Regular))
            using (var br = new SolidBrush(statusColor))
                g.DrawString(_status, fs, br, dx + 14, dy - 4);

            // Hint chip
            string hint = _def.Id == 20 ? "Marker 20" : "Marker " + _def.Id;
            using (var f = new Font(PadelTheme.TextFamily, 8.2f, FontStyle.Bold))
            {
                var sz = TextRenderer.MeasureText(hint, f);
                var hr = new Rectangle(r.Right - sz.Width - 28, r.Y + r.Height / 2 - 11, sz.Width + 16, 22);
                using (var p = PadelTheme.RoundedRect(hr, 11))
                using (var br = new SolidBrush(Color.FromArgb(220, _def.AccentA)))
                    g.FillPath(br, p);
                using (var b = new SolidBrush(Color.White))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(hint, f, b, hr, sf);
            }

            // Outline
            using (var path = PadelTheme.RoundedRect(r, PadelTheme.RadMd))
            using (var pen = new Pen(Color.FromArgb(_hover ? 110 : 45, 255, 255, 255), 1.2f))
                g.DrawPath(pen, path);
        }
    }
}
