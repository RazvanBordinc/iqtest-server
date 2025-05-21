using System.ComponentModel.DataAnnotations;

namespace IqTest_server.DTOs.Auth
{
    public class CheckUsernameDto
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; }
    }
}