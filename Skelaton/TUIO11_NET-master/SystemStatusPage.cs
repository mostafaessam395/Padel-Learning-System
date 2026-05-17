using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

/// <summary>
/// Admin-only system status page.
/// Shows Bluetooth, TUIO, YOLO, Face ID, Gaze, Gesture, and content JSON status.
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

    // Debounce: prevent repeated triggers while marker stays visible
    private bool _refreshCooldown = false;

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
        this.Size           = new Size(680, 580);
        this.StartPosition  = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;
        this.BackColor      = Color.FromArgb(14, 22, 44);

        BuildUI();

        Shown += (s, e) =>
        {
            if (_tuioClient != null) _tuioClient.addTuioListener(this);
            GestureRouter.OnGestureMarker += HandleGestureMarker;
        };
        FormClosed += (s, e) =>
        {
            GestureRouter.OnGestureMarker -= HandleGestureMarker;
            if (_tuioClient != null) _tuioClient.removeTuioListener(this);
        };
    }

    private void BuildUI()
    {
        var lbl = new Label
        {
            Text      = "📊  System Status",
            Font      = new Font("Segoe UI", 20, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(24, 18),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(lbl);

        var sub = new Label
        {
            Text      = "Live status of all system components",
            Font      = new Font("Segoe UI", 10, FontStyle.Italic),
            ForeColor = Color.FromArgb(130, 160, 210),
            AutoSize  = true,
            Location  = new Point(24, 54),
            BackColor = Color.Transparent,
        };
        this.Controls.Add(sub);

        int y = 96;

        // Content JSON
        var items = _svc.LoadAll();
        int activeCount = 0;
        foreach (var i in items) if (i.IsActive) activeCount++;

        string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "padel_content.json");
        bool jsonExists = File.Exists(jsonPath);

        AddRow(ref y, "Bluetooth",              _btConnected,                  _btConnected ? "Admin device detected" : "Not connected");
        AddRow(ref y, "Admin Detected",          _btConnected,                  _btConnected ? "E8:3A:12:40:1A:70 ✔" : "Not detected");
        AddRow(ref y, "TUIO Server",             _tuioConnected,                _tuioConnected ? "Connected on port 3333" : "Not connected");
        AddRow(ref y, "YOLO Server",             false,                         "Check localhost:5003 manually");
        AddRow(ref y, "Face Recognition",        _faceRef?.IsConnected ?? false, _faceRef?.IsConnected == true ? "Connected on port 5001" : "Not connected");
        AddRow(ref y, "Gaze Tracking",           _gazeRef?.IsConnected ?? false, _gazeRef?.IsConnected == true ? "Connected on port 5002" : "Not connected");
        AddRow(ref y, "Gesture Tracking",        _gestureRef?.IsConnected ?? false, _gestureRef?.IsConnected == true ? "Connected on port 5000" : "Not connected");
        AddRow(ref y, "Content JSON",            jsonExists,                    jsonExists ? $"{items.Count} items total" : "File missing — will be created on first use");
        AddRow(ref y, "Active Content Items",    activeCount > 0,               $"{activeCount} active items");

        y += 12;

        // Marker instructions — compact, fits within form
        this.Controls.Add(new Label {
            Text      = "▶  Marker 35 : Refresh Status",
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 125, 255),
            AutoSize  = false, Size = new Size(420, 24),
            Location  = new Point(24, y),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft });

        this.Controls.Add(new Label {
            Text      = "◀  Marker 20 : Back to Admin Dashboard",
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(190, 55, 75),
            AutoSize  = false, Size = new Size(420, 24),
            Location  = new Point(24, y + 28),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft });

        // Ensure form is tall enough to show both instructions
        int needed = y + 28 + 24 + 16;
        if (this.ClientSize.Height < needed)
            this.ClientSize = new Size(this.ClientSize.Width, needed);
    }

    // ── Marker / Gesture handling ─────────────────────────────────────────

    private void HandleGestureMarker(int id)
    {
        if (!Visible || IsDisposed) return;
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
            Controls.Clear();
            BuildUI();

            // Reset cooldown after 2 seconds
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); _refreshCooldown = false; };
            t.Start();
        }
    }

    public void addTuioObject(TuioObject o)
        => BeginInvoke((MethodInvoker)(() => Dispatch(o.SymbolID)));

    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c)    { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b)        { }
    public void updateTuioBlob(TuioBlob b)     { }
    public void removeTuioBlob(TuioBlob b)     { }
    public void refresh(TuioTime t)            { }

    private void AddRow(ref int y, string label, bool ok, string detail)
    {
        var lblName = new Label
        {
            Text      = label,
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(190, 205, 230),
            AutoSize  = false,
            Size      = new Size(200, 30),
            Location  = new Point(24, y),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var lblStatus = new Label
        {
            Text      = ok ? "● OK" : "● Offline",
            Font      = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = ok ? Color.FromArgb(60, 210, 100) : Color.FromArgb(220, 80, 60),
            AutoSize  = false,
            Size      = new Size(90, 30),
            Location  = new Point(230, y),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var lblDetail = new Label
        {
            Text      = detail,
            Font      = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(130, 155, 195),
            AutoSize  = false,
            Size      = new Size(320, 30),
            Location  = new Point(330, y),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        this.Controls.Add(lblName);
        this.Controls.Add(lblStatus);
        this.Controls.Add(lblDetail);

        // Separator line
        var sep = new Panel { Size = new Size(620, 1), Location = new Point(24, y + 32), BackColor = Color.FromArgb(35, 50, 80) };
        this.Controls.Add(sep);

        y += 42;
    }
}
