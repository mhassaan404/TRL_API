using System.Net.Mail;
using System.Text.RegularExpressions;
using TRL_API.Models;

namespace TRL_API.Helpers
{
    // Cleans and checks tenant form data before it is saved. Returns an error message, or null when valid.
    // The frontend applies the same rules; these are the ones that count.
    public static class TenantValidation
    {
        public const string Company = "Company";
        public const string Individual = "Individual";

        public static string? Normalize(Tenants t)
        {
            t.Name = Clean(t.Name);
            t.TenantType = Clean(t.TenantType);
            t.ContactPerson = Clean(t.ContactPerson);
            t.Email = Clean(t.Email)?.ToLowerInvariant();
            t.Contact = Clean(t.Contact);
            t.CnicNtn = Clean(t.CnicNtn);
            t.Address = Clean(t.Address);
            t.Notes = Clean(t.Notes);

            if (t.Name == null) return "Tenant / Company name is required.";
            if (t.Name.Length > 150) return "Tenant / Company name can be at most 150 characters.";

            // Match the two allowed values regardless of case, store them in the standard spelling
            if (string.Equals(t.TenantType, Company, StringComparison.OrdinalIgnoreCase)) t.TenantType = Company;
            else if (string.Equals(t.TenantType, Individual, StringComparison.OrdinalIgnoreCase)) t.TenantType = Individual;
            else return "Tenant type must be Company or Individual.";

            if (t.ContactPerson?.Length > 150) return "Contact person can be at most 150 characters.";
            if (t.Address?.Length > 500) return "Address can be at most 500 characters.";

            if (t.Email != null && (t.Email.Length > 100 || !IsEmail(t.Email)))
                return "Enter a valid email address, e.g. name@example.com.";

            if (t.Contact != null)
            {
                var phone = FormatPhone(t.Contact);
                if (phone == null) return "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.";
                t.Contact = phone;
            }

            if (t.CnicNtn != null)
            {
                var id = FormatCnicNtn(t.CnicNtn, t.TenantType);
                if (id == null)
                    return t.TenantType == Individual
                        ? "Enter the CNIC as 12345-1234567-1 (13 digits)."
                        : "Enter the NTN as 1234567-8 (8 digits) or a CNIC as 12345-1234567-1.";
                t.CnicNtn = id;
            }

            return null;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        public static bool IsEmail(string email)
        {
            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")) return false;
            try { return new MailAddress(email).Address == email; }
            catch (FormatException) { return false; }
        }

        // Pakistani numbers, stored in one format:
        //   mobile   03XX-XXXXXXX     (also accepts +92 3XX..., 0092 3XX..., 92 3XX...)
        //   landline 0XX-XXXXXXXX     (area code + number, 10–11 digits in total)
        public static string? FormatPhone(string input)
        {
            if (Regex.IsMatch(input, @"[^\d\s\-+()]")) return null;
            var digits = Regex.Replace(input, @"\D", "");
            if (digits.StartsWith("0092")) digits = "0" + digits[4..];
            else if (digits.StartsWith("92")) digits = "0" + digits[2..];

            if (Regex.IsMatch(digits, @"^03\d{9}$")) return $"{digits[..4]}-{digits[4..]}";
            if (Regex.IsMatch(digits, @"^0[124-9]\d{8,9}$")) return $"{digits[..3]}-{digits[3..]}";
            return null;
        }

        // CNIC 12345-1234567-1 (13 digits); NTN 1234567-8 (8 digits, companies only)
        public static string? FormatCnicNtn(string input, string? tenantType)
        {
            if (Regex.IsMatch(input, @"[^\d\s\-]")) return null;
            var digits = Regex.Replace(input, @"\D", "");
            if (digits.Length == 13) return $"{digits[..5]}-{digits[5..12]}-{digits[12..]}";
            if (digits.Length == 8 && tenantType == Company) return $"{digits[..7]}-{digits[7..]}";
            return null;
        }
    }
}
