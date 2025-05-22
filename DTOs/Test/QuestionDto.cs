using System.Collections.Generic;

namespace IqTest_server.DTOs.Test
{
    public class QuestionDto
    {
        public int Id { get; set; }
        public required string Type { get; set; }
        public required string Category { get; set; }
        public required string Text { get; set; }
        public required List<string> Options { get; set; }
        public int? MemorizationTime { get; set; }
        public required List<List<string>> Pairs { get; set; }
        public required List<List<int>> MissingIndices { get; set; }
        public required string CorrectAnswer {  get; set; }
        public int Weight { get; set; } = 3; // Difficulty weight from 2-8, default 3
    }
}