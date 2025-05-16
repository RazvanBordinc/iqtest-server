namespace IqTest_server.DTOs.Test
{
    public class TestTypeDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public TestStatsDto Stats { get; set; } = new TestStatsDto();
    }
}