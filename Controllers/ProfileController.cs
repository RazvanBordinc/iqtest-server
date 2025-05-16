using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using IqTest_server.DTOs.Profile;
using IqTest_server.Services;

namespace IqTest_server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ProfileController : BaseController
    {
        private readonly new ILogger<ProfileController> _logger;
        private readonly ProfileService _profileService;

        public ProfileController(
            ILogger<ProfileController> logger,
            ProfileService profileService) : base(logger)
        {
            _logger = logger;
            _profileService = profileService;
        }

        [HttpGet]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetUserId();
                var profile = await _profileService.GetUserProfileAsync(userId);

                if (profile == null)
                {
                    return NotFound("Profile not found");
                }

                return Ok(profile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving profile for user: {UserId}", GetUserId());
                return StatusCode(500, "An error occurred while retrieving your profile");
            }
        }

        [HttpPut("country")]
        public async Task<IActionResult> UpdateCountry([FromBody] UpdateCountryDto request)
        {
            try
            {
                var userId = GetUserId();
                var success = await _profileService.UpdateUserCountryAsync(userId, request.Country);

                if (!success)
                {
                    return BadRequest("Failed to update country");
                }

                return Ok(new { message = "Country updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating country for user: {UserId}", GetUserId());
                return StatusCode(500, "An error occurred while updating your country");
            }
        }

        [HttpPut("age")]
        public async Task<IActionResult> UpdateAge([FromBody] UpdateAgeDto request)
        {
            try
            {
                var userId = GetUserId();
                var success = await _profileService.UpdateUserAgeAsync(userId, request.Age);

                if (!success)
                {
                    return BadRequest("Failed to update age");
                }

                return Ok(new { message = "Age updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating age for user: {UserId}", GetUserId());
                return StatusCode(500, "An error occurred while updating your age");
            }
        }
    }
}