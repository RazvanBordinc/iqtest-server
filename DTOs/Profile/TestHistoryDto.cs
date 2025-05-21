using System;

namespace IqTest_server.DTOs.Profile
{
    public class TestHistoryDto
    {
        public int Id { get; set; }
        public string TestTypeId { get; set; }
        public string TestTitle { get; set; }
        public int Score { get; set; }
        public float Percentile { get; set; }
        public string BetterThanPercentage { get; set; } // e.g., "87.5%"
        public int? IQScore { get; set; }
        public float Accuracy { get; set; }
        public string Duration { get; set; }
        public DateTime CompletedAt { get; set; }
        public int QuestionsCompleted { get; set; }
        public string TimeAgo { get; set; } // e.g., "2 days ago"
    }
}