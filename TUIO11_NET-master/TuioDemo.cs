using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Net.Sockets;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TUIO;
using TuioDemo;

public static class AppSettings
{
    public static bool IsDarkMode = false;
    public static bool IsMuted = false;
    public static bool IsSlowVoice = false;

    public static event Action ThemeChanged;
    public static event Action AudioSettingsChanged;

    public static void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        ThemeChanged?.Invoke();
    }

    public static void ToggleMute()
    {
        IsMuted = !IsMuted;
        AudioSettingsChanged?.Invoke();
    }

    public static void ToggleVoiceSpeed()
    {
        IsSlowVoice = !IsSlowVoice;
        AudioSettingsChanged?.Invoke();
    }

    public static void ResetApp()
    {
    }

    public static int VoiceRate => IsSlowVoice ? -5 : -2;

    public static Color HomeBgTop => IsDarkMode ? Color.FromArgb(10, 15, 30) : Color.FromArgb(28, 76, 116);
    public static Color HomeBgBottom => IsDarkMode ? Color.FromArgb(25, 35, 60) : Color.FromArgb(106, 158, 198);
    public static Color PageBg => IsDarkMode ? Color.FromArgb(18, 22, 35) : Color.FromArgb(245, 250, 255);

    public static Color TitleText => IsDarkMode ? Color.FromArgb(235, 240, 248) : Color.FromArgb(15, 40, 75);
    public static Color SubText => IsDarkMode ? Color.FromArgb(190, 200, 215) : Color.FromArgb(70, 90, 110);
    public static Color AccentText => IsDarkMode ? Color.FromArgb(140, 190, 255) : Color.FromArgb(55, 90, 125);

    public static Color PanelFill => IsDarkMode ? Color.FromArgb(35, 42, 58) : Color.FromArgb(255, 252, 242);
    public static Color CardFill => IsDarkMode ? Color.FromArgb(40, 48, 68) : Color.White;
    public static Color Border => IsDarkMode ? Color.FromArgb(75, 90, 120) : Color.FromArgb(220, 228, 235);

    public static Color NavTop => IsDarkMode ? Color.FromArgb(16, 24, 40) : Color.FromArgb(24, 46, 90);
    public static Color NavBottom => IsDarkMode ? Color.FromArgb(8, 14, 28) : Color.FromArgb(10, 22, 48);
}

// ======================== Custom Panels ========================
public class SmoothPanel : Panel
{
    public SmoothPanel()
    {
        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
    }
}

public class ElegantCircularMenu : Panel
{
    private static readonly string[] InnerLabels = { "Display", "Audio", "System" };
    private static readonly Color[] InnerColors = {
        Color.FromArgb(70, 110, 200),
        Color.FromArgb(45, 150, 215),
        Color.FromArgb(55, 125, 205)
    };

    private static readonly string[][] OuterLabels = {
        new[] { "Dark Mode" },
        new[] { "Mute All", "Spd Toggle" },
        new[] { "Reset App", "App Info" }
    };

    private static readonly int[][] OuterMarkers = {
        new[] { 24 },
        new[] { 25, 26 },
        new[] { 27, 28 }
    };

    private const int InnerOuter = 112, InnerInner = 62;
    private const int OuterOuter = 188, OuterInner = 122;
    private const int CenterR = 23;

    private enum MenuState { Closed, Inner, Outer }
    private MenuState _state = MenuState.Closed;
    private int _highlightedIn = -1, _highlightedOut = -1, _lockedIn = -1;

    private System.Windows.Forms.Timer _dwellTimer, _glowTimer, _feedbackTimer;
    private float _glowPhase = 0f;
    private string _feedback = "";

    private readonly Font _fMain = new Font("Segoe UI", 9, FontStyle.Bold);
    private readonly Font _fCenter = new Font("Segoe UI", 13, FontStyle.Bold);
    private readonly Font _fFb = new Font("Segoe UI", 10, FontStyle.Bold);

    private Point _center;

    public event Action<int> OnActionTriggered;
    private void applyTheme()
    {
        if (AppSettings.IsDarkMode)
        {
            this.BackColor = Color.FromArgb(25, 25, 35);

            // ألوان غامقة للمنيو
            InnerColors[0] = Color.FromArgb(80, 120, 200);
            InnerColors[1] = Color.FromArgb(60, 140, 200);
            InnerColors[2] = Color.FromArgb(90, 110, 180);
        }
        else
        {
            this.BackColor = Color.Transparent;

            // رجع الألوان الأصلية
            InnerColors[0] = Color.FromArgb(70, 110, 200);
            InnerColors[1] = Color.FromArgb(45, 150, 215);
            InnerColors[2] = Color.FromArgb(55, 125, 205);
        }
    }
    void themeHandler()
    {
        if (!this.IsDisposed)
            this.BeginInvoke((MethodInvoker)(() =>
            {
                applyTheme();
                this.Invalidate();
            }));
    }


    public ElegantCircularMenu()
    {
        this.Size = new Size(420, 420);
        this.DoubleBuffered = true;
        this.BackColor = Color.Transparent;
        this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        this.Paint += OnMenuPaint;
        _center = new Point(this.Width / 2, this.Height / 2);

        _dwellTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _dwellTimer.Tick += OnDwellComplete;

        _glowTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _glowTimer.Tick += (s, e) =>
        {
            _glowPhase += 0.08f;
            if (_glowPhase > 6.28f) _glowPhase = 0f;
            Invalidate();
        };
        _glowTimer.Start();

        _feedbackTimer = new System.Windows.Forms.Timer { Interval = 2500 };
        _feedbackTimer.Tick += (s, e) =>
        {
            _feedback = "";
            _feedbackTimer.Stop();
            Invalidate();
        };

        // 👇 أهم سطرين
        applyTheme();
        AppSettings.ThemeChanged += themeHandler;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _center = new Point(this.Width / 2, this.Height / 2);
    }

    public void HandleTUIO(int id)
    {
        if (id == 30) { ToggleMenu(); return; }
        if (_state == MenuState.Closed) return;
        if (id >= 21 && id <= 23) { LockInner(id - 21); return; }
        if (id >= 24 && id <= 28 && _state == MenuState.Outer) TriggerAction(id);
    }

    public void HandleMarkerAdded(float angleDeg) { UpdateHighlight(angleDeg); }
    public void HandleMarkerRotation(float angleDeg) { UpdateHighlight(angleDeg); }

    public void HandleMarkerRemoved()
    {
        _dwellTimer.Stop();
        if (_state == MenuState.Outer && _highlightedOut >= 0 && _lockedIn >= 0)
        {
            int[] acts = OuterMarkers[_lockedIn];
            if (_highlightedOut < acts.Length)
                TriggerAction(acts[_highlightedOut]);
        }
    }

    private void ToggleMenu()
    {
        if (_state == MenuState.Closed)
        {
            _state = MenuState.Inner;
            _highlightedIn = _highlightedOut = _lockedIn = -1;
        }
        else
        {
            CloseMenu();
        }
        Invalidate();
    }

    private void CloseMenu()
    {
        _state = MenuState.Closed;
        _highlightedIn = _highlightedOut = _lockedIn = -1;
        _dwellTimer.Stop();
        Invalidate();
    }

    private void LockInner(int idx)
    {
        _lockedIn = idx;
        _highlightedIn = idx;
        _state = MenuState.Outer;
        _highlightedOut = -1;
        _dwellTimer.Stop();
        ShowFeedback(InnerLabels[idx] + " selected");
        Invalidate();
    }

    private void TriggerAction(int markerID)
    {
        ExecuteAction(markerID);
        OnActionTriggered?.Invoke(markerID);
    }

    private void ExecuteAction(int id)
    {
        switch (id)
        {
            case 24:
                AppSettings.ToggleDarkMode();
                ShowFeedback("Dark Mode: " + (AppSettings.IsDarkMode ? "ON" : "OFF"));
                break;
            case 25:
                AppSettings.ToggleMute();
                ShowFeedback("Sound: " + (AppSettings.IsMuted ? "MUTED" : "ON"));
                break;
            case 26:
                AppSettings.ToggleVoiceSpeed();
                ShowFeedback("Voice: " + (AppSettings.IsSlowVoice ? "SLOW" : "NORMAL"));
                break;
            case 27:
                AppSettings.ResetApp();
                ShowFeedback("Progress Reset!");
                break;
            case 28:
                ShowAppInfo();
                break;
        }
    }

    private void ShowFeedback(string text)
    {
        _feedback = text;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
        this.Invalidate();
    }

    private void ShowAppInfo()
    {
        ShowFeedback("App Info displayed");
        new AppInfoForm().Show();
    }

    private void UpdateHighlight(float angle)
    {
        angle = ((angle + 90f) % 360f + 360f) % 360f;

        if (_state == MenuState.Inner)
        {
            int seg = Math.Min(2, (int)(angle / 120f));
            if (seg != _highlightedIn)
            {
                _highlightedIn = seg;
                _dwellTimer.Stop();
                _dwellTimer.Start();
                Invalidate();
            }
        }
        else if (_state == MenuState.Outer && _lockedIn >= 0)
        {
            int n = OuterMarkers[_lockedIn].Length;
            int seg = Math.Min(n - 1, (int)(angle / (360f / n)));
            if (seg != _highlightedOut)
            {
                _highlightedOut = seg;
                _dwellTimer.Stop();
                _dwellTimer.Start();
                Invalidate();
            }
        }
    }

    private void OnDwellComplete(object sender, EventArgs e)
    {
        _dwellTimer.Stop();

        if (_state == MenuState.Inner && _highlightedIn >= 0)
        {
            LockInner(_highlightedIn);
        }
        else if (_state == MenuState.Outer && _highlightedOut >= 0 && _lockedIn >= 0)
        {
            int[] acts = OuterMarkers[_lockedIn];
            if (_highlightedOut < acts.Length)
                TriggerAction(acts[_highlightedOut]);
        }
    }

    private void OnMenuPaint(object sender, PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float glow = 0.5f + 0.5f * (float)Math.Sin(_glowPhase);

        if (_state != MenuState.Closed)
        {
            if (_state == MenuState.Outer) DrawOuterRing(g, glow);
            DrawInnerRing(g, glow);
        }

        DrawCenter(g, glow);

        if (_feedback != "")
            DrawFeedback(g);
    }

    private void DrawInnerRing(Graphics g, float glow)
    {
        for (int i = 0; i < 3; i++)
        {
            bool isHigh = (i == _highlightedIn), isLocked = (i == _lockedIn);
            Color c = isLocked ? Color.FromArgb(50, 175, 90) : isHigh ? Lighten(InnerColors[i], 40) : InnerColors[i];

            using (GraphicsPath path = MakeArc(_center, InnerOuter, InnerInner, -90f + i * 120f, 120f))
            {
                if (isHigh || isLocked)
                {
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb((int)(55 + 45 * glow), 255, 255, 255)))
                        g.FillPath(gb, path);
                }

                using (PathGradientBrush b = new PathGradientBrush(path))
                {
                    b.CenterColor = Lighten(c, 30);
                    b.SurroundColors = new[] { c };
                    g.FillPath(b, path);
                }

                using (Pen p = new Pen(Color.FromArgb(isHigh || isLocked ? 210 : 130, 255, 255, 255), 1.5f))
                    g.DrawPath(p, path);
            }

            double mid = (-90.0 + (i + 0.5) * 120.0) * Math.PI / 180.0;
            float r = (InnerOuter + InnerInner) / 2f;
            float lx = _center.X + (float)(r * Math.Cos(mid));
            float ly = _center.Y + (float)(r * Math.Sin(mid));
            DrawTxt(g, InnerLabels[i], _fMain, Color.White, lx, ly);
        }
    }

    private void DrawOuterRing(Graphics g, float glow)
    {
        if (_lockedIn < 0) return;

        int n = OuterMarkers[_lockedIn].Length;
        float step = 360f / n;

        for (int i = 0; i < n; i++)
        {
            bool isHigh = (i == _highlightedOut);
            Color c = isHigh ? Color.FromArgb(45, 200, 115) : Color.FromArgb(55, 155, 95);

            using (GraphicsPath path = MakeArc(_center, OuterOuter, OuterInner, -90f + i * step, step))
            {
                if (isHigh)
                {
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb((int)(65 + 55 * glow), 100, 255, 160)))
                        g.FillPath(gb, path);
                }

                using (PathGradientBrush b = new PathGradientBrush(path))
                {
                    b.CenterColor = Lighten(c, 35);
                    b.SurroundColors = new[] { c };
                    g.FillPath(b, path);
                }

                using (Pen p = new Pen(Color.FromArgb(isHigh ? 210 : 140, 255, 255, 255), isHigh ? 2f : 1.5f))
                    g.DrawPath(p, path);
            }

            double mid = (-90.0 + (i + 0.5) * step) * Math.PI / 180.0;
            float r = (OuterOuter + OuterInner) / 2f;
            float lx = _center.X + (float)(r * Math.Cos(mid));
            float ly = _center.Y + (float)(r * Math.Sin(mid));
            DrawTxt(g, OuterLabels[_lockedIn][i], _fMain, Color.White, lx, ly);
        }
    }

    private void DrawCenter(Graphics g, float glow)
    {
        int al = (int)(155 + 60 * glow);
        Color c = _state == MenuState.Closed ? Color.FromArgb(al, 65, 70, 85) : Color.FromArgb(al, 30, 110, 200);

        Rectangle r = new Rectangle(_center.X - CenterR, _center.Y - CenterR, CenterR * 2, CenterR * 2);
        using (SolidBrush b = new SolidBrush(c))
            g.FillEllipse(b, r);

        using (Pen p = new Pen(Color.FromArgb(180, 255, 255, 255), 1.5f))
            g.DrawEllipse(p, r);

        DrawTxt(g, _state == MenuState.Closed ? "\u2699" : "\u2715", _fCenter, Color.White, _center.X, _center.Y - 7);
    }

    private void DrawFeedback(Graphics g)
    {
        SizeF sz = g.MeasureString(_feedback, _fFb);
        float x = this.Width / 2f - sz.Width / 2f;
        float y = 6f;

        using (SolidBrush bg = new SolidBrush(Color.FromArgb(210, 15, 25, 55)))
            g.FillRectangle(bg, x - 8, y - 3, sz.Width + 16, sz.Height + 6);

        using (Pen pp = new Pen(Color.FromArgb(130, 90, 150, 255), 1f))
            g.DrawRectangle(pp, x - 8, y - 3, sz.Width + 16, sz.Height + 6);

        using (SolidBrush fb = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
            g.DrawString(_feedback, _fFb, fb, x, y);
    }

    private void DrawTxt(Graphics g, string text, Font font, Color color, float cx, float cy)
    {
        SizeF sz = g.MeasureString(text, font);
        using (SolidBrush b = new SolidBrush(color))
            g.DrawString(text, font, b, cx - sz.Width / 2, cy - sz.Height / 2);
    }

    private GraphicsPath MakeArc(Point center, int outer, int inner, float start, float sweep)
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(new Rectangle(center.X - outer, center.Y - outer, outer * 2, outer * 2), start, sweep);
        path.AddArc(new Rectangle(center.X - inner, center.Y - inner, inner * 2, inner * 2), start + sweep, -sweep);
        path.CloseFigure();
        return path;
    }

    private Color Lighten(Color c, int amt)
    {
        return Color.FromArgb(c.A,
            Math.Min(255, c.R + amt),
            Math.Min(255, c.G + amt),
            Math.Min(255, c.B + amt));
    }
}

public class RoundedShadowPanel : SmoothPanel
{
    private int _cornerRadius;
    private Color _fillColor;
    private Color _borderColor;
    private float _borderThickness;
    private Color _shadowColor;
    private bool _drawGloss;
    private int _shadowOffsetX;
    private int _shadowOffsetY;

    public int CornerRadius { get { return _cornerRadius; } set { _cornerRadius = value; } }
    public Color FillColor { get { return _fillColor; } set { _fillColor = value; } }
    public Color BorderColor { get { return _borderColor; } set { _borderColor = value; } }
    public float BorderThickness { get { return _borderThickness; } set { _borderThickness = value; } }
    public Color ShadowColor { get { return _shadowColor; } set { _shadowColor = value; } }
    public bool DrawGloss { get { return _drawGloss; } set { _drawGloss = value; } }
    public int ShadowOffsetX { get { return _shadowOffsetX; } set { _shadowOffsetX = value; } }
    public int ShadowOffsetY { get { return _shadowOffsetY; } set { _shadowOffsetY = value; } }

    public RoundedShadowPanel()
    {
        _cornerRadius = 25;
        _fillColor = Color.White;
        _borderColor = Color.FromArgb(150, 255, 255, 255);
        _borderThickness = 1.4f;
        _shadowColor = Color.FromArgb(35, 0, 0, 0);
        _drawGloss = false;
        _shadowOffsetX = 5;
        _shadowOffsetY = 8;
        this.BackColor = Color.Transparent;
        _adaptiveState = TuioDemo.AdaptiveState.Balanced;
    }

    // ── Adaptive gaze-tracking state ─────────────────────────────────
    private TuioDemo.AdaptiveState _adaptiveState;
    public TuioDemo.AdaptiveState AdaptiveState
    {
        get { return _adaptiveState; }
        set { _adaptiveState = value; Invalidate(); }
    }

    /// <summary>
    /// Animation phase for the adaptive glow (0..2PI). Updated by external timer.
    /// </summary>
    public float AdaptivePhase { get; set; } = 0f;

    /// <summary>
    /// Optional ribbon text displayed for Neglected/UnderFocused states.
    /// </summary>
    public string AdaptiveRibbonText { get; set; } = "";

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        using (GraphicsPath path = GetRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
        {
            this.Region = new Region(path);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle shadowRect = new Rectangle(
            ShadowOffsetX,
            ShadowOffsetY,
            Math.Max(1, this.Width - ShadowOffsetX - 2),
            Math.Max(1, this.Height - ShadowOffsetY - 2));

        using (GraphicsPath shadowPath = GetRoundedRectangle(shadowRect, CornerRadius))
        using (SolidBrush shadowBrush = new SolidBrush(ShadowColor))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        Rectangle rect = new Rectangle(0, 0, this.Width - 2, this.Height - 2);
        using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
        using (SolidBrush fillBrush = new SolidBrush(FillColor))
        using (Pen borderPen = new Pen(BorderColor, BorderThickness))
        {
            g.FillPath(fillBrush, path);
            g.DrawPath(borderPen, path);
        }

        if (DrawGloss)
        {
            Rectangle glossRect = new Rectangle(1, 1, this.Width - 4, this.Height / 2);
            using (GraphicsPath glossPath = GetRoundedRectangle(glossRect, CornerRadius))
            using (LinearGradientBrush glossBrush = new LinearGradientBrush(
                glossRect,
                Color.FromArgb(60, 255, 255, 255),
                Color.FromArgb(8, 255, 255, 255),
                90f))
            {
                g.FillPath(glossBrush, glossPath);
            }
        }

        // ── Adaptive state overlays ─────────────────────────────────
        PaintAdaptiveOverlay(g, rect);

        base.OnPaint(e);
    }

    /// <summary>
    /// Renders adaptive UI treatments based on gaze tracking state.
    /// </summary>
    private void PaintAdaptiveOverlay(Graphics g, Rectangle rect)
    {
        if (_adaptiveState == TuioDemo.AdaptiveState.Balanced) return;

        switch (_adaptiveState)
        {
            case TuioDemo.AdaptiveState.Neglected:
                PaintNeglectedState(g, rect);
                break;
            case TuioDemo.AdaptiveState.UnderFocused:
                PaintUnderFocusedState(g, rect);
                break;
            case TuioDemo.AdaptiveState.Familiar:
                PaintFamiliarState(g, rect);
                break;
        }
    }

    private void PaintNeglectedState(Graphics g, Rectangle rect)
    {
        // Pulsing teal glow ring
        float glow = 0.5f + 0.5f * (float)Math.Sin(AdaptivePhase);
        int alpha = (int)(100 + 155 * glow);
        float thickness = 3.0f + 2.0f * glow;

        // Outer glow (soft halo)
        using (GraphicsPath outerPath = GetRoundedRectangle(
            new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6), CornerRadius + 3))
        using (Pen glowPen = new Pen(Color.FromArgb((int)(alpha * 0.4f), 0, 210, 180), thickness + 4f))
        {
            g.DrawPath(glowPen, outerPath);
        }

        // Main pulsing ring
        using (GraphicsPath ringPath = GetRoundedRectangle(rect, CornerRadius))
        using (Pen ringPen = new Pen(Color.FromArgb(alpha, 0, 200, 170), thickness))
        {
            g.DrawPath(ringPen, ringPath);
        }

        // Bobbing animation offset for the ribbon
        float bobOffset = 2f * (float)Math.Sin(AdaptivePhase * 1.3f);

        // "New for you!" ribbon in top-right corner
        string ribbonText = string.IsNullOrEmpty(AdaptiveRibbonText) ? "New for you!" : AdaptiveRibbonText;
        DrawAdaptiveRibbon(g, ribbonText,
            Color.FromArgb(0, 190, 160),
            Color.White,
            rect, bobOffset);
    }

    private void PaintUnderFocusedState(Graphics g, Rectangle rect)
    {
        // Subtle warm orange outline
        float glow = 0.5f + 0.5f * (float)Math.Sin(AdaptivePhase * 0.6f);
        int alpha = (int)(80 + 80 * glow);

        using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
        using (Pen warmPen = new Pen(Color.FromArgb(alpha, 255, 160, 50), 2.2f))
        {
            g.DrawPath(warmPen, path);
        }

        // Small "Try this!" ribbon
        if (!string.IsNullOrEmpty(AdaptiveRibbonText))
        {
            DrawAdaptiveRibbon(g, AdaptiveRibbonText,
                Color.FromArgb(220, 140, 30),
                Color.White,
                rect, 0f);
        }
    }

    private void PaintFamiliarState(Graphics g, Rectangle rect)
    {
        // Gentle gold accent — positive highlight for "you used this most"
        float glow = 0.5f + 0.5f * (float)Math.Sin(AdaptivePhase * 0.6f);
        int outerAlpha = (int)(90 + 100 * glow);
        int innerAlpha = (int)(160 + 80 * glow);
        float thickness = 2.6f + 1.2f * glow;

        // Soft outer halo (warm amber)
        using (GraphicsPath outerPath = GetRoundedRectangle(
            new Rectangle(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6), CornerRadius + 3))
        using (Pen halo = new Pen(Color.FromArgb(outerAlpha, 255, 195, 70), thickness + 3f))
        {
            g.DrawPath(halo, outerPath);
        }

        // Main gold border
        using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
        using (Pen ringPen = new Pen(Color.FromArgb(innerAlpha, 240, 175, 40), thickness))
        {
            g.DrawPath(ringPen, path);
        }

        // "✓ explored" badge in bottom-right
        // Ribbon text takes priority when set (e.g. "Picked up from last time!"),
        // otherwise show the small explored badge in the corner.
        if (!string.IsNullOrEmpty(AdaptiveRibbonText))
        {
            float bobOffset = 1.5f * (float)Math.Sin(AdaptivePhase);
            DrawAdaptiveRibbon(g, AdaptiveRibbonText,
                Color.FromArgb(220, 160, 30),
                Color.White,
                rect, bobOffset);
        }
        else
        {
            string badge = "\u2713 explored";
            using (Font badgeFont = new Font("Arial", 8.5f, FontStyle.Bold))
            {
                SizeF textSize = g.MeasureString(badge, badgeFont);
                float bx = rect.Right - textSize.Width - 14;
                float by = rect.Bottom - textSize.Height - 10;

                RectangleF badgeRect = new RectangleF(bx - 6, by - 3, textSize.Width + 12, textSize.Height + 6);
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(190, 220, 165, 30)))
                    g.FillRectangle(bgBrush, badgeRect);

                using (SolidBrush textBrush = new SolidBrush(Color.White))
                    g.DrawString(badge, badgeFont, textBrush, bx, by);
            }
        }
    }

    private void DrawAdaptiveRibbon(Graphics g, string text, Color bgColor, Color textColor,
                                     Rectangle panelRect, float yOffset)
    {
        using (Font ribbonFont = new Font("Arial", 9f, FontStyle.Bold))
        {
            SizeF textSize = g.MeasureString(text, ribbonFont);
            float rx = panelRect.Right - textSize.Width - 16;
            float ry = panelRect.Y + 8 + yOffset;

            RectangleF ribbonRect = new RectangleF(rx - 6, ry - 2, textSize.Width + 12, textSize.Height + 4);

            // Ribbon background with rounded edges
            using (SolidBrush bgBrush = new SolidBrush(bgColor))
                g.FillRectangle(bgBrush, ribbonRect);

            using (SolidBrush textBrush = new SolidBrush(textColor))
                g.DrawString(text, ribbonFont, textBrush, rx, ry);
        }
    }

    private GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        int d = radius * 2;

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}

// ======================== HomePage ========================
public class HomePage : Form, TuioListener
{
    private UserData currentUser;
    private TuioClient client;
    private bool pageOpen = false;
    private int lastSymbolID = -1;
    private ElegantCircularMenu circMenu;
    private Image backgroundImage;

    private Label lblWelcome;
    private Label lblTitle;
    private Label lblSubtitle;
    private Label lblDescription;
    private Label lblInstruction;
    private Label lblFooter;

    private SmoothPanel overlayPanel;
    private RoundedShadowPanel heroPanel;
    private RoundedShadowPanel cardPrimary;
    private RoundedShadowPanel cardSecondary;
    private RoundedShadowPanel cardHighSchool;

    private double _bookAngle = 0.0;
    private System.Windows.Forms.Timer _bookTimer;
    private List<UserData> cachedUsers = new List<UserData>();

    private GestureClient _gestureClient;
    private ExpressionClient _expressionClient;
    private System.Windows.Forms.Timer _reconnectTimer;
    private bool _isProcessingGesture = false;

    // ── Admin Bluetooth MAC ───────────────────────────────────────────────
    private const string ADMIN_BLUETOOTH_MAC = "E8:3A:12:40:1A:70";
    private bool _adminPageOpen = false;

    // ── Face Recognition ──────────────────────────────────
    private FaceIDClient _faceIDClient;
    private System.Windows.Forms.Timer _faceReconnectTimer;
    private bool _faceLoginCompleted = false;
    private RoundedShadowPanel _faceScanHUD;
    private Label _faceScanStatusLabel;
    private Label _faceScanSubLabel;
    private SmoothPanel _faceScanRing;
    private System.Windows.Forms.Timer _facePulseTimer;
    private float _facePulsePhase = 0f;
    private SpeechSynthesizer _homeSynth;

    // ── Dual login coordinator (parallel face + Bluetooth race) ─────────
    private DualLoginManager _dualLogin;
    private CancellationTokenSource _dualLoginCts;
    private bool _enrollPageOpen = false;
    private int _lastEnrollTrigger = -1;

    public HomePage(int port)
    {
        string path = Path.Combine(Application.StartupPath, "Data", "primary_vocabulary.json");
        string json = File.ReadAllText(path);
        var words = JsonConvert.DeserializeObject<List<WordItemData>>(json);

        this.Text = "Smart Padel Coaching System";
        this.WindowState = FormWindowState.Maximized;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        LoadBackgroundImage();
        BuildUI();
        ApplyTheme();
        AppSettings.ThemeChanged += OnThemeChanged;
        cachedUsers = LoadUsersFromJson();

        this.Load += (s, e) => ArrangeControls();
        this.Resize += (s, e) => ArrangeControls();
        this.Shown += (s, e) =>
        {
            ArrangeControls();
            this.Invalidate(true);
            this.Update();
        };

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        InitializeGestureClient();
        InitializeExpressionClient();
        InitializeFaceID();

        _bookTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _bookTimer.Tick += delegate { _bookAngle += 0.04; this.Invalidate(); };
        _bookTimer.Start();

        circMenu = new ElegantCircularMenu();
        circMenu.Size = new Size(420, 420);
        circMenu.Location = new Point(this.ClientSize.Width - 430, this.ClientSize.Height - 430);
        circMenu.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        circMenu.OnActionTriggered += (markerID) => { };

        this.Controls.Add(circMenu);
        circMenu.BringToFront();

        this.FormClosed += delegate
        {
            _bookTimer.Stop();
            _bookTimer.Dispose();
        };
    }

