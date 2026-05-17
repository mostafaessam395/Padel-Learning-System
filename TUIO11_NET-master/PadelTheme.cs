using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TuioDemo
{
    public static class PadelTheme
    {
        // Brand palette
        public static readonly Color BgDeep      = Color.FromArgb(8, 12, 28);
        public static readonly Color BgPanel     = Color.FromArgb(18, 26, 50);
        public static readonly Color BgPanelAlt  = Color.FromArgb(24, 34, 64);
        public static readonly Color BgElevated  = Color.FromArgb(30, 42, 78);
        public static readonly Color Surface     = Color.FromArgb(36, 50, 92);
        public static readonly Color SurfaceHi   = Color.FromArgb(48, 64, 116);

        public static readonly Color TextHi      = Color.FromArgb(245, 248, 255);
        public static readonly Color TextMid     = Color.FromArgb(200, 212, 240);
        public static readonly Color TextLo      = Color.FromArgb(150, 165, 200);
        public static readonly Color TextMuted   = Color.FromArgb(110, 125, 165);

        public static readonly Color Accent      = Color.FromArgb(0, 220, 180);   // teal-mint
        public static readonly Color AccentSoft  = Color.FromArgb(60, 235, 200);
        public static readonly Color AccentDeep  = Color.FromArgb(0, 170, 145);

        public static readonly Color Primary     = Color.FromArgb(86, 130, 255);  // electric blue
        public static readonly Color PrimaryDeep = Color.FromArgb(60, 96, 220);
        public static readonly Color PrimarySoft = Color.FromArgb(120, 160, 255);

        public static readonly Color Hot         = Color.FromArgb(255, 95, 130);  // pink-coral
        public static readonly Color HotDeep     = Color.FromArgb(220, 60, 100);
        public static readonly Color Gold        = Color.FromArgb(255, 200, 80);
        public static readonly Color Lime        = Color.FromArgb(140, 230, 110);

        public static readonly Color Ok          = Color.FromArgb(60, 200, 130);
        public static readonly Color Warn        = Color.FromArgb(255, 175, 70);
        public static readonly Color Err         = Color.FromArgb(255, 90, 110);
        public static readonly Color Info        = Color.FromArgb(80, 175, 255);

        public static readonly Color Glow        = Color.FromArgb(120, 0, 220, 180);
        public static readonly Color Hairline    = Color.FromArgb(40, 255, 255, 255);

        // Typography
        public static string DisplayFamily = "Segoe UI";
        public static string TextFamily    = "Segoe UI";
        public static string MonoFamily    = "Consolas";

        public static Font Display(float size, FontStyle style = FontStyle.Bold)
            => new Font(DisplayFamily, size, style);
        public static Font Text(float size, FontStyle style = FontStyle.Regular)
            => new Font(TextFamily, size, style);
        public static Font Mono(float size, FontStyle style = FontStyle.Regular)
            => new Font(MonoFamily, size, style);

        // Spacing / radii
        public const int RadSm = 10;
        public const int RadMd = 18;
        public const int RadLg = 28;
        public const int Sp1 = 4, Sp2 = 8, Sp3 = 12, Sp4 = 16, Sp5 = 24, Sp6 = 32, Sp7 = 48;

        // High-quality graphics defaults
        public static void HiQ(Graphics g)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.CompositingQuality= CompositingQuality.HighQuality;
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(2, radius * 2);
            var p = new GraphicsPath();
            if (radius <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillVertical(Graphics g, Rectangle r, Color top, Color bot, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = RoundedRect(r, radius))
            using (var br = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)),
                top, bot, LinearGradientMode.Vertical))
            {
                g.FillPath(br, path);
            }
        }

        public static void FillHorizontal(Graphics g, Rectangle r, Color left, Color right, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = RoundedRect(r, radius))
            using (var br = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, Math.Max(1, r.Width), r.Height),
                left, right, LinearGradientMode.Horizontal))
            {
                g.FillPath(br, path);
            }
        }

        public static void DrawSoftShadow(Graphics g, Rectangle r, int radius, int blur, int alpha)
        {
            for (int i = blur; i > 0; i--)
            {
                int a = Math.Max(2, alpha / (i + 1));
                var rr = new Rectangle(r.X - i, r.Y - i + 2, r.Width + i * 2, r.Height + i * 2);
                using (var path = RoundedRect(rr, radius + i))
                using (var br = new SolidBrush(Color.FromArgb(a, 0, 0, 0)))
                    g.FillPath(br, path);
            }
        }

        public static void DrawGlow(Graphics g, Rectangle r, int radius, Color glow, int strength)
        {
            for (int i = strength; i > 0; i--)
            {
                int a = Math.Max(2, glow.A / (i + 1));
                var rr = new Rectangle(r.X - i, r.Y - i, r.Width + i * 2, r.Height + i * 2);
                using (var path = RoundedRect(rr, radius + i))
                using (var br = new SolidBrush(Color.FromArgb(a, glow.R, glow.G, glow.B)))
                    g.FillPath(br, path);
            }
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color WithAlpha(Color c, int a)
            => Color.FromArgb(Math.Max(0, Math.Min(255, a)), c.R, c.G, c.B);

        public static void PaintAppBackdrop(Control host, PaintEventArgs e)
        {
            HiQ(e.Graphics);
            var r = host.ClientRectangle;
            using (var br = new LinearGradientBrush(r, BgDeep, BgPanelAlt, 60f))
                e.Graphics.FillRectangle(br, r);

            // Subtle vignette dots
            var rnd = new Random(7);
            for (int i = 0; i < 40; i++)
            {
                int x = rnd.Next(r.Width);
                int y = rnd.Next(r.Height);
                int s = rnd.Next(2, 5);
                int a = rnd.Next(10, 35);
                using (var br = new SolidBrush(Color.FromArgb(a, 120, 180, 255)))
                    e.Graphics.FillEllipse(br, x, y, s, s);
            }
        }
    }

    // Animated gradient bar with title + subtitle. Lives at the top of a page.
    public class GradientHeader : Control
    {
        private readonly System.Windows.Forms.Timer _t;
        private float _phase;
        private string _title = "Title";
        private string _subtitle = "Subtitle";
        private string _icon = "";
        private Color _from = PadelTheme.Primary;
        private Color _to   = PadelTheme.Accent;
        private Color _accent = PadelTheme.Accent;

        public string Title { get { return _title; } set { _title = value ?? ""; Invalidate(); } }
        public string Subtitle { get { return _subtitle; } set { _subtitle = value ?? ""; Invalidate(); } }
        public string Icon { get { return _icon; } set { _icon = value ?? ""; Invalidate(); } }
        public Color GradientFrom { get { return _from; } set { _from = value; Invalidate(); } }
        public Color GradientTo   { get { return _to; }   set { _to = value;   Invalidate(); } }
        public Color AccentColor  { get { return _accent;} set { _accent = value; Invalidate(); } }

        public GradientHeader()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            Height = 110;
            Dock = DockStyle.Top;

            _t = new System.Windows.Forms.Timer { Interval = 33 };
            _t.Tick += (s, e) => { _phase += 0.012f; if (_phase > 2f) _phase -= 2f; Invalidate(); };
            _t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            var r = ClientRectangle;

            // Backdrop
            using (var br = new SolidBrush(PadelTheme.BgDeep))
                g.FillRectangle(br, r);

            // Moving gradient band
            float t = (float)((Math.Sin(_phase * Math.PI) + 1) * 0.5);
            var bandTop = PadelTheme.Lerp(_from, _to, t);
            var bandBot = PadelTheme.Lerp(_to, _from, t);
            using (var br = new LinearGradientBrush(r, bandTop, bandBot, 25f + (float)Math.Sin(_phase * Math.PI) * 20f))
                g.FillRectangle(br, r);

            // Diagonal sheen
            using (var sheen = new LinearGradientBrush(r,
                Color.FromArgb(45, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), 60f))
                g.FillRectangle(sheen, r);

            // Bottom accent line
            int accentH = 3;
            var accentRect = new Rectangle(0, r.Bottom - accentH, r.Width, accentH);
            using (var br = new LinearGradientBrush(accentRect,
                Color.FromArgb(255, _accent),
                Color.FromArgb(60, _accent), LinearGradientMode.Horizontal))
                g.FillRectangle(br, accentRect);

            // Icon glyph
            int padX = 28;
            int iconW = string.IsNullOrEmpty(_icon) ? 0 : 64;
            if (!string.IsNullOrEmpty(_icon))
            {
                using (var f = new Font(PadelTheme.DisplayFamily, 30, FontStyle.Bold))
                using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near })
                {
                    var ir = new Rectangle(padX, 0, iconW, r.Height);
                    using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                        g.DrawString(_icon, f, sh, new Rectangle(ir.X + 2, ir.Y + 2, ir.Width, ir.Height), sf);
                    using (var w  = new SolidBrush(Color.White))
                        g.DrawString(_icon, f, w, ir, sf);
                }
            }

            // Title + subtitle
            int textX = padX + iconW + (iconW > 0 ? 12 : 0);
            int textW = Math.Max(50, r.Width - textX - padX);

            using (var titleFont = new Font(PadelTheme.DisplayFamily, 22, FontStyle.Bold))
            using (var subFont   = new Font(PadelTheme.TextFamily,    11, FontStyle.Regular))
            using (var shadow    = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            using (var white     = new SolidBrush(Color.White))
            using (var sub       = new SolidBrush(Color.FromArgb(220, 245, 255, 255)))
            {
                var titleRect = new Rectangle(textX, r.Height / 2 - 32, textW, 36);
                g.DrawString(_title, titleFont, shadow, new Rectangle(titleRect.X + 1, titleRect.Y + 1, titleRect.Width, titleRect.Height));
                g.DrawString(_title, titleFont, white, titleRect);

                var subRect = new Rectangle(textX, titleRect.Bottom + 2, textW, 22);
                g.DrawString(_subtitle, subFont, sub, subRect);
            }
        }
    }

    // Glass-styled card with optional accent gradient ribbon and animated hover lift.
    public class GlassCard : Control
    {
        private bool _hover;
        private float _lift;
        private readonly System.Windows.Forms.Timer _anim;
        private Color _accentTop = PadelTheme.Primary;
        private Color _accentBot = PadelTheme.Accent;
        private string _badge = "";
        private bool _hoverable = true;

        public int CornerRadius { get; set; } = PadelTheme.RadLg;
        public Color FillTop    { get; set; } = Color.FromArgb(235, 30, 42, 78);
        public Color FillBot    { get; set; } = Color.FromArgb(235, 22, 32, 60);
        public Color BorderHigh { get; set; } = Color.FromArgb(60, 255, 255, 255);
        public Color BorderLow  { get; set; } = Color.FromArgb(15, 255, 255, 255);
        public Color AccentTop  { get { return _accentTop; } set { _accentTop = value; Invalidate(); } }
        public Color AccentBot  { get { return _accentBot; } set { _accentBot = value; Invalidate(); } }
        public bool   ShowAccent { get; set; } = true;
        public bool   ShowGloss  { get; set; } = true;
        public bool   Hoverable  { get { return _hoverable; } set { _hoverable = value; Invalidate(); } }
        public string Badge      { get { return _badge; } set { _badge = value ?? ""; Invalidate(); } }

        public GlassCard()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _anim = new System.Windows.Forms.Timer { Interval = 16 };
            _anim.Tick += (s, e) =>
            {
                float target = (_hover && _hoverable) ? 1f : 0f;
                float delta  = (target - _lift) * 0.25f;
                if (Math.Abs(delta) < 0.002f) { _lift = target; _anim.Stop(); }
                else                          { _lift += delta; }
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim.Stop(); _anim.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true;  _anim.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _anim.Start(); }

        /// <summary>External hook so parent pages can drive the hover state (gesture / TUIO).</summary>
        public void SetHover(bool on) { _hover = on; _anim.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int lift = (int)Math.Round(_lift * 4);
            var r = new Rectangle(6, 6 - lift, Width - 12, Height - 16);

            // Shadow
            PadelTheme.DrawSoftShadow(g, r, CornerRadius, 8 + (int)(_lift * 6), 60);

            // Optional accent glow under card (hover)
            if (_lift > 0.05f)
            {
                var glow = PadelTheme.Lerp(_accentTop, _accentBot, 0.5f);
                PadelTheme.DrawGlow(g, r, CornerRadius,
                    Color.FromArgb((int)(140 * _lift), glow.R, glow.G, glow.B), 8);
            }

            // Body gradient fill
            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            using (var br = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)),
                FillTop, FillBot, LinearGradientMode.Vertical))
            {
                g.FillPath(br, path);
            }

            // Gloss highlight (top half)
            if (ShowGloss)
            {
                var gloss = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, Math.Max(2, r.Height / 2));
                using (var path = PadelTheme.RoundedRect(gloss, CornerRadius))
                using (var br = new LinearGradientBrush(gloss,
                    Color.FromArgb(45, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillPath(br, path);
            }

            // Left accent ribbon
            if (ShowAccent)
            {
                int rw = 5;
                var rib = new Rectangle(r.X + 1, r.Y + 14, rw, r.Height - 28);
                using (var br = new LinearGradientBrush(rib, _accentTop, _accentBot, 90f))
                    g.FillRectangle(br, rib);
            }

            // Outer hairline
            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            using (var br = new LinearGradientBrush(r, BorderHigh, BorderLow, 90f))
            using (var pen = new Pen(br, 1.4f))
                g.DrawPath(pen, path);

            // Optional badge in upper-right
            if (!string.IsNullOrEmpty(_badge))
            {
                using (var f = new Font(PadelTheme.TextFamily, 8.5f, FontStyle.Bold))
                {
                    var sz = TextRenderer.MeasureText(_badge, f);
                    var br = new Rectangle(r.Right - sz.Width - 22, r.Y + 12, sz.Width + 14, 22);
                    using (var path = PadelTheme.RoundedRect(br, 11))
                    using (var fill = new SolidBrush(Color.FromArgb(220, _accentTop)))
                        g.FillPath(fill, path);
                    using (var tf = new SolidBrush(Color.White))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(_badge, f, tf, br, sf);
                }
            }
        }
    }

    // Pulsing dot indicator (live/offline/warning).
    public class PulseDot : Control
    {
        private readonly System.Windows.Forms.Timer _t;
        private float _phase;
        private Color _color = PadelTheme.Ok;
        private bool _pulse  = true;

        public Color Color { get { return _color; } set { _color = value; Invalidate(); } }
        public bool  Pulse { get { return _pulse; } set { _pulse = value; Invalidate(); } }

        public PulseDot()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(18, 18);

            _t = new System.Windows.Forms.Timer { Interval = 33 };
            _t.Tick += (s, e) => { _phase += 0.08f; if (_phase > 6.28f) _phase -= 6.28f; Invalidate(); };
            _t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int cx = Width / 2, cy = Height / 2;
            int baseR = Math.Min(Width, Height) / 4;

            if (_pulse)
            {
                float p = (float)((Math.Sin(_phase) + 1) * 0.5);
                int rOuter = baseR + 2 + (int)(p * 8);
                int a = 110 - (int)(p * 90);
                using (var br = new SolidBrush(Color.FromArgb(Math.Max(20, a), _color.R, _color.G, _color.B)))
                    g.FillEllipse(br, cx - rOuter, cy - rOuter, rOuter * 2, rOuter * 2);
            }

            using (var br = new SolidBrush(_color))
                g.FillEllipse(br, cx - baseR, cy - baseR, baseR * 2, baseR * 2);
            using (var br = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                g.FillEllipse(br, cx - baseR + 1, cy - baseR + 1, baseR, baseR);
        }
    }

    // Small text + dot pill, used for status rows and metric chips.
    public class StatusPill : Control
    {
        private string _label = "Label";
        private string _value = "";
        private Color  _color = PadelTheme.Ok;
        private bool   _pulse = true;
        private readonly System.Windows.Forms.Timer _t;
        private float _phase;

        public string Label { get { return _label; } set { _label = value ?? ""; Invalidate(); } }
        public string Value { get { return _value; } set { _value = value ?? ""; Invalidate(); } }
        public Color  PillColor { get { return _color; } set { _color = value; Invalidate(); } }
        public bool   Pulse { get { return _pulse; } set { _pulse = value; Invalidate(); } }

        public StatusPill()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 42;

            _t = new System.Windows.Forms.Timer { Interval = 33 };
            _t.Tick += (s, e) => { _phase += 0.08f; if (_phase > 6.28f) _phase -= 6.28f; if (_pulse) Invalidate(); };
            _t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = PadelTheme.RoundedRect(r, Height / 2))
            using (var br = new LinearGradientBrush(r,
                Color.FromArgb(225, 24, 34, 64),
                Color.FromArgb(225, 18, 26, 50), 90f))
                g.FillPath(br, path);
            using (var path = PadelTheme.RoundedRect(r, Height / 2))
            using (var pen = new Pen(Color.FromArgb(45, 255, 255, 255), 1.2f))
                g.DrawPath(pen, path);

            // dot
            int dotR = 6;
            int dotX = 16;
            int dotY = Height / 2 - dotR;
            if (_pulse)
            {
                float p = (float)((Math.Sin(_phase) + 1) * 0.5);
                int rr = dotR + 2 + (int)(p * 6);
                int a = 100 - (int)(p * 70);
                using (var br = new SolidBrush(Color.FromArgb(Math.Max(20, a), _color.R, _color.G, _color.B)))
                    g.FillEllipse(br, dotX - 2 - (rr - dotR), dotY - (rr - dotR), rr * 2, rr * 2);
            }
            using (var br = new SolidBrush(_color))
                g.FillEllipse(br, dotX, dotY, dotR * 2, dotR * 2);

            // label
            using (var lf = new Font(PadelTheme.TextFamily, 10f, FontStyle.Regular))
            using (var vf = new Font(PadelTheme.TextFamily, 10f, FontStyle.Bold))
            using (var lb = new SolidBrush(PadelTheme.TextMid))
            using (var vb = new SolidBrush(Color.White))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var rsf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            {
                var lr = new Rectangle(dotX + 18, 0, Width - dotX - 28, Height);
                g.DrawString(_label, lf, lb, lr, sf);
                if (!string.IsNullOrEmpty(_value))
                    g.DrawString(_value, vf, vb, lr, rsf);
            }
        }
    }

    // Animated circular progress ring (also works as indeterminate spinner).
    public class ProgressRing : Control
    {
        private float _value;
        private readonly System.Windows.Forms.Timer _t;
        private float _phase;
        private bool _indeterminate;
        private Color _color = PadelTheme.Accent;
        private string _caption = "";

        public float Value
        {
            get { return _value; }
            set { _value = Math.Max(0, Math.Min(1f, value)); Invalidate(); }
        }
        public bool Indeterminate { get { return _indeterminate; } set { _indeterminate = value; Invalidate(); } }
        public Color ArcColor { get { return _color; } set { _color = value; Invalidate(); } }
        public string Caption { get { return _caption; } set { _caption = value ?? ""; Invalidate(); } }
        public int Thickness { get; set; } = 10;

        public ProgressRing()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(120, 120);

            _t = new System.Windows.Forms.Timer { Interval = 16 };
            _t.Tick += (s, e) => { _phase += 0.04f; if (_phase > 6.28f) _phase -= 6.28f; Invalidate(); };
            _t.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int pad = Thickness + 2;
            var r = new Rectangle(pad, pad, Math.Max(1, Width - pad * 2), Math.Max(1, Height - pad * 2));

            using (var pen = new Pen(Color.FromArgb(60, 80, 100, 140), Thickness))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, r, 0, 360);
            }

            float start, sweep;
            if (_indeterminate)
            {
                start = (float)(_phase * 180 / Math.PI) * 2f;
                sweep = 100f + (float)Math.Sin(_phase * 2) * 40f;
            }
            else
            {
                start = -90f;
                sweep = _value * 360f;
            }
            using (var pen = new Pen(_color, Thickness))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, r, start, sweep);
            }

            // centre text
            string txt = _caption;
            if (string.IsNullOrEmpty(txt) && !_indeterminate)
                txt = Math.Round(_value * 100).ToString() + "%";
            if (!string.IsNullOrEmpty(txt))
            {
                using (var f = new Font(PadelTheme.DisplayFamily, Math.Max(10, Width / 7f), FontStyle.Bold))
                using (var br = new SolidBrush(Color.White))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(txt, f, br, ClientRectangle, sf);
            }
        }
    }

    // Modern gradient action button with hover lift + ripple-on-click.
    public class GradientButton : Control
    {
        private bool _hover, _down;
        private float _hoverAmt, _pressAmt;
        private float _ripple = -1f;
        private Point _rippleAt;
        private readonly System.Windows.Forms.Timer _t;
        private string _text = "Button";
        private string _icon = "";
        private Color _from = PadelTheme.Primary;
        private Color _to   = PadelTheme.PrimaryDeep;

        public Color GradientFrom { get { return _from; } set { _from = value; Invalidate(); } }
        public Color GradientTo   { get { return _to; }   set { _to = value;   Invalidate(); } }
        public string Icon { get { return _icon; } set { _icon = value ?? ""; Invalidate(); } }
        public new string Text { get { return _text; } set { _text = value ?? ""; Invalidate(); } }
        public int CornerRadius { get; set; } = 14;

        public GradientButton()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor    = Cursors.Hand;
            Size      = new Size(180, 46);
            Font      = new Font(PadelTheme.TextFamily, 11f, FontStyle.Bold);
            ForeColor = Color.White;

            _t = new System.Windows.Forms.Timer { Interval = 16 };
            _t.Tick += (s, e) =>
            {
                float th = _hover ? 1f : 0f;
                float tp = _down  ? 1f : 0f;
                _hoverAmt += (th - _hoverAmt) * 0.25f;
                _pressAmt += (tp - _pressAmt) * 0.35f;
                bool anim = Math.Abs(_hoverAmt - th) > 0.002f || Math.Abs(_pressAmt - tp) > 0.002f;
                if (_ripple >= 0) { _ripple += 0.05f; anim = true; if (_ripple > 1f) _ripple = -1f; }
                if (!anim) _t.Stop();
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true;  _t.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; _t.Start(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _down = true;
            _ripple = 0f;
            _rippleAt = e.Location;
            _t.Start();
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; _t.Start(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int liftY = (int)(_hoverAmt * 2 - _pressAmt * 2);
            var r = new Rectangle(3, 4 - liftY, Width - 6, Height - 8);

            // Drop shadow
            PadelTheme.DrawSoftShadow(g, r, CornerRadius, 6 + (int)(_hoverAmt * 4), 90);

            // Glow on hover
            if (_hoverAmt > 0.05f)
            {
                var glowColor = PadelTheme.Lerp(_from, _to, 0.5f);
                PadelTheme.DrawGlow(g, r, CornerRadius,
                    Color.FromArgb((int)(160 * _hoverAmt), glowColor.R, glowColor.G, glowColor.B), 6);
            }

            // Fill gradient (lighter on hover)
            var top = _hover ? PadelTheme.Lerp(_from, Color.White, 0.10f) : _from;
            var bot = _hover ? PadelTheme.Lerp(_to,   Color.White, 0.05f) : _to;
            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            using (var br = new LinearGradientBrush(
                new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)),
                top, bot, LinearGradientMode.Vertical))
            {
                g.FillPath(br, path);

                // Clip for gloss + ripple
                var clip = g.Clip;
                g.SetClip(path);

                // Gloss
                using (var gl = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, Math.Max(2, r.Height / 2)),
                    Color.FromArgb(70, 255, 255, 255),
                    Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(gl, new Rectangle(r.X, r.Y, r.Width, r.Height / 2));

                // Ripple
                if (_ripple >= 0f)
                {
                    int maxR = (int)(Math.Sqrt(r.Width * r.Width + r.Height * r.Height));
                    int rad = (int)(maxR * _ripple);
                    int a = (int)((1f - _ripple) * 110);
                    using (var rb = new SolidBrush(Color.FromArgb(Math.Max(0, a), 255, 255, 255)))
                        g.FillEllipse(rb, _rippleAt.X - rad, _rippleAt.Y - rad, rad * 2, rad * 2);
                }
                g.Clip = clip;
            }

            // Border
            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            using (var pen = new Pen(Color.FromArgb(100, 255, 255, 255), 1.2f))
                g.DrawPath(pen, path);

            // Text + icon
            string disp = string.IsNullOrEmpty(_icon) ? _text : _icon + "  " + _text;
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var br = new SolidBrush(ForeColor))
            using (var shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
            {
                g.DrawString(disp, Font, shadow, new RectangleF(r.X + 1, r.Y + 1, r.Width, r.Height), sf);
                g.DrawString(disp, Font, br, r, sf);
            }
        }
    }

    // Decorative divider with a gradient hairline and a small label.
    public class SectionDivider : Control
    {
        private string _text = "Section";
        public new string Text { get { return _text; } set { _text = value ?? ""; Invalidate(); } }
        public Color  AccentColor { get; set; } = PadelTheme.Accent;

        public SectionDivider()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = 28;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            var r = ClientRectangle;

            using (var f = new Font(PadelTheme.TextFamily, 9.5f, FontStyle.Bold))
            {
                var sz = TextRenderer.MeasureText(_text.ToUpperInvariant(), f);
                int textX = 0;
                int textY = r.Height / 2 - sz.Height / 2;

                // text
                using (var br = new SolidBrush(PadelTheme.TextLo))
                    g.DrawString(_text.ToUpperInvariant(), f, br, textX, textY);

                // hairline
                int lineX = textX + sz.Width + 12;
                int lineY = r.Height / 2;
                using (var br = new LinearGradientBrush(
                    new Rectangle(lineX, lineY - 1, Math.Max(1, r.Width - lineX), 2),
                    Color.FromArgb(180, AccentColor),
                    Color.FromArgb(0, AccentColor), LinearGradientMode.Horizontal))
                    g.FillRectangle(br, lineX, lineY, Math.Max(1, r.Width - lineX), 1);
            }
        }
    }

    // Hover-lift nav tile: icon + title + sub + marker hint.  Designed for grid layouts.
    public class NavTile : Control
    {
        private bool _hover;
        private float _lift;
        private readonly System.Windows.Forms.Timer _anim;
        private string _icon = "🎾";
        private string _title = "Title";
        private string _subtitle = "Subtitle";
        private string _hint = "";
        private Color _accentA = PadelTheme.Primary;
        private Color _accentB = PadelTheme.Accent;

        public string Icon     { get { return _icon; }     set { _icon = value ?? ""; Invalidate(); } }
        public string Title    { get { return _title; }    set { _title = value ?? ""; Invalidate(); } }
        public string Subtitle { get { return _subtitle; } set { _subtitle = value ?? ""; Invalidate(); } }
        public string Hint     { get { return _hint; }     set { _hint = value ?? ""; Invalidate(); } }
        public Color AccentA   { get { return _accentA; }  set { _accentA = value; Invalidate(); } }
        public Color AccentB   { get { return _accentB; }  set { _accentB = value; Invalidate(); } }
        public int CornerRadius { get; set; } = PadelTheme.RadLg;

        public event EventHandler Activated;

        public NavTile()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor    = Cursors.Hand;
            Size      = new Size(280, 220);

            _anim = new System.Windows.Forms.Timer { Interval = 16 };
            _anim.Tick += (s, e) =>
            {
                float target = _hover ? 1f : 0f;
                float d = (target - _lift) * 0.22f;
                if (Math.Abs(d) < 0.003f) { _lift = target; _anim.Stop(); }
                else _lift += d;
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _anim.Stop(); _anim.Dispose(); }
            base.Dispose(disposing);
        }

        public void SetHover(bool on) { _hover = on; _anim.Start(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true;  _anim.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _anim.Start(); }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var h = Activated; if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            int lift = (int)Math.Round(_lift * 6);
            var r = new Rectangle(8, 10 - lift, Width - 16, Height - 22);

            PadelTheme.DrawSoftShadow(g, r, CornerRadius, 10 + (int)(_lift * 8), 65);

            if (_lift > 0.05f)
            {
                var glow = PadelTheme.Lerp(_accentA, _accentB, 0.5f);
                PadelTheme.DrawGlow(g, r, CornerRadius,
                    Color.FromArgb((int)(180 * _lift), glow.R, glow.G, glow.B), 10);
            }

            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            {
                // body
                using (var br = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height)),
                    Color.FromArgb(245, 28, 38, 70),
                    Color.FromArgb(245, 18, 26, 52), LinearGradientMode.Vertical))
                    g.FillPath(br, path);

                // accent corner glow
                var ag = PadelTheme.Lerp(_accentA, _accentB, _hover ? 0.5f : 0.8f);
                using (var pgb = new PathGradientBrush(path))
                {
                    pgb.CenterPoint  = new PointF(r.Right - 16, r.Y + 16);
                    pgb.CenterColor  = Color.FromArgb(_hover ? 180 : 110, ag);
                    pgb.SurroundColors = new[] { Color.FromArgb(0, ag) };
                    g.FillPath(pgb, path);
                }
            }

            // gloss
            var gloss = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, Math.Max(2, r.Height / 2));
            using (var glossPath = PadelTheme.RoundedRect(gloss, CornerRadius))
            using (var gbr = new LinearGradientBrush(gloss,
                Color.FromArgb(40, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillPath(gbr, glossPath);

            // top accent ribbon
            int ribH = 5;
            var rib = new Rectangle(r.X + 20, r.Y, r.Width - 40, ribH);
            using (var br = new LinearGradientBrush(rib, _accentA, _accentB, LinearGradientMode.Horizontal))
                g.FillRectangle(br, rib);

            // icon
            using (var f = new Font(PadelTheme.DisplayFamily, 36, FontStyle.Bold))
            using (var sh = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
            using (var wh = new SolidBrush(Color.White))
            using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near })
            {
                var ir = new Rectangle(r.X + 22, r.Y + 22, 64, 60);
                g.DrawString(_icon, f, sh, new Rectangle(ir.X + 2, ir.Y + 2, ir.Width, ir.Height), sf);
                g.DrawString(_icon, f, wh, ir, sf);
            }

            // title
            using (var tf = new Font(PadelTheme.DisplayFamily, 15, FontStyle.Bold))
            using (var br = new SolidBrush(Color.White))
            {
                var tr = new Rectangle(r.X + 22, r.Y + 92, r.Width - 44, 28);
                g.DrawString(_title, tf, br, tr);
            }
            // sub
            using (var sf = new Font(PadelTheme.TextFamily, 9.5f, FontStyle.Regular))
            using (var br = new SolidBrush(PadelTheme.TextMid))
            {
                var sr = new Rectangle(r.X + 22, r.Y + 122, r.Width - 44, r.Height - 160);
                g.DrawString(_subtitle, sf, br, sr);
            }
            // hint chip
            if (!string.IsNullOrEmpty(_hint))
            {
                using (var f = new Font(PadelTheme.TextFamily, 8.2f, FontStyle.Bold))
                {
                    var sz = TextRenderer.MeasureText(_hint, f);
                    var hr = new Rectangle(r.X + 22, r.Bottom - 30, sz.Width + 16, 20);
                    using (var p = PadelTheme.RoundedRect(hr, 10))
                    using (var br = new SolidBrush(Color.FromArgb(200, _accentA)))
                        g.FillPath(br, p);
                    using (var tb = new SolidBrush(Color.White))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(_hint, f, tb, hr, sf);
                }
            }

            // outline
            using (var path = PadelTheme.RoundedRect(r, CornerRadius))
            using (var pen = new Pen(Color.FromArgb(_hover ? 120 : 50, 255, 255, 255), 1.3f))
                g.DrawPath(pen, path);
        }
    }

    // Animated number counter that lerps smoothly to a target value.
    public class AnimatedCounter : Control
    {
        private double _displayed;
        private double _target;
        private string _format = "0";
        private readonly System.Windows.Forms.Timer _t;
        private string _label = "";
        private string _suffix = "";

        public double Target { get { return _target; } set { _target = value; _t.Start(); Invalidate(); } }
        public string Format { get { return _format; } set { _format = value ?? "0"; Invalidate(); } }
        public string Label  { get { return _label;  } set { _label = value ?? "";  Invalidate(); } }
        public string Suffix { get { return _suffix; } set { _suffix = value ?? ""; Invalidate(); } }
        public Color  ValueColor { get; set; } = Color.White;

        public AnimatedCounter()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            _t = new System.Windows.Forms.Timer { Interval = 16 };
            _t.Tick += (s, e) =>
            {
                double d = (_target - _displayed) * 0.18;
                if (Math.Abs(_target - _displayed) < 0.01) { _displayed = _target; _t.Stop(); }
                else _displayed += d;
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _t.Stop(); _t.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PadelTheme.HiQ(g);
            var r = ClientRectangle;

            string val = _displayed.ToString(_format);
            if (!string.IsNullOrEmpty(_suffix)) val += _suffix;

            float labelH = string.IsNullOrEmpty(_label) ? 0 : 22f;
            float valH = Math.Max(20f, r.Height - labelH - 4);

            using (var vf = new Font(PadelTheme.DisplayFamily, Math.Max(14, valH * 0.62f), FontStyle.Bold))
            using (var br = new SolidBrush(ValueColor))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(val, vf, br, new RectangleF(0, 0, r.Width, valH), sf);

            if (labelH > 0)
                using (var lf = new Font(PadelTheme.TextFamily, 9.5f, FontStyle.Regular))
                using (var br = new SolidBrush(PadelTheme.TextLo))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(_label.ToUpperInvariant(), lf, br, new RectangleF(0, valH, r.Width, labelH), sf);
        }
    }

    // Themed page form that paints a smooth gradient backdrop with subtle stars.
    public class PadelPageForm : Form
    {
        public PadelPageForm()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor      = PadelTheme.BgDeep;
            Font           = new Font(PadelTheme.TextFamily, 10f);
            StartPosition  = FormStartPosition.CenterScreen;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            PadelTheme.PaintAppBackdrop(this, e);
        }
    }
}
