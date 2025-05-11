using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class TestType
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TypeId { get; set; } // e.g., "number-logic"

        [Required]
        [MaxLength(100)]
        public string Title { get; set; }

        [MaxLength(500)]
        public string Description { get; set; }

        [MaxLength(1000)]
        public string LongDescription { get; set; }

        [MaxLength(50)]
        public string Icon { get; set; }

        [MaxLength(255)]
        public string Color { get; set; }

        public int QuestionsCount { get; set; }

        [MaxLength(50)]
        public string TimeLimit { get; set; }

        [MaxLength(50)]
        public string Difficulty { get; set; }

        // Navigation properties
        public virtual ICollection<Question> Questions { get; set; }
        public virtual ICollection<TestResult> TestResults { get; set; }
        public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; }
    }
}