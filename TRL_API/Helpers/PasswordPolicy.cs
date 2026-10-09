using System.Text;

namespace TRL_API.Helpers
{
    // Rules for a new password (Change My Password). The frontend applies the same rules; these are the ones that count.
    // Returns an error message, or null when valid. Passwords are never trimmed, logged or returned.
    public static class PasswordPolicy
    {
        public const int MinLength = 8;
        public const int MaxLength = 64;
        // BCrypt only uses the first 72 bytes; longer passwords would be silently cut
        public const int MaxBytes = 72;

        public static string? Validate(string? current, string? newPassword, string? confirm, string username)
        {
            if (string.IsNullOrEmpty(current)) return "Please enter your current password.";
            if (string.IsNullOrEmpty(newPassword)) return "Please enter a new password.";
            if (newPassword != confirm) return "The new password and its confirmation don't match.";
            if (newPassword.Length < MinLength) return $"The new password must be at least {MinLength} characters.";
            if (newPassword.Length > MaxLength || Encoding.UTF8.GetByteCount(newPassword) > MaxBytes)
                return $"The new password can be at most {MaxLength} characters.";
            // The login page trims what is typed, so a password starting or ending with a space could never be used
            if (char.IsWhiteSpace(newPassword[0]) || char.IsWhiteSpace(newPassword[^1]))
                return "The new password can't start or end with a space.";
            if (newPassword.Any(char.IsControl)) return "The new password contains characters that aren't allowed.";
            if (!newPassword.Any(char.IsLetter) || !newPassword.Any(char.IsDigit))
                return "The new password must contain at least one letter and one number.";
            if (string.Equals(newPassword, username, StringComparison.OrdinalIgnoreCase))
                return "The new password can't be your username.";
            return null;
        }
    }
}
