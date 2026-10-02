namespace TRL_API.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Role { get; set; } = null!;
        // NULL is treated as inactive: only IsActive = 1 may log in or refresh
        public bool? IsActive { get; set; }
    }
}
