using System;
using System.Drawing;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin shortcut to open any level's training page for demo/testing.
/// Does not affect normal player navigation.
/// </summary>
public class TestLevelsPage : Form, TuioListener
{
    private readonly TuioClient _tuioClient;

    public TestLevelsPage(TuioClient tuioClient = null)
    {
        _tuioClient = tuioClient;

        this.Text           = "Test Levels — Admin";
        this.Size           = new Size(700, 480);
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
            Text      = "🎾  Test Levels",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(30, 24),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(lbl);

        var sub = new Label
        {
            Text      = "Open a level page for demo testing. This does not affect normal player flow.",
            Font      = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(140, 170, 220),
            AutoSize  = true,
            Location  = new Point(30, 62),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(sub);

        var levels = new (string Label, string Level, Color Accent)[]
        {
            ("🟢  Beginner",      "Primary",    Color.FromArgb(60, 190, 110)),
            ("🔵  Intermediate",  "Secondary",  Color.FromArgb(60, 130, 255)),
            ("🔴  Advanced",      "HighSchool", Color.FromArgb(220, 80, 60)),
        };

        int y = 120;
        foreach (var (label, level, accent) in levels)
        {
            var btn = new Button
            {
                Text      = label,
                Size      = new Size(580, 70),
                Location  = new Point(50, y),
                FlatStyle = FlatStyle.Flat,
                BackColor = accent,
                ForeColor = Color.White,
                Font      = new Font("Segoe UI", 16, FontStyle.Bold),
                Cursor    = Cursors.Hand,
            };
            btn.FlatAppearance.BorderSize = 0;
            string lvl = level; // capture
            btn.Click += (s, e) =>
            {
                // Create a dummy admin user for the level
                var dummyUser = new UserData
                {
                    Name  = "Admin Test",
                    Level = lvl,
                    GazeProfile = new GazeProfile(),
                };
                var page = new LearningPage(dummyUser, _tuioClient);
                page.Show();
            };
            this.Controls.Add(btn);
            y += 90;
        }

        var btnClose = new Button
        {
            Text      = "← Back",
            Size      = new Size(140, 36),
            Location  = new Point(50, y + 10),
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
