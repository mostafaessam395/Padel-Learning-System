using System;

namespace TuioDemo
{
    /// <summary>
    /// Represents a single training content item stored in Data/padel_content.json.
    /// </summary>
    public class PadelContentItem
    {
        public string Id          { get; set; } = "";
        public string Level       { get; set; } = "";   // Beginner / Intermediate / Advanced
        public int    MarkerId    { get; set; } = 0;    // 3-8
        public string Module      { get; set; } = "";   // Padel Shots / Padel Rules / AI Vision Coach / etc.
        public string Activity    { get; set; } = "";
        public string Title       { get; set; } = "";
        public string Description { get; set; } = "";
        public string CoachTip    { get; set; } = "";
        public string Image       { get; set; } = "";
        public string Audio       { get; set; } = "";
        public string TargetZone  { get; set; } = "";
        public string Difficulty  { get; set; } = "";
        public bool   IsActive    { get; set; } = true;
    }
}
