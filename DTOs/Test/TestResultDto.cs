using System;

namespace IqTest_server.DTOs.Test
{
    public class TestResultDto
    {
        public int Id { get; set; }
        public int Score { get; set; }
        public float Percentile { get; set; }
        public string TestTypeId { get; set; }
        public string TestTitle { get; set; }
        public string Duration { get; set; }
        public int QuestionsCompleted { get; set; }
        public float Accuracy { get; set; }
        public DateTime CompletedAt { get; set; }
        public int? IQScore { get; set; } // Only for comprehensive test
    }
}