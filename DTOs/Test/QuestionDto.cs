using System.Collections.Generic;

namespace IqTest_server.DTOs.Test
{
    public class QuestionDto
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public string Category { get; set; }
        public string Text { get; set; }
        public List<string> Options { get; set; }
        public int? MemorizationTime { get; set; }
        public List<List<string>> Pairs { get; set; }
        public List<List<int>> MissingIndices { get; set; }
        public string CorrectAnswer {  get; set; }
        public int Weight { get; set; } = 3; // Difficulty weight from 2-8, default 3
    }
}