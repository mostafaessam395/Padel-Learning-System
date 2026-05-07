using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin page to quickly test any marker page by clicking a button.
/// Shows marker ID, page name, and an Open/Test button.
/// </summary>
public class TestMarkersPage : Form, TuioListener
{
    private readonly TuioClient _tuioClient;

    private static readonly (int Id, string Name, string Level)[] MarkerDefs =
    {
        (3, "Padel Shots",      "Primary"),
        (4, "Padel Rules",      "Primary"),
        (5, "AI Vision Coach",  "Primary"),
        (6, "Quick Challenge",  "Primary"),
        (7, "Speed Mode",       "Primary"),
        (8, "Competition",      "Primary"),
        (20, "Back / Home",     ""),
    };

    private readonly Dictionary<int, Label> _statusLabels = new Dictionary<int, Label>();

    public TestMarkersPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        this.Text           = "Test Markers — Admin";
        this.Size           = new Size(820, 580);
        this.StartPosition  = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;
        this.BackColor      = Color.FromArgb(18, 28, 55);

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

    private void BuildUI()
    {
        var lbl = new Label
        {
            Text      = "🎯  Test Markers",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(24, 18),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(lbl);

        var sub = new Label
        {
            Text      = "Click 'Test' to open the page for each marker. Status shows last result.",
            Font      = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 170, 220),
            AutoSize  = true,
            Location  = new Point(24, 52),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(sub);

        // Header row
        AddHeaderRow(24, 80);

        int y = 112;
        foreach (var (id, name, level) in MarkerDefs)
        {
            AddMarkerRow(id, name, level, y);
            y += 52;
        }

        var btnClose = new Button
        {
            Text      = "← Back",
            Size      = new Size(140, 36),
            Location  = new Point(24, y + 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 70, 100),
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor    = Cursors.Hand,
        };
        btnClose.FlatAppearance.BorderSize = 0;
        btnClose.Click += (s, e) => this.Close();
        this.Controls.Add(btnClose);
    }

    private void AddHeaderRow(int x, int y)
    {
        void H(string t, int lx, int w)
        {
            var l = new Label { Text = t, Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(120, 160, 220), AutoSize = false,
                Size = new Size(w, 22), Location = new Point(lx, y), BackColor = Color.Transparent };
            this.Controls.Add(l);
        }
        H("Marker ID", x,       80);
        H("Page Name", x + 90,  220);
        H("Action",    x + 320, 120);
        H("Status",    x + 450, 200);
    }

    private void AddMarkerRow(int id, string name, string level, int y)
    {
        int x = 24;

        var lblId = new Label { Text = id.ToString(), Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255), AutoSize = false,
            Size = new Size(80, 36), Location = new Point(x, y), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft };
        this.Controls.Add(lblId);

        var lblName = new Label { Text = name, Font = new Font("Segoe UI", 11),
            ForeColor = Color.White, AutoSize = false,
            Size = new Size(220, 36), Location = new Point(x + 90, y), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft };
        this.Controls.Add(lblName);

        var statusLbl = new Label { Text = "—", Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(160, 170, 190), AutoSize = false,
            Size = new Size(200, 36), Location = new Point(x + 450, y), BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft };
        this.Controls.Add(statusLbl);
        _statusLabels[id] = statusLbl;

        if (id == 20)
        {
            var btnBack = new Button { Text = "Close Page", Size = new Size(110, 32),
                Location = new Point(x + 320, y + 2), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(180, 60, 80), ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click += (s, e) => { statusLbl.Text = "✔ Back triggered"; statusLbl.ForeColor = Color.FromArgb(60, 200, 100); };
            this.Controls.Add(btnBack);
            return;
        }

        var btn = new Button { Text = "▶ Test", Size = new Size(110, 32),
            Location = new Point(x + 320, y + 2), FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(40, 100, 200), ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
        btn.FlatAppearance.BorderSize = 0;

        int markerId = id; string lvl = level; Label sl = statusLbl;
        btn.Click += (s, e) =>
        {
            try
            {
                var dummyUser = new UserData { Name = "Admin Test", Level = lvl, GazeProfile = new GazeProfile() };
                var page = new LearningPage(dummyUser, _tuioClient);

                // Simulate the marker being placed after the page opens
                page.Shown += (ps, pe) =>
                {
                    var timer = new System.Windows.Forms.Timer { Interval = 600 };
                    timer.Tick += (ts, te) =>
                    {
                        timer.Stop();
                        timer.Dispose();
                        // Simulate marker via a TuioObject
                        long sid = 900000 + markerId;
                        var obj = new TuioObject(sid, markerId, 0.5f, 0.5f, 0f);
                        page.addTuioObject(obj);
                    };
                    timer.Start();
                };

                page.Show();
                sl.Text      = "✔ Working";
                sl.ForeColor = Color.FromArgb(60, 200, 100);
            }
            catch (Exception ex)
            {
                sl.Text      = "✘ Error: " + ex.Message.Substring(0, Math.Min(30, ex.Message.Length));
                sl.ForeColor = Color.FromArgb(220, 80, 60);
            }
        };
        this.Controls.Add(btn);
    }

    private void HandleGestureMarker(int id)
    {
        if (!this.Visible || this.IsDisposed) return;
        if (id == 20) this.BeginInvoke((MethodInvoker)(() => this.Close()));
    }

    public void addTuioObject(TuioObject o)
    {
        if (o.SymbolID == 20)
            this.BeginInvoke((MethodInvoker)(() => this.Close()));
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
