using System.Collections.Generic;

namespace IqTest_server.DTOs.Leaderboard
{
    public class UserRankingDto
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public int GlobalRank { get; set; }
        public float GlobalPercentile { get; set; }
        public int IqScore { get; set; }
        public Dictionary<string, TestTypeRankingDto> TestResults { get; set; }
    }

    public class TestTypeRankingDto
    {
        public int Rank { get; set; }
        public int Score { get; set; }
        public float Percentile { get; set; }
        public int TotalTests { get; set; }
    }
}