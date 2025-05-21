using System.ComponentModel.DataAnnotations;

namespace IqTest_server.DTOs.Profile
{
    public class UpdateAgeDto
    {
        [Required]
        [Range(1, 120, ErrorMessage = "Age must be between 1 and 120")]
        public int Age { get; set; }
    }
}