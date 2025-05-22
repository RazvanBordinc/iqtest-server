using System.ComponentModel.DataAnnotations;

namespace IqTest_server.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public required string Username { get; set; }

        [Required]
        public required string Password { get; set; }
    }
}