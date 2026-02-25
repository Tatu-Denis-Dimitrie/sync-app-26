using Microsoft.AspNetCore.Mvc;
using SyncApp26.Domain.Entities;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly IAuthenticationService _authenticationService;

        public AuthenticationController(IUserService userService, ITokenService tokenService, IAuthenticationService authenticationService)
        {
            _userService = userService;
            _tokenService = tokenService;
            _authenticationService = authenticationService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserRequestDTO request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.FirstName) ||
                    string.IsNullOrWhiteSpace(request.LastName) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "All fields are required." });
                }

                // Normalize email to lowercase for consistent checking
                var normalizedEmail = request.Email.ToLowerInvariant().Trim();

                var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "Email is already registered." });
                }

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    PersonalId = Guid.NewGuid().ToString(),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Email = normalizedEmail,
                    PasswordHash = await _authenticationService.HashPasswordAsync(request.Password),
                    CreatedAt = DateTime.UtcNow
                };

                await _userService.AddUserAsync(user);

                var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email);

                return Ok(new { message = "Registration successful. Please check your email to verify your account.", token });
            }
            catch (Exception ex)
            {
                // Log the exception here if you have a logger
                return StatusCode(500, new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserRequestDTO request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Email and password are required." });
                }

                var normalizedEmail = request.Email.ToLowerInvariant().Trim();
                var user = await _userService.GetUserByEmailAsync(normalizedEmail);
                if (user == null || user.PasswordHash == null || !await _authenticationService.VerifyPasswordAsync(request.Password, user.PasswordHash))
                {
                    return Unauthorized(new { message = "Invalid email or password." });
                }

                if (user.IsEmailVerified != true)
                {
                    return Unauthorized(new { message = "Email is not verified. Please check your email for verification instructions." });
                }

                var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email);

                return Ok(new { message = "Login successful.", token });
            }
            catch (Exception ex)
            {
                // Log the exception here if you have a logger
                return StatusCode(500, new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }
    }
}