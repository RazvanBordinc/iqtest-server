using System.ComponentModel.DataAnnotations;
using IqTest_server.Attributes;

namespace IqTest_server.DTOs.Auth
{
    public class CreateUserDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; }
        
        [Required]
        [StringLength(10)]
        public string Gender { get; set; }
        
        [Required]
        [Range(1, 120)]
        public int Age { get; set; }
        
        [Required]
        [StrongPassword]
        public string Password { get; set; }
    }
}