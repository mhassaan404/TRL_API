namespace TRL_API.Helpers
{
    // What a staff user (Users.Role = "User") may open and do. The Super Admin (Role = "Admin") always has everything
    // and is never given permissions. Stored one row per permission in dbo.UserPermissions:
    //   "<page>:view" / "<page>:edit" (edit includes view) and "action:<name>" for risky actions inside pages.
    // The frontend has the same list with labels (src/permissions.js).
    public static class Permissions
    {
        public const string SuperAdminRole = "Admin";
        public const string StaffRole = "User";

        // Only these roles can log in (any other role in an old row is refused, as before)
        public static bool CanLogIn(string? role) => role == SuperAdminRole || role == StaffRole;

        public const string View = "view";
        public const string Edit = "edit";

        // Pages; true = the page has an Edit level (otherwise view only)
        public static readonly IReadOnlyDictionary<string, bool> Pages = new Dictionary<string, bool>
        {
            ["dashboard"] = false,
            ["tenants"] = true,
            ["properties"] = true,
            ["occupancy"] = false,
            ["rentCollection"] = true,
            ["rentHistory"] = true,
            ["leases"] = true,
            ["deposits"] = true,
            ["settlements"] = true,
            ["reminders"] = false,
            ["maintenance"] = true,
            ["arrearsReport"] = false,
            ["collectionsReport"] = false,
            ["statementReport"] = false,
            ["billingReport"] = false,
            ["rentRollReport"] = false,
            ["maintenanceCostReport"] = false,
            ["companyProfile"] = true,
            ["lateFeeSettings"] = true,
        };

        // Risky actions, ticked separately (the page's Edit is needed too)
        public const string ReversePayment = "action:reversePayment";
        public const string LateFee = "action:lateFee";          // charge or waive a late fee
        public const string Discount = "action:discount";        // a discount on a payment
        public const string DeleteCancel = "action:deleteCancel"; // delete tenants/buildings/floors/units, cancel invoices/leases/renewals
        public const string EndLease = "action:endLease";        // end a lease early (move-out)

        public static readonly IReadOnlyList<string> Actions = new[] { ReversePayment, LateFee, Discount, DeleteCancel, EndLease };

        public static string PageView(string page) => $"{page}:{View}";
        public static string PageEdit(string page) => $"{page}:{Edit}";

        // Every permission string that may be stored
        public static readonly IReadOnlySet<string> All = new HashSet<string>(
            Pages.SelectMany(p => p.Value ? new[] { PageView(p.Key), PageEdit(p.Key) } : new[] { PageView(p.Key) })
                .Concat(Actions));

        // The permissions to store for what was ticked: unknown ones dropped, "edit" kept without its "view"
        // (edit includes view), so each page is stored once
        public static List<string> Normalize(IEnumerable<string>? requested)
        {
            var set = new HashSet<string>((requested ?? Enumerable.Empty<string>()).Where(p => p != null && All.Contains(p)));
            foreach (var page in Pages.Keys)
                if (set.Contains(PageEdit(page))) set.Remove(PageView(page));
            return set.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        // Does a stored set grant this permission? "<page>:view" is granted by "<page>:edit" too
        public static bool Grants(IReadOnlySet<string> granted, string permission)
        {
            if (granted.Contains(permission)) return true;
            return permission.EndsWith(":" + View, StringComparison.Ordinal)
                && granted.Contains(permission[..^View.Length] + Edit);
        }
    }
}