    private string MapDisplayedLevel(string rawLevel)
    {
        if (string.IsNullOrWhiteSpace(rawLevel))
            return "Unknown";

        if (rawLevel.Equals("Primary", StringComparison.OrdinalIgnoreCase))
            return "Beginner";

        if (rawLevel.Equals("Secondary", StringComparison.OrdinalIgnoreCase))
            return "Intermediate";

        if (rawLevel.Equals("High School", StringComparison.OrdinalIgnoreCase) ||
            rawLevel.Equals("HighSchool", StringComparison.OrdinalIgnoreCase))
            return "Advanced";

        return rawLevel;
    }

    private string MapDetectedPlayerLevel(string rawLevel)
    {
        return "Detected Level: " + MapDisplayedLevel(rawLevel);
    }

    private void ScanBluetoothDevices()
    {
        BluetoothClient client = new BluetoothClient();
        var devices = client.DiscoverDevices();

        foreach (var device in devices)
        {
            string formattedMac = FormatBluetoothAddress(device.DeviceAddress.ToString());

            MessageBox.Show(
                "Name: " + device.DeviceName + "\n" +
                "MAC: " + formattedMac
            );
        }
    }

    private string FormatBluetoothAddress(string rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
            return "";

        rawAddress = rawAddress.Replace(":", "").Replace("-", "").ToUpper();

        if (rawAddress.Length != 12)
            return rawAddress;

        return string.Join(":", Enumerable.Range(0, 6)
            .Select(i => rawAddress.Substring(i * 2, 2)));
    }

    private void ApplyTheme()
    {
        this.BackColor = AppSettings.PageBg;

        lblWelcome.ForeColor = Color.White;

        heroPanel.FillColor = AppSettings.IsDarkMode
            ? Color.FromArgb(185, 20, 30, 45)
            : Color.FromArgb(182, 255, 255, 255);

        heroPanel.BorderColor = AppSettings.IsDarkMode
            ? Color.FromArgb(90, 120, 160)
            : Color.FromArgb(130, 255, 255, 255);

        lblTitle.ForeColor = AppSettings.TitleText;
        lblSubtitle.ForeColor = AppSettings.IsDarkMode
            ? Color.FromArgb(205, 215, 230)
            : Color.FromArgb(45, 70, 95);

        lblDescription.ForeColor = AppSettings.SubText;
        lblInstruction.ForeColor = AppSettings.IsDarkMode
            ? Color.FromArgb(225, 232, 245)
            : Color.FromArgb(245, 248, 255);

        lblFooter.ForeColor = Color.White;

        if (cardPrimary != null) cardPrimary.FillColor = AppSettings.CardFill;
        if (cardSecondary != null) cardSecondary.FillColor = AppSettings.CardFill;
        if (cardHighSchool != null) cardHighSchool.FillColor = AppSettings.CardFill;

        if (cardPrimary != null) cardPrimary.BorderColor = AppSettings.Border;
        if (cardSecondary != null) cardSecondary.BorderColor = AppSettings.Border;
        if (cardHighSchool != null) cardHighSchool.BorderColor = AppSettings.Border;

        Invalidate(true);
    }

    private void OnThemeChanged()
    {
        if (!this.IsDisposed)
            this.BeginInvoke((MethodInvoker)(() => ApplyTheme()));
    }

    private void LoadBackgroundImage()
    {
        try
        {
            string path = Path.Combine(Application.StartupPath, "Data", "2.png");
            if (!File.Exists(path))
                path = Path.Combine(Application.StartupPath, "2.png");
            if (!File.Exists(path))
                path = Path.Combine(Application.StartupPath, "Images", "2.png");
            if (File.Exists(path))
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var img = Image.FromStream(fs))
                {
                    backgroundImage = new Bitmap(img);
                }
            }
            else
            {
                backgroundImage = null;
            }
        }
        catch
        {
            backgroundImage = null;
        }
    }

    private List<UserData> LoadUsersFromJson()
    {
        string path = Path.Combine(Application.StartupPath, "Data", "users.json");

        try
        {
            if (!File.Exists(path))
                return new List<UserData>();

            string json = File.ReadAllText(path).Trim();
            if (string.IsNullOrEmpty(json) || json == "[]")
                return new List<UserData>();

            var list = JsonConvert.DeserializeObject<List<UserData>>(json)
                       ?? new List<UserData>();

            // Assign stable deterministic UserId to users that lack one
            foreach (var u in list.Where(u => string.IsNullOrEmpty(u.UserId)))
            {
                string key = (u.Name ?? "") + "|" + (u.BluetoothId ?? "") + "|" + (u.FaceId ?? "");
                u.UserId = "usr_" + Math.Abs(key.GetHashCode()).ToString("x8");
            }

            BtLog($"LoadUsers path={path} count={list.Count}");
            return list;
        }
        catch (Exception ex)
        {
            BtLog($"LoadUsers ERROR: {ex.Message}");
            return new List<UserData>();
        }
    }

    private UserData GetUserByBluetoothId(string bluetoothId)
    {
        // Always reload from disk so Admin Management edits are picked up immediately
        cachedUsers = LoadUsersFromJson();

        string normalizedInput = NormalizeMac(bluetoothId);
        BtLog($"GetUserByBT raw={bluetoothId} normalized={normalizedInput} totalUsers={cachedUsers.Count}");

        foreach (var u in cachedUsers)
        {
            string normalizedStored = NormalizeMac(u.BluetoothId);
            BtLog($"  checking user={u.Name} storedBT={u.BluetoothId} normalized={normalizedStored} role={u.Role} active={u.IsActive}");

            if (normalizedStored == normalizedInput)
            {
                BtLog($"  MATCH found: {u.Name} role={u.Role} active={u.IsActive}");

                // Inactive users cannot log in
                if (!u.IsActive)
                {
                    BtLog($"  BLOCKED: user is inactive");
                    return null;
                }

                // Admin-role users are handled separately via ADMIN_BLUETOOTH_MAC check
                // If somehow an admin-role user has a different MAC, still block player flow
                if (string.Equals(u.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    BtLog($"  BLOCKED: user has Admin role — handled by admin flow");
                    return null;
                }

                return u;
            }
        }

        BtLog($"  NO MATCH for {normalizedInput}");
        return null;
    }

    /// <summary>
    /// Normalizes a Bluetooth MAC address: removes colons, dashes, spaces, converts to uppercase.
    /// E8:C2:DD:1A:36:60 == e8-c2-dd-1a-36-60 == E8C2DD1A3660
    /// </summary>
    private static string NormalizeMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return "";
        return mac.Replace(":", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();
    }

    private static void BtLog(string msg)
    {
        try
        {
            string logPath = Path.Combine(Application.StartupPath, "bluetooth_login_debug_log.txt");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    /// <summary>
    /// Scans Bluetooth devices and returns the first matching MAC.
    /// Returns ADMIN_BLUETOOTH_MAC if the admin device is found (checked first).
    /// Returns a player MAC if a known player device is found.
    /// Returns "" if nothing is found or scan fails.
    /// </summary>
    private string GetCurrentBluetoothId()
    {
        try
        {
            using (BluetoothClient btClient = new BluetoothClient())
            {
                var devices = btClient.DiscoverDevices();
                cachedUsers = LoadUsersFromJson();

                foreach (var device in devices)
                {
                    string formattedMac = FormatBluetoothAddress(device.DeviceAddress.ToString());
                    string normalizedMac = NormalizeMac(formattedMac);
                    BtLog($"Scan found device={device.DeviceName} mac={formattedMac} normalized={normalizedMac}");

                    // Match against users.json — role determines routing
                    var matched = cachedUsers.FirstOrDefault(u =>
                        !string.IsNullOrEmpty(u.BluetoothId) &&
                        NormalizeMac(u.BluetoothId) == normalizedMac);

                    if (matched != null)
                    {
                        BtLog($"MAC matched user={matched.Name} role={matched.Role} active={matched.IsActive}");
                        return formattedMac;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BtLog($"GetCurrentBluetoothId ERROR: {ex.Message}");
        }

        return "";
    }

    private void BuildUI()
    {
        overlayPanel = new SmoothPanel();
        overlayPanel.BackColor = Color.Transparent;
        overlayPanel.Dock = DockStyle.Fill;

        lblWelcome = new Label();
        lblWelcome.Text = "WELCOME TO";
        lblWelcome.Font = new Font("Arial", 14, FontStyle.Bold);
        lblWelcome.ForeColor = Color.White;
        lblWelcome.AutoSize = false;
        lblWelcome.Size = new Size(250, 30);
        lblWelcome.TextAlign = ContentAlignment.MiddleCenter;
        lblWelcome.BackColor = Color.Transparent;

        heroPanel = CreateGlassPanel(Color.FromArgb(182, 255, 255, 255), 30);
        heroPanel.Size = new Size(1080, 260);

        lblTitle = new Label();
        lblTitle.Text = "Smart Padel Coaching System";
        lblTitle.Font = new Font("Arial", 30, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(15, 40, 75);
        lblTitle.AutoSize = false;
        lblTitle.Size = new Size(960, 55);
        lblTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblTitle.BackColor = Color.Transparent;

        lblSubtitle = new Label();
        lblSubtitle.Text = "Interactive training experience for padel players across all skill levels";
        lblSubtitle.Font = new Font("Arial", 15, FontStyle.Regular);
        lblSubtitle.ForeColor = Color.FromArgb(50, 75, 105);
        lblSubtitle.AutoSize = false;
        lblSubtitle.Size = new Size(960, 35);
        lblSubtitle.TextAlign = ContentAlignment.MiddleCenter;
        lblSubtitle.BackColor = Color.Transparent;

        lblDescription = new Label();
        lblDescription.Text =
            "The system automatically detects the player and prepares the training journey.\n" +
            "Use Bluetooth login and interactive controls to start your padel coaching session.";
        lblDescription.Font = new Font("Arial", 12, FontStyle.Regular);
        lblDescription.ForeColor = Color.FromArgb(70, 90, 110);
        lblDescription.AutoSize = false;
        lblDescription.Size = new Size(960, 60);
        lblDescription.TextAlign = ContentAlignment.MiddleCenter;
        lblDescription.BackColor = Color.Transparent;

        heroPanel.Controls.Add(lblTitle);
        heroPanel.Controls.Add(lblSubtitle);
        heroPanel.Controls.Add(lblDescription);

        lblInstruction = new Label();
        lblInstruction.Text = "Get ready... your padel level is being detected 🎾";
        lblInstruction.Font = new Font("Arial", 13, FontStyle.Bold);
        lblInstruction.ForeColor = Color.FromArgb(245, 248, 255);
        lblInstruction.AutoSize = false;
        lblInstruction.Size = new Size(900, 32);
        lblInstruction.TextAlign = ContentAlignment.MiddleCenter;
        lblInstruction.BackColor = Color.Transparent;

        cardPrimary = CreateLevelCard(
            "B",
            "Beginner",
            "New to padel? Start with basic strokes and simple training",
            Color.FromArgb(228, 255, 245, 219),
            Color.FromArgb(255, 192, 140),
            "primary");

        cardSecondary = CreateLevelCard(
            "I",
            "Intermediate",
            "Improve your skills with faster reactions and advanced drills",
            Color.FromArgb(228, 228, 242, 255),
            Color.FromArgb(100, 155, 255),
            "secondary");

        cardHighSchool = CreateLevelCard(
            "A",
            "Advanced",
            "Master the game with high-speed challenges and competition",
            Color.FromArgb(228, 232, 245, 229),
            Color.FromArgb(90, 170, 125),
            "high");

        lblFooter = new Label();
        lblFooter.Text = "Scanning for player...";
        lblFooter.Font = new Font("Arial", 13, FontStyle.Italic);
        lblFooter.ForeColor = Color.White;
        lblFooter.AutoSize = false;
        lblFooter.Size = new Size(500, 30);
        lblFooter.TextAlign = ContentAlignment.MiddleCenter;
        lblFooter.BackColor = Color.Transparent;

        overlayPanel.Controls.Add(lblWelcome);
        overlayPanel.Controls.Add(heroPanel);
        overlayPanel.Controls.Add(lblInstruction);
        overlayPanel.Controls.Add(cardPrimary);
        overlayPanel.Controls.Add(cardSecondary);
        overlayPanel.Controls.Add(cardHighSchool);
        overlayPanel.Controls.Add(lblFooter);

        this.Controls.Add(overlayPanel);

        // ── Face Scan HUD ────────────────────────────────────────
        _faceScanHUD = new RoundedShadowPanel
        {
            CornerRadius = 28,
            FillColor = Color.FromArgb(210, 12, 20, 40),
            BorderColor = Color.FromArgb(100, 80, 160, 255),
            BorderThickness = 2f,
            ShadowColor = Color.FromArgb(80, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 6,
            ShadowOffsetY = 10,
            Size = new Size(400, 200),
            Visible = false
        };

        _faceScanRing = new SmoothPanel();
        _faceScanRing.Size = new Size(70, 70);
        _faceScanRing.BackColor = Color.Transparent;
        _faceScanRing.Paint += (s, ev) =>
        {
            Graphics g = ev.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float glow = 0.5f + 0.5f * (float)Math.Sin(_facePulsePhase);
            int alpha = (int)(100 + 120 * glow);
            Color ringColor = _faceLoginCompleted
                ? Color.FromArgb(alpha, 60, 220, 100)
                : Color.FromArgb(alpha, 80, 160, 255);
            using (Pen p = new Pen(ringColor, 3.5f))
                g.DrawEllipse(p, 6, 6, 56, 56);
            using (Pen p2 = new Pen(Color.FromArgb((int)(60 * glow), 255, 255, 255), 1.5f))
                g.DrawEllipse(p2, 14, 14, 40, 40);
            string icon = _faceLoginCompleted ? "\u2714" : "\uD83D\uDC64";
            using (Font f = new Font("Segoe UI", 18, FontStyle.Regular))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
            {
                SizeF sz = g.MeasureString(icon, f);
                g.DrawString(icon, f, b, 35 - sz.Width / 2, 35 - sz.Height / 2);
            }
        };
        _faceScanRing.Location = new Point(30, 50);
        _faceScanHUD.Controls.Add(_faceScanRing);

        _faceScanStatusLabel = new Label
        {
            Text = "Scanning Face...",
            Font = new Font("Arial", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(230, 240, 255),
            AutoSize = false,
            Size = new Size(260, 34),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Location = new Point(115, 60)
        };
        _faceScanHUD.Controls.Add(_faceScanStatusLabel);

        _faceScanSubLabel = new Label
        {
            Text = "Stand in front of the camera",
            Font = new Font("Arial", 11, FontStyle.Italic),
            ForeColor = Color.FromArgb(160, 180, 210),
            AutoSize = false,
            Size = new Size(260, 24),
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Location = new Point(115, 100)
        };
        _faceScanHUD.Controls.Add(_faceScanSubLabel);

        this.Controls.Add(_faceScanHUD);
        _faceScanHUD.BringToFront();

        // Pulse animation for the scan ring
        _facePulseTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _facePulseTimer.Tick += (s, ev) =>
        {
            _facePulsePhase += 0.09f;
            if (_facePulsePhase > 6.28f) _facePulsePhase = 0f;
            if (_faceScanRing != null && _faceScanHUD.Visible)
                _faceScanRing.Invalidate();
        };

        lblWelcome.BringToFront();
        heroPanel.BringToFront();
        lblInstruction.BringToFront();
        cardPrimary.BringToFront();
        cardSecondary.BringToFront();
        cardHighSchool.BringToFront();
        lblFooter.BringToFront();

        NavHelper.AddNavBar(this, "Smart Padel Coaching System", false);
    }

    private RoundedShadowPanel CreateLevelCard(string markerNumber, string title, string desc, Color backColor, Color accent, string iconType)
    {
        RoundedShadowPanel card = CreateRoundedCard(backColor, 25);
        card.Size = new Size(300, 195);

        Panel topBar = new Panel();
        topBar.Size = new Size(card.Width - 24, 4);
        topBar.Location = new Point(12, 10);
        topBar.BackColor = accent;

        Panel iconBadge = CreateIconBadge(accent, iconType, markerNumber);
        iconBadge.Location = new Point(18, 22);

        Label lblMarker = new Label();
        lblMarker.Text = "Auto Detect";
        lblMarker.Font = new Font("Arial", 10, FontStyle.Bold);
        lblMarker.ForeColor = Color.FromArgb(80, 110, 140);
        lblMarker.AutoSize = false;
        lblMarker.Size = new Size(150, 22);
        lblMarker.Location = new Point(75, 32);
        lblMarker.BackColor = Color.Transparent;

        Label lblCardTitle = new Label();
        lblCardTitle.Text = title;
        lblCardTitle.Font = new Font("Arial", 20, FontStyle.Bold);
        lblCardTitle.ForeColor = Color.FromArgb(15, 40, 65);
        lblCardTitle.AutoSize = false;
        lblCardTitle.Size = new Size(240, 40);
        lblCardTitle.Location = new Point(18, 76);
        lblCardTitle.BackColor = Color.Transparent;

        Label lblDesc = new Label();
        lblDesc.Text = desc;
        lblDesc.Font = new Font("Arial", 11, FontStyle.Regular);
        lblDesc.ForeColor = Color.FromArgb(70, 90, 110);
        lblDesc.AutoSize = false;
        lblDesc.Size = new Size(250, 55);
        lblDesc.Location = new Point(18, 125);
        lblDesc.BackColor = Color.Transparent;

        card.Controls.Add(topBar);
        card.Controls.Add(iconBadge);
        card.Controls.Add(lblMarker);
        card.Controls.Add(lblCardTitle);
        card.Controls.Add(lblDesc);

        return card;
    }

    private Panel CreateIconBadge(Color accent, string iconType, string number)
    {
        SmoothPanel badge = new SmoothPanel();
        badge.Size = new Size(52, 52);
        badge.BackColor = Color.Transparent;

        badge.Paint += (s, e) =>
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowRect = new Rectangle(4, 5, 42, 42);
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
                g.FillEllipse(shadowBrush, shadowRect);

            Rectangle rect = new Rectangle(2, 2, 42, 42);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = LightenColor(accent, 35);
                    pgb.SurroundColors = new Color[] { accent };
                    g.FillPath(pgb, path);
                }

                using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2))
                    g.DrawPath(pen, path);
            }

            DrawMiniIcon(g, iconType, new Rectangle(12, 12, 22, 22));

            using (Font f = new Font("Arial", 8, FontStyle.Bold))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            {
                SizeF sz = g.MeasureString(number, f);
                g.DrawString(number, f, whiteBrush, 31 - sz.Width / 2, 33 - sz.Height / 2);
            }
        };

        return badge;
    }

    private void DrawMiniIcon(Graphics g, string iconType, Rectangle r)
    {
        using (Pen pen = new Pen(Color.White, 2.2f))
        using (SolidBrush brush = new SolidBrush(Color.White))
        {
            if (iconType == "primary")
            {
                Rectangle racketHead = new Rectangle(r.X + 4, r.Y + 2, 12, 14);
                g.DrawEllipse(pen, racketHead);
                g.DrawLine(pen, racketHead.X + racketHead.Width / 2, racketHead.Bottom, racketHead.X + racketHead.Width / 2 + 4, racketHead.Bottom + 6);
                g.FillEllipse(brush, r.X + 17, r.Y + 15, 4, 4);
            }
            else if (iconType == "secondary")
            {
                Rectangle racketHead = new Rectangle(r.X + 3, r.Y + 1, 13, 15);
                g.DrawEllipse(pen, racketHead);
                g.DrawLine(pen, racketHead.X + racketHead.Width / 2, racketHead.Bottom, racketHead.X + racketHead.Width / 2 + 5, racketHead.Bottom + 7);
                g.DrawArc(pen, r.X + 13, r.Y + 8, 8, 8, 20, 280);
            }
            else if (iconType == "high")
            {
                PointF[] star = CreateStarPoints(r.X + r.Width / 2f, r.Y + r.Height / 2f, 10, 5);
                g.FillPolygon(brush, star);
            }
        }
    }

    private PointF[] CreateStarPoints(float cx, float cy, float outerRadius, float innerRadius)
    {
        PointF[] pts = new PointF[10];
        double angle = -Math.PI / 2;

        for (int i = 0; i < 10; i++)
        {
            float radius = (i % 2 == 0) ? outerRadius : innerRadius;
            pts[i] = new PointF(
                cx + (float)(Math.Cos(angle) * radius),
                cy + (float)(Math.Sin(angle) * radius));
            angle += Math.PI / 5;
        }
        return pts;
    }

    private RoundedShadowPanel CreateGlassPanel(Color backColor, int radius)
    {
        return new RoundedShadowPanel
        {
            CornerRadius = radius,
            FillColor = backColor,
            BorderColor = Color.FromArgb(130, 255, 255, 255),
            BorderThickness = 1.6f,
            ShadowColor = Color.FromArgb(45, 0, 0, 0),
            DrawGloss = true,
            ShadowOffsetX = 5,
            ShadowOffsetY = 8
        };
    }

    private RoundedShadowPanel CreateRoundedCard(Color backColor, int radius)
    {
        return new RoundedShadowPanel
        {
            CornerRadius = radius,
            FillColor = backColor,
            BorderColor = Color.FromArgb(150, 255, 255, 255),
            BorderThickness = 1.2f,
            ShadowColor = Color.FromArgb(38, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 5,
            ShadowOffsetY = 8
        };
    }

    private void ArrangeControls()
    {
        overlayPanel.Size = this.ClientSize;
        overlayPanel.Location = new Point(0, 0);

        lblWelcome.Location = new Point((this.ClientSize.Width - lblWelcome.Width) / 2, 28);

        heroPanel.Location = new Point((this.ClientSize.Width - heroPanel.Width) / 2, 60);
        lblTitle.Location = new Point((heroPanel.Width - lblTitle.Width) / 2, 35);
        lblSubtitle.Location = new Point((heroPanel.Width - lblSubtitle.Width) / 2, 100);
        lblDescription.Location = new Point((heroPanel.Width - lblDescription.Width) / 2, 150);

        lblInstruction.Location = new Point((this.ClientSize.Width - lblInstruction.Width) / 2, 345);

        int cardsY = 395;
        int spacing = 35;
        int totalWidth = cardPrimary.Width + cardSecondary.Width + cardHighSchool.Width + (spacing * 2);
        int startX = (this.ClientSize.Width - totalWidth) / 2;

        cardPrimary.Location = new Point(startX, cardsY);
        cardSecondary.Location = new Point(startX + cardPrimary.Width + spacing, cardsY);
        cardHighSchool.Location = new Point(startX + cardPrimary.Width + cardSecondary.Width + (spacing * 2), cardsY);

        lblFooter.Location = new Point((this.ClientSize.Width - lblFooter.Width) / 2, cardsY + 220);

        // Position Face Scan HUD centered above the cards
        if (_faceScanHUD != null)
            _faceScanHUD.Location = new Point((this.ClientSize.Width - _faceScanHUD.Width) / 2, cardsY + 230);

        overlayPanel.Invalidate(true);
        heroPanel.Invalidate(true);
        cardPrimary.Invalidate(true);
        cardSecondary.Invalidate(true);
        cardHighSchool.Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.HighQuality;

        if (backgroundImage != null)
        {
            g.DrawImage(backgroundImage, this.ClientRectangle);
        }
        else
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(
                this.ClientRectangle,
                AppSettings.HomeBgTop,
                AppSettings.HomeBgBottom,
                90f))
            {
                g.FillRectangle(brush, this.ClientRectangle);
            }
        }

        using (SolidBrush overlayBrush = new SolidBrush(
            AppSettings.IsDarkMode
                ? Color.FromArgb(120, 0, 0, 0)
                : Color.FromArgb(95, 10, 28, 48)))
        {
            g.FillRectangle(overlayBrush, this.ClientRectangle);
        }

        Draw3DBook(g);

        base.OnPaint(e);
    }

    private void Draw3DBook(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int cx = this.ClientSize.Width - 90;
        int cy = 195;
        int bookW = 140;
        int bookH = 200;
        int spineW = 28;
        int topH = 18;

        double cosA = Math.Cos(_bookAngle);
        int frontW = (int)(bookW * Math.Abs(cosA));
        bool frontVisible = cosA >= 0;

        Color frontColor = Color.FromArgb(220, 60, 90, 160);
        Color spineColor = Color.FromArgb(255, 35, 60, 120);
        Color topColor = Color.FromArgb(200, 80, 115, 185);
        Color pageColor = Color.FromArgb(245, 240, 225);
        Color glowColor = Color.FromArgb(60, 140, 200, 255);

        int left = cx - frontW;
        int right = cx;
        int top = cy;
        int bot = cy + bookH;

        Rectangle glowRect = new Rectangle(left - spineW - 20, top - 20, frontW + spineW + 40, bookH + 40);
        using (GraphicsPath glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(glowRect);
            using (PathGradientBrush gb = new PathGradientBrush(glowPath))
            {
                gb.CenterColor = glowColor;
                gb.SurroundColors = new Color[] { Color.Transparent };
                g.FillPath(gb, glowPath);
            }
        }

        Point[] spinePts = new Point[] {
            new Point(left - spineW, top  + topH),
            new Point(left,          top  + topH),
            new Point(left,          bot),
            new Point(left - spineW, bot)
        };
        using (SolidBrush sb = new SolidBrush(spineColor))
            g.FillPolygon(sb, spinePts);
        using (Pen p = new Pen(Color.FromArgb(100, 255, 255, 255), 1))
            g.DrawPolygon(p, spinePts);

        Point[] topFace = new Point[] {
            new Point(left - spineW, top + topH),
            new Point(left,          top + topH),
            new Point(right,         top),
            new Point(left - spineW, top)
        };
        using (SolidBrush sb = new SolidBrush(topColor))
            g.FillPolygon(sb, topFace);
        using (Pen p = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
            g.DrawPolygon(p, topFace);

        if (frontW < bookW - 5)
        {
            Rectangle pages = new Rectangle(right, top + topH, 6, bookH - topH);
            using (SolidBrush pb = new SolidBrush(pageColor))
                g.FillRectangle(pb, pages);
        }

        if (frontW > 3)
        {
            Rectangle front = new Rectangle(left, top + topH, frontW, bookH - topH);
            Color cover = frontVisible ? frontColor : Color.FromArgb(180, 40, 65, 130);
            using (LinearGradientBrush lb = new LinearGradientBrush(
                front,
                LightenColor(cover, 30),
                cover,
                LinearGradientMode.Horizontal))
            {
                g.FillRectangle(lb, front);
            }

            using (Pen p = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f))
                g.DrawRectangle(p, front);

            if (frontVisible && frontW > 50)
            {
                g.Clip = new Region(front);
                float textScale = (float)frontW / bookW;
                using (Font titleFont = new Font("Arial", (int)(11 * textScale + 1), FontStyle.Bold))
                using (Font subFont = new Font("Arial", (int)(7 * textScale + 1), FontStyle.Regular))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
                {
                    using (Pen lp = new Pen(Color.FromArgb(80, 255, 255, 255), 1))
                    {
                        g.DrawLine(lp, front.Left + 8, front.Top + 30, front.Right - 8, front.Top + 30);
                        g.DrawLine(lp, front.Left + 8, front.Bottom - 30, front.Right - 8, front.Bottom - 30);
                    }

                    SizeF sz = g.MeasureString("Padel", titleFont);
                    g.DrawString("Padel", titleFont, tb, front.Left + (front.Width - sz.Width) / 2, front.Top + front.Height / 2 - sz.Height);
                    SizeF sz2 = g.MeasureString("Coach", subFont);
                    g.DrawString("Coach", subFont, tb, front.Left + (front.Width - sz2.Width) / 2, front.Top + front.Height / 2 + 4);
                }
                g.ResetClip();
            }

            Rectangle glossRect = new Rectangle(front.Left + 4, front.Top + 4, Math.Max(1, front.Width / 3), Math.Max(1, front.Height / 3));
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddRectangle(glossRect);
                using (LinearGradientBrush gloss =
                    new LinearGradientBrush(glossRect, Color.FromArgb(55, 255, 255, 255), Color.Transparent, 135f))
                {
                    g.FillPath(gloss, gp);
                }
            }
        }
    }

    public void addTuioObject(TuioObject o)
    {
        if (o.SymbolID >= 21 && o.SymbolID <= 30)
        {
            float angleDeg = o.Angle * (180f / (float)Math.PI);
            this.BeginInvoke((MethodInvoker)(() =>
            {
                circMenu.HandleTUIO(o.SymbolID);
                circMenu.HandleMarkerAdded(angleDeg);
            }));
            return;
        }

        // Marker 10 → open enrollment wizard
        if (o.SymbolID == 10)
        {
            if (_lastEnrollTrigger == 10) return;
            _lastEnrollTrigger = 10;
            this.BeginInvoke((MethodInvoker)(() => OpenEnrollmentPage()));
            return;
        }
    }

    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == lastSymbolID)
            lastSymbolID = -1;

        if (o.SymbolID == 30)
            this.BeginInvoke((MethodInvoker)(() => circMenu.HandleMarkerRemoved()));

        if (o.SymbolID == 10)
            _lastEnrollTrigger = -1;
    }

    public void updateTuioObject(TuioObject o)
    {
        if (o.SymbolID == 30)
        {
            float angleDeg = o.Angle * (180f / (float)Math.PI);
            this.BeginInvoke((MethodInvoker)(() => circMenu.HandleMarkerRotation(angleDeg)));
        }
    }

    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    [STAThread]
    public static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Catch any unhandled UI-thread exception and log it before showing the dialog
        Application.ThreadException += (s, e) =>
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "content_manager_error_log.txt");
                string entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED THREAD EXCEPTION\n" +
                    $"Type:    {e.Exception.GetType().FullName}\n" +
                    $"Message: {e.Exception.Message}\n" +
                    $"Stack:\n{e.Exception.StackTrace}\n" +
                    (e.Exception.InnerException != null
                        ? $"Inner: {e.Exception.InnerException.Message}\n{e.Exception.InnerException.StackTrace}\n"
                        : "") +
                    new string('-', 60) + "\n";
                System.IO.File.AppendAllText(logPath, entry);
            }
            catch { }
        };

        Application.Run(new HomePage(3333));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (client != null)
        {
            client.removeTuioListener(this);
            client.disconnect();
        }

        if (backgroundImage != null)
            backgroundImage.Dispose();

        AppSettings.ThemeChanged -= OnThemeChanged;

        _reconnectTimer?.Stop();
        _reconnectTimer?.Dispose();
        _gestureClient?.Disconnect();
        _expressionClient?.Disconnect();
        GestureRouter.OnGestureMarker -= HandleGestureMarker;

        // Face ID + dual-login cleanup
        _faceReconnectTimer?.Stop();
        _faceReconnectTimer?.Dispose();
        _facePulseTimer?.Stop();
        _facePulseTimer?.Dispose();
        _faceIDClient?.Disconnect();
        FaceIDRouter.OnFaceScanProgress -= HandleFaceScanProgress;
        FaceIDRouter.OnFaceRecognized -= HandlePermanentFaceLogin;
        try { _dualLoginCts?.Cancel(); } catch { }
        _dualLoginCts?.Dispose();
        try { _homeSynth?.Dispose(); } catch { }

        base.OnFormClosed(e);
    }

    private Color LightenColor(Color color, int amount)
    {
        int r = Math.Min(255, color.R + amount);
        int g = Math.Min(255, color.G + amount);
        int b = Math.Min(255, color.B + amount);
        return Color.FromArgb(r, g, b);
    }

    private void InitializeGestureClient()
    {
        GestureRouter.OnGestureMarker += HandleGestureMarker;

        _gestureClient = new GestureClient();
        ConnectGestureWithRetry();

        _reconnectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _reconnectTimer.Tick += (s, e) =>
        {
            if (!_gestureClient.IsConnected)
                ConnectGestureWithRetry();
        };
        _reconnectTimer.Start();
    }

    private void InitializeExpressionClient()
    {
        _expressionClient = new ExpressionClient();
        _expressionClient.Connect("127.0.0.1", 5005);

        var exprTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        exprTimer.Tick += (s, e) =>
        {
            if (_expressionClient != null && !_expressionClient.IsConnected)
                _expressionClient.Connect("127.0.0.1", 5005);
        };
        exprTimer.Start();
    }

    private void ConnectGestureWithRetry()
    {
        try
        {
            _gestureClient.Connect("127.0.0.1", 5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] Gesture connect failed: {ex.Message}");
        }
    }

    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed) return;
        if (_isProcessingGesture) return;

        _isProcessingGesture = true;

        try
        {
            long sessionId = 999900 + markerId + DateTime.Now.Millisecond;
            var dummyObj = new TuioObject(sessionId, markerId, 0.5f, 0.5f, 0f);

            this.BeginInvoke(new Action(() =>
            {
                this.addTuioObject(dummyObj);

                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    this.removeTuioObject(dummyObj);
                    timer.Dispose();
                    _isProcessingGesture = false;
                };
                timer.Start();
            }));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] Gesture error: {ex.Message}");
            _isProcessingGesture = false;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Dual Login (parallel face + Bluetooth race) + Enrollment
    // ══════════════════════════════════════════════════════════════

    private void InitializeFaceID()
    {
        // TTS for greetings
        try
        {
            _homeSynth = new SpeechSynthesizer();
            _homeSynth.Rate = -1;
            _homeSynth.Volume = 100;
            foreach (InstalledVoice v in _homeSynth.GetInstalledVoices())
            {
                if (v.VoiceInfo.Culture.Name.StartsWith("en"))
                { _homeSynth.SelectVoice(v.VoiceInfo.Name); break; }
            }
        }
        catch { _homeSynth = null; }

        // Live confidence ticker (additive listener)
        FaceIDRouter.OnFaceScanProgress += HandleFaceScanProgress;

        // Permanent face-login listener — bypasses DualLoginManager's race
        // lifecycle. If a confident match for a known user arrives at ANY
        // time while we're on HomePage and not logged in, navigate them in.
        // This is the robust fallback for slow auto-enrol / timing edge cases.
        FaceIDRouter.OnFaceRecognized += HandlePermanentFaceLogin;

        // Connect to Python face server
        _faceIDClient = new FaceIDClient();
        ConnectFaceIDWithRetry();

        _faceReconnectTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _faceReconnectTimer.Tick += (s, e) =>
        {
            if (!_faceIDClient.IsConnected && !_faceLoginCompleted)
                ConnectFaceIDWithRetry();
        };
        _faceReconnectTimer.Start();

        // Kick off the dual-login race
        _dualLogin = new DualLoginManager(
            loadUsers:           () => LoadUsersFromJson(),
            scanBluetoothOnce:   () => GetCurrentBluetoothId(),
            adminBluetoothMac:   ADMIN_BLUETOOTH_MAC);

        StartDualLogin();
    }

    private void ConnectFaceIDWithRetry()
    {
        Task.Run(() =>
        {
            try { _faceIDClient.Connect("127.0.0.1", 5001); }
            catch (Exception ex) { Console.WriteLine($"[HomePage] FaceID connect failed: {ex.Message}"); }
        });
    }

    private async void StartDualLogin()
    {
        if (this.IsDisposed) return;

        _faceLoginCompleted = false;
        this.BeginInvoke((MethodInvoker)(() =>
            ShowFaceScanHUD("Scanning...", "Look at the camera or pair your phone")));

        try { _dualLoginCts?.Cancel(); } catch { }
        _dualLoginCts?.Dispose();
        _dualLoginCts = new CancellationTokenSource();

        DualLoginManager.LoginResult result;
        try
        {
            result = await _dualLogin.RunAsync(_dualLoginCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[HomePage] DualLogin exception: {ex.Message}");
            return;
        }

        if (this.IsDisposed) return;

        if (!result.Success)
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (this.IsDisposed) return;
                _faceScanStatusLabel.Text = "Looking for a face...";
                _faceScanSubLabel.Text = "Sit in front of the camera — auto-enrol kicks in after a few seconds";
                lblFooter.Text = "Auto-enrol active — or place marker 10 to enrol manually";
            }));

            // Auto-retry the dual-login race so that slow auto-enrol still wins.
            // The face server keeps broadcasting face_detected events at its own
            // pace, but DualLoginManager only subscribes to OnFaceRecognized while
            // a race is active. Restart the race every couple of seconds while
            // HomePage is the visible page and no one is logged in.
            if (!this.IsDisposed && !pageOpen && !_adminPageOpen && !_enrollPageOpen && !_faceLoginCompleted)
            {
                this.BeginInvoke((MethodInvoker)(() =>
                {
                    var retryTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                    retryTimer.Tick += (s, e) =>
                    {
                        retryTimer.Stop();
                        retryTimer.Dispose();
                        if (!this.IsDisposed && !pageOpen && !_adminPageOpen
                            && !_enrollPageOpen && !_faceLoginCompleted)
                        {
                            StartDualLogin();
                        }
                    };
                    retryTimer.Start();
                }));
            }
            return;
        }

        _faceLoginCompleted = true;
        this.BeginInvoke((MethodInvoker)(() => CompleteLoginAndNavigate(result)));
    }

    private void ShowFaceScanHUD(string status, string sub)
    {
        if (_faceScanHUD == null) return;
        _faceScanStatusLabel.Text = status;
        _faceScanSubLabel.Text = sub;
        _faceScanHUD.FillColor = Color.FromArgb(210, 12, 20, 40);
        _faceScanHUD.BorderColor = Color.FromArgb(100, 80, 160, 255);
        _faceScanHUD.Visible = true;
        _faceScanHUD.BringToFront();
        _faceScanHUD.Invalidate();
        _facePulseTimer?.Start();
    }

    private void HideFaceScanHUD()
    {
        _facePulseTimer?.Stop();
        if (_faceScanHUD != null)
            _faceScanHUD.Visible = false;
    }

    /// <summary>
    /// Long-lived face-login fallback. Subscribed once in InitializeFaceID and
    /// active for the whole HomePage lifecycle. Whenever a confident face match
    /// for a known user arrives, navigate immediately — even if the dual-login
    /// race already gave up.
    /// </summary>
    private void HandlePermanentFaceLogin(string name, float confidence)
    {
        if (this.IsDisposed || _faceLoginCompleted) return;
        if (pageOpen || _adminPageOpen || _enrollPageOpen) return;
        if (string.IsNullOrEmpty(name)) return;
        if (confidence < DualLoginManager.FACE_CONFIDENCE_THRESHOLD) return;

        UserData user = null;
        try
        {
            var users = LoadUsersFromJson();
            user = users.FirstOrDefault(u =>
                u.IsActive && (
                    string.Equals(u.Name?.Trim(),   name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.FaceId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(u.UserId?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) { Console.WriteLine($"[HomePage] PermanentFaceLogin lookup error: {ex.Message}"); return; }

        if (user == null)
        {
            Console.WriteLine($"[HomePage] PermanentFaceLogin: '{name}' not in users.json — ignoring");
            return;
        }

        _faceLoginCompleted = true;
        try { _dualLoginCts?.Cancel(); } catch { }

        Console.WriteLine($"[HomePage] PermanentFaceLogin SUCCESS: {user.Name} ({user.UserId}) conf={confidence:F2}");
        try
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (this.IsDisposed) return;
                CompleteLoginAndNavigate(new DualLoginManager.LoginResult
                {
                    Success    = true,
                    User       = user,
                    Source     = DualLoginManager.LoginSource.Face,
                    Confidence = confidence,
                });
            }));
        }
        catch { }
    }

    private void HandleFaceScanProgress(string userName, float confidence, bool matched)
    {
        if (this.IsDisposed || _faceLoginCompleted) return;
        try
        {
            this.BeginInvoke((MethodInvoker)(() =>
            {
                if (_faceScanSubLabel == null || this.IsDisposed) return;
                if (matched && !string.IsNullOrEmpty(userName))
                    _faceScanSubLabel.Text = $"Recognising {userName} ({confidence:F2})";
                else
                    _faceScanSubLabel.Text = $"Scanning... ({confidence:F2})";
            }));
        }
        catch { }
    }

    private void CompleteLoginAndNavigate(DualLoginManager.LoginResult result)
    {
        if (pageOpen || _adminPageOpen) return;
        var user = result.User;
        if (user == null) return;

        currentUser = user;

        _faceScanStatusLabel.Text = $"Welcome, {user.Name}!";
        _faceScanSubLabel.Text = $"{result.Source} login  •  {MapDisplayedLevel(user.Level)}";
        _faceScanHUD.FillColor = Color.FromArgb(215, 15, 55, 35);
        _faceScanHUD.BorderColor = Color.FromArgb(140, 60, 220, 100);
        _faceScanHUD.Invalidate();
        _faceScanRing?.Invalidate();
        lblFooter.Text = "Player identified: " + user.Name;
        lblInstruction.Text = MapDetectedPlayerLevel(user.Level);

        try
        {
            if (_homeSynth != null && !AppSettings.IsMuted)
            {
                _homeSynth.Rate = AppSettings.VoiceRate;
                _homeSynth.SpeakAsyncCancelAll();
                _homeSynth.SpeakAsync(
                    $"Welcome, {user.Name}. Loading {MapDisplayedLevel(user.Level)} Padel training.");
            }
        }
        catch { }

        var navTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        navTimer.Tick += (s, e) =>
        {
            navTimer.Stop();
            navTimer.Dispose();
            HideFaceScanHUD();
            NavigateByRole(user);
        };
        navTimer.Start();
    }

    private void NavigateByRole(UserData user)
    {
        if (string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            _adminPageOpen = true;
            var adminPage = new AdminDashboardPage(
                tuioClient:  client,
                adminName:   user.Name,
                btConnected: true,
                gestureRef:  _gestureClient,
                faceRef:     _faceIDClient,
                gazeRef:     null);
            adminPage.FormClosed += (s, e) =>
            {
                _adminPageOpen = false;
                lblInstruction.Text = "Waiting to detect your player level automatically...";
                lblFooter.Text = "Scanning for player...";
                this.Show();
                StartDualLogin();
            };
            adminPage.Show();
            this.Hide();
            return;
        }

        pageOpen = true;
        var page = new LearningPage(user, client);
        page.FormClosed += (s, e) =>
        {
            pageOpen = false;
            currentUser = null;
            _faceLoginCompleted = false;
            lblInstruction.Text = "Waiting to detect your player level automatically...";
            lblFooter.Text = "Scanning for player...";
            this.Show();
            StartDualLogin();
        };
        page.Show();
        this.Hide();
    }

    private void OpenEnrollmentPage()
    {
        if (_enrollPageOpen || pageOpen || _adminPageOpen) return;
        if (_faceIDClient == null || !_faceIDClient.IsConnected)
        {
            lblFooter.Text = "Face server not running — start face_recognition_server.py first.";
            return;
        }

        try { _dualLoginCts?.Cancel(); } catch { }
        _enrollPageOpen = true;

        var page = new EnrollmentPage(client, _faceIDClient, onCompleted: newUser =>
        {
            _enrollPageOpen = false;
            if (newUser == null)
            {
                // Cancelled — restart login race
                this.Show();
                StartDualLogin();
                return;
            }

            // Auto-login the new user (skip dual-login wait)
            _faceLoginCompleted = true;
            this.Show();
            CompleteLoginAndNavigate(new DualLoginManager.LoginResult
            {
                Success    = true,
                User       = newUser,
                Source     = DualLoginManager.LoginSource.Face,
                Confidence = 1.0f
            });
        });

        page.FormClosed += (s, e) =>
        {
            _enrollPageOpen = false;
            if (!_faceLoginCompleted && !pageOpen && !_adminPageOpen)
            {
                this.Show();
                StartDualLogin();
            }
        };

        page.Show();
        this.Hide();
    }
}

