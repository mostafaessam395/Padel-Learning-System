using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TuioDemo;

/// <summary>
/// Processes raw gaze points into fixations, maps them to UI activity regions,
/// calculates per-category attention scores, and persists the GazeProfile.
/// </summary>
public class AnalyticsEngine
{
    // ── Activity region definitions (normalized 0–1 screen coords) ──
    // Row 1: Strokes, Rules, Practice  |  Row 2: Quiz, Spelling, Competition
    private static readonly Dictionary<string, RectangleF> ActivityRegions = new Dictionary<string, RectangleF>
    {
        { "Strokes",     new RectangleF(0.05f, 0.35f, 0.30f, 0.30f) },
        { "Rules",       new RectangleF(0.35f, 0.35f, 0.30f, 0.30f) },
        { "Practice",    new RectangleF(0.65f, 0.35f, 0.30f, 0.30f) },
        { "Quiz",        new RectangleF(0.05f, 0.65f, 0.30f, 0.25f) },
        { "Spelling",    new RectangleF(0.35f, 0.65f, 0.30f, 0.25f) },
        { "Competition", new RectangleF(0.65f, 0.65f, 0.30f, 0.25f) }
    };

    // ── Fixation detection params ──
    private const float FIXATION_RADIUS = 0.04f;   // normalized distance threshold
    private const int FIXATION_MIN_MS = 200;        // minimum dwell time in ms

    // ── Session data ──
    private readonly List<GazePoint> _rawPoints = new List<GazePoint>();
    private readonly object _pointsLock = new object();
    private readonly List<Fixation> _fixations = new List<Fixation>();
    private readonly Dictionary<string, float> _timeOnTarget = new Dictionary<string, float>();
    private DateTime _sessionStart;
    private bool _isActive;

    private struct GazePoint
    {
        public float X, Y;
        public DateTime Timestamp;
    }

    private struct Fixation
    {
        public float X, Y;
        public float DurationMs;
    }

    // ── Public API ──

    public void StartSession()
    {
        _rawPoints.Clear();
        _fixations.Clear();
        _timeOnTarget.Clear();
        foreach (var key in ActivityRegions.Keys)
            _timeOnTarget[key] = 0f;
        _sessionStart = DateTime.Now;
        _isActive = true;
    }

    public void StopSession()
    {
        _isActive = false;
    }

    public bool IsActive => _isActive;

    /// <summary>
    /// Returns the raw per-category dwell time dictionary (ms) for the current session.
    /// </summary>
    public Dictionary<string, float> GetDwellTimes()
    {
        return new Dictionary<string, float>(_timeOnTarget);
    }

    /// <summary>
    /// Returns the total number of detected fixations in the current session.
    /// </summary>
    public int FixationCount => _fixations.Count;

    /// <summary>
    /// Returns the session start timestamp.
    /// </summary>
    public DateTime SessionStart => _sessionStart;

    /// <summary>
    /// Feed a raw gaze point (called from GazeRouter handler).
    /// </summary>
    public void AddGazePoint(float x, float y)
    {
        if (!_isActive) return;
        lock (_pointsLock)
        {
            _rawPoints.Add(new GazePoint { X = x, Y = y, Timestamp = DateTime.Now });
        }
    }

    /// <summary>
    /// Process all collected gaze points into fixations and attention scores.
    /// Call this when session ends.
    /// </summary>
    public Dictionary<string, int> ComputeSessionScores()
    {
        List<GazePoint> snapshot;
        lock (_pointsLock)
        {
            snapshot = new List<GazePoint>(_rawPoints);
        }
        DetectFixations(snapshot);
        MapFixationsToRegions();
        return CalculateAttentionScores();
    }

    /// <summary>
    /// Update the user's GazeProfile using weighted moving average:
    ///   Score_new = Score_old × 0.7 + SessionScore × 0.3
    /// Then persist to users.json.
    /// </summary>
    public static void UpdateAndSaveProfile(TuioDemo.UserData user, Dictionary<string, int> sessionScores)
    {
        if (user.GazeProfile == null)
            user.GazeProfile = new TuioDemo.GazeProfile();

        var gp = user.GazeProfile;
        gp.Strokes_Score     = WeightedAvg(gp.Strokes_Score,     sessionScores.ContainsKey("Strokes") ? sessionScores["Strokes"] : 50);
        gp.Rules_Score       = WeightedAvg(gp.Rules_Score,       sessionScores.ContainsKey("Rules") ? sessionScores["Rules"] : 50);
        gp.Practice_Score    = WeightedAvg(gp.Practice_Score,    sessionScores.ContainsKey("Practice") ? sessionScores["Practice"] : 50);
        gp.Quiz_Score        = WeightedAvg(gp.Quiz_Score,        sessionScores.ContainsKey("Quiz") ? sessionScores["Quiz"] : 50);
        gp.Spelling_Score    = WeightedAvg(gp.Spelling_Score,    sessionScores.ContainsKey("Spelling") ? sessionScores["Spelling"] : 50);
        gp.Competition_Score = WeightedAvg(gp.Competition_Score, sessionScores.ContainsKey("Competition") ? sessionScores["Competition"] : 50);

        PersistUsers(user);
    }

