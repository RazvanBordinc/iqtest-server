using System.ComponentModel.DataAnnotations;

namespace IqTest_server.DTOs.Auth
{
    public class LoginRequestDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; }

        [Required]
        public string Password { get; set; }
    }
}