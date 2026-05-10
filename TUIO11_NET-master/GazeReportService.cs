using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TuioDemo
{
    /// <summary>
    /// Handles save/load/query of per-user gaze session history.
    /// Each user's history is stored in Data/gaze_reports/{userId}_history.json.
    /// A rolling window of the last 20 sessions is maintained to keep the JSON small.
    /// </summary>
    public static class GazeReportService
    {
        private const int MAX_SESSIONS_PER_USER = 20;

        private static readonly string ReportsDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "gaze_reports");

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Append a new session report and persist to disk.
        /// Trims the history to the most recent MAX_SESSIONS_PER_USER entries.
        /// </summary>
        public static void Save(GazeSessionReport report)
        {
            if (report == null || string.IsNullOrEmpty(report.UserId)) return;

            try
            {
                Directory.CreateDirectory(ReportsDir);
                string filePath = GetFilePath(report.UserId);

                var history = LoadHistory(report.UserId);
                history.Add(report);

                // Rolling window: keep only the most recent sessions
                if (history.Count > MAX_SESSIONS_PER_USER)
                    history = history.Skip(history.Count - MAX_SESSIONS_PER_USER).ToList();

                string json = JsonConvert.SerializeObject(history, Formatting.Indented);
                File.WriteAllText(filePath, json);

                Console.WriteLine($"[GazeReportService] Saved session {report.SessionId} for user {report.UserId} " +
                                  $"({history.Count} total sessions)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GazeReportService] Save error: {ex.Message}");
            }
        }

        /// <summary>
        /// Load the full session history for a user.
        /// </summary>
        public static List<GazeSessionReport> LoadHistory(string userId)
        {
            try
            {
                string filePath = GetFilePath(userId);
                if (!File.Exists(filePath)) return new List<GazeSessionReport>();

                string json = File.ReadAllText(filePath).Trim();
                if (string.IsNullOrEmpty(json) || json == "[]")
                    return new List<GazeSessionReport>();

                return JsonConvert.DeserializeObject<List<GazeSessionReport>>(json)
                       ?? new List<GazeSessionReport>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GazeReportService] Load error: {ex.Message}");
                return new List<GazeSessionReport>();
            }
        }

        /// <summary>
        /// Get the most recent session report for a user, or null if none exists.
        /// </summary>
        public static GazeSessionReport GetLatest(string userId)
        {
            var history = LoadHistory(userId);
            return history.Count > 0 ? history[history.Count - 1] : null;
        }

        /// <summary>
        /// Get the N most recent session reports for a user.
        /// </summary>
        public static List<GazeSessionReport> GetRecent(string userId, int count = 5)
        {
            var history = LoadHistory(userId);
            return history.Skip(Math.Max(0, history.Count - count)).ToList();
        }

        /// <summary>
        /// Get total number of sessions recorded for a user.
        /// </summary>
        public static int GetSessionCount(string userId)
        {
            return LoadHistory(userId).Count;
        }

        /// <summary>
        /// Compute the trend direction for each category over the last N sessions.
        /// Returns +1 (improving), 0 (stable), -1 (declining) per category.
        /// </summary>
        public static Dictionary<string, int> GetTrends(string userId, int windowSize = 5)
        {
            var trends = new Dictionary<string, int>();
            var categories = new[] { "Strokes", "Rules", "Practice", "Quiz", "Spelling", "Competition" };

            foreach (var cat in categories)
                trends[cat] = 0;

            var recent = GetRecent(userId, windowSize);
            if (recent.Count < 2) return trends;

            foreach (var cat in categories)
            {
                // Compare first half average vs second half average
                int mid = recent.Count / 2;
                double firstHalf = 0, secondHalf = 0;
                int firstCount = 0, secondCount = 0;

                for (int i = 0; i < recent.Count; i++)
                {
                    if (recent[i].SessionScores != null && recent[i].SessionScores.ContainsKey(cat))
                    {
                        if (i < mid)
                        { firstHalf += recent[i].SessionScores[cat]; firstCount++; }
                        else
                        { secondHalf += recent[i].SessionScores[cat]; secondCount++; }
                    }
                }

                if (firstCount > 0 && secondCount > 0)
                {
                    double diff = (secondHalf / secondCount) - (firstHalf / firstCount);
                    if (diff > 8) trends[cat] = 1;       // improving
                    else if (diff < -8) trends[cat] = -1; // declining
                }
            }

            return trends;
        }

        // ── Private helpers ──────────────────────────────────────────────

        private static string GetFilePath(string userId)
        {
            // Sanitize userId for filesystem safety
            string safe = userId.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return Path.Combine(ReportsDir, safe + "_history.json");
        }
    }
}