    /// <summary>
    /// Get the categories that need focus (score below threshold).
    /// Returns list of (CategoryName, Score) sorted ascending.
    /// </summary>
    public static List<KeyValuePair<string, int>> GetWeakCategories(TuioDemo.GazeProfile profile, int threshold = 40)
    {
        if (profile == null) return new List<KeyValuePair<string, int>>();

        var scores = new Dictionary<string, int>
        {
            { "Strokes", profile.Strokes_Score },
            { "Rules", profile.Rules_Score },
            { "Practice", profile.Practice_Score },
            { "Quiz", profile.Quiz_Score },
            { "Spelling", profile.Spelling_Score },
            { "Competition", profile.Competition_Score }
        };

        return scores.Where(kv => kv.Value < threshold)
                     .OrderBy(kv => kv.Value)
                     .ToList();
    }

    /// <summary>
    /// Get the display name for the card corresponding to a category name.
    /// </summary>
    public static string GetCardDisplayName(string category)
    {
        switch (category)
        {
            case "Strokes": return "Learn Strokes";
            case "Rules": return "Court Rules";
            case "Practice": return "Practice";
            case "Quiz": return "Quick Challenge";
            case "Spelling": return "Speed Mode";
            case "Competition": return "Competition";
            default: return category;
        }
    }

    /// <summary>
    /// Create a full GazeSessionReport, persist it to the gaze_reports directory,
    /// and update the user's GazeProfile in users.json. Call this on session end.
    /// </summary>
    public void PersistSessionReport(UserData user)
    {
        if (user == null || string.IsNullOrEmpty(user.UserId)) return;

        try
        {
            // Ensure scores are computed
            var sessionScores = ComputeSessionScores();
            var dwellTimes = GetDwellTimes();

            // Determine dominant & neglected categories
            string dominant = "";
            var neglected = new List<string>();

            if (sessionScores.Count > 0)
            {
                dominant = sessionScores.OrderByDescending(kv => kv.Value).First().Key;
                neglected = sessionScores.Where(kv => kv.Value < 30)
                                         .OrderBy(kv => kv.Value)
                                         .Select(kv => kv.Key)
                                         .Take(2)
                                         .ToList();
            }

            // Build the dwell times dictionary<string,double> from the float version
            var dwellDouble = new Dictionary<string, double>();
            foreach (var kvp in dwellTimes)
                dwellDouble[kvp.Key] = kvp.Value;

            var report = new GazeSessionReport
            {
                SessionId = Guid.NewGuid().ToString("N").Substring(0, 12),
                UserId = user.UserId,
                Timestamp = DateTime.UtcNow,
                DurationSeconds = (DateTime.UtcNow - _sessionStart).TotalSeconds,
                TotalFixations = _fixations.Count,
                DominantCategory = dominant,
                NeglectedCategories = neglected,
                CardDwellTimes = dwellDouble,
                SessionScores = sessionScores
            };

            // Persist the session report to gaze_reports/{userId}_history.json
            GazeReportService.Save(report);

            // Update GazeProfile in users.json using weighted moving average
            UpdateAndSaveProfile(user, sessionScores);

            Console.WriteLine($"[AnalyticsEngine] Session report persisted for {user.Name} " +
                              $"(duration={report.DurationSeconds:F0}s, fixations={report.TotalFixations}, " +
                              $"dominant={dominant})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalyticsEngine] PersistSessionReport error: {ex.Message}");
        }
    }

    /// <summary>
    /// Classifies attention level into an AdaptiveState based on the user's
    /// accumulated GazeProfile score and optional last-session context.
    /// </summary>
    public static AdaptiveState ClassifyAttention(int score, GazeSessionReport lastSession = null)
    {
        // If we have a recent session, check if the user just engaged with this card
        // (score might still be low overall but trending up)
        if (lastSession != null && lastSession.SessionScores != null)
        {
            // Don't penalize a category that was dominant last session
            // even if its accumulated score is still moderate
        }

        if (score < 30)  return AdaptiveState.Neglected;    // Strong pulsing glow + ribbon
        if (score < 50)  return AdaptiveState.UnderFocused;  // Soft warm outline + scale nudge
        if (score > 75)  return AdaptiveState.Familiar;      // De-emphasized, "✓ explored" badge
        return AdaptiveState.Balanced;                        // Default rendering
    }