// ======================== NavBar Helper ========================
public static class NavHelper
{
    public static void AddNavBar(Form host, string pageTitle, bool canGoBack)
    {
        SmoothPanel bar = new SmoothPanel();
        bar.Size = new Size(Math.Max(host.ClientSize.Width, 800), 54);
        bar.Location = new Point(0, 0);
        bar.BackColor = Color.Transparent;
        bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        bar.Paint += (s, ev) =>
        {
            Graphics g = ev.Graphics;
            Rectangle rc = new Rectangle(0, 0, bar.Width, bar.Height);

            using (LinearGradientBrush gb = new LinearGradientBrush(
                rc,
                AppSettings.NavTop,
                AppSettings.NavBottom,
                90f))
            {
                g.FillRectangle(gb, rc);
            }

            using (Pen glow = new Pen(
                AppSettings.IsDarkMode
                    ? Color.FromArgb(50, 85, 120, 170)
                    : Color.FromArgb(55, 100, 170, 255), 2))
            {
                g.DrawLine(glow, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            }
        };

        // Back / Home pill
        RoundedShadowPanel backPill = new RoundedShadowPanel();
        backPill.CornerRadius = 20;
        backPill.FillColor = canGoBack ? Color.FromArgb(38, 72, 145) : Color.FromArgb(22, 42, 80);
        backPill.BorderColor = canGoBack ? Color.FromArgb(90, 130, 230) : Color.FromArgb(40, 65, 115);
        backPill.BorderThickness = 1.4f;
        backPill.ShadowColor = Color.FromArgb(0, 0, 0, 0);
        backPill.DrawGloss = canGoBack;
        backPill.ShadowOffsetX = 0;
        backPill.ShadowOffsetY = 0;
        backPill.Size = new Size(200, 36);
        backPill.Location = new Point(14, 9);

        Label lblBack = new Label();
        lblBack.Text = canGoBack ? "\u2190  Back   [ Marker 20 ]" : "\u2302  Home Screen";
        lblBack.Font = new Font("Arial", 9, FontStyle.Bold);
        lblBack.ForeColor = canGoBack ? Color.FromArgb(215, 232, 255) : Color.FromArgb(155, 190, 240);
        lblBack.Size = new Size(184, 28);
        lblBack.Location = new Point(8, 4);
        lblBack.TextAlign = ContentAlignment.MiddleCenter;
        lblBack.BackColor = Color.Transparent;
        backPill.Controls.Add(lblBack);

        // Center title
        Label lblTitle = new Label();
        lblTitle.Text = pageTitle;
        lblTitle.Font = new Font("Arial", 14, FontStyle.Bold);
        lblTitle.ForeColor = Color.White;
        lblTitle.Size = new Size(520, 36);
        lblTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblTitle.BackColor = Color.Transparent;

        // Right hint pill

        RoundedShadowPanel hintPill = new RoundedShadowPanel();
        hintPill.CornerRadius = 14;
        hintPill.FillColor = Color.FromArgb(18, 34, 68);
        hintPill.BorderColor = Color.FromArgb(38, 62, 115);
        hintPill.BorderThickness = 1.0f;
        hintPill.ShadowColor = Color.FromArgb(0, 0, 0, 0);
        hintPill.DrawGloss = false;
        hintPill.ShadowOffsetX = 0;
        hintPill.ShadowOffsetY = 0;
        hintPill.Size = new Size(162, 32);

        Label lblHint = new Label();
        lblHint.Text = "[ 20 ] \u2192 Go Back";
        lblHint.Font = new Font("Arial", 9, FontStyle.Italic);
        lblHint.ForeColor = Color.FromArgb(95, 148, 220);
        lblHint.Size = new Size(146, 24);
        lblHint.Location = new Point(8, 4);
        lblHint.TextAlign = ContentAlignment.MiddleCenter;
        lblHint.BackColor = Color.Transparent;
        hintPill.Controls.Add(lblHint);
        bar.Controls.Add(hintPill);

        bar.Controls.Add(backPill);
        bar.Controls.Add(lblTitle);
        Action applyTheme = () =>
        {
            bar.Invalidate();

            backPill.FillColor = canGoBack
                ? (AppSettings.IsDarkMode ? Color.FromArgb(42, 72, 120) : Color.FromArgb(38, 72, 145))
                : (AppSettings.IsDarkMode ? Color.FromArgb(26, 34, 56) : Color.FromArgb(22, 42, 80));

            backPill.BorderColor = canGoBack
                ? (AppSettings.IsDarkMode ? Color.FromArgb(90, 130, 190) : Color.FromArgb(90, 130, 230))
                : (AppSettings.IsDarkMode ? Color.FromArgb(55, 70, 100) : Color.FromArgb(40, 65, 115));

            backPill.DrawGloss = canGoBack && !AppSettings.IsDarkMode;

            lblBack.ForeColor = canGoBack
                ? (AppSettings.IsDarkMode ? Color.FromArgb(220, 230, 245) : Color.FromArgb(215, 232, 255))
                : (AppSettings.IsDarkMode ? Color.FromArgb(170, 185, 215) : Color.FromArgb(155, 190, 240));

            lblTitle.ForeColor = AppSettings.IsDarkMode
                ? Color.FromArgb(235, 240, 248)
                : Color.White;

            hintPill.FillColor = AppSettings.IsDarkMode
                ? Color.FromArgb(22, 30, 48)
                : Color.FromArgb(18, 34, 68);

            hintPill.BorderColor = AppSettings.IsDarkMode
                ? Color.FromArgb(55, 75, 110)
                : Color.FromArgb(38, 62, 115);

            lblHint.ForeColor = AppSettings.IsDarkMode
                ? Color.FromArgb(145, 175, 225)
                : Color.FromArgb(95, 148, 220);
        };
        host.Controls.Add(bar);
        bar.BringToFront();

        System.Action layout = delegate
        {
            bar.Width = host.ClientSize.Width;
            lblTitle.Location = new Point((bar.Width - lblTitle.Width) / 2, 9);
            bar.Invalidate();
        };

        host.Load += delegate { layout(); };
        host.Resize += delegate { layout(); };
        host.Shown += delegate { layout(); };
    }
}

/// <summary>
/// ////LearningPage
/// </summary>
public class LearningPage : Form, TuioListener
{
    private TuioClient client;
    private string level;
    private UserData currentUser;
    private Label lblWelcomeUser;
    private bool lessonOpen = false;
    private int lastLessonSymbolID = -1;

    // Happy background mode — set by AdaptiveUIHelper when sad/bored detected
    internal static bool HappyModeActive = false;
    private static Image _happyBgImage = null;

    // ── Debounce: prevent same marker firing twice within 2 seconds ──
    private DateTime _lastMarkerTime = DateTime.MinValue;
    private int _lastMarkerDebounce = -1;
    private const int DEBOUNCE_MS = 2000;

    private Label lblLevelBadge;
    private Label lblTitle;
    private Label lblSubtitle;
    private Label lblHint;
    private RoundedShadowPanel headerPanel;
    private RoundedShadowPanel cardVocabulary;
    private RoundedShadowPanel cardGrammar;
    private RoundedShadowPanel cardArranging;
    private RoundedShadowPanel cardQuiz;
    private RoundedShadowPanel cardSpelling;
    private RoundedShadowPanel cardCompetition;
    private Label lblFooter;

    private RoundedShadowPanel primaryImageFrame;
    private PictureBox picPrimaryStudent;
    private Image primaryStudentImage;

    // ── Gaze Tracking & Adaptive Coaching ──────────────────────
    private GazeClient _gazeClient;
    private AnalyticsEngine _analyticsEngine;
    private System.Windows.Forms.Timer _gazeReconnectTimer;
    private System.Windows.Forms.Timer _focusGlowTimer;
    private float _glowPhase = 0f;
    private List<RoundedShadowPanel> _glowCards = new List<RoundedShadowPanel>();
    private Dictionary<RoundedShadowPanel, Color> _originalBorderColors = new Dictionary<RoundedShadowPanel, Color>();
    private SpeechSynthesizer _learningSynth;
    private void BuildWelcomeLabel()
    {
        lblWelcomeUser = new Label();
        lblWelcomeUser.Text = "Welcome " + currentUser.Name;
        lblWelcomeUser.Font = new Font("Arial", 14, FontStyle.Bold);
        lblWelcomeUser.ForeColor = Color.FromArgb(30, 70, 120);
        lblWelcomeUser.BackColor = Color.FromArgb(230, 240, 255);
        lblWelcomeUser.AutoSize = false;
        lblWelcomeUser.Size = new Size(260, 36);
        lblWelcomeUser.TextAlign = ContentAlignment.MiddleCenter;

        RoundBadgeLabel(lblWelcomeUser, 18);

        this.Controls.Add(lblWelcomeUser);
        lblWelcomeUser.BringToFront();
    }
    public LearningPage(UserData user, TuioClient sharedClient)
    {
        currentUser = user;
        level = user.Level;
        client = sharedClient;

        string displayLevel = level;

        if (level == "Primary")
            displayLevel = "Beginner";
        else if (level == "Secondary")
            displayLevel = "Intermediate";
        else if (level == "HighSchool" || level == "High School")
            displayLevel = "Advanced";

        this.Text = displayLevel + " Page";
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.FromArgb(248, 251, 255);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.DoubleBuffered = true;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        //if (IsPrimary())
        //    LoadPrimaryStudentImage();

        BuildUI();
        BuildWelcomeLabel();

        this.Load += (s, e) => ArrangeControls();
        this.Resize += (s, e) => ArrangeControls();
        this.Shown += (s, e) =>
        {
            ArrangeControls();
            this.Invalidate(true);
            this.Update();
        };

        client.addTuioListener(this);
        NavHelper.AddNavBar(this, displayLevel + " Padel Level", true);        // Animation timer for dynamic background decorations
        System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer();
        animTimer.Interval = 55;
        animTimer.Tick += delegate { this.Invalidate(); };
        animTimer.Start();
        this.FormClosed += delegate { animTimer.Stop(); animTimer.Dispose(); };

        // Subscribe to gesture router (legacy marker + named gestures)
        this.Shown += (s, e) => { GestureRouter.OnGestureMarker += HandleGestureMarker; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureMarker -= HandleGestureMarker; };
        this.Shown += (s, e) => { GestureRouter.OnGestureRecognized += HandleGestureName; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureRecognized -= HandleGestureName; };

        // Subscribe to expression-based adaptive UI (happy2.jpg bg + music)
        AdaptiveUIHelper.Register(this);

        // ── Initialize Gaze Tracking & Adaptive UI ──
        InitializeGazeTracking();
        ApplyAdaptiveLayout();
    }

