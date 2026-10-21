namespace TRL_API.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        // "Admin" = Super Admin (everything), "User" = staff with the permissions in UserPermissions (Helpers/Permissions.cs)
        public string Role { get; set; } = null!;
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public DateTime? CreatedAt { get; set; }
        // NULL is treated as inactive: only IsActive = 1 may log in or refresh
        public bool? IsActive { get; set; }
    }
}
