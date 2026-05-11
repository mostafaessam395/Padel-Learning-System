using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

/// <summary>
/// Adaptive UI helper — reacts to confirmed emotions from ExpressionRouter.
/// Sad/Bored for 5s  → happy2.jpg background + happy panel colors + music (smooth fade in)
/// Happy for 5s      → reverts to original appearance (smooth fade out)
/// </summary>
public static class AdaptiveUIHelper
{
    [DllImport("winmm.dll")]
    private static extern long mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr hwnd);

    // ──────────────────────────────────────────────────────────────────
    //  Public API
    // ──────────────────────────────────────────────────────────────────

    public static void Register(Form form)
    {
        // Subscribe to future emotion events
        form.Shown += (s, e) =>
        {
            ExpressionRouter.OnEmotionDetected += (emotion) => OnEmotion(form, emotion);

            // If happy mode is ALREADY active (from a previous page), apply it immediately
            if (_happyModeOn)
            {
                Console.WriteLine($"[AdaptiveUI] Resuming happy mode on new page: {form.Text}");
                LearningPage.HappyModeActive = true;
                LessonPage.HappyModeActive   = true;
                SaveOriginalPanelColors(form);
                ApplyHappyBackground(form);
                ApplyHappyPanelColorsInstant(form);
                StartMusic();   // restart music for this page
            }
        };

        // When page closes: stop music, unsubscribe, but KEEP _happyModeOn state
        // so the next page that opens will auto-resume
        form.FormClosed += (s, e) =>
        {
            ExpressionRouter.OnEmotionDetected -= (emotion) => OnEmotion(form, emotion);
            StopMusic();  // stop while navigating; next page's Shown will restart
        };
    }

    // ──────────────────────────────────────────────────────────────────
    //  State
    // ──────────────────────────────────────────────────────────────────

    private static bool _happyModeOn = false;

    // Original panel colours stored before first override
    private static readonly Dictionary<RoundedShadowPanel, Color> _originalColors
        = new Dictionary<RoundedShadowPanel, Color>();

    // ──────────────────────────────────────────────────────────────────
    //  Emotion handler
    // ──────────────────────────────────────────────────────────────────

    private static void OnEmotion(Form form, string emotion)
    {
        Console.WriteLine($"[AdaptiveUI] Confirmed emotion={emotion}");

        bool isBad     = emotion == "sad" || emotion == "bored" || emotion == "uncomfortable";
        bool isHappy   = emotion == "happy";

        if (isBad && !_happyModeOn)
        {
            // ── Activate happy mode ──
            Invoke(form, () =>
            {
                _happyModeOn = true;
                LearningPage.HappyModeActive = true;
                LessonPage.HappyModeActive   = true;

                SaveOriginalPanelColors(form);
                ApplyHappyBackground(form);  // image immediately
                StartMusic();                // music immediately (no waiting for fade)
                FadePanelColors(form, HappyColors, () => form.Invalidate(true));
                Console.WriteLine("[AdaptiveUI] Happy mode ON — music started immediately.");
            });
        }
        else if (isHappy && _happyModeOn)
        {
            // ── Revert to normal ──
            Invoke(form, () =>
            {
                _happyModeOn = false;
                LearningPage.HappyModeActive = false;
                LessonPage.HappyModeActive   = false;

                StopMusic();   // stop immediately

                // Remove background image immediately
                var old = form.BackgroundImage;
                form.BackgroundImage = null;
                old?.Dispose();
                form.BackColor = Color.FromArgb(248, 251, 255);

                // Fade panels back to original
                FadePanelColors(form, null, () => form.Invalidate(true));
                Console.WriteLine("[AdaptiveUI] Reverted to normal — music stopped immediately.");
            });
        }
    }

    private static void Invoke(Form form, Action action)
    {
        try
        {
            if (!form.IsHandleCreated || form.IsDisposed) return;
            form.BeginInvoke((MethodInvoker)delegate
            {
                try { action(); }
                catch (Exception ex) { Console.WriteLine("[AdaptiveUI] Error: " + ex.Message); }
            });
        }
        catch (Exception ex) { Console.WriteLine("[AdaptiveUI] Invoke error: " + ex.Message); }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Background image
    // ──────────────────────────────────────────────────────────────────

    private static void ApplyHappyBackground(Form form)
    {
        string imgPath = @"C:\Users\agmail\Desktop\padel proj\Padel-Learning-System-1\TUIO11_NET-master\bin\Debug\happy2.jpg";
        if (!File.Exists(imgPath))
            imgPath = Path.Combine(Application.StartupPath, "happy2.jpg");

        Console.WriteLine($"[AdaptiveUI] Image: {imgPath} | Exists={File.Exists(imgPath)}");

        if (File.Exists(imgPath))
        {
            var old = form.BackgroundImage;
            form.BackgroundImage       = Image.FromFile(imgPath);
            form.BackgroundImageLayout = ImageLayout.Stretch;
            old?.Dispose();
            Console.WriteLine("[AdaptiveUI] Background image applied.");
        }
        form.BackColor = Color.FromArgb(255, 145, 70);
        form.Invalidate(true);
        form.Update();
    }

    // ──────────────────────────────────────────────────────────────────
    //  Panel colour fade
    // ──────────────────────────────────────────────────────────────────

    private static readonly Color[] HappyColors =
    {
        Color.FromArgb(140, 255, 230, 240), // coral pink
        Color.FromArgb(140, 200, 255, 220), // mint green
        Color.FromArgb(140, 200, 230, 255), // sky blue
        Color.FromArgb(140, 255, 250, 190), // sunny gold
        Color.FromArgb(140, 240, 210, 255), // lavender
    };

    private static void SaveOriginalPanelColors(Form form)
    {
        _originalColors.Clear();
        int idx = 0;
        foreach (Control ctrl in form.Controls)
        {
            if (ctrl is RoundedShadowPanel rsp && !_originalColors.ContainsKey(rsp))
            {
                _originalColors[rsp] = rsp.FillColor;
                idx++;
            }
        }
        Console.WriteLine($"[AdaptiveUI] Saved {idx} original panel colours.");
    }

    /// <summary>Instantly set happy panel colours — used when resuming on a new page.</summary>
    private static void ApplyHappyPanelColorsInstant(Form form)
    {
        int idx = 0;
        foreach (Control ctrl in form.Controls)
        {
            if (ctrl is RoundedShadowPanel rsp)
            {
                rsp.FillColor = HappyColors[idx % HappyColors.Length];
                rsp.Invalidate();
                idx++;
            }
        }
        form.Refresh();
    }

    /// <summary>
    /// Smooth 20-step fade from current panel colours to target colours.
    /// Pass null targetColors to fade back to stored original colours.
    /// </summary>
    private static void FadePanelColors(Form form, Color[] targetColors, Action onComplete)
    {
        const int STEPS    = 20;
        const int INTERVAL = 40; // ms → 800ms total fade

        var panels = new List<RoundedShadowPanel>();
        foreach (Control ctrl in form.Controls)
            if (ctrl is RoundedShadowPanel rsp) panels.Add(rsp);

        // Capture starting and destination colours for each panel
        var from = new Color[panels.Count];
        var to   = new Color[panels.Count];

        for (int i = 0; i < panels.Count; i++)
        {
            from[i] = panels[i].FillColor;
            if (targetColors != null)
                to[i] = targetColors[i % targetColors.Length];
            else
                to[i] = _originalColors.TryGetValue(panels[i], out Color orig) ? orig : Color.White;
        }

        int step = 0;
        var fadeTimer = new System.Windows.Forms.Timer { Interval = INTERVAL };
        fadeTimer.Tick += (s, e) =>
        {
            step++;
            float t = (float)step / STEPS; // 0..1

            for (int i = 0; i < panels.Count; i++)
            {
                panels[i].FillColor = Lerp(from[i], to[i], t);
                panels[i].Invalidate();
            }

            if (step >= STEPS)
            {
                fadeTimer.Stop();
                fadeTimer.Dispose();
                onComplete?.Invoke();
            }
        };
        fadeTimer.Start();
    }

    private static Color Lerp(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Music
    // ──────────────────────────────────────────────────────────────────

    private static System.Windows.Forms.Timer _loopTimer;

    private static void StartMusic()
    {
        string mp3 = @"C:\Users\agmail\Desktop\padel proj\Padel-Learning-System-1\TUIO11_NET-master\bin\Debug\happy2.mp3";
        if (!File.Exists(mp3)) mp3 = Path.Combine(Application.StartupPath, "happy2.mp3");

        Console.WriteLine($"[Music] {mp3} | Exists={File.Exists(mp3)}");
        if (!File.Exists(mp3)) return;

        try
        {
            mciSendString("close happyMusic", null, 0, IntPtr.Zero);
            mciSendString($"open \"{mp3}\" alias happyMusic", null, 0, IntPtr.Zero);
            mciSendString("play happyMusic from 0", null, 0, IntPtr.Zero);
            mciSendString("setaudio happyMusic volume to 50", null, 0, IntPtr.Zero);
            Console.WriteLine("[Music] Playing at 5% volume.");

            _loopTimer?.Stop();
            _loopTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _loopTimer.Tick += (s, e) =>
            {
                try
                {
                    var sb = new StringBuilder(64);
                    mciSendString("status happyMusic mode", sb, 64, IntPtr.Zero);
                    if (sb.ToString().Trim().ToLower() == "stopped")
                    {
                        mciSendString("play happyMusic from 0", null, 0, IntPtr.Zero);
                        mciSendString("setaudio happyMusic volume to 50", null, 0, IntPtr.Zero);
                    }
                }
                catch { }
            };
            _loopTimer.Start();
        }
        catch (Exception ex) { Console.WriteLine("[Music] Error: " + ex.Message); }
    }

    public static void StopMusic()
    {
        try
        {
            _loopTimer?.Stop();
            _loopTimer = null;
            mciSendString("stop happyMusic", null, 0, IntPtr.Zero);
            mciSendString("close happyMusic", null, 0, IntPtr.Zero);
            Console.WriteLine("[Music] Stopped.");
        }
        catch { }
    }

    private static void ResetState()
    {
        _happyModeOn = false;
        _originalColors.Clear();
    }
}