    private bool IsPrimary()
    {
        return level.Equals("Primary", StringComparison.OrdinalIgnoreCase)
            || level.Equals("Beginner", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSecondary()
    {
        return level.Equals("Secondary", StringComparison.OrdinalIgnoreCase)
            || level.Equals("Intermediate", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsHighSchool()
    {
        return level.Equals("High School", StringComparison.OrdinalIgnoreCase)
            || level.Equals("HighSchool", StringComparison.OrdinalIgnoreCase)
            || level.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
    }
    //private void LoadPrimaryStudentImage()
    //{
    //    try
    //    {
    //        //string imgPath = Path.Combine(Application.StartupPath, "pr.jpg");
    //        if (File.Exists(imgPath))
    //        {
    //            using (FileStream fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read))
    //            using (Image img = Image.FromStream(fs))
    //            {
    //                primaryStudentImage = new Bitmap(img);
    //            }
    //        }
    //    }
    //    catch
    //    {
    //        primaryStudentImage = null;
    //    }
    //}

    private void BuildUI()
    {
        if (IsPrimary())
            BuildPrimaryUI();
        else if (IsSecondary())
            BuildSecondaryUI();
        else
            BuildHighSchoolUI();
    }

    private void BuildPrimaryUI()
    {
        this.BackColor = Color.FromArgb(236, 247, 255);

        headerPanel = CreateRoundedPanel(Color.FromArgb(250, 252, 255), 42);
        headerPanel.Size = new Size(1220, 290);
        headerPanel.BorderColor = Color.FromArgb(210, 230, 245);
        headerPanel.BorderThickness = 1.8f;
        headerPanel.ShadowColor = Color.FromArgb(38, 0, 0, 0);
        headerPanel.ShadowOffsetX = 5;
        headerPanel.ShadowOffsetY = 8;

        lblLevelBadge = new Label();
        lblLevelBadge.Text = "LEVEL 1  •  BEGINNER";
        lblLevelBadge.Font = new Font("Arial", 12, FontStyle.Bold);
        lblLevelBadge.ForeColor = Color.FromArgb(38, 105, 165);
        lblLevelBadge.BackColor = Color.FromArgb(220, 239, 255);
        lblLevelBadge.AutoSize = false;
        lblLevelBadge.Size = new Size(220, 38);
        lblLevelBadge.TextAlign = ContentAlignment.MiddleCenter;

        lblTitle = new Label();
        lblTitle.Text = "Beginner Padel Training";
        lblTitle.Font = new Font("Arial", 34, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(18, 56, 108);
        lblTitle.AutoSize = false;
        lblTitle.Size = new Size(720, 62);
        lblTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblTitle.BackColor = Color.Transparent;

        lblSubtitle = new Label();
        lblSubtitle.Text = "Choose a beginner activity with your TUIO marker";
        lblSubtitle.Font = new Font("Arial", 18, FontStyle.Regular);
        lblSubtitle.ForeColor = Color.FromArgb(72, 98, 125);
        lblSubtitle.AutoSize = false;
        lblSubtitle.Size = new Size(720, 36);
        lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        lblSubtitle.BackColor = Color.Transparent;

        lblHint = new Label();
        lblHint.Text = "Start learning padel in a simple and interactive way!";
        lblHint.Font = new Font("Arial", 14, FontStyle.Bold | FontStyle.Italic);
        lblHint.ForeColor = Color.FromArgb(88, 120, 155);
        lblHint.AutoSize = false;
        lblHint.Size = new Size(560, 30);
        lblHint.TextAlign = ContentAlignment.MiddleLeft;
        lblHint.BackColor = Color.Transparent;

        primaryImageFrame = new RoundedShadowPanel
        {
            CornerRadius = 45,
            FillColor = Color.White,
            BorderColor = Color.FromArgb(220, 235, 248),
            BorderThickness = 2f,
            ShadowColor = Color.FromArgb(40, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 6,
            ShadowOffsetY = 10,
            Size = new Size(240, 240),
            Padding = new Padding(12)
        };

        picPrimaryStudent = new PictureBox();
        picPrimaryStudent.Dock = DockStyle.Fill;
        picPrimaryStudent.SizeMode = PictureBoxSizeMode.Zoom;
        picPrimaryStudent.BackColor = Color.White;
        picPrimaryStudent.Margin = new Padding(0);
        picPrimaryStudent.Image = primaryStudentImage;

        primaryImageFrame.Controls.Add(picPrimaryStudent);

        headerPanel.Controls.Add(lblLevelBadge);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);
        headerPanel.Controls.Add(lblHint);
        headerPanel.Controls.Add(primaryImageFrame);

        cardVocabulary = CreatePrimaryLessonCard(
            "Marker 3",
            "Learn Strokes",
            "Learn forehand, backhand\nand serve",
            Color.FromArgb(255, 239, 214),
            Color.FromArgb(245, 162, 66),
            "vocabulary");
        cardVocabulary.Size = new Size(340, 250);

        cardGrammar = CreatePrimaryLessonCard(
            "Marker 4",
            "Court Rules",
            "Learn court zones\nand basic rules",
            Color.FromArgb(223, 240, 255),
            Color.FromArgb(82, 151, 255),
            "grammar");
        cardGrammar.Size = new Size(340, 250);

        cardArranging = CreatePrimaryLessonCard(
            "Marker 5",
            "AI Vision Coach",
            "YOLO tracking detects\nyour position in real time",
            Color.FromArgb(224, 245, 221),
            Color.FromArgb(92, 182, 116),
            "arranging");
        cardArranging.Size = new Size(340, 250);

        cardQuiz = CreatePrimaryLessonCard(
            "Marker 6",
            "Quick Challenge",
            "Answer fast and test\nyour reaction",
            Color.FromArgb(255, 228, 230),
            Color.FromArgb(220, 60, 100),
            "quiz");
        cardQuiz.Size = new Size(340, 210);

        cardSpelling = CreatePrimaryLessonCard(
            "Marker 7",
            "Speed Mode",
            "Play faster and improve\nyour accuracy",
            Color.FromArgb(235, 228, 255),
            Color.FromArgb(110, 60, 210),
            "spelling");
        cardSpelling.Size = new Size(340, 210);

        cardCompetition = CreatePrimaryLessonCard(
            "Marker 8",
            "Competition 🏆",
            "Play against others\nand win points",
            Color.FromArgb(255, 240, 220),
            Color.FromArgb(255, 180, 60),
            "competition");
        cardCompetition.Size = new Size(340, 210);

        lblFooter = new Label();
        lblFooter.Text = "Touch your marker to start padel training!";
        lblFooter.Font = new Font("Arial", 15, FontStyle.Bold);
        lblFooter.ForeColor = Color.FromArgb(54, 92, 135);
        lblFooter.AutoSize = false;
        lblFooter.Size = new Size(640, 42);
        lblFooter.TextAlign = ContentAlignment.MiddleCenter;
        lblFooter.BackColor = Color.FromArgb(228, 240, 255);

        this.Controls.Add(headerPanel);
        this.Controls.Add(cardVocabulary);
        this.Controls.Add(cardGrammar);
        this.Controls.Add(cardArranging);
        this.Controls.Add(cardQuiz);
        this.Controls.Add(cardSpelling);
        this.Controls.Add(cardCompetition);
        this.Controls.Add(lblFooter);

        RoundBadgeLabel(lblLevelBadge, 18);
        RoundBadgeLabel(lblFooter, 20);
    }
    private void BuildSecondaryUI()
    {
        this.BackColor = Color.FromArgb(230, 248, 248);

        headerPanel = new RoundedShadowPanel
        {
            CornerRadius = 36,
            FillColor = Color.FromArgb(245, 254, 254),
            BorderColor = Color.FromArgb(160, 210, 210),
            BorderThickness = 1.8f,
            ShadowColor = Color.FromArgb(35, 0, 80, 80),
            ShadowOffsetX = 5,
            ShadowOffsetY = 8,
            Size = new Size(1160, 200)
        };

        lblLevelBadge = new Label();
        lblLevelBadge.Text = "LEVEL 2  •  INTERMEDIATE";
        lblLevelBadge.Font = new Font("Arial", 12, FontStyle.Bold);
        lblLevelBadge.ForeColor = Color.FromArgb(22, 128, 128);
        lblLevelBadge.BackColor = Color.FromArgb(210, 245, 245);
        lblLevelBadge.AutoSize = false;
        lblLevelBadge.Size = new Size(260, 38);
        lblLevelBadge.TextAlign = ContentAlignment.MiddleCenter;

        lblTitle = new Label();
        lblTitle.Text = "Intermediate Padel Training";
        lblTitle.Font = new Font("Arial", 32, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(10, 90, 90);
        lblTitle.AutoSize = false;
        lblTitle.Size = new Size(820, 58);
        lblTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblTitle.BackColor = Color.Transparent;

        lblSubtitle = new Label();
        lblSubtitle.Text = "Improve your padel skills with better control and movement";
        lblSubtitle.Font = new Font("Arial", 16, FontStyle.Regular);
        lblSubtitle.ForeColor = Color.FromArgb(55, 115, 115);
        lblSubtitle.AutoSize = false;
        lblSubtitle.Size = new Size(820, 34);
        lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        lblSubtitle.BackColor = Color.Transparent;

        lblHint = new Label();
        lblHint.Text = "Build stronger reactions, positioning, and shot accuracy!";
        lblHint.Font = new Font("Arial", 13, FontStyle.Bold | FontStyle.Italic);
        lblHint.ForeColor = Color.FromArgb(40, 140, 140);
        lblHint.AutoSize = false;
        lblHint.Size = new Size(820, 28);
        lblHint.TextAlign = ContentAlignment.MiddleLeft;
        lblHint.BackColor = Color.Transparent;

        headerPanel.Controls.Add(lblLevelBadge);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);
        headerPanel.Controls.Add(lblHint);

        cardVocabulary = CreatePrimaryLessonCard(
            "Marker 3",
            "Improve Strokes",
            "Improve forehand,\nbackhand and serve",
            Color.FromArgb(220, 248, 242),
            Color.FromArgb(32, 178, 158),
            "vocabulary");
        cardVocabulary.Size = new Size(340, 250);

        cardGrammar = CreatePrimaryLessonCard(
            "Marker 4",
            "Positioning",
            "Learn where to stand\nand how to move",
            Color.FromArgb(218, 238, 255),
            Color.FromArgb(55, 130, 220),
            "grammar");
        cardGrammar.Size = new Size(340, 250);

        cardArranging = CreatePrimaryLessonCard(
            "Marker 5",
            "AI Vision Coach",
            "YOLO tracking detects\nyour position in real time",
            Color.FromArgb(255, 238, 220),
            Color.FromArgb(220, 120, 60),
            "arranging");
        cardArranging.Size = new Size(340, 250);

        cardQuiz = CreatePrimaryLessonCard(
            "Marker 6",
            "Reaction Mode",
            "React faster to game\nsituations and shots",
            Color.FromArgb(255, 228, 235),
            Color.FromArgb(210, 50, 95),
            "quiz");
        cardQuiz.Size = new Size(340, 210);

        cardSpelling = CreatePrimaryLessonCard(
            "Marker 7",
            "Speed Mode",
            "Take faster and harder\npadel challenges",
            Color.FromArgb(235, 228, 255),
            Color.FromArgb(105, 55, 200),
            "spelling");
        cardSpelling.Size = new Size(340, 210);

        cardCompetition = CreatePrimaryLessonCard(
            "Marker 8",
            "Competition 🏆",
            "Compete with stronger\nplayers and score higher",
            Color.FromArgb(255, 240, 220),
            Color.FromArgb(255, 180, 60),
            "competition");
        cardCompetition.Size = new Size(340, 210);

        lblFooter = new Label();
        lblFooter.Text = "Place your marker to start intermediate training!";
        lblFooter.Font = new Font("Arial", 13, FontStyle.Bold);
        lblFooter.ForeColor = Color.FromArgb(80, 45, 160);
        lblFooter.AutoSize = false;
        lblFooter.Size = new Size(620, 38);
        lblFooter.TextAlign = ContentAlignment.MiddleCenter;
        lblFooter.BackColor = Color.FromArgb(228, 220, 255);

        this.Controls.Add(headerPanel);
        this.Controls.Add(cardVocabulary);
        this.Controls.Add(cardGrammar);
        this.Controls.Add(cardArranging);
        this.Controls.Add(cardQuiz);
        this.Controls.Add(cardSpelling);
        this.Controls.Add(cardCompetition);
        this.Controls.Add(lblFooter);

        RoundBadgeLabel(lblLevelBadge, 18);
        RoundBadgeLabel(lblFooter, 18);
    }
    private void BuildHighSchoolUI()
    {
        this.BackColor = Color.FromArgb(235, 232, 250);

        headerPanel = new RoundedShadowPanel
        {
            CornerRadius = 40,
            FillColor = Color.FromArgb(248, 246, 255),
            BorderColor = Color.FromArgb(170, 155, 215),
            BorderThickness = 1.8f,
            ShadowColor = Color.FromArgb(40, 60, 0, 100),
            ShadowOffsetX = 6,
            ShadowOffsetY = 9,
            Size = new Size(1160, 200)
        };

        lblLevelBadge = new Label();
        lblLevelBadge.Text = "LEVEL 3  •  ADVANCED";
        lblLevelBadge.Font = new Font("Arial", 12, FontStyle.Bold);
        lblLevelBadge.ForeColor = Color.FromArgb(95, 55, 175);
        lblLevelBadge.BackColor = Color.FromArgb(228, 220, 255);
        lblLevelBadge.AutoSize = false;
        lblLevelBadge.Size = new Size(240, 38);
        lblLevelBadge.TextAlign = ContentAlignment.MiddleCenter;

        lblTitle = new Label();
        lblTitle.Text = "Advanced Padel Training Area";
        lblTitle.Font = new Font("Arial", 32, FontStyle.Bold);
        lblTitle.ForeColor = Color.FromArgb(55, 28, 130);
        lblTitle.AutoSize = false;
        lblTitle.Size = new Size(820, 58);
        lblTitle.TextAlign = ContentAlignment.MiddleLeft;
        lblTitle.BackColor = Color.Transparent;

        lblSubtitle = new Label();
        lblSubtitle.Text = "Advanced control, speed, and competition";
        lblSubtitle.Font = new Font("Arial", 15, FontStyle.Regular);
        lblSubtitle.ForeColor = Color.FromArgb(100, 75, 170);
        lblSubtitle.AutoSize = false;
        lblSubtitle.Size = new Size(820, 34);
        lblSubtitle.TextAlign = ContentAlignment.MiddleLeft;
        lblSubtitle.BackColor = Color.Transparent;

        lblHint = new Label();
        lblHint.Text = "Train like a competitive player and make better decisions";
        lblHint.Font = new Font("Arial", 13, FontStyle.Bold | FontStyle.Italic);
        lblHint.ForeColor = Color.FromArgb(130, 100, 200);
        lblHint.AutoSize = false;
        lblHint.Size = new Size(820, 28);
        lblHint.TextAlign = ContentAlignment.MiddleLeft;
        lblHint.BackColor = Color.Transparent;

        headerPanel.Controls.Add(lblLevelBadge);
        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);
        headerPanel.Controls.Add(lblHint);

        cardVocabulary = CreatePrimaryLessonCard(
            "Marker 3",
            "Shot Control",
            "Control power,\ndirection and precision",
            Color.FromArgb(238, 228, 255),
            Color.FromArgb(120, 60, 210),
            "vocabulary");
        cardVocabulary.Size = new Size(340, 250);

        cardGrammar = CreatePrimaryLessonCard(
            "Marker 4",
            "Game Strategy",
            "Choose the right shot\nfor each situation",
            Color.FromArgb(255, 238, 248),
            Color.FromArgb(195, 55, 130),
            "grammar");
        cardGrammar.Size = new Size(340, 250);

        cardArranging = CreatePrimaryLessonCard(
            "Marker 5",
            "AI Vision Coach",
            "YOLO tracking detects\nyour position in real time",
            Color.FromArgb(255, 248, 220),
            Color.FromArgb(200, 145, 30),
            "arranging");
        cardArranging.Size = new Size(340, 250);

        cardQuiz = CreatePrimaryLessonCard(
            "Marker 6",
            "Quick Challenge",
            "Face harder and faster\npadel questions",
            Color.FromArgb(255, 228, 238),
            Color.FromArgb(200, 45, 105),
            "quiz");
        cardQuiz.Size = new Size(340, 210);

        cardSpelling = CreatePrimaryLessonCard(
            "Marker 7",
            "Speed Mode",
            "Push your speed and\naccuracy to the limit",
            Color.FromArgb(235, 228, 255),
            Color.FromArgb(105, 55, 200),
            "spelling");
        cardSpelling.Size = new Size(340, 210);

        cardCompetition = CreatePrimaryLessonCard(
            "Marker 8",
            "Competition 🏆",
            "Play high-level matches\nand fight for the top",
            Color.FromArgb(255, 240, 220),
            Color.FromArgb(255, 180, 60),
            "competition");
        cardCompetition.Size = new Size(340, 210);

        lblFooter = new Label();
        lblFooter.Text = "Place your marker to start advanced training!";
        lblFooter.Font = new Font("Arial", 13, FontStyle.Bold);
        lblFooter.ForeColor = Color.FromArgb(80, 45, 160);
        lblFooter.AutoSize = false;
        lblFooter.Size = new Size(620, 38);
        lblFooter.TextAlign = ContentAlignment.MiddleCenter;
        lblFooter.BackColor = Color.FromArgb(228, 220, 255);

        this.Controls.Add(headerPanel);
        this.Controls.Add(cardVocabulary);
        this.Controls.Add(cardGrammar);
        this.Controls.Add(cardArranging);
        this.Controls.Add(cardQuiz);
        this.Controls.Add(cardSpelling);
        this.Controls.Add(cardCompetition);
        this.Controls.Add(lblFooter);

        RoundBadgeLabel(lblLevelBadge, 18);
        RoundBadgeLabel(lblFooter, 18);
    }

    private RoundedShadowPanel CreatePrimaryLessonCard(string marker, string title, string desc, Color backColor, Color accent, string iconType)
    {
        RoundedShadowPanel card = new RoundedShadowPanel
        {
            CornerRadius = 34,
            FillColor = backColor,
            BorderColor = Color.FromArgb(240, 255, 255, 255),
            BorderThickness = 1.5f,
            ShadowColor = Color.FromArgb(30, 0, 0, 0),
            DrawGloss = true,
            ShadowOffsetX = 5,
            ShadowOffsetY = 8,
            Size = new Size(340, 230)
        };

        Panel accentBar = new Panel();
        accentBar.Size = new Size(card.Width - 28, 6);
        accentBar.Location = new Point(14, 12);
        accentBar.BackColor = accent;

        Panel iconBadge = CreatePrimaryLessonIcon(accent, iconType);
        iconBadge.Location = new Point(22, 26);

        Label lblMarker = new Label();
        lblMarker.Text = marker;
        lblMarker.Font = new Font("Arial", 10, FontStyle.Bold);
        lblMarker.ForeColor = Color.FromArgb(55, 85, 115);
        lblMarker.AutoSize = false;
        lblMarker.Size = new Size(180, 22);
        lblMarker.Location = new Point(98, 36);
        lblMarker.BackColor = Color.Transparent;

        Label lblCardTitle = new Label();
        lblCardTitle.Text = title;
        lblCardTitle.Font = new Font("Arial", 25, FontStyle.Bold);
        lblCardTitle.ForeColor = Color.FromArgb(18, 46, 85);
        lblCardTitle.AutoSize = false;
        lblCardTitle.Size = new Size(260, 46);
        lblCardTitle.Location = new Point(22, 86);
        lblCardTitle.BackColor = Color.Transparent;

        Label lblDesc = new Label();
        lblDesc.Text = desc;
        lblDesc.Font = new Font("Arial", 12, FontStyle.Regular);
        lblDesc.ForeColor = Color.FromArgb(76, 96, 116);
        lblDesc.AutoSize = false;
        lblDesc.Size = new Size(275, 65);
        lblDesc.Location = new Point(22, 140);
        lblDesc.BackColor = Color.Transparent;

        Label lblTap = new Label();
        lblTap.Text = "Ready to play!";
        lblTap.Font = new Font("Arial", 11, FontStyle.Bold);
        lblTap.ForeColor = accent;
        lblTap.AutoSize = false;
        lblTap.Size = new Size(140, 22);
        lblTap.Location = new Point(22, 212);
        lblTap.BackColor = Color.Transparent;

        card.Controls.Add(accentBar);
        card.Controls.Add(iconBadge);
        card.Controls.Add(lblMarker);
        card.Controls.Add(lblCardTitle);
        card.Controls.Add(lblDesc);
        card.Controls.Add(lblTap);

        return card;
    }
    private Panel CreatePrimaryLessonIcon(Color accent, string iconType)
    {
        SmoothPanel p = new SmoothPanel();
        p.Size = new Size(64, 64);
        p.BackColor = Color.Transparent;

        p.Paint += (s, e) =>
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowRect = new Rectangle(6, 7, 50, 50);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                g.FillEllipse(sb, shadowRect);

            Rectangle rect = new Rectangle(4, 4, 50, 50);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = LightenColor(accent, 55);
                    pgb.SurroundColors = new Color[] { accent };
                    g.FillPath(pgb, path);
                }

                using (Pen pen = new Pen(Color.FromArgb(230, 255, 255, 255), 2))
                    g.DrawPath(pen, path);
            }

            using (Pen pen = new Pen(Color.White, 2.7f))
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                if (iconType == "vocabulary")
                {
                    g.DrawString("A", new Font("Arial", 12, FontStyle.Bold), brush, 14, 16);
                    g.DrawString("B", new Font("Arial", 12, FontStyle.Bold), brush, 31, 25);
                    g.DrawArc(pen, 12, 13, 26, 20, 10, 320);
                }
                else if (iconType == "grammar")
                {
                    Rectangle page = new Rectangle(20, 15, 20, 26);
                    g.DrawRectangle(pen, page);
                    g.DrawLine(pen, 23, 21, 36, 21);
                    g.DrawLine(pen, 23, 27, 36, 27);
                    g.DrawLine(pen, 23, 33, 32, 33);
                }
                else if (iconType == "arranging")
                {
                    g.DrawRectangle(pen, 16, 18, 10, 10);
                    g.DrawRectangle(pen, 31, 18, 10, 10);
                    g.DrawRectangle(pen, 23, 34, 10, 10);
                    g.DrawLine(pen, 26, 23, 31, 23);
                    g.DrawLine(pen, 28, 28, 28, 34);
                }
                else if (iconType == "quiz")
                {
                    g.DrawEllipse(pen, 14, 14, 22, 22);
                    g.DrawString("?", new Font("Arial", 14, FontStyle.Bold), brush, 20, 15);
                }
                else if (iconType == "spelling")
                {
                    g.DrawLine(pen, 18, 34, 32, 16);
                    g.DrawLine(pen, 32, 16, 28, 12);
                    g.DrawLine(pen, 28, 12, 14, 30);
                    g.DrawLine(pen, 14, 30, 18, 34);
                    g.DrawLine(pen, 14, 37, 16, 32);
                }
                else if (iconType == "competition")
                {
                    g.DrawEllipse(pen, 20, 12, 20, 20);
                    g.DrawLine(pen, 30, 32, 30, 45);
                    g.DrawLine(pen, 22, 45, 38, 45);
                    g.DrawLine(pen, 18, 20, 20, 18);
                    g.DrawLine(pen, 42, 20, 40, 18);
                }
            }
        };

        return p;
    }

    private RoundedShadowPanel CreateLessonCard(string marker, string title, string desc, Color backColor, Color accent, string iconType)
    {
        RoundedShadowPanel card = CreateRoundedPanel(backColor, 25);
        card.Size = new Size(320, 200);

        Panel accentBar = new Panel();
        accentBar.Size = new Size(card.Width - 24, 4);
        accentBar.Location = new Point(12, 10);
        accentBar.BackColor = accent;

        Panel iconBadge = CreateLessonIcon(accent, iconType);
        iconBadge.Location = new Point(20, 24);

        Label lblMarker = new Label();
        lblMarker.Text = marker;
        lblMarker.Font = new Font("Arial", 10, FontStyle.Bold);
        lblMarker.ForeColor = Color.FromArgb(30, 60, 90);
        lblMarker.AutoSize = false;
        lblMarker.Size = new Size(180, 22);
        lblMarker.Location = new Point(90, 30);
        lblMarker.BackColor = Color.Transparent;

        Label lblCardTitle = new Label();
        lblCardTitle.Text = title;
        lblCardTitle.Font = new Font("Arial", 22, FontStyle.Bold);
        lblCardTitle.ForeColor = Color.FromArgb(15, 40, 65);
        lblCardTitle.AutoSize = false;
        lblCardTitle.Size = new Size(250, 40);
        lblCardTitle.Location = new Point(20, 72);
        lblCardTitle.BackColor = Color.Transparent;

        Label lblDesc = new Label();
        lblDesc.Text = desc;
        lblDesc.Font = new Font("Arial", 11, FontStyle.Regular);
        lblDesc.ForeColor = Color.FromArgb(70, 90, 110);
        lblDesc.AutoSize = false;
        lblDesc.Size = new Size(260, 50);
        lblDesc.Location = new Point(20, 122);
        lblDesc.BackColor = Color.Transparent;

        card.Controls.Add(accentBar);
        card.Controls.Add(iconBadge);
        card.Controls.Add(lblMarker);
        card.Controls.Add(lblCardTitle);
        card.Controls.Add(lblDesc);

        return card;
    }

    private Panel CreateLessonIcon(Color accent, string iconType)
    {
        SmoothPanel p = new SmoothPanel();
        p.Size = new Size(52, 52);
        p.BackColor = Color.Transparent;

        p.Paint += (s, e) =>
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadowRect = new Rectangle(4, 5, 42, 42);
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
                g.FillEllipse(sb, shadowRect);

            Rectangle rect = new Rectangle(2, 2, 42, 42);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                using (PathGradientBrush pgb = new PathGradientBrush(path))
                {
                    pgb.CenterColor = Color.White;
                    pgb.SurroundColors = new Color[] { accent };
                    g.FillPath(pgb, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), 2))
                    g.DrawPath(pen, path);
            }

