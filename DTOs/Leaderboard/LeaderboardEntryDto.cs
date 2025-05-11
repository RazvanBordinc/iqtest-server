using System;

namespace IqTest_server.DTOs.Leaderboard
{
    public class LeaderboardEntryDto
    {
        public int Rank { get; set; }
        public string Username { get; set; }
        public int Score { get; set; }
        public int TestsCompleted { get; set; }
        public float Percentile { get; set; }
        public string Country { get; set; } // Optional field
    }
}