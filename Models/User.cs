using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IqTest_server.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Username { get; set; }

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Required]
        [MaxLength(10)]
        public string Gender { get; set; }

        [Required]
        public int Age { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        // Refresh token for JWT auth
        public string RefreshToken { get; set; }
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Navigation properties
        public virtual ICollection<TestResult> TestResults { get; set; }
        public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; }
    }
}