            using (Pen pen = new Pen(Color.White, 2.3f))
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                if (iconType == "vocabulary")
                {
                    Rectangle a = new Rectangle(11, 15, 10, 18);
                    Rectangle b = new Rectangle(25, 15, 10, 18);
                    g.DrawRectangle(pen, a);
                    g.DrawRectangle(pen, b);
                    g.DrawString("A", new Font("Arial", 8, FontStyle.Bold), brush, 13, 18);
                    g.DrawString("B", new Font("Arial", 8, FontStyle.Bold), brush, 27, 18);
                }
                else if (iconType == "grammar")
                {
                    Rectangle page = new Rectangle(13, 12, 18, 24);
                    g.DrawRectangle(pen, page);
                    g.DrawLine(pen, 16, 18, 28, 18);
                    g.DrawLine(pen, 16, 23, 28, 23);
                    g.DrawLine(pen, 16, 28, 24, 28);
                }
                else if (iconType == "arranging")
                {
                    g.DrawRectangle(pen, 12, 15, 8, 8);
                    g.DrawRectangle(pen, 24, 15, 8, 8);
                    g.DrawRectangle(pen, 18, 27, 8, 8);
                    g.DrawLine(pen, 20, 19, 24, 19);
                    g.DrawLine(pen, 22, 23, 22, 27);
                }
                else if (iconType == "quiz")
                {
                    // Question mark target circle
                    g.DrawEllipse(pen, 10, 12, 24, 24);
                    g.DrawString("?", new Font("Arial", 13, FontStyle.Bold), brush, 17, 15);
                }
                else if (iconType == "spelling")
                {
                    // Pencil icon
                    g.DrawLine(pen, 16, 30, 28, 14);
                    g.DrawLine(pen, 28, 14, 24, 10);
                    g.DrawLine(pen, 24, 10, 12, 26);
                    g.DrawLine(pen, 12, 26, 16, 30);
                    g.DrawLine(pen, 12, 33, 14, 28);
                }
                else if (iconType == "competition")
                {
                    g.DrawEllipse(pen, 16, 10, 20, 20);
                    g.DrawLine(pen, 26, 30, 26, 40);
                    g.DrawLine(pen, 18, 40, 34, 40);
                    g.DrawLine(pen, 14, 18, 16, 16);
                    g.DrawLine(pen, 38, 18, 36, 16);
                }
            }
        };

        return p;
    }

    private RoundedShadowPanel CreateRoundedPanel(Color backColor, int radius)
    {
        return new RoundedShadowPanel
        {
            CornerRadius = radius,
            FillColor = backColor,
            BorderColor = Color.FromArgb(190, 210, 225),
            BorderThickness = 1.5f,
            ShadowColor = Color.FromArgb(32, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 5,
            ShadowOffsetY = 7
        };
    }

    private void ArrangeControls()
    {
        headerPanel.Location = new Point((this.ClientSize.Width - headerPanel.Width) / 2, 42);

        lblLevelBadge.Location = new Point(42, 24);
        lblTitle.Location = new Point(42, 72);
        lblSubtitle.Location = new Point(42, 132);
        lblHint.Location = new Point(42, 172);

        if (IsPrimary() && primaryImageFrame != null)
            primaryImageFrame.Location = new Point(headerPanel.Width - primaryImageFrame.Width - 38, 24);

        // الصف الأول
        int cardsY = 340;
        int spacing = 36;
        int totalW = cardVocabulary.Width + cardGrammar.Width + cardArranging.Width + (spacing * 2);
        int startX = (this.ClientSize.Width - totalW) / 2;

        cardVocabulary.Location = new Point(startX, cardsY);
        cardGrammar.Location = new Point(startX + cardVocabulary.Width + spacing, cardsY);
        cardArranging.Location = new Point(startX + cardVocabulary.Width + cardGrammar.Width + spacing * 2, cardsY);

        // الصف الثاني
        int row2Y = cardsY + cardVocabulary.Height + 28;
        int row2W = cardQuiz.Width + cardSpelling.Width + cardCompetition.Width + (spacing * 2);
        int row2X = (this.ClientSize.Width - row2W) / 2;

        cardQuiz.Location = new Point(row2X, row2Y);
        cardSpelling.Location = new Point(row2X + cardQuiz.Width + spacing, row2Y);
        cardCompetition.Location = new Point(row2X + cardQuiz.Width + cardSpelling.Width + spacing * 2, row2Y);

        // الفوتر لكل المستويات
        if (lblFooter != null)
            lblFooter.Location = new Point((this.ClientSize.Width - lblFooter.Width) / 2, row2Y + cardQuiz.Height + 20);

        headerPanel.Invalidate(true);
        cardVocabulary.Invalidate(true);
        cardGrammar.Invalidate(true);
        cardArranging.Invalidate(true);

        if (cardQuiz != null) cardQuiz.Invalidate(true);
        if (cardSpelling != null) cardSpelling.Invalidate(true);
        if (cardCompetition != null) cardCompetition.Invalidate(true);
        if (primaryImageFrame != null) primaryImageFrame.Invalidate(true);

        if (lblWelcomeUser != null)
        {
            lblWelcomeUser.Location = new Point(this.ClientSize.Width - lblWelcomeUser.Width - 30, 60);
            lblWelcomeUser.BringToFront();
        }
    }


    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (HappyModeActive)
        {
            // ── Happy mode: draw happy2.jpg as full background, no circles ──
            if (_happyBgImage == null)
            {
                string path = @"C:\Users\agmail\Desktop\padel proj\Padel-Learning-System-1\TUIO11_NET-master\bin\Debug\happy2.jpg";
                if (!File.Exists(path)) path = Path.Combine(Application.StartupPath, "happy2.jpg");
                if (!File.Exists(path)) path = Path.Combine(Application.StartupPath, "Images", "happy2.jpg");
                if (File.Exists(path))
                {
                    _happyBgImage = Image.FromFile(path);
                    Console.WriteLine("[LearningPage] happy2.jpg loaded for OnPaint.");
                }
            }
            if (_happyBgImage != null)
                g.DrawImage(_happyBgImage, 0, 0, this.ClientSize.Width, this.ClientSize.Height);
            else
                g.Clear(Color.FromArgb(255, 145, 70)); // vivid orange fallback
        }
        else
        {
            long tick = DateTime.Now.Ticks / 200000L;
            float phase = (float)(tick % 628) / 100f;
            float pulse = 1f + 0.08f * (float)Math.Sin(phase);

            if (IsPrimary())
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    this.ClientRectangle,
                    Color.FromArgb(236, 247, 255), Color.FromArgb(210, 236, 255), 90f))
                    g.FillRectangle(brush, this.ClientRectangle);
                DrawPrimaryDecorations(g, pulse);
            }
            else if (IsSecondary())
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    this.ClientRectangle,
                    Color.FromArgb(230, 250, 250), Color.FromArgb(200, 238, 235), 90f))
                    g.FillRectangle(brush, this.ClientRectangle);
                DrawSecondaryDecorations(g, pulse);
            }
            else
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    this.ClientRectangle,
                    Color.FromArgb(238, 234, 252), Color.FromArgb(218, 210, 245), 90f))
                    g.FillRectangle(brush, this.ClientRectangle);
                DrawHighSchoolDecorations(g, pulse);
            }
        }

        base.OnPaint(e);
    }

    private void DrawPrimaryDecorations(Graphics g, float pulse)
    {
        int r1 = (int)(120 * pulse); int r2 = (int)(105 * pulse); int r3 = (int)(135 * pulse);
        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(45, 255, 201, 120)))
        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(42, 120, 190, 255)))
        using (SolidBrush b3 = new SolidBrush(Color.FromArgb(40, 118, 214, 143)))
        using (SolidBrush b4 = new SolidBrush(Color.FromArgb(34, 255, 182, 102)))
        using (SolidBrush b5 = new SolidBrush(Color.FromArgb(32, 175, 160, 255)))
        {
            g.FillEllipse(b1, 35, 75, r1, r1);
            g.FillEllipse(b2, this.ClientSize.Width - 170, 95, r2, r2);
            g.FillEllipse(b3, 70, this.ClientSize.Height - 200, r3, r3);
            g.FillEllipse(b4, this.ClientSize.Width - 210, this.ClientSize.Height - 205, r1, r1);
            g.FillEllipse(b5, 245, 275, 40, 40);
            g.FillEllipse(b2, 820, 265, 36, 36);
        }
        DrawStar(g, new Point(105, 136), 12, Color.FromArgb(140, 255, 190, 90));
        DrawStar(g, new Point(this.ClientSize.Width - 95, 160), 11, Color.FromArgb(125, 100, 170, 255));
        DrawStar(g, new Point(205, this.ClientSize.Height - 95), 10, Color.FromArgb(120, 110, 200, 130));
        DrawStar(g, new Point(this.ClientSize.Width - 275, this.ClientSize.Height - 115), 12, Color.FromArgb(120, 255, 170, 100));
        DrawStar(g, new Point(295, 248), 7, Color.FromArgb(105, 255, 180, 90));
        DrawStar(g, new Point(780, 258), 7, Color.FromArgb(105, 120, 170, 255));
        DrawStar(g, new Point(980, 310), 6, Color.FromArgb(105, 100, 200, 130));
    }

    private void DrawSecondaryDecorations(Graphics g, float pulse)
    {
        int r1 = (int)(130 * pulse); int r2 = (int)(120 * pulse); int r3 = (int)(150 * pulse);
        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(38, 32, 178, 158)))
        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(32, 55, 130, 220)))
        using (SolidBrush b3 = new SolidBrush(Color.FromArgb(30, 220, 120, 60)))
        {
            g.FillEllipse(b1, 40, 80, r1, r1);
            g.FillEllipse(b2, this.ClientSize.Width - 185, 100, r2, r2);
            g.FillEllipse(b3, 80, this.ClientSize.Height - 210, r3, r3);
            g.FillEllipse(b1, this.ClientSize.Width - 220, this.ClientSize.Height - 200, r2, r2);
        }
        using (SolidBrush dot = new SolidBrush(Color.FromArgb(65, 32, 178, 158)))
        {
            int[] xs = new int[] { 310, 340, 370, 820, 850, 880, 1050 };
            int[] ys = new int[] { 268, 248, 268, 268, 248, 268, 295 };
            for (int i = 0; i < xs.Length; i++)
                g.FillEllipse(dot, xs[i], ys[i], 11, 11);
        }
    }

    private void DrawHighSchoolDecorations(Graphics g, float pulse)
    {
        int r1 = (int)(145 * pulse); int r2 = (int)(130 * pulse); int r3 = (int)(160 * pulse);
        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(42, 120, 60, 210)))
        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(35, 195, 55, 130)))
        using (SolidBrush b3 = new SolidBrush(Color.FromArgb(32, 200, 145, 30)))
        {
            g.FillEllipse(b1, 30, 90, r1, r1);
            g.FillEllipse(b2, this.ClientSize.Width - 200, 95, r2, r2);
            g.FillEllipse(b3, 90, this.ClientSize.Height - 220, r3, r3);
            g.FillEllipse(b1, this.ClientSize.Width - 230, this.ClientSize.Height - 210, r2, r2);
        }
        float da = (float)Math.Sin(0) * 2;  // static diamond offset
        using (SolidBrush dot = new SolidBrush(Color.FromArgb(70, 120, 60, 210)))
        {
            PointF[] diamond = new PointF[] {
                new PointF(300, 250), new PointF(308, 258), new PointF(300, 266), new PointF(292, 258)
            };
            g.FillPolygon(dot, diamond);
            for (int i = 0; i < 4; i++)
            {
                diamond[0] = new PointF(830 + i * 30, 250); diamond[1] = new PointF(838 + i * 30, 258);
                diamond[2] = new PointF(830 + i * 30, 266); diamond[3] = new PointF(822 + i * 30, 258);
                g.FillPolygon(dot, diamond);
            }
        }
    }

    private void DrawStar(Graphics g, Point center, int radius, Color color)
    {
        PointF[] pts = CreateStarPoints(center.X, center.Y, radius, radius / 2.2f);
        using (SolidBrush brush = new SolidBrush(color))
        {
            g.FillPolygon(brush, pts);
        }
    }

    private PointF[] CreateStarPoints(float cx, float cy, float outerRadius, float innerRadius)
    {
        PointF[] pts = new PointF[10];
        double angle = -Math.PI / 2;

        for (int i = 0; i < 10; i++)
        {
            float r = (i % 2 == 0) ? outerRadius : innerRadius;
            pts[i] = new PointF(
                cx + (float)(Math.Cos(angle) * r),
                cy + (float)(Math.Sin(angle) * r));
            angle += Math.PI / 5;
        }

        return pts;
    }

    private void RoundBadgeLabel(Label lbl, int radius)
    {
        lbl.Resize += (s, e) =>
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = radius * 2;
                Rectangle rect = new Rectangle(0, 0, lbl.Width - 1, lbl.Height - 1);

                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                lbl.Region = new Region(path);
            }
        };

        lbl.PerformLayout();
    }

    public void addTuioObject(TuioObject o)
    {
        // Safety: ignore if form not ready or not visible
        if (!this.IsHandleCreated || this.IsDisposed) return;

        if (o.SymbolID == 20 && !lessonOpen)
        {
            Console.WriteLine($"[LearningPage] TUIO marker 20 → Back");
            this.BeginInvoke((MethodInvoker)delegate { if (!this.IsDisposed) this.Close(); });
            return;
        }

        if (lessonOpen || o.SymbolID == lastLessonSymbolID) return;

        // Debounce: ignore same marker within DEBOUNCE_MS
        if (o.SymbolID == _lastMarkerDebounce &&
            (DateTime.Now - _lastMarkerTime).TotalMilliseconds < DEBOUNCE_MS) return;

        Console.WriteLine($"[LearningPage] TUIO marker {o.SymbolID}  x={o.X:F2} y={o.Y:F2}");

        _lastMarkerDebounce = o.SymbolID;
        _lastMarkerTime = DateTime.Now;

        this.BeginInvoke((MethodInvoker)delegate
        {
            if (this.IsDisposed || lessonOpen) return;

            lessonOpen = true;
            lastLessonSymbolID = o.SymbolID;
            Form page = null;

            try
            {
                if (o.SymbolID == 3)
                {
                    Console.WriteLine("[LearningPage] Marker 3 → Padel Shots");
                    page = new LessonPage(LessonPage.MapLevel(level) + " Padel Shots", client, 6);
                }
                else if (o.SymbolID == 4)
                {
                    Console.WriteLine("[LearningPage] Marker 4 → Padel Rules");
                    page = new LessonPage(LessonPage.MapLevel(level) + " Padel Rules", client, 7);
                }
                else if (o.SymbolID == 5)
                {
                    Console.WriteLine("[LearningPage] Marker 5 → AI Vision Coach");
                    page = new AIVisionCoachPage(LessonPage.MapLevel(level), client);
                }
                else if (o.SymbolID == 6)
                {
                    Console.WriteLine("[LearningPage] Marker 6 → Quick Challenge");
                    page = new QuizPage(level, client);
                }
                else if (o.SymbolID == 7)
                {
                    Console.WriteLine("[LearningPage] Marker 7 → Speed Mode");
                    page = new SpellingPage(level, client);
                }
                else if (o.SymbolID == 8)
                {
                    Console.WriteLine("[LearningPage] Marker 8 → Competition");
                    page = new CompetitionMode(level, client);
                }

                if (page != null)
                {
                    page.FormClosed += (s, e) =>
                    {
                        Console.WriteLine("[LearningPage] Sub-page closed → resuming");
                        lessonOpen = false;
                        lastLessonSymbolID = -1;
                        if (!this.IsDisposed) this.Show();
                    };
                    page.Show();
                    this.Hide();
                }
                else
                {
                    lessonOpen = false;
                    lastLessonSymbolID = -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LearningPage] ERROR opening page for marker {o.SymbolID}: {ex}");
                lessonOpen = false;
                lastLessonSymbolID = -1;
                page?.Dispose();
                try
                {
                    MessageBox.Show(
                        $"Could not open page for marker {o.SymbolID}.\n\n{ex.Message}",
                        "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            }
        });
    }

    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == lastLessonSymbolID)
            lastLessonSymbolID = -1;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client.removeTuioListener(this);

        // ── Save gaze session data + persist session report ──
        try
        {
            if (_analyticsEngine != null && _analyticsEngine.IsActive)
            {
                _analyticsEngine.StopSession();
                _analyticsEngine.PersistSessionReport(currentUser);
                Console.WriteLine("[LearningPage] Gaze session report persisted.");
            }
        }
        catch (Exception ex) { Console.WriteLine($"[LearningPage] Gaze save error: {ex.Message}"); }

        // ── Cleanup gaze resources ──
        _focusGlowTimer?.Stop();
        _focusGlowTimer?.Dispose();
        _gazeReconnectTimer?.Stop();
        _gazeReconnectTimer?.Dispose();
        GazeRouter.OnGazePoint -= HandleGazePoint;
        _gazeClient?.Disconnect();
        try { _learningSynth?.Dispose(); } catch { }

        if (primaryStudentImage != null)
        {
            primaryStudentImage.Dispose();
            primaryStudentImage = null;
        }

        base.OnFormClosed(e);
    }

    // ══════════════════════════════════════════════════════════════
    //  Gaze Tracking & Adaptive Visual Coaching
    // ══════════════════════════════════════════════════════════════

    private void InitializeGazeTracking()
    {
        // TTS for proactive coaching
        try
        {
            _learningSynth = new SpeechSynthesizer();
            _learningSynth.Rate = 0;
            _learningSynth.Volume = 100;
            foreach (InstalledVoice v in _learningSynth.GetInstalledVoices())
                if (v.VoiceInfo.Culture.Name.StartsWith("en"))
                { _learningSynth.SelectVoice(v.VoiceInfo.Name); break; }
        }
        catch { _learningSynth = null; }

        // Start analytics session
        _analyticsEngine = new AnalyticsEngine();
        _analyticsEngine.StartSession();

        // Subscribe to gaze events
        GazeRouter.OnGazePoint += HandleGazePoint;

        // Connect to gaze server
        _gazeClient = new GazeClient();
        Task.Run(() =>
        {
            try { _gazeClient.Connect("127.0.0.1", 5002); }
            catch { }
        });

        _gazeReconnectTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        _gazeReconnectTimer.Tick += (s, e) =>
        {
            if (!_gazeClient.IsConnected)
                Task.Run(() => { try { _gazeClient.Connect("127.0.0.1", 5002); } catch { } });
        };
        _gazeReconnectTimer.Start();
    }

    private void HandleGazePoint(float x, float y)
    {
        _analyticsEngine?.AddGazePoint(x, y);
    }

    private void ApplyAdaptiveLayout()
    {
        if (currentUser?.GazeProfile == null) return;
        var gp = currentUser.GazeProfile;

        // Get last session report for context-aware classification
        GazeSessionReport lastReport = null;
        try { lastReport = GazeReportService.GetLatest(currentUser.UserId); }
        catch { }

        // Classify all categories into AdaptiveState
        var classifications = AnalyticsEngine.ClassifyAllCategories(gp, lastReport);

        // Map category names to card controls
        var cardMap = new Dictionary<string, RoundedShadowPanel>();
        if (cardVocabulary != null)  cardMap["Strokes"]     = cardVocabulary;
        if (cardGrammar != null)     cardMap["Rules"]        = cardGrammar;
        if (cardArranging != null)   cardMap["Practice"]     = cardArranging;
        if (cardQuiz != null)        cardMap["Quiz"]         = cardQuiz;
        if (cardSpelling != null)    cardMap["Spelling"]     = cardSpelling;
        if (cardCompetition != null) cardMap["Competition"]  = cardCompetition;

        // Store original border colors and build adaptive card lists
        _glowCards.Clear();
        _originalBorderColors.Clear();
        bool hasAnimatedCards = false;

        foreach (var kvp in classifications)
        {
            if (!cardMap.ContainsKey(kvp.Key)) continue;
            var card = cardMap[kvp.Key];
            _originalBorderColors[card] = card.BorderColor;

            card.AdaptiveState = kvp.Value;

            // Set ribbon text based on state
            switch (kvp.Value)
            {
                case AdaptiveState.Neglected:
                    card.AdaptiveRibbonText = "New for you!";
                    _glowCards.Add(card);
                    hasAnimatedCards = true;
                    break;
                case AdaptiveState.UnderFocused:
                    card.AdaptiveRibbonText = "Try this!";
                    _glowCards.Add(card);
                    hasAnimatedCards = true;
                    break;
                case AdaptiveState.Familiar:
                    card.AdaptiveRibbonText = "";
                    break;
                default:
                    card.AdaptiveRibbonText = "";
                    break;
            }
        }

        // ── Last-session favourite highlight ────────────────────────────
        // If the user has a previous session, mark whichever card they engaged
        // with most last time as "Picked up from last time!" — a positive
        // visual cue (Familiar paint, plus a gentle pulse via _glowCards).
        if (lastReport != null && !string.IsNullOrEmpty(lastReport.DominantCategory)
            && cardMap.TryGetValue(lastReport.DominantCategory, out var favCard))
        {
            favCard.AdaptiveState = AdaptiveState.Familiar;
            favCard.AdaptiveRibbonText = "Picked up from last time!";
            if (!_glowCards.Contains(favCard))
            {
                _glowCards.Add(favCard);
                hasAnimatedCards = true;
            }
        }

        // Start unified animation timer for all adaptive cards
        if (hasAnimatedCards)
        {
            _focusGlowTimer = new System.Windows.Forms.Timer { Interval = 45 };
            _focusGlowTimer.Tick += (s, e) =>
            {
                _glowPhase += 0.07f;
                if (_glowPhase > 6.28f) _glowPhase = 0f;

                foreach (var card in _glowCards)
                {
                    card.AdaptivePhase = _glowPhase;
                    card.Invalidate();
                }

                // Also invalidate Familiar cards (static overlay, no animation needed after first paint)
                foreach (var kvp in cardMap)
                {
                    if (kvp.Value.AdaptiveState == AdaptiveState.Familiar)
                        kvp.Value.Invalidate();
                }
            };
            _focusGlowTimer.Start();
        }

        // ── Proactive TTS coaching nudge (context-aware) ──
        BuildAdaptiveTTSMessage(classifications, lastReport);
    }

    /// <summary>
    /// Generates a personalized TTS coaching message based on the user's
    /// cross-session gaze pattern.
    /// </summary>
    private void BuildAdaptiveTTSMessage(
        Dictionary<string, AdaptiveState> classifications,
        GazeSessionReport lastReport)
    {
        if (_learningSynth == null || AppSettings.IsMuted) return;

        try
        {
            var neglected = new List<string>();
            var familiar = new List<string>();

            foreach (var kvp in classifications)
            {
                if (kvp.Value == AdaptiveState.Neglected)
                    neglected.Add(AnalyticsEngine.GetCardDisplayName(kvp.Key));
                else if (kvp.Value == AdaptiveState.Familiar)
                    familiar.Add(AnalyticsEngine.GetCardDisplayName(kvp.Key));
            }

            string message = $"Welcome back, {currentUser.Name}! ";

            // Mention what they're good at
            if (familiar.Count > 0)
                message += $"Great progress on {familiar[0]}. ";

            // Steer them toward neglected content
            if (neglected.Count > 0)
            {
                message += $"Today, let's explore {neglected[0]}";
                if (neglected.Count > 1)
                    message += $" and {neglected[1]}";
                message += ". I've highlighted them for you.";
            }
            else
            {
                message += "Your training looks well-balanced. Keep it up!";
            }

            // Check session count for returning-user awareness
            int sessionCount = GazeReportService.GetSessionCount(currentUser.UserId);
            if (sessionCount > 3)
                message += $" This is session number {sessionCount + 1}.";

            _learningSynth.Rate = AppSettings.VoiceRate;
            _learningSynth.SpeakAsync(message);
        }
        catch { }
    }

    public void updateTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    private Color LightenColor(Color color, int amount)
    {
        int r = Math.Min(255, color.R + amount);
        int g = Math.Min(255, color.G + amount);
        int b = Math.Min(255, color.B + amount);
        return Color.FromArgb(r, g, b);
    }

    // ─────────────────────────────────────────────────────────────────
    //  Gesture focus cursor (hand-gesture parallel navigation)
    // ─────────────────────────────────────────────────────────────────
    //  When the user has only the 4 gestures (no TUIO markers), they can
    //  cycle a visual focus through the 6 lesson cards with SwipeRight /
    //  SwipeLeft and open the focused one with Checkmark. Circle closes
    //  back to HomePage. Once focus is active, the legacy universal
    //  marker fallback (Checkmark→4, SwipeR→7) is suppressed on this
    //  page so the focused card wins.

    private int _gestureFocus = -1;          // -1 = inactive, 0..5 = card index
    private bool _gestureFocusActive = false;
    private readonly Dictionary<int, Color> _origBorderColor = new Dictionary<int, Color>();
    private readonly Dictionary<int, float> _origBorderThickness = new Dictionary<int, float>();

    private RoundedShadowPanel GetCardByIndex(int index)
    {
        switch (index)
        {
            case 0: return cardVocabulary;   // marker 3 → Strokes
            case 1: return cardGrammar;       // marker 4 → Rules
            case 2: return cardArranging;     // marker 5 → AI Vision Coach
            case 3: return cardQuiz;          // marker 6 → Quick Challenge
            case 4: return cardSpelling;      // marker 7 → Speed Mode
            case 5: return cardCompetition;   // marker 8 → Competition
        }
        return null;
    }

    private void ApplyGestureFocus(int newIndex)
    {
        // Restore previously focused card's border
        if (_gestureFocus >= 0)
        {
            var prev = GetCardByIndex(_gestureFocus);
            if (prev != null && _origBorderColor.TryGetValue(_gestureFocus, out Color oc))
            {
                prev.BorderColor = oc;
                prev.BorderThickness = _origBorderThickness[_gestureFocus];
                prev.Invalidate();
            }
        }

        _gestureFocus = newIndex;

        if (_gestureFocus >= 0)
        {
            var card = GetCardByIndex(_gestureFocus);
            if (card != null)
            {
                if (!_origBorderColor.ContainsKey(_gestureFocus))
                {
                    _origBorderColor[_gestureFocus] = card.BorderColor;
                    _origBorderThickness[_gestureFocus] = card.BorderThickness;
                }
                card.BorderColor = Color.FromArgb(255, 30, 130, 255);
                card.BorderThickness = 5f;
                card.Invalidate();
            }
        }
    }

    private void HandleGestureName(string name, float score)
    {
        if (!this.Visible || this.IsDisposed || !this.IsHandleCreated) return;
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed || lessonOpen) return;

                switch (name)
                {
                    case "SwipeRight":
                        _gestureFocusActive = true;
                        ApplyGestureFocus(_gestureFocus < 0 ? 0 : (_gestureFocus + 1) % 6);
                        break;

                    case "SwipeLeft":
                        if (!_gestureFocusActive)
                        {
                            // No focus yet → SwipeLeft acts as "back to home"
                            this.Close();
                            return;
                        }
                        ApplyGestureFocus(((_gestureFocus - 1) % 6 + 6) % 6);
                        break;

                    case "Checkmark":
                        if (_gestureFocus < 0) return;  // no focus → let legacy marker-4 fallback handle it
                        // Synthesise the equivalent TUIO marker placement and route through the existing handler
                        int targetMarker = 3 + _gestureFocus;
                        long sid = 888800L + targetMarker + DateTime.Now.Millisecond;
                        try { this.addTuioObject(new TuioObject(sid, targetMarker, 0.5f, 0.5f, 0f)); }
                        catch (Exception ex) { Console.WriteLine($"[LearningPage] gesture-open error: {ex.Message}"); }
                        break;

                    case "Circle":
                        this.Close();
                        break;
                }
            });
        }
        catch { }
    }

    // Gesture handler
    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed || !this.IsHandleCreated) return;

        // When the user is navigating with the gesture focus cursor, the
        // name handler is in charge — silence the universal marker fallback
        // so Checkmark (→4) doesn't race to open Padel Rules over the focused card.
        if (_gestureFocusActive && markerId != 20) return;

        Console.WriteLine($"[LearningPage] Gesture marker: {markerId}");

        if (markerId == 20)
        {
            Console.WriteLine("[LearningPage] Gesture 20 → Back");
            this.BeginInvoke((MethodInvoker)delegate { if (!this.IsDisposed) this.Close(); });
            return;
        }

        if (lessonOpen || markerId == lastLessonSymbolID) return;

        // Debounce
        if (markerId == _lastMarkerDebounce &&
            (DateTime.Now - _lastMarkerTime).TotalMilliseconds < DEBOUNCE_MS) return;

        _lastMarkerDebounce = markerId;
        _lastMarkerTime = DateTime.Now;

        this.BeginInvoke((MethodInvoker)delegate
        {
            if (this.IsDisposed || lessonOpen) return;

            lessonOpen = true;
            lastLessonSymbolID = markerId;
            Form page = null;

            try
            {
                if (markerId == 3)
                {
                    Console.WriteLine("[LearningPage] Gesture 3 → Padel Shots");
                    page = new LessonPage(LessonPage.MapLevel(level) + " Padel Shots", client, 6);
                }
                else if (markerId == 4)
                {
                    Console.WriteLine("[LearningPage] Gesture 4 → Padel Rules");
                    page = new LessonPage(LessonPage.MapLevel(level) + " Padel Rules", client, 7);
                }
                else if (markerId == 5)
                {
                    Console.WriteLine("[LearningPage] Gesture 5 → AI Vision Coach");
                    page = new AIVisionCoachPage(LessonPage.MapLevel(level), client);
                }
                else if (markerId == 6)
                {
                    Console.WriteLine("[LearningPage] Gesture 6 → Quick Challenge");
                    page = new QuizPage(level, client);
                }
                else if (markerId == 7)
                {
                    Console.WriteLine("[LearningPage] Gesture 7 → Speed Mode");
                    page = new SpellingPage(level, client);
                }
                else if (markerId == 8 || markerId == 30)
                {
                    Console.WriteLine("[LearningPage] Gesture 8/30 → Competition");
                    page = new CompetitionMode(level, client);
                }

                if (page != null)
                {
                    page.FormClosed += (s, e) =>
                    {
                        Console.WriteLine("[LearningPage] Sub-page closed → resuming");
                        lessonOpen = false;
                        lastLessonSymbolID = -1;
                        if (!this.IsDisposed) this.Show();
                    };
                    page.Show();
                    this.Hide();
                }
                else
                {
                    lessonOpen = false;
                    lastLessonSymbolID = -1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LearningPage] ERROR opening page for gesture {markerId}: {ex}");
                lessonOpen = false;
                lastLessonSymbolID = -1;
                page?.Dispose();
                try
                {
                    MessageBox.Show(
                        $"Could not open page for marker {markerId}.\n\n{ex.Message}",
                        "Navigation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            }
        });
    }
}

public class LessonPage : Form, TuioListener
{
    private TuioClient client;
    private int controlMarkerId;
    private string lessonTitle;

    // Happy background mode — set by AdaptiveUIHelper when sad/bored detected
    internal static bool HappyModeActive = false;
    private static Image _happyBgImage = null;

    private Label lblHeader;
    private Label lblWord;
    private Label lblMeaning;
    private Label lblExample;
    private Label lblInstruction;
    private Label lblCounter;
    private Label lblTip;

    private Label lblArrangeTitle;
    private Label lblArrangeHintTitle;
    private Label lblArrangeHint;
    private Label lblCorrectSentenceTitle;
    private Label lblCorrectSentence;
    private Label lblProgress;
    private Label lblArrangeBadge;

    private PictureBox picWord;

    private RoundedShadowPanel headerPanel;
    private RoundedShadowPanel wordPanel;
    private RoundedShadowPanel instructionPanel;
    private RoundedShadowPanel examplePanel;
    private RoundedShadowPanel counterPanel;
    private RoundedShadowPanel correctSentencePanel;
    private RoundedShadowPanel hintPanel;
    private RoundedShadowPanel progressPanel;

    private FlowLayoutPanel wordsFlowPanel;
    private Panel accentBar;

    private int currentIndex = 0;
    private float anchorAngleDegrees = -1f;
    private const float ROTATION_STEP = 20f;   // 20° per step — easier to trigger

    private SpeechSynthesizer _synth;          // Windows TTS
    private SoundPlayer _wavPlayer;      // .wav file fallback
    private string _lastSpokenKey;  // avoid repeating same speech

    private WordItem[] vocabularyItems;
    private WordItem[] grammarItems;
    private ArrangeItem[] arrangingSentenceItems;

    private readonly System.Collections.Generic.Dictionary<string, Image> imageCache =
        new System.Collections.Generic.Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

    private bool isChangingItem = false;
    private List<WordItemData> LoadWordsFromJson(string fileName)
    {
        string path = Path.Combine(Application.StartupPath, "Data", fileName);

        if (!File.Exists(path))
            return new List<WordItemData>();

        string json = File.ReadAllText(path);

        return JsonConvert.DeserializeObject<List<WordItemData>>(json);
    }
    private List<UserData> LoadUsersFromJson()
    {
        string path = Path.Combine(Application.StartupPath, "Data", "users.json");

        if (!File.Exists(path))
            return new List<UserData>();

        string json = File.ReadAllText(path);

        return JsonConvert.DeserializeObject<List<UserData>>(json);
    }
    public LessonPage(string title, TuioClient sharedClient, int markerId)
    {

        lessonTitle = title;
        client = sharedClient;
        controlMarkerId = markerId;
        var users = LoadUsersFromJson();




        this.Text = title;
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.FromArgb(245, 250, 255);
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;

        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);

        BuildData();
        PreloadLessonImages();
        BuildUI(title);

        this.Load += (s, e) => ArrangeControls();
        this.Resize += (s, e) => ArrangeControls();
        this.Shown += (s, e) =>
        {
            ShowCurrentItem();
            ArrangeControls();
            this.Invalidate(true);
            this.Update();
        };

        client.addTuioListener(this);
        NavHelper.AddNavBar(this, title, true);

        // Keyboard fallback: Left/Right arrows navigate items (useful without physical marker)
        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Right) NextItem();
            else if (e.KeyCode == Keys.Left) PreviousItem();
            else if (e.KeyCode == Keys.Space) ReplayVoice();
            else if (e.KeyCode == Keys.Escape) this.Close();
        };

        // Initialise TTS synthesiser
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.Rate = -2;   // slightly slower
            _synth.Volume = 100;
            // Prefer an English voice when available
            foreach (InstalledVoice v in _synth.GetInstalledVoices())
            {
                if (v.VoiceInfo.Culture.Name.StartsWith("en"))
                { _synth.SelectVoice(v.VoiceInfo.Name); break; }
            }
        }
        catch { _synth = null; }

        // Subscribe to gesture router (legacy marker route + named route)
        this.Shown += (s, e) => { GestureRouter.OnGestureMarker += HandleGestureMarker; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureMarker -= HandleGestureMarker; };
        this.Shown += (s, e) => { GestureRouter.OnGestureRecognized += HandleGestureName; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureRecognized -= HandleGestureName; };

        // Subscribe to expression router for panel color updates
        // NOTE: music is handled exclusively by AdaptiveUIHelper — no PlayBackgroundMusic here
        this.Shown += (s, e) => { ExpressionRouter.OnEmotionDetected += HandleEmotion; };
        this.FormClosed += (s, e) => { ExpressionRouter.OnEmotionDetected -= HandleEmotion; };

        // Register shared adaptive UI helper (handles background + music)
        AdaptiveUIHelper.Register(this);
    }

    /// <summary>
    /// Hand-gesture handler for LessonPage. Swipes cycle through padel
    /// terms (same effect as rotating marker 6); Checkmark replays the
    /// current term's voice; Circle closes the lesson.
    /// </summary>
    private void HandleGestureName(string name, float score)
    {
        if (!this.Visible || this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;
                switch (name)
                {
                    case "SwipeRight": NextItem(); break;
                    case "SwipeLeft":  PreviousItem(); break;
                    case "Checkmark":  ReplayVoice(); break;
                    case "Circle":     this.Close(); break;
                }
            });
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern long mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);



    private void HandleEmotion(string emotion)
    {
        Console.WriteLine($"[LessonPage.HandleEmotion] emotion={emotion}");
        // Panel color updates only — music is handled by AdaptiveUIHelper
        if (emotion == "sad" || emotion == "bored" || emotion == "uncomfortable")
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        try
                        {
                            var panelColors = new Color[]
                            {
                                Color.FromArgb(140, 255, 230, 240), // soft pink
                                Color.FromArgb(140, 200, 255, 220), // mint green
                                Color.FromArgb(140, 200, 230, 255), // sky blue
                            };
                            int idx = 0;
                            foreach (Control ctrl in this.Controls)
                            {
                                if (ctrl is RoundedShadowPanel rsp)
                                {
                                    rsp.FillColor = panelColors[idx % panelColors.Length];
                                    rsp.Invalidate();
                                    idx++;
                                }
                            }
                            this.Refresh();
                        }
                        catch (Exception ex2) { Console.WriteLine("[HandleEmotion] " + ex2.Message); }
                    });
                }
            }
            catch (Exception ex) { Console.WriteLine("[HandleEmotion] Invoke error: " + ex.Message); }
        }
    }

    private void PlayBackgroundMusic()
    {
        try
        {
            // Search Data folder for any MP3
            string[] searchDirs = new[]
            {
                Path.Combine(Application.StartupPath, "Data"),
                Application.StartupPath,
            };

            string mp3 = null;
            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "*.mp3");
                if (files.Length > 0) { mp3 = files[0]; break; }
            }

            if (mp3 != null)
            {
                Console.WriteLine($"[Music] Playing MP3: {mp3}");
                mciSendString("close calmMusic", null, 0, IntPtr.Zero);
                mciSendString($"open \"{mp3}\" type mpegvideo alias calmMusic", null, 0, IntPtr.Zero);
                mciSendString("play calmMusic repeat", null, 0, IntPtr.Zero);
            }
            else
            {
                Console.WriteLine("[Music] No MP3 found in Data folder. Please add a calm.mp3 file there.");
            }
        }
        catch (Exception ex) { Console.WriteLine("[Music] Error: " + ex.Message); }
    }

    private void StopBackgroundMusic()
    {
        try
        {
            mciSendString("stop calmMusic", null, 0, IntPtr.Zero);
            mciSendString("close calmMusic", null, 0, IntPtr.Zero);
        }
        catch { }
    }

    private bool IsArrangementLesson()
    {
        return lessonTitle.ToLower().Contains("arranging") || lessonTitle.ToLower().Contains("rule builder");
    }

    // Maps raw level string to a clean display name
    internal static string MapLevel(string rawLevel)
    {
        if (rawLevel == null) return "Beginner";
        string l = rawLevel.Trim();
        if (l.Equals("Primary", StringComparison.OrdinalIgnoreCase)) return "Beginner";
        if (l.Equals("Secondary", StringComparison.OrdinalIgnoreCase)) return "Intermediate";
        if (l.Equals("HighSchool", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("High School", StringComparison.OrdinalIgnoreCase)) return "Advanced";
        return l; // already a display name
    }

    private void BuildData()
    {
        bool isPrimary = lessonTitle.ToLower().Contains("primary") || lessonTitle.ToLower().Contains("beginner");
        bool isSecondary = lessonTitle.ToLower().Contains("secondary") || lessonTitle.ToLower().Contains("intermediate");

        if (isPrimary)
        {
            var primaryWords = LoadWordsFromJson("primary_vocabulary.json");

            vocabularyItems = primaryWords
                .Select(w => new WordItem(w.Word, w.Meaning, w.Example, w.ImageName))
                .ToArray();
            grammarItems = new WordItem[]
            {
                new WordItem("SERVE RULE",  "Ball must bounce in diagonal box",   "Serve must land in opposite service box.",         "serve.png"),
                new WordItem("NET RULE", "Ball cannot touch net on serve","If serve hits net, it's a fault.",      "net_rules.png"),
                new WordItem("SCORING",  "Points: 15, 30, 40, game",        "Padel uses tennis scoring system.",       "scoring.png"),
                new WordItem("COURT ZONES", "Front, mid, back court areas",       "Position yourself in the right zone.",       "court_zones.png")
            };
            arrangingSentenceItems = new ArrangeItem[]
            {
                new ArrangeItem(new string[]{"SERVE","MUST","BOUNCE","IN","DIAGONAL","BOX"},
                    "Serve must bounce in diagonal box.",
                    "Start with serve, then the rule requirement."),
                new ArrangeItem(new string[]{"HIT","THE","BALL","BEFORE","SECOND","BOUNCE"},
                    "Hit the ball before second bounce.",
                    "Action first, then the timing condition."),
                new ArrangeItem(new string[]{"VOLLEY","AT","THE","NET","IS","EFFECTIVE"},
                    "Volley at the net is effective.",
                    "Shot type, location, then result."),
                new ArrangeItem(new string[]{"PLAYER","MUST","STAY","BEHIND","SERVICE","LINE"},
                    "Player must stay behind service line.",
                    "Player, obligation, then position rule."),
                new ArrangeItem(new string[]{"BALL","CAN","HIT","THE","WALL","ONCE"},
                    "Ball can hit the wall once.",
                    "Ball + permission + action + limit.")
            };
        }
        else if (isSecondary)
        {
            var secondaryWords = LoadWordsFromJson("secondary_vocabulary.json");

            vocabularyItems = secondaryWords
                .Select(w => new WordItem(w.Word, w.Meaning, w.Example, w.ImageName))
                .ToArray();
            grammarItems = new WordItem[]
            {
                new WordItem("DOUBLE BOUNCE",   "Ball bounces twice before return",       "Double bounce means you lose the point.",   "double_bounce.png"),
                new WordItem("FOOT FAULT",   "Stepping over line during serve",   "Avoid foot fault when serving.",      "foot_fault.png"),
                new WordItem("WALL USAGE",  "Using walls strategically",      "Use wall rebound to your advantage.",    "wall_usage.png"),
                new WordItem("CHANGE COURT",  "Switch sides during match", "Change court after odd games.",     "change_court.png")
            };
            arrangingSentenceItems = new ArrangeItem[]
            {
                new ArrangeItem(new string[]{"BALL","MUST","NOT","TOUCH","NET","ON","SERVE"},
                    "Ball must not touch net on serve.",
                    "Rule statement: main element + prohibition + condition."),
                new ArrangeItem(new string[]{"PLAYERS","CHANGE","COURT","AFTER","ODD","GAMES"},
                    "Players change court after odd games.",
                    "Players + action + timing condition."),
                new ArrangeItem(new string[]{"WALL","REBOUND","CAN","BE","USED","STRATEGICALLY"},
                    "Wall rebound can be used strategically.",
                    "Feature + permission + usage method."),
                new ArrangeItem(new string[]{"DEJADA","IS","EFFECTIVE","WHEN","OPPONENT","IS","BACK"},
                    "Dejada is effective when opponent is back.",
                    "Shot type + effectiveness + tactical condition."),
                new ArrangeItem(new string[]{"DOUBLE","BOUNCE","MEANS","POINT","IS","LOST"},
                    "Double bounce means point is lost.",
                    "Violation + consequence: clear cause and effect.")
            };
        }
        else // High School
        {
            var highWords = LoadWordsFromJson("high_vocabulary.json");

            vocabularyItems = highWords
                .Select(w => new WordItem(w.Word, w.Meaning, w.Example, w.ImageName))
                .ToArray();
            grammarItems = new WordItem[]
            {
                new WordItem("GOLDEN POINT",    "Deciding point played at deuce",         "At golden point, receiving team chooses side.",   "golden_point.png"),
                new WordItem("LET RULE",      "Serve interference requires replay",      "If ball hits net and lands in, it's a let.","net_rules.png"),
                new WordItem("TIME VIOLATION",      "Limited time between points",        "Players have 25 seconds between points.", "time_violation.png"),
                new WordItem("DOUBLE WALL",      "Ball hits two walls before return",  "Double wall shots require quick reflexes.",   "double_wall.png")
            };
            arrangingSentenceItems = new ArrangeItem[]
            {
                new ArrangeItem(new string[]{"GOLDEN","POINT","DECIDES","THE","GAME","AT","DEUCE"},
                    "Golden point decides the game at deuce.",
                    "Special rule: main action + timing + condition."),
                new ArrangeItem(new string[]{"BANDEJA","KEEPS","BALL","LOW","AFTER","BOUNCE"},
                    "Bandeja keeps ball low after bounce.",
                    "Advanced shot: technique + effect + timing."),
                new ArrangeItem(new string[]{"VIBORA","CREATES","DIFFICULT","ANGLES","FOR","OPPONENT"},
                    "Vibora creates difficult angles for opponent.",
                    "Tactical shot: action + result + target."),
                new ArrangeItem(new string[]{"CHIQUITA","FORCES","OPPONENT","TO","HIT","UP"},
                    "Chiquita forces opponent to hit up.",
                    "Strategic shot: compels specific response."),
                new ArrangeItem(new string[]{"CONTRA","PARED","REQUIRES","QUICK","REFLEXES","AND","TIMING"},
                    "Contra pared requires quick reflexes and timing.",
                    "Advanced technique: demands multiple skills.")
            };
        }
    }

    private void PreloadLessonImages()
    {
        LoadImageToCache("default_word.png");

        foreach (WordItem item in vocabularyItems)
            LoadImageToCache(item.ImageName);

        foreach (WordItem item in grammarItems)
            LoadImageToCache(item.ImageName);
    }

    private void LoadImageToCache(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName)) return;
        if (imageCache.ContainsKey(imageName)) return;

        try
        {
            // Try Data subfolder first, then startup path directly, then Images subfolder
            string imagePath = Path.Combine(Application.StartupPath, "Data", imageName);
            if (!File.Exists(imagePath))
                imagePath = Path.Combine(Application.StartupPath, imageName);
            if (!File.Exists(imagePath))
                imagePath = Path.Combine(Application.StartupPath, "Images", imageName);
            if (!File.Exists(imagePath)) return;

            using (FileStream fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read))
            using (Image img = Image.FromStream(fs))
            {
                imageCache[imageName] = new Bitmap(img);
            }
        }
        catch
        {
        }
    }

    private void BuildUI(string title)
    {
        if (IsArrangementLesson())
            BuildArrangementUI(title);
        else
            BuildDefaultLessonUI(title);
    }

    private void BuildDefaultLessonUI(string title)
    {
        headerPanel = CreateRoundedPanel(Color.FromArgb(255, 247, 230), 28);
        headerPanel.Size = new Size(920, 115);

        lblHeader = new Label();
        lblHeader.Text = title;
        lblHeader.Font = new Font("Arial", 28, FontStyle.Bold);
        lblHeader.ForeColor = Color.FromArgb(95, 60, 20);
        lblHeader.AutoSize = false;
        lblHeader.Size = new Size(840, 50);
        lblHeader.TextAlign = ContentAlignment.MiddleCenter;
        lblHeader.BackColor = Color.Transparent;

        headerPanel.Controls.Add(lblHeader);

        wordPanel = CreateRoundedPanel(Color.FromArgb(255, 252, 242), 36);
        wordPanel.Size = new Size(1050, 540);

        accentBar = new Panel();
        accentBar.Size = new Size(wordPanel.Width - 50, 8);
        accentBar.Location = new Point(25, 18);
        accentBar.BackColor = GetAccentColor();

        counterPanel = CreateRoundedPanel(Color.FromArgb(238, 245, 255), 18);
        counterPanel.Size = new Size(120, 44);

        lblCounter = new Label();
        lblCounter.Text = "1 / 1";
        lblCounter.Font = new Font("Arial", 12, FontStyle.Bold);
        lblCounter.ForeColor = Color.FromArgb(50, 90, 130);
        lblCounter.AutoSize = false;
        lblCounter.Size = new Size(100, 26);
        lblCounter.TextAlign = ContentAlignment.MiddleCenter;
        lblCounter.BackColor = Color.Transparent;
        counterPanel.Controls.Add(lblCounter);

        picWord = new PictureBox();
        picWord.Size = new Size(250, 250);
        picWord.SizeMode = PictureBoxSizeMode.Zoom;
        picWord.BackColor = Color.Transparent;

        lblWord = new Label();
        lblWord.Text = "SERVE";
        lblWord.Font = new Font("Arial", 34, FontStyle.Bold);
        lblWord.ForeColor = Color.FromArgb(35, 60, 95);
        lblWord.AutoSize = false;
        lblWord.Size = new Size(760, 58);
        lblWord.TextAlign = ContentAlignment.MiddleCenter;
        lblWord.BackColor = Color.Transparent;

        lblMeaning = new Label();
        lblMeaning.Text = "Starting shot in padel";
        lblMeaning.Font = new Font("Arial", 18, FontStyle.Regular);
        lblMeaning.ForeColor = Color.FromArgb(90, 110, 125);
        lblMeaning.AutoSize = false;
        lblMeaning.Size = new Size(780, 40);
        lblMeaning.TextAlign = ContentAlignment.MiddleCenter;
        lblMeaning.BackColor = Color.Transparent;

        examplePanel = CreateRoundedPanel(Color.FromArgb(244, 248, 255), 24);
        examplePanel.Size = new Size(720, 78);

        lblExample = new Label();
        lblExample.Text = "Coach Tip: Serve must bounce in diagonal box.";
        lblExample.Font = new Font("Arial", 16, FontStyle.Bold);
        lblExample.ForeColor = Color.FromArgb(55, 90, 125);
        lblExample.AutoSize = false;
        lblExample.Size = new Size(660, 34);
        lblExample.TextAlign = ContentAlignment.MiddleCenter;
        lblExample.BackColor = Color.Transparent;

        examplePanel.Controls.Add(lblExample);

        lblTip = new Label();
        lblTip.Text = "Look at the image, read the padel term, then follow the coach instruction.";
        lblTip.Font = new Font("Arial", 12, FontStyle.Italic);
        lblTip.ForeColor = Color.FromArgb(110, 120, 130);
        lblTip.AutoSize = false;
        lblTip.Size = new Size(720, 28);
        lblTip.TextAlign = ContentAlignment.MiddleCenter;
        lblTip.BackColor = Color.Transparent;

        wordPanel.Controls.Add(accentBar);
        wordPanel.Controls.Add(counterPanel);
        wordPanel.Controls.Add(picWord);
        wordPanel.Controls.Add(lblWord);
        wordPanel.Controls.Add(lblMeaning);
        wordPanel.Controls.Add(examplePanel);
        wordPanel.Controls.Add(lblTip);

        instructionPanel = CreateRoundedPanel(Color.FromArgb(228, 242, 255), 24);
        instructionPanel.Size = new Size(720, 74);

        lblInstruction = new Label();
        lblInstruction.Text = "Rotate marker " + controlMarkerId + " slowly to change the padel term";
        lblInstruction.Font = new Font("Arial", 14, FontStyle.Bold);
        lblInstruction.ForeColor = Color.FromArgb(55, 90, 125);
        lblInstruction.AutoSize = false;
        lblInstruction.Size = new Size(650, 34);
        lblInstruction.TextAlign = ContentAlignment.MiddleCenter;
        lblInstruction.BackColor = Color.Transparent;

        instructionPanel.Controls.Add(lblInstruction);

        this.Controls.Add(headerPanel);
        this.Controls.Add(wordPanel);
        this.Controls.Add(instructionPanel);
    }

    private void BuildArrangementUI(string title)
    {
        headerPanel = CreateRoundedPanel(Color.FromArgb(240, 249, 238), 32);
        headerPanel.Size = new Size(990, 140);

        lblHeader = new Label();
        lblHeader.Text = title;
        lblHeader.Font = new Font("Arial", 30, FontStyle.Bold);
        lblHeader.ForeColor = Color.FromArgb(38, 86, 52);
        lblHeader.AutoSize = false;
        lblHeader.Size = new Size(860, 50);
        lblHeader.TextAlign = ContentAlignment.MiddleCenter;
        lblHeader.BackColor = Color.Transparent;

        Label lblSubHeader = new Label();
        lblSubHeader.Text = "Build the correct padel rule using the tiles";
        lblSubHeader.Font = new Font("Arial", 13, FontStyle.Italic);
        lblSubHeader.ForeColor = Color.FromArgb(82, 115, 90);
        lblSubHeader.AutoSize = false;
        lblSubHeader.Size = new Size(720, 28);
        lblSubHeader.TextAlign = ContentAlignment.MiddleCenter;
        lblSubHeader.BackColor = Color.Transparent;
        lblSubHeader.Location = new Point((headerPanel.Width - 720) / 2, 84);

        headerPanel.Controls.Add(lblHeader);
        headerPanel.Controls.Add(lblSubHeader);

        wordPanel = CreateRoundedPanel(Color.FromArgb(255, 255, 252), 40);
        wordPanel.Size = new Size(1140, 630);

        accentBar = new Panel();
        accentBar.Size = new Size(wordPanel.Width - 56, 8);
        accentBar.Location = new Point(28, 18);
        accentBar.BackColor = Color.FromArgb(96, 182, 120);

        counterPanel = CreateRoundedPanel(Color.FromArgb(235, 243, 255), 20);
        counterPanel.Size = new Size(120, 46);

        lblCounter = new Label();
        lblCounter.Text = "1 / 1";
        lblCounter.Font = new Font("Arial", 12, FontStyle.Bold);
        lblCounter.ForeColor = Color.FromArgb(50, 90, 130);
        lblCounter.AutoSize = false;
        lblCounter.Size = new Size(100, 26);
        lblCounter.TextAlign = ContentAlignment.MiddleCenter;
        lblCounter.BackColor = Color.Transparent;
        counterPanel.Controls.Add(lblCounter);

        lblArrangeBadge = new Label();
        lblArrangeBadge.Text = "RULE BUILDER";
        lblArrangeBadge.Font = new Font("Arial", 10, FontStyle.Bold);
        lblArrangeBadge.ForeColor = Color.FromArgb(52, 100, 68);
        lblArrangeBadge.BackColor = Color.FromArgb(228, 246, 233);
        lblArrangeBadge.AutoSize = false;
        lblArrangeBadge.Size = new Size(170, 30);
        lblArrangeBadge.TextAlign = ContentAlignment.MiddleCenter;

        lblArrangeTitle = new Label();
        lblArrangeTitle.Text = "Arrange Padel Rule Tiles";
        lblArrangeTitle.Font = new Font("Arial", 26, FontStyle.Bold);
        lblArrangeTitle.ForeColor = Color.FromArgb(34, 70, 48);
        lblArrangeTitle.AutoSize = false;
        lblArrangeTitle.Size = new Size(520, 44);
        lblArrangeTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblArrangeTitle.BackColor = Color.Transparent;

        wordsFlowPanel = new FlowLayoutPanel();
        wordsFlowPanel.Size = new Size(920, 140);
        wordsFlowPanel.BackColor = Color.Transparent;
        wordsFlowPanel.FlowDirection = FlowDirection.LeftToRight;
        wordsFlowPanel.WrapContents = false;
        wordsFlowPanel.AutoScroll = false;
        wordsFlowPanel.Padding = new Padding(12, 18, 12, 10);

        progressPanel = CreateRoundedPanel(Color.FromArgb(240, 246, 255), 22);
        progressPanel.Size = new Size(250, 74);

        lblProgress = new Label();
        lblProgress.Text = "Padel Rule Builder";
        lblProgress.Font = new Font("Arial", 12, FontStyle.Bold);
        lblProgress.ForeColor = Color.FromArgb(60, 95, 130);
        lblProgress.AutoSize = false;
        lblProgress.Size = new Size(210, 28);
        lblProgress.TextAlign = ContentAlignment.MiddleCenter;
        lblProgress.BackColor = Color.Transparent;

        progressPanel.Controls.Add(lblProgress);

        correctSentencePanel = CreateRoundedPanel(Color.FromArgb(236, 247, 238), 28);
        correctSentencePanel.Size = new Size(790, 110);

        lblCorrectSentenceTitle = new Label();
        lblCorrectSentenceTitle.Text = "Correct Padel Rule";
        lblCorrectSentenceTitle.Font = new Font("Arial", 12, FontStyle.Bold);
        lblCorrectSentenceTitle.ForeColor = Color.FromArgb(56, 105, 72);
        lblCorrectSentenceTitle.AutoSize = false;
        lblCorrectSentenceTitle.Size = new Size(220, 24);
        lblCorrectSentenceTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblCorrectSentenceTitle.BackColor = Color.Transparent;

        lblCorrectSentence = new Label();
        lblCorrectSentence.Text = "Serve must bounce in diagonal box.";
        lblCorrectSentence.Font = new Font("Arial", 21, FontStyle.Bold);
        lblCorrectSentence.ForeColor = Color.FromArgb(30, 65, 45);
        lblCorrectSentence.AutoSize = false;
        lblCorrectSentence.Size = new Size(700, 42);
        lblCorrectSentence.TextAlign = ContentAlignment.MiddleCenter;
        lblCorrectSentence.BackColor = Color.Transparent;

        correctSentencePanel.Controls.Add(lblCorrectSentenceTitle);
        correctSentencePanel.Controls.Add(lblCorrectSentence);

        hintPanel = CreateRoundedPanel(Color.FromArgb(255, 248, 230), 24);
        hintPanel.Size = new Size(790, 92);

        lblArrangeHintTitle = new Label();
        lblArrangeHintTitle.Text = "Coach Hint";
        lblArrangeHintTitle.Font = new Font("Arial", 12, FontStyle.Bold);
        lblArrangeHintTitle.ForeColor = Color.FromArgb(150, 100, 30);
        lblArrangeHintTitle.AutoSize = false;
        lblArrangeHintTitle.Size = new Size(120, 22);
        lblArrangeHintTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblArrangeHintTitle.BackColor = Color.Transparent;

        lblArrangeHint = new Label();
        lblArrangeHint.Text = "Start with the main action.";
        lblArrangeHint.Font = new Font("Arial", 14, FontStyle.Regular);
        lblArrangeHint.ForeColor = Color.FromArgb(120, 92, 40);
        lblArrangeHint.AutoSize = false;
        lblArrangeHint.Size = new Size(700, 32);
        lblArrangeHint.TextAlign = ContentAlignment.MiddleCenter;
        lblArrangeHint.BackColor = Color.Transparent;

        hintPanel.Controls.Add(lblArrangeHintTitle);
        hintPanel.Controls.Add(lblArrangeHint);

        instructionPanel = CreateRoundedPanel(Color.FromArgb(228, 242, 255), 28);
        instructionPanel.Size = new Size(860, 86);

        lblInstruction = new Label();
        lblInstruction.Text = "Rotate marker " + controlMarkerId + " to move between padel rule cards";
        lblInstruction.Font = new Font("Arial", 14, FontStyle.Bold);
        lblInstruction.ForeColor = Color.FromArgb(45, 85, 125);
        lblInstruction.AutoSize = false;
        lblInstruction.Size = new Size(780, 34);
        lblInstruction.TextAlign = ContentAlignment.MiddleCenter;
        lblInstruction.BackColor = Color.Transparent;

        instructionPanel.Controls.Add(lblInstruction);

        wordPanel.Controls.Add(accentBar);
        wordPanel.Controls.Add(counterPanel);
        wordPanel.Controls.Add(lblArrangeBadge);
        wordPanel.Controls.Add(lblArrangeTitle);
        wordPanel.Controls.Add(wordsFlowPanel);
        wordPanel.Controls.Add(progressPanel);
        wordPanel.Controls.Add(correctSentencePanel);
        wordPanel.Controls.Add(hintPanel);

        this.Controls.Add(headerPanel);
        this.Controls.Add(wordPanel);
        this.Controls.Add(instructionPanel);

        RoundBadgeLabel(lblArrangeBadge, 14);
    }

    private Color GetAccentColor()
    {
        string lower = lessonTitle.ToLower();

        if (lower.Contains("vocabulary") || lower.Contains("shots") || lower.Contains("shot"))
            return Color.FromArgb(245, 159, 77);

        if (lower.Contains("grammar") || lower.Contains("rules") || lower.Contains("rule"))
            return Color.FromArgb(88, 150, 255);

        return Color.FromArgb(96, 182, 120);
    }

    private WordItem[] GetCurrentItems()
    {
        string lower = lessonTitle.ToLower();

        if (lower.Contains("vocabulary") || lower.Contains("shots") || lower.Contains("shot"))
            return vocabularyItems;

        if (lower.Contains("grammar") || lower.Contains("rules") || lower.Contains("rule"))
            return grammarItems;

        return null;
    }

    private int GetCurrentItemCount()
    {
        if (IsArrangementLesson())
            return arrangingSentenceItems != null ? arrangingSentenceItems.Length : 0;

        WordItem[] items = GetCurrentItems();
        return items != null ? items.Length : 0;
    }

    private void ShowCurrentItem()
    {
        if (isChangingItem) return;
        isChangingItem = true;

        try
        {
            if (IsArrangementLesson())
            {
                ShowArrangementItem();
                return;
            }

            WordItem[] items = GetCurrentItems();
            if (items == null || items.Length == 0) return;

            if (currentIndex < 0) currentIndex = 0;
            if (currentIndex >= items.Length) currentIndex = 0;

            WordItem item = items[currentIndex];

            lblWord.Text = item.Word;
            lblMeaning.Text = item.Meaning;
            lblExample.Text = item.Example;
            lblCounter.Text = (currentIndex + 1).ToString() + " / " + items.Length.ToString();

            LoadWordImage(item.ImageName);

            // Speak the word followed by its meaning
            SpeakText(item.Word + " ... " + item.Meaning, item.Word);
        }
        finally
        {
            isChangingItem = false;
        }
    }

    private void ShowArrangementItem()
    {
        if (arrangingSentenceItems == null || arrangingSentenceItems.Length == 0)
            return;

        if (currentIndex < 0) currentIndex = 0;
        if (currentIndex >= arrangingSentenceItems.Length) currentIndex = 0;

        ArrangeItem item = arrangingSentenceItems[currentIndex];

        lblCounter.Text = (currentIndex + 1).ToString() + " / " + arrangingSentenceItems.Length.ToString();
        lblCorrectSentence.Text = item.CorrectSentence;
        lblArrangeHint.Text = item.Hint;

        wordsFlowPanel.SuspendLayout();
        wordsFlowPanel.Controls.Clear();

        int wordCount = item.Words.Length;
        for (int i = 0; i < wordCount; i++)
        {
            wordsFlowPanel.Controls.Add(CreateWordTile(item.Words[i], i, wordCount));
            if (i < wordCount - 1)
                wordsFlowPanel.Controls.Add(CreateArrowLabel());
        }

        wordsFlowPanel.ResumeLayout();

        // Speak the correct sentence so student knows the goal
        SpeakText(item.CorrectSentence, item.CorrectSentence);
    }

    // ── Voice helpers ──────────────────────────────────────────
    private void SpeakText(string speechText, string key)
    {
        if (_synth == null || AppSettings.IsMuted) return;
        if (key == _lastSpokenKey) return;   // don't repeat same item
        _lastSpokenKey = key;

        // Check for a matching .wav audio file first
        string wavName = key.ToLower().Split(' ')[0] + ".wav";
        string wavPath = Path.Combine(Application.StartupPath, wavName);
        if (File.Exists(wavPath))
        {
            try
            {
                if (_wavPlayer != null) { _wavPlayer.Stop(); _wavPlayer.Dispose(); }
                _wavPlayer = new SoundPlayer(wavPath);
                _wavPlayer.Play();  // non-blocking
                return;
            }
            catch { /* fall through to TTS */ }
        }

        // TTS fallback
        try
        {
            _synth.Rate = AppSettings.VoiceRate;
            _synth.SpeakAsyncCancelAll();
            _synth.SpeakAsync(speechText);
        }
        catch { }
    }

    private void ReplayVoice()
    {
        _lastSpokenKey = null;   // clear cache so next SpeakText forces replay
        ShowCurrentItem();
    }

    private Control CreateArrowLabel()
    {
        Label lbl = new Label();
        lbl.Text = "➜";
        lbl.Font = new Font("Arial", 20, FontStyle.Bold);
        lbl.ForeColor = Color.FromArgb(96, 182, 120);
        lbl.AutoSize = false;
        lbl.Size = new Size(38, 72);
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.BackColor = Color.Transparent;
        lbl.Margin = new Padding(0, 10, 0, 0);
        return lbl;
    }

    private RoundedShadowPanel CreateWordTile(string text, int index, int totalWords)
    {
        // Scale down tiles when sentence is long
        int tileW, tileH, fontSize;
        if (totalWords <= 4) { tileW = 155; tileH = 72; fontSize = 18; }
        else if (totalWords <= 6) { tileW = 125; tileH = 64; fontSize = 15; }
        else { tileW = 105; tileH = 58; fontSize = 13; }

        Color[] colors = new Color[]
        {
            Color.FromArgb(232, 246, 222),
            Color.FromArgb(223, 242, 255),
            Color.FromArgb(255, 240, 217),
            Color.FromArgb(238, 232, 255)
        };

        Color back = colors[index % colors.Length];

        RoundedShadowPanel tile = new RoundedShadowPanel
        {
            CornerRadius = 22,
            FillColor = back,
            BorderColor = Color.FromArgb(235, 255, 255, 255),
            BorderThickness = 1.3f,
            ShadowColor = Color.FromArgb(28, 0, 0, 0),
            DrawGloss = true,
            ShadowOffsetX = 4,
            ShadowOffsetY = 6,
            Size = new Size(tileW, tileH),
            Margin = new Padding(6, 6, 6, 6)
        };

        Label lbl = new Label();
        lbl.Text = text;
        lbl.Font = new Font("Arial", fontSize, FontStyle.Bold);
        lbl.ForeColor = Color.FromArgb(35, 60, 85);
        lbl.AutoSize = false;
        lbl.Size = new Size(tileW - 16, tileH - 16);
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.BackColor = Color.Transparent;
        lbl.Location = new Point(8, 8);

        tile.Controls.Add(lbl);
        return tile;
    }

    private void NextItem()
    {
        int count = GetCurrentItemCount();
        if (count == 0) return;

        currentIndex++;
        if (currentIndex >= count)
            currentIndex = 0;

        ShowCurrentItem();
    }

    private void PreviousItem()
    {
        int count = GetCurrentItemCount();
        if (count == 0) return;

        currentIndex--;
        if (currentIndex < 0)
            currentIndex = count - 1;

        ShowCurrentItem();
    }

    private void LoadWordImage(string imageName)
    {
        string finalImageName = imageName;

        if (string.IsNullOrWhiteSpace(finalImageName) || !imageCache.ContainsKey(finalImageName))
            finalImageName = "default_word.png";

        if (imageCache.ContainsKey(finalImageName))
        {
            picWord.Image = imageCache[finalImageName];
            picWord.Visible = true;
            // Reset fonts to originals
            lblWord.Font = new Font("Arial", 34, FontStyle.Bold);
            lblWord.Size = new Size(760, 58);
            lblMeaning.Font = new Font("Arial", 18, FontStyle.Regular);
            lblMeaning.Size = new Size(780, 40);
        }
        else
        {
            picWord.Image = null;
            picWord.Visible = false;
            // No image: shift content up to fill space
            lblWord.Font = new Font("Arial", 48, FontStyle.Bold);
            lblWord.Size = new Size(wordPanel.Width - 80, 80);
            lblWord.Location = new Point((wordPanel.Width - lblWord.Width) / 2, 100);
            lblMeaning.Font = new Font("Arial", 22, FontStyle.Regular);
            lblMeaning.Size = new Size(wordPanel.Width - 80, 50);
            lblMeaning.Location = new Point((wordPanel.Width - lblMeaning.Width) / 2, 210);
            examplePanel.Location = new Point((wordPanel.Width - examplePanel.Width) / 2, 300);
            lblTip.Location = new Point((wordPanel.Width - lblTip.Width) / 2, 400);
        }
    }

    private float NormalizeAngle(float angle)
    {
        while (angle < 0) angle += 360f;
        while (angle >= 360f) angle -= 360f;
        return angle;
    }

    private float SmallestAngleDifference(float a, float b)
    {
        float diff = a - b;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return diff;
    }

    private RoundedShadowPanel CreateRoundedPanel(Color backColor, int radius)
    {
        return new RoundedShadowPanel
        {
            CornerRadius = radius,
            FillColor = backColor,
            BorderColor = Color.FromArgb(220, 228, 235),
            BorderThickness = 1.5f,
            ShadowColor = Color.FromArgb(30, 0, 0, 0),
            DrawGloss = false,
            ShadowOffsetX = 5,
            ShadowOffsetY = 8
        };
    }

    private void ArrangeControls()
    {
        if (IsArrangementLesson())
        {
            ArrangeArrangementControls();
            return;
        }

        headerPanel.Location = new Point((this.ClientSize.Width - headerPanel.Width) / 2, 58);
        lblHeader.Location = new Point((headerPanel.Width - lblHeader.Width) / 2, 30);

        wordPanel.Location = new Point((this.ClientSize.Width - wordPanel.Width) / 2, 190);

        counterPanel.Location = new Point(wordPanel.Width - counterPanel.Width - 30, 18);
        lblCounter.Location = new Point((counterPanel.Width - lblCounter.Width) / 2, 8);

        if (picWord != null && picWord.Visible)
        {
            // Reset fonts to originals then position
            lblWord.Font = new Font("Arial", 34, FontStyle.Bold);
            lblWord.Size = new Size(760, 58);
            lblMeaning.Font = new Font("Arial", 18, FontStyle.Regular);
            lblMeaning.Size = new Size(780, 40);
            picWord.Location = new Point((wordPanel.Width - picWord.Width) / 2, 50);
            lblWord.Location = new Point((wordPanel.Width - lblWord.Width) / 2, 310);
            lblMeaning.Location = new Point((wordPanel.Width - lblMeaning.Width) / 2, 374);
            examplePanel.Location = new Point((wordPanel.Width - examplePanel.Width) / 2, 422);
            lblTip.Location = new Point((wordPanel.Width - lblTip.Width) / 2, 510);
        }
        else
        {
            // Layout WITHOUT image — boost text size and bring up
            lblWord.Font = new Font("Arial", 52, FontStyle.Bold);
            lblWord.Size = new Size(wordPanel.Width - 60, 86);
            lblWord.Location = new Point((wordPanel.Width - lblWord.Width) / 2, 80);
            lblMeaning.Font = new Font("Arial", 22, FontStyle.Regular);
            lblMeaning.Size = new Size(wordPanel.Width - 60, 50);
            lblMeaning.Location = new Point((wordPanel.Width - lblMeaning.Width) / 2, 190);
            examplePanel.Location = new Point((wordPanel.Width - examplePanel.Width) / 2, 262);
            lblTip.Location = new Point((wordPanel.Width - lblTip.Width) / 2, 355);
        }

        lblExample.Location = new Point((examplePanel.Width - lblExample.Width) / 2, 20);

        instructionPanel.Location = new Point((this.ClientSize.Width - instructionPanel.Width) / 2, 750);
        lblInstruction.Location = new Point((instructionPanel.Width - lblInstruction.Width) / 2, 20);

        headerPanel.Invalidate(true);
        wordPanel.Invalidate(true);
        instructionPanel.Invalidate(true);
        examplePanel.Invalidate(true);
        counterPanel.Invalidate(true);
    }

    private void ArrangeArrangementControls()
    {
        headerPanel.Location = new Point((this.ClientSize.Width - headerPanel.Width) / 2, 34);
        lblHeader.Location = new Point((headerPanel.Width - lblHeader.Width) / 2, 24);

        wordPanel.Location = new Point((this.ClientSize.Width - wordPanel.Width) / 2, 190);

        counterPanel.Location = new Point(wordPanel.Width - counterPanel.Width - 28, 36);
        lblCounter.Location = new Point((counterPanel.Width - lblCounter.Width) / 2, 9);

        lblArrangeBadge.Location = new Point(45, 40);
        lblArrangeTitle.Location = new Point((wordPanel.Width - lblArrangeTitle.Width) / 2, 92);

        wordsFlowPanel.Location = new Point((wordPanel.Width - wordsFlowPanel.Width) / 2, 160);

        progressPanel.Location = new Point((wordPanel.Width - progressPanel.Width) / 2, 320);
        lblProgress.Location = new Point((progressPanel.Width - lblProgress.Width) / 2, 22);

        correctSentencePanel.Location = new Point((wordPanel.Width - correctSentencePanel.Width) / 2, 405);
        lblCorrectSentenceTitle.Location = new Point((correctSentencePanel.Width - lblCorrectSentenceTitle.Width) / 2, 16);
        lblCorrectSentence.Location = new Point((correctSentencePanel.Width - lblCorrectSentence.Width) / 2, 42);

        hintPanel.Location = new Point((wordPanel.Width - hintPanel.Width) / 2, 525);
        lblArrangeHintTitle.Location = new Point((hintPanel.Width - lblArrangeHintTitle.Width) / 2, 14);
        lblArrangeHint.Location = new Point((hintPanel.Width - lblArrangeHint.Width) / 2, 40);

        instructionPanel.Location = new Point((this.ClientSize.Width - instructionPanel.Width) / 2, 845);
        lblInstruction.Location = new Point((instructionPanel.Width - lblInstruction.Width) / 2, 25);

        headerPanel.Invalidate(true);
        wordPanel.Invalidate(true);
        instructionPanel.Invalidate(true);
        correctSentencePanel.Invalidate(true);
        hintPanel.Invalidate(true);
        progressPanel.Invalidate(true);
        counterPanel.Invalidate(true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (HappyModeActive)
        {
            // Happy mode: draw happy2.jpg as full-screen background, skip circles
            if (_happyBgImage == null)
            {
                string path = @"C:\Users\agmail\Desktop\padel proj\Padel-Learning-System-1\TUIO11_NET-master\bin\Debug\happy2.jpg";
                if (!File.Exists(path)) path = Path.Combine(Application.StartupPath, "happy2.jpg");
                if (!File.Exists(path)) path = Path.Combine(Application.StartupPath, "Images", "happy2.jpg");
                if (File.Exists(path))
                {
                    _happyBgImage = Image.FromFile(path);
                    Console.WriteLine("[LessonPage] happy2.jpg loaded.");
                }
            }
            if (_happyBgImage != null)
                g.DrawImage(_happyBgImage, 0, 0, this.ClientSize.Width, this.ClientSize.Height);
            else
                g.Clear(Color.FromArgb(255, 145, 70));
        }
        else if (IsArrangementLesson())
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(241, 248, 255),
                Color.FromArgb(228, 241, 255),
                90f))
            {
                g.FillRectangle(brush, this.ClientRectangle);
            }
            DrawArrangementDecorations(g);
        }
        else
        {
            using (LinearGradientBrush brush = new LinearGradientBrush(
                this.ClientRectangle,
                Color.FromArgb(244, 249, 255),
                Color.FromArgb(225, 239, 255),
                90f))
            {
                g.FillRectangle(brush, this.ClientRectangle);
            }
            DrawDecorations(g);
        }

        base.OnPaint(e);
    }

    private void DrawDecorations(Graphics g)
    {
        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(35, 255, 200, 120)))
        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(32, 120, 190, 255)))
        using (SolidBrush b3 = new SolidBrush(Color.FromArgb(28, 118, 214, 143)))
        {
            g.FillEllipse(b1, 80, 110, 120, 120);
            g.FillEllipse(b2, this.ClientSize.Width - 180, 120, 110, 110);
            g.FillEllipse(b3, this.ClientSize.Width - 250, this.ClientSize.Height - 230, 150, 150);
        }
    }

    private void DrawArrangementDecorations(Graphics g)
    {
        using (SolidBrush b1 = new SolidBrush(Color.FromArgb(35, 96, 182, 120)))
        using (SolidBrush b2 = new SolidBrush(Color.FromArgb(28, 120, 190, 255)))
        using (SolidBrush b3 = new SolidBrush(Color.FromArgb(30, 255, 200, 120)))
        using (SolidBrush b4 = new SolidBrush(Color.FromArgb(22, 160, 210, 180)))
        {
            g.FillEllipse(b1, 70, 120, 140, 140);
            g.FillEllipse(b2, this.ClientSize.Width - 230, 130, 120, 120);
            g.FillEllipse(b3, 110, this.ClientSize.Height - 260, 170, 170);
            g.FillEllipse(b4, this.ClientSize.Width - 290, this.ClientSize.Height - 270, 190, 190);
        }

        DrawArrowChain(g, new Point(470, 350), 4);
        DrawPuzzleDots(g);
    }

    private void DrawArrowChain(Graphics g, Point start, int count)
    {
        using (Pen pen = new Pen(Color.FromArgb(90, 96, 182, 120), 4))
        {
            pen.EndCap = LineCap.ArrowAnchor;

            int x = start.X;
            for (int i = 0; i < count; i++)
            {
                g.DrawLine(pen, x, start.Y, x + 55, start.Y);
                x += 85;
            }
        }
    }

    private void DrawPuzzleDots(Graphics g)
    {
        using (SolidBrush dot1 = new SolidBrush(Color.FromArgb(80, 96, 182, 120)))
        using (SolidBrush dot2 = new SolidBrush(Color.FromArgb(80, 120, 190, 255)))
        using (SolidBrush dot3 = new SolidBrush(Color.FromArgb(80, 255, 200, 120)))
        {
            g.FillEllipse(dot1, 330, 120, 12, 12);
            g.FillEllipse(dot2, 350, 140, 12, 12);
            g.FillEllipse(dot3, 370, 120, 12, 12);

            g.FillEllipse(dot2, this.ClientSize.Width - 390, 160, 12, 12);
            g.FillEllipse(dot3, this.ClientSize.Width - 370, 180, 12, 12);
            g.FillEllipse(dot1, this.ClientSize.Width - 350, 160, 12, 12);
        }
    }

    public void addTuioObject(TuioObject o)
    {
        if (!this.IsHandleCreated || this.IsDisposed) return;
        if (o.SymbolID == 20)
        {
            this.BeginInvoke((MethodInvoker)delegate { if (!this.IsDisposed) this.Close(); });
            return;
        }
        if (o.SymbolID == controlMarkerId)
        {
            anchorAngleDegrees = NormalizeAngle(o.AngleDegrees);
        }
    }

    public void updateTuioObject(TuioObject o)
    {
        if (o.SymbolID != controlMarkerId) return;

        this.BeginInvoke((MethodInvoker)delegate
        {
            float currentAngle = NormalizeAngle(o.AngleDegrees);

            // Self-initialise anchor if addTuioObject was missed
            if (anchorAngleDegrees < 0f)
            {
                anchorAngleDegrees = currentAngle;
                return;
            }

            float delta = SmallestAngleDifference(currentAngle, anchorAngleDegrees);

            while (delta >= ROTATION_STEP)
            {
                NextItem();
                anchorAngleDegrees = NormalizeAngle(anchorAngleDegrees + ROTATION_STEP);
                delta = SmallestAngleDifference(currentAngle, anchorAngleDegrees);
            }

            while (delta <= -ROTATION_STEP)
            {
                PreviousItem();
                anchorAngleDegrees = NormalizeAngle(anchorAngleDegrees - ROTATION_STEP);
                delta = SmallestAngleDifference(currentAngle, anchorAngleDegrees);
            }
        });
    }

    public void removeTuioObject(TuioObject o)
    {
        if (o.SymbolID == controlMarkerId)
        {
            anchorAngleDegrees = -1f;
        }
    }

    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime frameTime) { }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client.removeTuioListener(this);

        // Stop and dispose audio resources
        try { if (_synth != null) { _synth.SpeakAsyncCancelAll(); _synth.Dispose(); _synth = null; } } catch { }
        try { if (_wavPlayer != null) { _wavPlayer.Stop(); _wavPlayer.Dispose(); _wavPlayer = null; } } catch { }

        foreach (var kv in imageCache)
        {
            if (kv.Value != null)
                kv.Value.Dispose();
        }
        imageCache.Clear();

        base.OnFormClosed(e);
    }

    private void RoundBadgeLabel(Label lbl, int radius)
    {
        lbl.Resize += (s, e) =>
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                int d = radius * 2;
                Rectangle rect = new Rectangle(0, 0, lbl.Width - 1, lbl.Height - 1);

                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();

                lbl.Region = new Region(path);
            }
        };

        lbl.PerformLayout();
    }

    private class WordItem
    {
        public string Word;
        public string Meaning;
        public string Example;
        public string ImageName;

        public WordItem(string word, string meaning, string example, string imageName)
        {
            Word = word;
            Meaning = meaning;
            Example = example;
            ImageName = imageName;
        }
    }

    private class ArrangeItem
    {
        public string[] Words;
        public string CorrectSentence;
        public string Hint;

        public ArrangeItem(string[] words, string correctSentence, string hint)
        {
            Words = words;
            CorrectSentence = correctSentence;
            Hint = hint;
        }
    }

    // Gesture handler
    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed || !this.IsHandleCreated) return;
        if (markerId == 20)
            this.BeginInvoke((MethodInvoker)delegate { if (!this.IsDisposed) this.Close(); });
    }
}

