using System;
using System.Collections.Generic;

namespace IqTest_server.DTOs.Profile
{
    public class UserProfileDto
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public int Age { get; set; }
        public string Country { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalTestsCompleted { get; set; }
        public List<TestStatDto> TestStats { get; set; }
        public List<RecentTestResultDto> RecentResults { get; set; }
    }

    public class TestStatDto
    {
        public string TestTypeId { get; set; }
        public string TestTypeName { get; set; }
        public int TestsCompleted { get; set; }
        public int BestScore { get; set; }
        public int AverageScore { get; set; }
        public DateTime LastAttempt { get; set; }
        public string BestTime { get; set; }
        public int? IQScore { get; set; }
    }

    public class RecentTestResultDto
    {
        public string TestTypeId { get; set; }
        public string TestTypeName { get; set; }
        public int Score { get; set; }
        public float Percentile { get; set; }
        public string Duration { get; set; }
        public DateTime CompletedAt { get; set; }
        public int? IQScore { get; set; }
    }
}