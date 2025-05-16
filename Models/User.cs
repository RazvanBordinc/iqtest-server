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
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Country { get; set; }

        public int? Age { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        // Refresh token for JWT auth
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime? RefreshTokenExpiryTime { get; set; }

        // Navigation properties
        public virtual ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
        public virtual ICollection<LeaderboardEntry> LeaderboardEntries { get; set; } = new List<LeaderboardEntry>();
        public virtual ICollection<Answer> Answers { get; set; } = new List<Answer>();
    }
}