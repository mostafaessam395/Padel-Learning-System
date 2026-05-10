using System;
using System.Collections.Generic;

namespace TuioDemo
{
    /// <summary>
    /// Classifies a card's visual treatment based on accumulated gaze attention.
    /// </summary>
    public enum AdaptiveState
    {
        /// <summary>Normal rendering — no visual modification.</summary>
        Balanced,
        /// <summary>Score > 75 — card is well-explored; subtly de-emphasized.</summary>
        Familiar,
        /// <summary>Score 30–50 — card deserves a soft visual nudge.</summary>
        UnderFocused,
        /// <summary>Score < 30 — strong animated glow + ribbon overlay.</summary>
        Neglected
    }

    /// <summary>
    /// Captures a single gaze-tracking session for a user.
    /// Persisted in Data/gaze_reports/{userId}_history.json.
    /// </summary>
    public class GazeSessionReport
    {
        public string SessionId          { get; set; } = "";
        public string UserId             { get; set; } = "";
        public DateTime Timestamp        { get; set; } = DateTime.UtcNow;
        public double DurationSeconds    { get; set; }
        public int TotalFixations        { get; set; }
        public string DominantCategory   { get; set; } = "";
        public List<string> NeglectedCategories { get; set; } = new List<string>();

        /// <summary>
        /// Per-card dwell time in milliseconds during this session.
        /// Keys: Strokes, Rules, Practice, Quiz, Spelling, Competition.
        /// </summary>
        public Dictionary<string, double> CardDwellTimes { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Per-card attention score (0-100) computed for this session.
        /// </summary>
        public Dictionary<string, int> SessionScores { get; set; } = new Dictionary<string, int>();
    }
}
