using TRL_API.Helpers;
using TRL_API.Services;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace TRL_API.Tests
{
    // Change My Password rules (PasswordPolicy) and the separate lockout counter (LoginThrottle scope)
    public class PasswordPolicyTests
    {
        private static string? V(string? current, string? next, string? confirm, string username = "admin") =>
            PasswordPolicy.Validate(current, next, confirm, username);

        [Theory]
        [InlineData("NewPass123")]
        [InlineData("long pass phrase 2026")]   // spaces inside are fine
        [InlineData("Pässwört9ü")]              // non-ASCII letters
        [InlineData("abcdefg1")]                 // exactly 8
        public void Valid(string password) => Assert.Null(V("old", password, password));

        [Theory]
        [InlineData(null, "NewPass123", "NewPass123", "Please enter your current password.")]
        [InlineData("", "NewPass123", "NewPass123", "Please enter your current password.")]
        [InlineData("old", "", "", "Please enter a new password.")]
        [InlineData("old", "NewPass123", "NewPass124", "The new password and its confirmation don't match.")]
        [InlineData("old", "Abc1234", "Abc1234", "The new password must be at least 8 characters.")]
        [InlineData("old", " NewPass123", " NewPass123", "The new password can't start or end with a space.")]
        [InlineData("old", "NewPass123 ", "NewPass123 ", "The new password can't start or end with a space.")]
        [InlineData("old", "onlyletters", "onlyletters", "The new password must contain at least one letter and one number.")]
        [InlineData("old", "12345678", "12345678", "The new password must contain at least one letter and one number.")]
        [InlineData("old", "Pass\t1234", "Pass\t1234", "The new password contains characters that aren't allowed.")]
        public void Invalid(string? current, string? next, string? confirm, string message) => Assert.Equal(message, V(current, next, confirm));

        [Fact]
        public void Length_limits_in_characters_and_bcrypt_bytes()
        {
            var max = "a1" + new string('b', 62);                       // 64 chars
            Assert.Null(V("old", max, max));
            var tooLong = max + "c";
            Assert.Equal("The new password can be at most 64 characters.", V("old", tooLong, tooLong));
            var wide = "1" + new string('ü', 40);                        // 41 chars but 81 UTF-8 bytes (> 72)
            Assert.Equal("The new password can be at most 64 characters.", V("old", wide, wide));
        }

        [Fact]
        public void Not_the_username()
        {
            Assert.Equal("The new password can't be your username.", V("old", "Admin2026", "Admin2026", "admin2026"));
        }

        [Fact]
        public void Password_lockout_is_separate_from_login_lockout()
        {
            var t = new LoginThrottle(new MemoryCache(new MemoryCacheOptions()));
            for (var i = 0; i < LoginThrottle.MaxFailures; i++) t.RecordFailure("TRL", "admin", "1.2.3.4", "password");
            Assert.NotNull(t.LockedMinutesLeft("TRL", "admin", "1.2.3.4", "password"));
            Assert.Null(t.LockedMinutesLeft("TRL", "admin", "1.2.3.4"));          // login still allowed
            t.Reset("TRL", "admin", "1.2.3.4", "password");
            Assert.Null(t.LockedMinutesLeft("TRL", "admin", "1.2.3.4", "password"));
            for (var i = 0; i < LoginThrottle.MaxFailures; i++) t.RecordFailure("TRL", "admin", "1.2.3.4");
            Assert.NotNull(t.LockedMinutesLeft("TRL", "admin", "1.2.3.4"));       // default scope = login, as before
            Assert.Null(t.LockedMinutesLeft("TRL", "admin", "1.2.3.4", "password"));
        }
    }
}