// ================================================================
//  QuizPage  — image shown, pick correct word with marker 10/11/12
// ================================================================
public class QuizPage : Form, TuioListener
{
    private struct QWord { public string Word, ImageName; }

    private TuioClient client;
    private string levelName;
    private QWord[] vocab;
    private Random rng = new Random();

    private int qIndex = 0;
    private int stars = 0;
    private const int TOTAL_Q = 6;
    private int correctSlot;
    private bool answerLocked = false;
    private string[] currentOptions = new string[3];

    private int timeLeft;
    private System.Windows.Forms.Timer countdown;

    private Label lblProgress, lblStars, lblQuestion, lblFeedback, lblTimerNum;
    private Panel timerBg, timerFill;
    private PictureBox picQ;
    private RoundedShadowPanel[] slots = new RoundedShadowPanel[3];
    private Label[] slotLbl = new Label[3];

    private System.Collections.Generic.Dictionary<string, Image> imgCache =
        new System.Collections.Generic.Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    private SpeechSynthesizer synth;

    public QuizPage(string level, TuioClient shared)
    {
        client = shared;
        levelName = level;
        vocab = GetVocab(level);

        this.Text = "Padel Quiz";
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.FromArgb(240, 248, 255);
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;

        foreach (var w in vocab) LoadImg(w.ImageName);

        try
        {
            synth = new SpeechSynthesizer();
            synth.Rate = -1; synth.Volume = 100;
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (v.VoiceInfo.Culture.Name.StartsWith("en")) { synth.SelectVoice(v.VoiceInfo.Name); break; }
        }
        catch { synth = null; }

        BuildUI();
        client.addTuioListener(this);
        NavHelper.AddNavBar(this, "Padel Quiz", false);

        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) TryAnswer(0);
            else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) TryAnswer(1);
            else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) TryAnswer(2);
            else if (e.KeyCode == Keys.Escape) this.Close();
        };

        this.Shown += (s, e) => LoadQuestion();

        // Subscribe to gesture router (legacy marker + named gestures)
        this.Shown += (s, e) => { GestureRouter.OnGestureMarker += HandleGestureMarker; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureMarker -= HandleGestureMarker; };
        this.Shown += (s, e) => { GestureRouter.OnGestureRecognized += HandleGestureName; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureRecognized -= HandleGestureName; };
    }

    /// <summary>
    /// Quiz/Spelling hand-gesture handler: SwipeLeft / Checkmark /
    /// SwipeRight pick answer A / B / C respectively. Circle closes.
    /// </summary>
    private void HandleGestureName(string name, float score)
    {
        if (!this.Visible || this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;
                switch (name)
                {
                    case "SwipeLeft":  TryAnswer(0); break;
                    case "Checkmark":  TryAnswer(1); break;
                    case "SwipeRight": TryAnswer(2); break;
                    case "Circle":     this.Close(); break;
                }
            });
        }
        catch { }
    }

    private void BuildUI()
    {
        lblProgress = MakeLabel("Question 1 / 6", 15, FontStyle.Bold, Color.FromArgb(40, 60, 100));
        lblStars = MakeLabel("Correct: 0 / " + TOTAL_Q, 15, FontStyle.Bold, Color.FromArgb(200, 140, 0));
        lblQuestion = MakeLabel("Which padel term matches this image?", 17, FontStyle.Regular, Color.FromArgb(50, 75, 115));
        lblFeedback = MakeLabel("", 22, FontStyle.Bold, Color.Green);
        lblTimerNum = MakeLabel("20", 15, FontStyle.Bold, Color.FromArgb(50, 90, 160));
        timerBg = new Panel { BackColor = Color.FromArgb(210, 225, 245) };
        timerFill = new Panel { BackColor = Color.FromArgb(80, 160, 240), Dock = DockStyle.Left };
        timerBg.Controls.Add(timerFill);

        picQ = new PictureBox { Size = new Size(280, 280), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };

        Color[] optColors = { Color.FromArgb(232, 246, 222), Color.FromArgb(223, 242, 255), Color.FromArgb(255, 240, 217) };
        string[] letters = { "10  A", "11  B", "12  C" };

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var p = new RoundedShadowPanel
            {
                CornerRadius = 22,
                FillColor = optColors[i],
                BorderColor = Color.FromArgb(200, 220, 240),
                BorderThickness = 1.8f,
                ShadowColor = Color.FromArgb(40, 0, 0, 0),
                DrawGloss = true,
                ShadowOffsetX = 4,
                ShadowOffsetY = 6
            };
            var lbl = new Label
            {
                Font = new Font("Arial", 17, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 60, 85),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                AutoSize = false,
                Dock = DockStyle.Fill
            };
            p.Controls.Add(lbl);
            p.Click += (s, e) => TryAnswer(idx);
            lbl.Click += (s, e) => TryAnswer(idx);
            slots[i] = p; slotLbl[i] = lbl;
            this.Controls.Add(p);
        }

        this.Controls.AddRange(new Control[] { timerBg, picQ, lblProgress, lblStars, lblQuestion, lblFeedback, lblTimerNum });

        this.Load += (s, e) => ArrangeUI();
        this.Resize += (s, e) => ArrangeUI();
    }

    private void ArrangeUI()
    {
        int cx = this.ClientSize.Width / 2;
        int nt = 70;

        lblProgress.Location = new Point(40, nt);
        lblStars.Location = new Point(this.ClientSize.Width - 160, nt);
        lblTimerNum.Location = new Point(cx - 20, nt);
        lblQuestion.Location = new Point(cx - 300, 145);
        lblQuestion.Size = new Size(600, 32);

        timerBg.Size = new Size(this.ClientSize.Width - 80, 14);
        timerBg.Location = new Point(40, nt + 30);

        picQ.Location = new Point(cx - 140, 185);

        int slotW = 280, slotH = 88;
        int startX = cx - (slotW * 3 + 40) / 2;
        for (int i = 0; i < 3; i++)
        {
            slots[i].Size = new Size(slotW, slotH);
            slots[i].Location = new Point(startX + i * (slotW + 20), 490);
        }

        lblFeedback.Location = new Point(cx - 150, 596);
        lblFeedback.Size = new Size(300, 40);
    }

    private void LoadQuestion()
    {
        if (qIndex >= TOTAL_Q) { ShowCelebration(); return; }

        answerLocked = false;
        lblFeedback.Text = "";

        int vi = qIndex % vocab.Length;
        QWord correct = vocab[vi];

        var pool = new System.Collections.Generic.List<int>();
        for (int i = 0; i < vocab.Length; i++) if (i != vi) pool.Add(i);
        Shuffle(pool);

        currentOptions[0] = correct.Word;
        currentOptions[1] = vocab[pool[0]].Word;
        currentOptions[2] = vocab[pool[1]].Word;
        ShuffleArr(currentOptions);

        correctSlot = 0;
        for (int i = 0; i < 3; i++) if (currentOptions[i] == correct.Word) { correctSlot = i; break; }

        lblProgress.Text = "Question " + (qIndex + 1) + " / " + TOTAL_Q;
        picQ.Image = imgCache.ContainsKey(correct.ImageName) ? imgCache[correct.ImageName] : null;

        Color[] orig = { Color.FromArgb(232, 246, 222), Color.FromArgb(223, 242, 255), Color.FromArgb(255, 240, 217) };
        for (int i = 0; i < 3; i++)
        {
            slotLbl[i].Text = new string[] { "10  A", "11  B", "12  C" }[i] + "  ·  " + currentOptions[i];
            slots[i].FillColor = orig[i];
            slots[i].Invalidate();
        }

        try
        {
            if (synth != null && !AppSettings.IsMuted)
            {
                synth.Rate = AppSettings.VoiceRate;
                synth.SpeakAsyncCancelAll();
                synth.SpeakAsync("What shot matches this image?");
            }
        }
        catch { }
        StartTimer();
    }

    private void StartTimer()
    {
        timeLeft = 20;
        UpdateTimerBar();

        if (countdown != null) { countdown.Stop(); countdown.Dispose(); }

        countdown = new System.Windows.Forms.Timer { Interval = 1000 }; // ثانية كاملة
        countdown.Tick += (s, e) =>
        {
            timeLeft--;
            UpdateTimerBar();

            if (timeLeft <= 0)
            {
                countdown.Stop();
                TryAnswer(-1);
            }
        };
        countdown.Start();
    }

    private void UpdateTimerBar()
    {
        if (timerBg.Width == 0) return;

        timerFill.Width = Math.Max(0, (int)(timerBg.Width * timeLeft / 20f));
        timerFill.BackColor = timeLeft > 10
            ? Color.FromArgb(80, 160, 240)
            : Color.FromArgb(220, 60, 60);

        lblTimerNum.Text = timeLeft.ToString();
    }

    private void TryAnswer(int slot)
    {
        if (answerLocked) return;
        answerLocked = true;
        if (countdown != null) countdown.Stop();

        Color green = Color.FromArgb(110, 195, 110);
        Color red = Color.FromArgb(215, 90, 90);

        if (slot == correctSlot)
        {
            stars++; lblStars.Text = "Correct: " + stars + " / " + TOTAL_Q;
            slots[slot].FillColor = green;
            slots[slot].Invalidate();
            lblFeedback.Text = "✓  Correct!";
            lblFeedback.ForeColor = Color.FromArgb(30, 130, 30);
            try { if (synth != null && !AppSettings.IsMuted) synth.SpeakAsync("Correct!"); } catch { }
        }
        else
        {
            if (slot >= 0) { slots[slot].FillColor = red; slots[slot].Invalidate(); }
            slots[correctSlot].FillColor = green; slots[correctSlot].Invalidate();
            lblFeedback.Text = "✗  The answer is:  " + vocab[qIndex % vocab.Length].Word;
            lblFeedback.ForeColor = Color.FromArgb(175, 35, 35);
            try { if (synth != null && !AppSettings.IsMuted) synth.SpeakAsync("The correct answer is " + vocab[qIndex % vocab.Length].Word); } catch { }
        }

        var next = new System.Windows.Forms.Timer { Interval = 2500 }; // 2.5 ثانية
        next.Tick += (s, e) =>
        {
            next.Stop();
            next.Dispose();
            qIndex++;
            LoadQuestion();
        };
        next.Start();
    }

    private void ShowCelebration()
    {
        synth.Rate = AppSettings.VoiceRate;
        try { if (synth != null) synth.SpeakAsync("Congratulations! You scored " + stars + " out of " + TOTAL_Q + "."); } catch { }
        var cel = new CelebrationForm(stars, TOTAL_Q, this);
        cel.Show();
    }

    private static Label MakeLabel(string text, int size, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Arial", size, style),
            ForeColor = color,
            AutoSize = true,
            BackColor = Color.Transparent
        };
    }

    private void LoadImg(string name)
    {
        if (string.IsNullOrEmpty(name) || imgCache.ContainsKey(name)) return;
        try
        {
            string p = System.IO.Path.Combine(Application.StartupPath, "Data", name);
            if (!System.IO.File.Exists(p))
                p = System.IO.Path.Combine(Application.StartupPath, name);
            if (!System.IO.File.Exists(p))
                p = System.IO.Path.Combine(Application.StartupPath, "Images", name);
            if (!System.IO.File.Exists(p)) return;
            using (var fs = new System.IO.FileStream(p, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            using (Image img = Image.FromStream(fs))
                imgCache[name] = new Bitmap(img);
        }
        catch { }
    }

    private void Shuffle<T>(System.Collections.Generic.List<T> list)
    { for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); T t = list[i]; list[i] = list[j]; list[j] = t; } }

    private void ShuffleArr<T>(T[] arr)
    { for (int i = arr.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); T t = arr[i]; arr[i] = arr[j]; arr[j] = t; } }



    internal static string[][] GetLevelWords(string level)
    {
        var v = GetVocab(level);
        var result = new string[v.Length][];
        for (int i = 0; i < v.Length; i++)
            result[i] = new string[] { v[i].Word, v[i].ImageName };
        return result;
    }

    private static QWord[] GetVocab(string level)
    {
        if (level.Contains("Primary"))
            return new QWord[] {
                new QWord{Word="SERVE",      ImageName="serve.png"},
                new QWord{Word="FOREHAND",   ImageName="forehand.png"},
                new QWord{Word="BACKHAND",   ImageName="backhand.png"},
                new QWord{Word="VOLLEY",     ImageName="volley.png"},
                new QWord{Word="COURT ZONES",ImageName="court_zones.png"},
                new QWord{Word="SCORING",    ImageName="scoring.png"}
            };
        if (level.Contains("Secondary"))
            return new QWord[] {
                new QWord{Word="NET RULE",      ImageName="net_rules.png"},
                new QWord{Word="DOUBLE BOUNCE", ImageName="double_bounce.png"},
                new QWord{Word="FOOT FAULT",    ImageName="foot_fault.png"},
                new QWord{Word="WALL USAGE",    ImageName="wall_usage.png"},
                new QWord{Word="CHANGE COURT",  ImageName="change_court.png"},
                new QWord{Word="DEJADA",        ImageName="dejada.png"}
            };
        return new QWord[] {
            new QWord{Word="BANDEJA",       ImageName="bandeja.png"},
            new QWord{Word="VIBORA",        ImageName="vibora.png"},
            new QWord{Word="SMASH",         ImageName="smash.png"},
            new QWord{Word="CORNER SHOT",   ImageName="corner_shot.png"},
            new QWord{Word="GOLDEN POINT",  ImageName="golden_point.png"},
            new QWord{Word="CONTRA PARED",  ImageName="contra_pared.png"}
        };
    }

    public void addTuioObject(TuioObject o)
    {
        if (o.SymbolID == 20) this.Invoke((MethodInvoker)(() => this.Close()));
        else if (o.SymbolID == 10) this.BeginInvoke((MethodInvoker)(() => TryAnswer(0)));
        else if (o.SymbolID == 11) this.BeginInvoke((MethodInvoker)(() => TryAnswer(1)));
        else if (o.SymbolID == 12) this.BeginInvoke((MethodInvoker)(() => TryAnswer(2)));
    }
    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime t) { }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client.removeTuioListener(this);
        if (countdown != null) countdown.Stop();
        try { if (synth != null) { synth.SpeakAsyncCancelAll(); synth.Dispose(); } } catch { }
        foreach (var kv in imgCache) { if (kv.Value != null) kv.Value.Dispose(); }
        base.OnFormClosed(e);
    }

    // Gesture handler
    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed) return;

        if (markerId == 20)
        {
            this.Invoke((MethodInvoker)(() => this.Close()));
        }
        else if (markerId == 10) this.BeginInvoke((MethodInvoker)(() => TryAnswer(0)));
        else if (markerId == 11) this.BeginInvoke((MethodInvoker)(() => TryAnswer(1)));
        else if (markerId == 12) this.BeginInvoke((MethodInvoker)(() => TryAnswer(2)));
    }
}

