using System.ComponentModel.DataAnnotations;

namespace IqTest_server.DTOs.Auth
{
    public class RefreshTokenDto
    {
        [Required]
        public string RefreshToken { get; set; }
    }
}