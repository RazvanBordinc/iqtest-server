using System.ComponentModel.DataAnnotations;
using IqTest_server.Attributes;

namespace IqTest_server.DTOs.Auth
{
    public class CreateUserDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public required string Username { get; set; }
        
        [StringLength(100)]
        public required string Country { get; set; }
        
        [Range(1, 120)]
        public int? Age { get; set; }
        
        [Required]
        [StrongPassword]
        public required string Password { get; set; }
    }
}