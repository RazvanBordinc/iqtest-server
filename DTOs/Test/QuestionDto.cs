using System.Collections.Generic;

namespace IqTest_server.DTOs.Test
{
    public class QuestionDto
    {
        public int Id { get; set; }
        public required string Type { get; set; }
        public required string Category { get; set; }
        public required string Text { get; set; }
        public List<string>? Options { get; set; } // Optional - used for multiple choice questions
        public int? MemorizationTime { get; set; }
        public List<List<string>>? Pairs { get; set; } // Optional - used for memory pair questions
        public List<List<int>>? MissingIndices { get; set; } // Optional - used for fill-in-gap questions
        public required string CorrectAnswer {  get; set; }
        public int Weight { get; set; } = 3; // Difficulty weight from 2-8, default 3
    }
}