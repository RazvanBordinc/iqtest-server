using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace IqTest_server.Models
{
    public class Question
    {
        public int Id { get; set; }

        [Required]
        public int TestTypeId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } // e.g., "multiple-choice", "fill-in-gap", "memory-pair"

        [Required]
        [MaxLength(1000)]
        public string Text { get; set; }

        [MaxLength(200)]
        public string Category { get; set; } // e.g., "numerical", "verbal", "memory"

        // Serialized as JSON, will be deserialized when needed
        public string Options { get; set; }

        public string CorrectAnswer { get; set; }

        public int? MemorizationTime { get; set; } // For memory questions

        public string Pairs { get; set; } // JSON serialized pairs for memory questions

        public string MissingIndices { get; set; } // JSON serialized indices for memory questions

        public int OrderIndex { get; set; } // For ordering questions in a test

        // Navigation properties
        public virtual TestType TestType { get; set; }
        public virtual ICollection<Answer> Answers { get; set; }
    }
}