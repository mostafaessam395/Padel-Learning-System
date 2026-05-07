using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TuioDemo
{
    /// <summary>
    /// Handles all JSON read/write operations for padel training content.
    /// Storage: Data/padel_content.json  (created automatically if missing).
    /// </summary>
    public class ContentService
    {
        private static readonly string DataFolder =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

        private static readonly string JsonPath =
            Path.Combine(DataFolder, "padel_content.json");

        // ── Public API ────────────────────────────────────────────────────

        public List<PadelContentItem> LoadAll()
        {
            try
            {
                EnsureFileExists();
                string raw = File.ReadAllText(JsonPath);
                var list = JsonConvert.DeserializeObject<List<PadelContentItem>>(raw);
                return list ?? new List<PadelContentItem>();
            }
            catch
            {
                return new List<PadelContentItem>();
            }
        }

        public void SaveAll(List<PadelContentItem> items)
        {
            try
            {
                EnsureDataFolder();
                string json = JsonConvert.SerializeObject(items, Formatting.Indented);
                File.WriteAllText(JsonPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ContentService] SaveAll failed: {ex.Message}");
            }
        }

        /// <summary>Returns active items matching level + markerId.</summary>
        public List<PadelContentItem> GetByContext(string level, int markerId)
        {
            return LoadAll()
                .Where(i => i.IsActive
                    && string.Equals(i.Level, level, StringComparison.OrdinalIgnoreCase)
                    && i.MarkerId == markerId)
                .ToList();
        }

        /// <summary>Returns active items matching level + activity.</summary>
        public List<PadelContentItem> GetByActivity(string level, string activity)
        {
            return LoadAll()
                .Where(i => i.IsActive
                    && string.Equals(i.Level, level, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(i.Activity, activity, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>Returns active items matching level + module + activity + targetZone (AI Vision context).</summary>
        public PadelContentItem GetAIVisionContent(string level, string module, string activity, string targetZone)
        {
            return LoadAll().FirstOrDefault(i =>
                i.IsActive
                && string.Equals(i.Level,      level,      StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Module,     module,     StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.Activity,   activity,   StringComparison.OrdinalIgnoreCase)
                && string.Equals(i.TargetZone, targetZone, StringComparison.OrdinalIgnoreCase));
        }

        public void AddItem(PadelContentItem item)
        {
            var list = LoadAll();
            // Prevent duplicate IDs
            if (list.Any(i => string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
                item.Id = item.Id + "_" + DateTime.Now.Ticks;
            list.Add(item);
            SaveAll(list);
        }

        public void UpdateItem(PadelContentItem updated)
        {
            var list = LoadAll();
            int idx = list.FindIndex(i => string.Equals(i.Id, updated.Id, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                list[idx] = updated;
            else
                list.Add(updated);
            SaveAll(list);
        }

        /// <summary>Soft-delete: sets IsActive = false.</summary>
        public void DeactivateItem(string id)
        {
            var list = LoadAll();
            var item = list.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                item.IsActive = false;
                SaveAll(list);
            }
        }

        /// <summary>Hard-delete: permanently removes the item.</summary>
        public void DeleteItem(string id)
        {
            var list = LoadAll();
            list.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            SaveAll(list);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void EnsureDataFolder()
        {
            if (!Directory.Exists(DataFolder))
                Directory.CreateDirectory(DataFolder);
        }

        private void EnsureFileExists()
        {
            EnsureDataFolder();
            if (!File.Exists(JsonPath))
                SaveAll(DefaultContent());
        }

        /// <summary>Seed data used when padel_content.json does not exist yet.</summary>
        private List<PadelContentItem> DefaultContent()
        {
            return new List<PadelContentItem>
            {
                // ── Beginner – Marker 3 – Padel Shots ──────────────────────
                new PadelContentItem { Id="beg_shot_001", Level="Beginner", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="SERVE",
                    Description="The serve starts every point. Toss the ball and hit it below waist height.",
                    CoachTip="Keep your wrist relaxed and aim for the service box diagonally opposite.",
                    Image="serve.png", TargetZone="Back Zone", Difficulty="Beginner", IsActive=true },

                new PadelContentItem { Id="beg_shot_002", Level="Beginner", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="FOREHAND",
                    Description="A forehand is hit on your dominant side with a smooth swing.",
                    CoachTip="Step into the ball and follow through toward your target.",
                    Image="forehand.png", TargetZone="Center Zone", Difficulty="Beginner", IsActive=true },

                new PadelContentItem { Id="beg_shot_003", Level="Beginner", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="BACKHAND",
                    Description="A backhand is hit on your non-dominant side.",
                    CoachTip="Rotate your shoulders early and keep the racket face open.",
                    Image="backhand.png", TargetZone="Center Zone", Difficulty="Beginner", IsActive=true },

                // ── Beginner – Marker 4 – Padel Rules ──────────────────────
                new PadelContentItem { Id="beg_rule_001", Level="Beginner", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="SERVE RULE",
                    Description="The serve must land in the diagonal service box. One fault is allowed.",
                    CoachTip="Aim for the center of the service box to reduce fault risk.",
                    Image="serve_rule.png", TargetZone="Service Box", Difficulty="Beginner", IsActive=true },

                new PadelContentItem { Id="beg_rule_002", Level="Beginner", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="SCORING",
                    Description="Padel uses tennis scoring: 15, 30, 40, Game. Sets are first to 6 games.",
                    CoachTip="Remember: deuce means both players are at 40. Win two consecutive points to win the game.",
                    Image="scoring.png", TargetZone="Full Court", Difficulty="Beginner", IsActive=true },

                // ── Intermediate – Marker 3 – Padel Shots ──────────────────
                new PadelContentItem { Id="int_shot_001", Level="Intermediate", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="VOLLEY",
                    Description="A volley is hit before the ball bounces. Used at the net to finish points.",
                    CoachTip="Keep your elbow up and punch the ball — don't swing.",
                    Image="volley.png", TargetZone="Net Zone", Difficulty="Intermediate", IsActive=true },

                new PadelContentItem { Id="int_shot_002", Level="Intermediate", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="DEJADA",
                    Description="A soft drop shot that lands close to the net after bouncing off the glass.",
                    CoachTip="Use a short backswing and disguise the shot until the last moment.",
                    Image="dejada.png", TargetZone="Net Zone", Difficulty="Intermediate", IsActive=true },

                // ── Intermediate – Marker 4 – Padel Rules ──────────────────
                new PadelContentItem { Id="int_rule_001", Level="Intermediate", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="DOUBLE BOUNCE",
                    Description="The ball may bounce off the back glass and be played. It must bounce on the floor first.",
                    CoachTip="Let the ball come off the glass and position yourself early.",
                    Image="double_bounce.png", TargetZone="Back Zone", Difficulty="Intermediate", IsActive=true },

                new PadelContentItem { Id="int_rule_002", Level="Intermediate", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="WALL USAGE",
                    Description="Players can use the side and back walls to keep the ball in play.",
                    CoachTip="Anticipate the rebound angle — the ball comes off the glass at the same angle it hits.",
                    Image="wall_usage.png", TargetZone="Back Zone", Difficulty="Intermediate", IsActive=true },

                // ── Advanced – Marker 3 – Padel Shots ──────────────────────
                new PadelContentItem { Id="adv_shot_001", Level="Advanced", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="BANDEJA",
                    Description="Controlled overhead shot with spin, used to keep the ball low after bounce.",
                    CoachTip="Use bandeja to keep the ball low after bounce and maintain net position.",
                    Image="bandeja.png", TargetZone="Center Zone", Difficulty="Advanced", IsActive=true },

                new PadelContentItem { Id="adv_shot_002", Level="Advanced", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="VIBORA",
                    Description="An aggressive overhead with heavy topspin that kicks high off the glass.",
                    CoachTip="Snap your wrist at contact to generate maximum spin.",
                    Image="vibora.png", TargetZone="Back Zone", Difficulty="Advanced", IsActive=true },

                new PadelContentItem { Id="adv_shot_003", Level="Advanced", MarkerId=3, Module="Padel Shots",
                    Activity="Shot Training", Title="SMASH",
                    Description="A powerful overhead aimed to end the point outright.",
                    CoachTip="Hit through the ball and aim for the corners to make it unreturnable.",
                    Image="smash.png", TargetZone="Back Zone", Difficulty="Advanced", IsActive=true },

                // ── Advanced – Marker 4 – Padel Rules ──────────────────────
                new PadelContentItem { Id="adv_rule_001", Level="Advanced", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="GOLDEN POINT",
                    Description="Deciding point played at deuce in professional padel.",
                    CoachTip="The receiving team chooses which side to receive from. Stay calm and play your best shot.",
                    Image="golden_point.png", TargetZone="Full Court", Difficulty="Advanced", IsActive=true },

                new PadelContentItem { Id="adv_rule_002", Level="Advanced", MarkerId=4, Module="Padel Rules",
                    Activity="Rules Training", Title="LET RULE",
                    Description="A let is called when the serve clips the net and lands in the correct box.",
                    CoachTip="On a let, the serve is replayed with no penalty.",
                    Image="let_rule.png", TargetZone="Service Box", Difficulty="Advanced", IsActive=true },

                // ── Advanced – Marker 5 – AI Vision Coach ──────────────────
                new PadelContentItem { Id="adv_ai_001", Level="Advanced", MarkerId=5, Module="AI Vision Coach",
                    Activity="Net Control", Title="NET CONTROL",
                    Description="Dominate the net to put pressure on opponents and finish points.",
                    CoachTip="Move forward together with your partner and volley aggressively.",
                    Image="net_control.png", TargetZone="Net Zone", Difficulty="Advanced", IsActive=true },

                new PadelContentItem { Id="adv_ai_002", Level="Advanced", MarkerId=5, Module="AI Vision Coach",
                    Activity="Smash Position", Title="SMASH POSITION",
                    Description="Position yourself behind the ball to execute a powerful overhead smash.",
                    CoachTip="Turn sideways, raise your non-racket arm to track the ball, and explode upward.",
                    Image="smash_position.png", TargetZone="Center Zone", Difficulty="Advanced", IsActive=true },

                new PadelContentItem { Id="int_ai_001", Level="Intermediate", MarkerId=5, Module="AI Vision Coach",
                    Activity="Forehand Position", Title="FOREHAND POSITION",
                    Description="Stand sideways with your weight on your back foot before hitting a forehand.",
                    CoachTip="Rotate your hips and shoulders together for maximum power.",
                    Image="forehand_position.png", TargetZone="Center Zone", Difficulty="Intermediate", IsActive=true },

                new PadelContentItem { Id="beg_ai_001", Level="Beginner", MarkerId=5, Module="AI Vision Coach",
                    Activity="Court Positioning", Title="COURT POSITIONING",
                    Description="Stay in the center of your half of the court to cover all angles.",
                    CoachTip="After each shot, return to the center position quickly.",
                    Image="court_position.png", TargetZone="Center Zone", Difficulty="Beginner", IsActive=true },
            };
        }
    }
}