    /// <summary>
    /// Get all categories with their AdaptiveState classification for a user.
    /// </summary>
    public static Dictionary<string, AdaptiveState> ClassifyAllCategories(GazeProfile profile, GazeSessionReport lastSession = null)
    {
        var result = new Dictionary<string, AdaptiveState>();
        if (profile == null) return result;

        var scores = new Dictionary<string, int>
        {
            { "Strokes", profile.Strokes_Score },
            { "Rules", profile.Rules_Score },
            { "Practice", profile.Practice_Score },
            { "Quiz", profile.Quiz_Score },
            { "Spelling", profile.Spelling_Score },
            { "Competition", profile.Competition_Score }
        };

        foreach (var kvp in scores)
            result[kvp.Key] = ClassifyAttention(kvp.Value, lastSession);

        return result;
    }

    // ── Internal logic ──

    private void DetectFixations(List<GazePoint> points)
    {
        _fixations.Clear();
        if (points.Count < 2) return;

        int i = 0;
        while (i < points.Count)
        {
            float sumX = points[i].X, sumY = points[i].Y;
            int count = 1;
            int j = i + 1;

            while (j < points.Count)
            {
                float cx = sumX / count, cy = sumY / count;
                float dx = points[j].X - cx, dy = points[j].Y - cy;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                if (dist > FIXATION_RADIUS) break;
                sumX += points[j].X;
                sumY += points[j].Y;
                count++;
                j++;
            }

            float durationMs = (float)(points[Math.Min(j - 1, points.Count - 1)].Timestamp
                                - points[i].Timestamp).TotalMilliseconds;

            if (durationMs >= FIXATION_MIN_MS)
            {
                _fixations.Add(new Fixation
                {
                    X = sumX / count,
                    Y = sumY / count,
                    DurationMs = durationMs
                });
            }

            i = j;
        }
    }

    private void MapFixationsToRegions()
    {
        foreach (var key in ActivityRegions.Keys.ToList())
            _timeOnTarget[key] = 0f;

        foreach (var fix in _fixations)
        {
            foreach (var kvp in ActivityRegions)
            {
                if (kvp.Value.Contains(fix.X, fix.Y))
                {
                    _timeOnTarget[kvp.Key] += fix.DurationMs;
                    break;
                }
            }
        }
    }

    private Dictionary<string, int> CalculateAttentionScores()
    {
        float totalTime = _timeOnTarget.Values.Sum();
        var scores = new Dictionary<string, int>();

        foreach (var kvp in _timeOnTarget)
        {
            int score;
            if (totalTime <= 0)
                score = 50; // neutral
            else
                score = Math.Min(100, (int)((kvp.Value / totalTime) * 600f)); // scale so equal attention ≈ 100

            scores[kvp.Key] = score;
        }

        return scores;
    }

    private static int WeightedAvg(int oldScore, int sessionScore)
    {
        return (int)(oldScore * 0.7 + sessionScore * 0.3);
    }

    private static void PersistUsers(TuioDemo.UserData updatedUser)
    {
        try
        {
            string path = System.IO.Path.Combine(System.Windows.Forms.Application.StartupPath, "Data", "users.json");
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            var users = JsonConvert.DeserializeObject<List<TuioDemo.UserData>>(json) ?? new List<TuioDemo.UserData>();

            // Match by UserId when available (preferred — unique). Fall back to Name only for
            // legacy records that never received a UserId.
            for (int i = 0; i < users.Count; i++)
            {
                bool match;
                if (!string.IsNullOrEmpty(updatedUser.UserId) && !string.IsNullOrEmpty(users[i].UserId))
                    match = string.Equals(users[i].UserId, updatedUser.UserId, StringComparison.Ordinal);
                else
                    match = string.Equals(users[i].Name, updatedUser.Name, StringComparison.OrdinalIgnoreCase);

                if (match)
                {
                    users[i].GazeProfile = updatedUser.GazeProfile;
                    break;
                }
            }

            string output = JsonConvert.SerializeObject(users, Formatting.Indented);
            File.WriteAllText(path, output);
            Console.WriteLine($"[AnalyticsEngine] Saved GazeProfile for {updatedUser.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnalyticsEngine] Failed to persist: {ex.Message}");
        }
    }
}

// Extension for RectangleF.Contains with floats
internal static class RectFExtensions
{
    public static bool Contains(this RectangleF r, float x, float y)
    {
        return x >= r.X && x <= r.X + r.Width && y >= r.Y && y <= r.Y + r.Height;
    }
}
