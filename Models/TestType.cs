using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class TestType
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public required string TypeId { get; set; } // e.g., "number-logic"

        [Required]
        [MaxLength(100)]
        public required string Title { get; set; }

        [MaxLength(500)]
        public required string Description { get; set; }

        [MaxLength(1000)]
        public required string LongDescription { get; set; }

        [MaxLength(50)]
        public required string Icon { get; set; }

        [MaxLength(255)]
        public required string Color { get; set; }

        public int QuestionsCount { get; set; }

        [MaxLength(50)]
        public required string TimeLimit { get; set; }

        [MaxLength(50)]
        public required string Difficulty { get; set; }

        // Navigation properties
        public virtual ICollection<Question> Questions { get; set; } = new List<Question>();
        public virtual ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
        public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; } = new List<LeaderboardEntry>();
    }
}