 
namespace IqTest_server.Models
{
    public class HardcodedQuestion
    {
        public int Id { get; set; }
        public required string TestTypeId { get; set; }
        public required string Type { get; set; } // "multiple-choice", "fill-in-gap", "memory-pair"
        public required string Text { get; set; }
        public required string Category { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public required string CorrectAnswer { get; set; }
        public int? MemorizationTime { get; set; }
        public List<List<string>> Pairs { get; set; } = new List<List<string>>();
        public List<List<int>> MissingIndices { get; set; } = new List<List<int>>();
        public int OrderIndex { get; set; }
    }
}