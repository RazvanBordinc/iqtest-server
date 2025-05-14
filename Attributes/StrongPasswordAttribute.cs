using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace IqTest_server.Attributes
{
    public class StrongPasswordAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return new ValidationResult("Password is required");
            }

            var password = value.ToString();

            // Check minimum length
            if (password.Length < 8)
            {
                return new ValidationResult("Password must be at least 8 characters long");
            }

            // Check for at least one uppercase letter
            if (!Regex.IsMatch(password, @"[A-Z]"))
            {
                return new ValidationResult("Password must contain at least one uppercase letter");
            }

            // Check for at least one lowercase letter
            if (!Regex.IsMatch(password, @"[a-z]"))
            {
                return new ValidationResult("Password must contain at least one lowercase letter");
            }

            // Check for at least one digit
            if (!Regex.IsMatch(password, @"\d"))
            {
                return new ValidationResult("Password must contain at least one number");
            }

            // Check for at least one special character
            if (!Regex.IsMatch(password, @"[!@#$%^&*(),.?""':{}|<>]"))
            {
                return new ValidationResult("Password must contain at least one special character");
            }

            // Check for no spaces
            if (password.Contains(" "))
            {
                return new ValidationResult("Password must not contain spaces");
            }

            // Check for common weak passwords
            var commonPasswords = new[] { "password", "12345678", "qwerty", "abc123", "password123" };
            if (commonPasswords.Any(common => password.ToLower().Contains(common)))
            {
                return new ValidationResult("Password is too common. Please choose a stronger password");
            }

            return ValidationResult.Success;
        }
    }
}