// ================================================================
//  SpellingPage  — show image, pick the correct spelling
// ================================================================
public class SpellingPage : Form, TuioListener
{
    private struct QWord { public string Word, ImageName; }

    private TuioClient client;
    private QWord[] vocab;
    private Random rng = new Random();

    private int qIndex = 0;
    private int stars = 0;
    private const int TOTAL_Q = 6;
    private int correctSlot;
    private bool answerLocked = false;
    private string[] currentOptions = new string[3];

    private int timeLeft;
    private System.Windows.Forms.Timer countdown;

    private Label lblProgress, lblStars, lblQuestion, lblFeedback, lblTimerNum;
    private Panel timerBg, timerFill;
    private PictureBox picQ;
    private RoundedShadowPanel[] slots = new RoundedShadowPanel[3];
    private Label[] slotLbl = new Label[3];

    private System.Collections.Generic.Dictionary<string, Image> imgCache =
        new System.Collections.Generic.Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
    private SpeechSynthesizer synth;

    public SpellingPage(string level, TuioClient shared)
    {
        client = shared;
        // Build vocab from QuizPage's shared word list
        string[][] raw = QuizPage.GetLevelWords(level);
        vocab = new QWord[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            vocab[i] = new QWord { Word = raw[i][0], ImageName = raw[i][1] };

        this.Text = "Padel Speed Mode";
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.FromArgb(245, 240, 255);
        this.DoubleBuffered = true;
        this.StartPosition = FormStartPosition.CenterScreen;

        foreach (var w in vocab) LoadImg(w.ImageName);

        try
        {
            synth = new SpeechSynthesizer();
            synth.Rate = -1; synth.Volume = 100;
            foreach (InstalledVoice v in synth.GetInstalledVoices())
                if (v.VoiceInfo.Culture.Name.StartsWith("en")) { synth.SelectVoice(v.VoiceInfo.Name); break; }
        }
        catch { synth = null; }

        BuildUI();
        client.addTuioListener(this);
        NavHelper.AddNavBar(this, "Padel Speed Mode", false);

        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1) TryAnswer(0);
            else if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2) TryAnswer(1);
            else if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3) TryAnswer(2);
            else if (e.KeyCode == Keys.Escape) this.Close();
        };

        this.Shown += (s, e) => LoadQuestion();

        // Subscribe to gesture router (legacy marker + named gestures)
        this.Shown += (s, e) => { GestureRouter.OnGestureMarker += HandleGestureMarker; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureMarker -= HandleGestureMarker; };
        this.Shown += (s, e) => { GestureRouter.OnGestureRecognized += HandleGestureName; };
        this.FormClosed += (s, e) => { GestureRouter.OnGestureRecognized -= HandleGestureName; };
    }

    /// <summary>
    /// Quiz/Spelling hand-gesture handler: SwipeLeft / Checkmark /
    /// SwipeRight pick answer A / B / C respectively. Circle closes.
    /// </summary>
    private void HandleGestureName(string name, float score)
    {
        if (!this.Visible || this.IsDisposed) return;
        try
        {
            this.BeginInvoke((MethodInvoker)delegate
            {
                if (this.IsDisposed) return;
                switch (name)
                {
                    case "SwipeLeft":  TryAnswer(0); break;
                    case "Checkmark":  TryAnswer(1); break;
                    case "SwipeRight": TryAnswer(2); break;
                    case "Circle":     this.Close(); break;
                }
            });
        }
        catch { }
    }

    private void BuildUI()
    {
        lblProgress = MakeLabel("Question 1 / 6", 15, FontStyle.Bold, Color.FromArgb(70, 40, 120));
        lblStars = MakeLabel("Correct: 0 / " + TOTAL_Q, 15, FontStyle.Bold, Color.FromArgb(200, 140, 0));
        lblQuestion = MakeLabel("Identify the padel term:", 17, FontStyle.Regular, Color.FromArgb(70, 40, 120));
        lblFeedback = MakeLabel("", 22, FontStyle.Bold, Color.Green);
        lblTimerNum = MakeLabel("20", 15, FontStyle.Bold, Color.FromArgb(100, 60, 180));
        timerBg = new Panel { BackColor = Color.FromArgb(220, 210, 245) };
        timerFill = new Panel { BackColor = Color.FromArgb(130, 90, 220), Dock = DockStyle.Left };
        timerBg.Controls.Add(timerFill);

        picQ = new PictureBox { Size = new Size(260, 260), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };

        Color[] optColors = { Color.FromArgb(238, 232, 255), Color.FromArgb(230, 248, 255), Color.FromArgb(255, 244, 220) };

        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var p = new RoundedShadowPanel
            {
                CornerRadius = 22,
                FillColor = optColors[i],
                BorderColor = Color.FromArgb(200, 185, 240),
                BorderThickness = 1.8f,
                ShadowColor = Color.FromArgb(40, 0, 0, 0),
                DrawGloss = true,
                ShadowOffsetX = 4,
                ShadowOffsetY = 6
            };
            var lbl = new Label
            {
                Font = new Font("Arial", 16, FontStyle.Bold),
                ForeColor = Color.FromArgb(55, 30, 95),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                AutoSize = false,
                Dock = DockStyle.Fill
            };
            p.Controls.Add(lbl);
            p.Click += (s, e) => TryAnswer(idx);
            lbl.Click += (s, e) => TryAnswer(idx);
            slots[i] = p; slotLbl[i] = lbl;
            this.Controls.Add(p);
        }

        this.Controls.AddRange(new Control[] { timerBg, picQ, lblProgress, lblStars, lblQuestion, lblFeedback, lblTimerNum });
        this.Load += (s, e) => ArrangeUI();
        this.Resize += (s, e) => ArrangeUI();
    }

    private void ArrangeUI()
    {
        int cx = this.ClientSize.Width / 2;
        int nt = 70;
        lblProgress.Location = new Point(40, nt);
        lblStars.Location = new Point(this.ClientSize.Width - 160, nt);
        lblTimerNum.Location = new Point(cx - 20, nt);
        lblQuestion.Location = new Point(cx - 300, 145);
        lblQuestion.Size = new Size(600, 32);
        timerBg.Size = new Size(this.ClientSize.Width - 80, 14);
        timerBg.Location = new Point(40, nt + 30);
        picQ.Location = new Point(cx - 130, 185);
        int slotW = 300, slotH = 80;
        int startX = cx - (slotW * 3 + 40) / 2;
        for (int i = 0; i < 3; i++)
        { slots[i].Size = new Size(slotW, slotH); slots[i].Location = new Point(startX + i * (slotW + 20), 470); }
        lblFeedback.Location = new Point(cx - 200, 568);
        lblFeedback.Size = new Size(400, 40);
    }

    private void LoadQuestion()
    {
        if (qIndex >= TOTAL_Q) { ShowCelebration(); return; }
        answerLocked = false; lblFeedback.Text = "";

        int vi = qIndex % vocab.Length;
        string correct = vocab[vi].Word;
        picQ.Image = imgCache.ContainsKey(vocab[vi].ImageName) ? imgCache[vocab[vi].ImageName] : null;

        // Generate 2 plausible misspellings
        currentOptions[0] = correct;
        currentOptions[1] = Misspell(correct, 0);
        currentOptions[2] = Misspell(correct, 1);
        ShuffleArr(currentOptions);

        correctSlot = 0;
        for (int i = 0; i < 3; i++) if (currentOptions[i] == correct) { correctSlot = i; break; }

        lblProgress.Text = "Question " + (qIndex + 1) + " / " + TOTAL_Q;
        Color[] orig = { Color.FromArgb(238, 232, 255), Color.FromArgb(230, 248, 255), Color.FromArgb(255, 244, 220) };
        string[] letters = { "10  A", "11  B", "12  C" };
        for (int i = 0; i < 3; i++)
        { slotLbl[i].Text = letters[i] + "  ·  " + currentOptions[i]; slots[i].FillColor = orig[i]; slots[i].Invalidate(); }

        try { if (synth != null) { synth.SpeakAsyncCancelAll(); synth.SpeakAsync("Choose the correct answer."); } } catch { }
        StartTimer();
    }

    // Makes a believable misspelling by swapping or replacing a character
    private string Misspell(string w, int variant)
    {
        if (w.Length < 3) return w + "X";
        char[] c = w.ToCharArray();
        if (variant == 0)
        {   // swap two middle chars
            int mid = c.Length / 2;
            char tmp = c[mid]; c[mid] = c[mid - 1]; c[mid - 1] = tmp;
        }
        else
        {   // replace one char near the end
            int pos = c.Length - 2;
            c[pos] = c[pos] == 'E' ? 'A' : 'E';
        }
        string result = new string(c);
        return result == w ? w + "S" : result;   // failsafe
    }

    private void StartTimer()
    {
        timeLeft = 20;
        UpdateTimerBar();

        if (countdown != null) { countdown.Stop(); countdown.Dispose(); }

        countdown = new System.Windows.Forms.Timer { Interval = 1000 }; // ثانية كاملة
        countdown.Tick += (s, e) =>
        {
            timeLeft--;
            UpdateTimerBar();

            if (timeLeft <= 0)
            {
                countdown.Stop();
                TryAnswer(-1);
            }
        };
        countdown.Start();
    }

    private void UpdateTimerBar()
    {
        if (timerBg.Width == 0) return;

        timerFill.Width = Math.Max(0, (int)(timerBg.Width * timeLeft / 20f));
        timerFill.BackColor = timeLeft > 10
            ? Color.FromArgb(130, 90, 220)
            : Color.FromArgb(210, 55, 55);

        lblTimerNum.Text = timeLeft.ToString();
    }
    private void TryAnswer(int slot)
    {
        if (answerLocked) return;
        answerLocked = true; if (countdown != null) countdown.Stop();
        Color green = Color.FromArgb(110, 195, 110), red = Color.FromArgb(215, 90, 90);
        if (slot == correctSlot)
        {
            stars++; lblStars.Text = "Correct: " + stars + " / " + TOTAL_Q;
            slots[slot].FillColor = green; slots[slot].Invalidate();
            lblFeedback.Text = "✓  Correct!"; lblFeedback.ForeColor = Color.FromArgb(30, 130, 30);
            synth.Rate = AppSettings.VoiceRate;
            try
            {
                if (synth != null && !AppSettings.IsMuted)
                {
                    synth.Rate = AppSettings.VoiceRate;
                    synth.SpeakAsync("Correct!");
                }
            }
            catch { }
        }
        else
        {
            if (slot >= 0) { slots[slot].FillColor = red; slots[slot].Invalidate(); }
            slots[correctSlot].FillColor = green; slots[correctSlot].Invalidate();
            lblFeedback.Text = "✗  Correct term: " + vocab[qIndex % vocab.Length].Word;
            lblFeedback.ForeColor = Color.FromArgb(175, 35, 35);
            synth.Rate = AppSettings.VoiceRate;
            try
            {
                if (synth != null && !AppSettings.IsMuted)
                {
                    synth.Rate = AppSettings.VoiceRate;
                    synth.SpeakAsync("The correct answer is " + vocab[qIndex % vocab.Length].Word);
                }
            }
            catch { }
        }
        var next = new System.Windows.Forms.Timer { Interval = 2500 }; // 2.5 ثانية
        next.Tick += (s, e) =>
        {
            next.Stop();
            next.Dispose();
            qIndex++;
            LoadQuestion();
        };
        next.Start();
    }

    private void ShowCelebration()
    {
        try { if (synth != null && !AppSettings.IsMuted) synth.SpeakAsync("Great job! You scored " + stars + " out of " + TOTAL_Q + "."); } catch { }
        new CelebrationForm(stars, TOTAL_Q, this).Show();
    }

    private static Label MakeLabel(string text, int size, FontStyle style, Color color)
    { return new Label { Text = text, Font = new Font("Arial", size, style), ForeColor = color, AutoSize = true, BackColor = Color.Transparent }; }

    private void LoadImg(string name)
    {
        if (string.IsNullOrEmpty(name) || imgCache.ContainsKey(name)) return;
        try
        {
            string p = System.IO.Path.Combine(Application.StartupPath, "Data", name);
            if (!System.IO.File.Exists(p))
                p = System.IO.Path.Combine(Application.StartupPath, name);
            if (!System.IO.File.Exists(p))
                p = System.IO.Path.Combine(Application.StartupPath, "Images", name);
            if (!System.IO.File.Exists(p)) return;
            Bitmap bmp;
            using (var fs = new System.IO.FileStream(p, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            using (Image img = Image.FromStream(fs))
                bmp = new Bitmap(img);
            // Hide any embedded word label at bottom of image
            using (Graphics gm = Graphics.FromImage(bmp))
            using (SolidBrush wb = new SolidBrush(Color.White))
                gm.FillRectangle(wb, 0, (int)(bmp.Height * 0.78f), bmp.Width, (int)(bmp.Height * 0.22f) + 1);
            imgCache[name] = bmp;
        }
        catch { }
    }

    private void ShuffleArr<T>(T[] arr)
    { for (int i = arr.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); T t = arr[i]; arr[i] = arr[j]; arr[j] = t; } }

    public void addTuioObject(TuioObject o)
    {
        if (o.SymbolID == 20) this.Invoke((MethodInvoker)(() => this.Close()));
        else if (o.SymbolID == 10) this.BeginInvoke((MethodInvoker)(() => TryAnswer(0)));
        else if (o.SymbolID == 11) this.BeginInvoke((MethodInvoker)(() => TryAnswer(1)));
        else if (o.SymbolID == 12) this.BeginInvoke((MethodInvoker)(() => TryAnswer(2)));
    }
    public void updateTuioObject(TuioObject o) { }
    public void removeTuioObject(TuioObject o) { }
    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }
    public void refresh(TuioTime t) { }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        client.removeTuioListener(this);
        if (countdown != null) countdown.Stop();
        try { if (synth != null && !AppSettings.IsMuted) { synth.SpeakAsyncCancelAll(); synth.Dispose(); } } catch { }
        foreach (var kv in imgCache) { if (kv.Value != null) kv.Value.Dispose(); }

        base.OnFormClosed(e);
    }

    // Gesture handler
    private void HandleGestureMarker(int markerId)
    {
        if (!this.Visible || this.IsDisposed) return;

        if (markerId == 20)
        {
            this.Invoke((MethodInvoker)(() => this.Close()));
        }
        else if (markerId == 10) this.BeginInvoke((MethodInvoker)(() => TryAnswer(0)));
        else if (markerId == 11) this.BeginInvoke((MethodInvoker)(() => TryAnswer(1)));
        else if (markerId == 12) this.BeginInvoke((MethodInvoker)(() => TryAnswer(2)));
    }
}

