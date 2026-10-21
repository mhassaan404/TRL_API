namespace TRL_API.Models
{
    // One permission of a staff user (Helpers/Permissions.cs), e.g. "tenants:edit" or "action:reversePayment"
    public class UserPermission
    {
        public int UserId { get; set; }
        public string Permission { get; set; } = null!;
    }
}
