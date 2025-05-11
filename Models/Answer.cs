using System;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class Answer
    {
        public int Id { get; set; }

        [Required]
        public int TestResultId { get; set; }

        [Required]
        public int QuestionId { get; set; }

        [Required]
        public string UserAnswer { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } // multiple-choice, fill-in-gap, memory-pair

        public bool IsCorrect { get; set; }

        // Navigation properties
        public virtual TestResult TestResult { get; set; }
        public virtual Question Question { get; set; }
    }
}