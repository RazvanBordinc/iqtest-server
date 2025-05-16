using System;

namespace IqTest_server.DTOs.Leaderboard
{
    public class LeaderboardEntryDto
    {
        public int UserId { get; set; }
        public int Rank { get; set; }
        public string Username { get; set; }
        public int Score { get; set; }
        public int TestsCompleted { get; set; }
        public float Percentile { get; set; }
        public string Country { get; set; } // Optional field
        public string AverageTime { get; set; }
        public string BestTime { get; set; }
        public int? IQScore { get; set; } // Only for comprehensive test

        public string PercentileDisplay => 
            Percentile <= 0.01f ? "0.01" : Percentile.ToString("F2");
    }
}