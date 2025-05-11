 
namespace IqTest_server.Models
{
    public class HardcodedQuestion
    {
        public int Id { get; set; }
        public string TestTypeId { get; set; }
        public string Type { get; set; } // "multiple-choice", "fill-in-gap", "memory-pair"
        public string Text { get; set; }
        public string Category { get; set; }
        public List<string> Options { get; set; }
        public string CorrectAnswer { get; set; }
        public int? MemorizationTime { get; set; }
        public List<List<string>> Pairs { get; set; }
        public List<List<int>> MissingIndices { get; set; }
        public int OrderIndex { get; set; }
    }
}