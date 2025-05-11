using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class TestResult
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int TestTypeId { get; set; }

        public int Score { get; set; }

        public float Percentile { get; set; }

        public string Duration { get; set; }

        public int QuestionsCompleted { get; set; }

        public float Accuracy { get; set; }

        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; }
        public virtual TestType TestType { get; set; }
        public virtual ICollection<Answer> Answers { get; set; }
    }
}