using System;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class LeaderboardEntry
    {
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int TestTypeId { get; set; }

        public int Rank { get; set; }

        public int Score { get; set; }

        public float Percentile { get; set; }

        public int TestsCompleted { get; set; }
        
        public string? AverageTime { get; set; } // Format: "2m 30s"
        
        public string? BestTime { get; set; } // Format: "2m 30s"
        
        public int? IQScore { get; set; } // Only for comprehensive test
        
        [MaxLength(100)]
        public string Country { get; set; } = "United States"; // Default value

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual TestType TestType { get; set; } = null!;
    }
}