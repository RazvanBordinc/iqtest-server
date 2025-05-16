namespace IqTest_server.DTOs.Auth
{
    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string Token { get; set; } = null!;
        public string? Country { get; set; }
        public int? Age { get; set; }
    }
}