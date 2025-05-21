using System.ComponentModel.DataAnnotations;
using IqTest_server.Attributes;

namespace IqTest_server.DTOs.Auth
{
    public class RegisterRequestDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; }

        [Required]
        [StrongPassword]
        public string Password { get; set; }
        
        [StringLength(100)]
        public string Country { get; set; }
        
        [Range(1, 120)]
        public int? Age { get; set; }
    }
}