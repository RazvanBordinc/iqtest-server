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
    }
}