// ================================================================
//  CelebrationForm  — animated confetti + score overlay
// ================================================================
public class CelebrationForm : Form
{
    private struct Particle { public float X, Y, VX, VY; public Color Col; public int Sz; }

    private System.Collections.Generic.List<Particle> parts =
        new System.Collections.Generic.List<Particle>();
    private System.Windows.Forms.Timer animTmr, closeTmr;
    private Random rng = new Random();
    private int score, total;
    private Form parent;

    public CelebrationForm(int score, int total, Form parent)
    {
        this.score = score;
        this.total = total;
        this.parent = parent;

        this.FormBorderStyle = FormBorderStyle.None;
        this.WindowState = FormWindowState.Maximized;
        this.BackColor = Color.Black;
        this.TransparencyKey = Color.Black;
        this.TopMost = true;
        this.ShowInTaskbar = false;
        this.DoubleBuffered = true;

        for (int i = 0; i < 100; i++)
            parts.Add(new Particle
            {
                X = rng.Next(0, 1920),
                Y = rng.Next(-300, 0),
                VX = (float)(rng.NextDouble() * 6 - 3),
                VY = (float)(rng.NextDouble() * 4 + 2),
                Col = Color.FromArgb(rng.Next(140, 255), rng.Next(140, 255), rng.Next(100, 255)),
                Sz = rng.Next(10, 22)
            });

        animTmr = new System.Windows.Forms.Timer { Interval = 40 };
        animTmr.Tick += (s, e) => { UpdateParticles(); this.Invalidate(); };
        animTmr.Start();

        closeTmr = new System.Windows.Forms.Timer { Interval = 5000 };
        closeTmr.Tick += (s, e) => CloseBoth();
        closeTmr.Start();

        this.Click += (s, e) => CloseBoth();
        this.KeyPreview = true;
        this.KeyDown += (s, e) => CloseBoth();
    }

    private void CloseBoth()
    {
        if (closeTmr != null) closeTmr.Stop(); if (animTmr != null) animTmr.Stop();
        if (!this.IsDisposed) this.Close();
        if (parent != null && !parent.IsDisposed) parent.Close();
    }

    private void UpdateParticles()
    {
        int W = this.Width == 0 ? 1920 : this.Width;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            p.X += p.VX; p.Y += p.VY; p.VY += 0.12f;
            if (p.Y > this.Height + 30) { p.Y = -30; p.X = rng.Next(0, W); p.VY = (float)(rng.NextDouble() * 3 + 2); }
            parts[i] = p;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (var p in parts)
        {
            using (SolidBrush b = new SolidBrush(p.Col))
                g.FillRectangle(b, p.X, p.Y, p.Sz, p.Sz / 2);
        }

        string line1 = score >= total * 2 / 3 ? "🎉  Amazing!  🎉" : (score >= total / 2 ? "Good Job! 👍" : "Keep Practicing!");
        string line2 = "Score:  ⭐ " + score + " / " + total;

        int cx = this.Width / 2, cy = this.Height / 2;

        DrawCentred(g, line1, new Font("Arial", 52, FontStyle.Bold), Color.FromArgb(255, 230, 50), cx, cy - 60);
        DrawCentred(g, line2, new Font("Arial", 36, FontStyle.Bold), Color.White, cx, cy + 20);
    }

    private void DrawCentred(Graphics g, string text, Font font, Color color, int cx, int y)
    {
        SizeF sz = g.MeasureString(text, font);
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0)))
        using (SolidBrush fore = new SolidBrush(color))
        {
            g.DrawString(text, font, shadow, cx - sz.Width / 2 + 4, y + 4);
            g.DrawString(text, font, fore, cx - sz.Width / 2, y);
        }
        font.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (animTmr != null) animTmr.Stop(); if (closeTmr != null) closeTmr.Stop();
        base.OnFormClosed(e);
    }
}
public class AppInfoForm : Form
{
    public AppInfoForm()
    {
        this.Text = "App Info";
        this.Size = new Size(430, 300);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.BackColor = AppSettings.IsDarkMode ? Color.FromArgb(18, 22, 38) : Color.FromArgb(240, 248, 255);

        Label lbl = new Label();
        lbl.Text = string.Format(
            "English Learning Platform\n" +
            "Build Version : 1.0.0\n\n" +
            "TUIO Port     : 3333\n" +
            "Dark Mode     : {0}\n" +
            "Sound         : {1}\n" +
            "Voice Speed   : {2}",
            AppSettings.IsDarkMode ? "Enabled" : "Disabled",
            AppSettings.IsMuted ? "Muted" : "Active",
            AppSettings.IsSlowVoice ? "Slow" : "Normal");

        lbl.Font = new Font("Segoe UI", 12);
        lbl.ForeColor = AppSettings.IsDarkMode ? Color.FromArgb(218, 224, 240) : Color.FromArgb(20, 50, 90);
        lbl.AutoSize = false;
        lbl.Size = new Size(390, 200);
        lbl.Location = new Point(20, 14);
        lbl.BackColor = Color.Transparent;

        Button btn = new Button();
        btn.Text = "Close";
        btn.Size = new Size(110, 36);
        btn.Location = new Point(160, 224);
        btn.Click += (s, e) => this.Close();

        this.Controls.Add(lbl);
        this.Controls.Add(btn);

        var t = new System.Windows.Forms.Timer { Interval = 12000 };
        t.Tick += (s, e) =>
        {
            t.Stop();
            if (!this.IsDisposed) this.Close();
        };
        t.Start();
    }
}

