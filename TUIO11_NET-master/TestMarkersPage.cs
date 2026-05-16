using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin Test Markers page — marker-only navigation, no mouse/click interaction.
/// Markers 3-8 open their respective pages; Marker 20 = Back.
/// </summary>
public class TestMarkersPage : Form, TuioListener
{
    private readonly TuioClient _tuioClient;

    // Debounce: track which marker is currently open to prevent repeated opens
    private int  _openMarkerId  = -1;
    private bool _pageOpen      = false;

    // Status labels keyed by marker ID
    private readonly Dictionary<int, Label> _statusLabels = new Dictionary<int, Label>();

    // Marker definitions: id, display name, level passed to LearningPage
    private static readonly (int Id, string Name, string Level, Color Accent)[] Markers =
    {
        (3,  "Padel Shots",      "Primary",    Color.FromArgb(55,  125, 255)),
        (4,  "Padel Rules",      "Primary",    Color.FromArgb(50,  185, 105)),
        (5,  "AI Vision Coach",  "Advanced",   Color.FromArgb(220, 140,  40)),
        (6,  "Quick Challenge",  "Secondary",  Color.FromArgb(175,  55, 220)),
        (7,  "Speed Mode",       "Secondary",  Color.FromArgb(220,  80,  60)),
        (8,  "Competition",      "HighSchool", Color.FromArgb( 60, 190, 110)),
        (20, "Back / Home",      "",           Color.FromArgb(190,  55,  75)),
    };

    public TestMarkersPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        Text           = "Test Markers — Admin";
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
        // Header
        var header = new Panel { Dock = DockStyle.Top, Height = 100,
            BackColor = Color.FromArgb(14, 24, 52) };
        header.Paint += (s, e) =>
        {
            using (var b = new LinearGradientBrush(header.ClientRectangle,
                Color.FromArgb(20, 36, 72), Color.FromArgb(10, 18, 44), 90f))
                e.Graphics.FillRectangle(b, header.ClientRectangle);
            using (var p = new Pen(Color.FromArgb(55, 100, 200), 2))
                e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.Controls.Add(new Label {
            Text = "🎯  Test Markers", Font = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White, AutoSize = false, Size = new Size(500, 44),
            Location = new Point(28, 12), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft });
        header.Controls.Add(new Label {
            Text = "Use TUIO Markers to open pages  •  Marker-Only Interaction  •  No mouse required",
            Font = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(120, 165, 240), AutoSize = false,
            Size = new Size(860, 24), Location = new Point(30, 62),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });
        Controls.Add(header);

        // Scrollable content area
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = Color.FromArgb(14, 20, 42), Padding = new Padding(28, 16, 28, 16) };
        Controls.Add(scroll);

        // Column header row
        int y = 0;
        AddHeaderRow(scroll, y);
        y += 36;

        // Separator
        var sep = new Panel { Location = new Point(0, y), Size = new Size(1400, 2),
            BackColor = Color.FromArgb(40, 60, 100) };
        scroll.Controls.Add(sep);
        y += 8;

        // Data rows
        foreach (var (id, name, level, accent) in Markers)
        {
            AddRow(scroll, id, name, level, accent, y);
            y += 52;
        }
    }

    private void AddHeaderRow(Panel parent, int y)
    {
        void H(string t, int x, int w) => parent.Controls.Add(new Label {
            Text = t, Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 140, 210), AutoSize = false,
            Size = new Size(w, 28), Location = new Point(x, y),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });

        H("Marker ID",   0,   90);
        H("Page Name",   100, 200);
        H("Instruction", 310, 320);
        H("Status",      640, 300);
    }

    private void AddRow(Panel parent, int id, string name, string level, Color accent, int y)
    {
        // Marker ID badge
        parent.Controls.Add(new Label {
            Text = id.ToString(),
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            ForeColor = accent, AutoSize = false,
            Size = new Size(90, 40), Location = new Point(0, y),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });

        // Page name
        parent.Controls.Add(new Label {
            Text = name, Font = new Font("Segoe UI", 11),
            ForeColor = Color.White, AutoSize = false,
            Size = new Size(200, 40), Location = new Point(100, y),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });

        // Instruction
        string instruction = id == 20
            ? "▶  Place Marker 20 to go back"
            : $"▶  Place Marker {id} to open";
        parent.Controls.Add(new Label {
            Text = instruction, Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = accent, AutoSize = false,
            Size = new Size(320, 40), Location = new Point(310, y),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft });

        // Status label
        var statusLbl = new Label {
            Text = "—", Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 155, 185), AutoSize = false,
            Size = new Size(300, 40), Location = new Point(640, y),
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        parent.Controls.Add(statusLbl);
        _statusLabels[id] = statusLbl;

        // Row separator
        parent.Controls.Add(new Panel {
            Location = new Point(0, y + 44), Size = new Size(1400, 1),
            BackColor = Color.FromArgb(25, 40, 70) });
    }

    // ─────────────────────────────────────────────────────────────────────
    private void SetStatus(int id, string text, Color color)
    {
        if (_statusLabels.TryGetValue(id, out var lbl))
        {
            lbl.Text      = text;
            lbl.ForeColor = color;
        }
    }

    private void Dispatch(int id)
    {
        Console.WriteLine($"[TestMarkers] Marker detected: {id}");

        if (id == 20)
        {
            Console.WriteLine("[TestMarkers] Marker 20 → Back to Admin Dashboard");
            SetStatus(20, "✔ Going back...", Color.FromArgb(60, 200, 100));
            if (!IsDisposed) Close();
            return;
        }

        // Debounce: ignore if same marker already opened a page
        if (_pageOpen && _openMarkerId == id) return;
        if (_pageOpen) return;

        var def = System.Array.Find(Markers, m => m.Id == id);
        if (def.Id == 0) return;   // unknown marker

        _pageOpen     = true;
        _openMarkerId = id;
        SetStatus(id, "Opening...", Color.FromArgb(255, 200, 60));
        Console.WriteLine($"[TestMarkers] Opening page={def.Name} level={def.Level}");

        try
        {
            var dummyUser = new UserData {
                Name  = "Admin Test",
                Level = def.Level,
                GazeProfile = new GazeProfile()
            };
            var page = new LearningPage(dummyUser, _tuioClient);

            // After the page opens, simulate the marker so LearningPage navigates
            // to the correct sub-page (Padel Shots, Rules, AI Vision, etc.)
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
                SetStatus(id, "✔ Opened successfully", Color.FromArgb(60, 200, 100));
                Console.WriteLine($"[TestMarkers] Page closed: {def.Name}");
            };

            page.Show();
            SetStatus(id, "✔ Opened successfully", Color.FromArgb(60, 200, 100));
        }
        catch (Exception ex)
        {
            _pageOpen     = false;
            _openMarkerId = -1;
            string msg = ex.Message.Length > 40 ? ex.Message.Substring(0, 40) : ex.Message;
            SetStatus(id, "✘ Error: " + msg, Color.FromArgb(220, 80, 60));
            Console.WriteLine($"[TestMarkers] Error opening {def.Name}: {ex.Message}");
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
