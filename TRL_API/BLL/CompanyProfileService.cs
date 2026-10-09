using System.Text.RegularExpressions;
using TRL_API.DAL;
using TRL_API.Data;
using TRL_API.Helpers;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class CompanyProfileService : ICompanyProfileService
    {
        // Same limits as the page and the database (CK_CompanyProfile_Logo, column sizes)
        public const int MaxLogoBytes = 204800; // 200 KB
        public const int MaxAddress = 300;
        public const int MaxWebsite = 150;
        public const int MaxFooterNote = 200;

        private readonly ICompanyProfileRepository _dal;
        private readonly IClientContext _client;
        public CompanyProfileService(ICompanyProfileRepository dal, IClientContext client)
        {
            _dal = dal;
            _client = client;
        }

        public async Task<CompanyProfile> GetAsync()
        {
            var r = (await _dal.GetAsync()).Rows[0];
            var logo = r["Logo"] as byte[];
            var logoType = r["LogoContentType"] as string;
            return new CompanyProfile
            {
                CompanyName = _client.Client.ClientName,
                Phone = r["Phone"] as string,
                Email = r["Email"] as string,
                Ntn = r["Ntn"] as string,
                Address = r["Address"] as string,
                Website = r["Website"] as string,
                FooterNote = r["FooterNote"] as string,
                LogoDataUrl = logo != null && logoType != null ? $"data:{logoType};base64,{Convert.ToBase64String(logo)}" : null,
                UpdatedBy = r["UpdatedBy"] as string,
                UpdatedAt = r["UpdatedAt"] == DBNull.Value ? null : Convert.ToDateTime(r["UpdatedAt"]),
            };
        }

        public async Task<ApiResponse> SaveAsync(SaveCompanyProfileRequest req, int userId)
        {
            var error = Normalize(req);
            if (error != null)
                return new ApiResponse { IsSuccess = false, ErrorMessage = error };

            var change = LogoChange.Keep;
            byte[]? bytes = null;
            string? type = null;
            if (req.RemoveLogo)
                change = LogoChange.Remove;
            else if (req.Logo != null)
            {
                (bytes, type, error) = ReadLogo(req.Logo);
                if (error != null)
                    return new ApiResponse { IsSuccess = false, ErrorMessage = error };
                change = LogoChange.Replace;
            }

            var result = await _dal.SaveAsync(req, change, bytes, type, userId);
            return result.IsSuccess
                ? new ApiResponse { IsSuccess = true, Message = "Company profile saved. It is used on printed receipts, invoices and reports." }
                : new ApiResponse { IsSuccess = false, ErrorMessage = "Company profile could not be saved." };
        }

        // Cleans the text fields in place. Returns an error message, or null when valid.
        public static string? Normalize(SaveCompanyProfileRequest? req)
        {
            if (req == null) return "Please enter the company details.";
            req.Phone = Clean(req.Phone);
            req.Email = Clean(req.Email)?.ToLowerInvariant();
            req.Ntn = Clean(req.Ntn);
            req.Address = Clean(req.Address);
            req.Website = Clean(req.Website);
            req.FooterNote = Clean(req.FooterNote);

            if (req.Phone == null) return "Please enter the company phone number.";
            var phone = TenantValidation.FormatPhone(req.Phone);
            if (phone == null) return "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.";
            req.Phone = phone;

            if (req.Email != null && (req.Email.Length > 100 || !TenantValidation.IsEmail(req.Email)))
                return "Enter a valid email address, e.g. info@example.com.";

            if (req.Ntn != null)
            {
                var ntn = TenantValidation.FormatCnicNtn(req.Ntn, TenantValidation.Company);
                if (ntn == null) return "Enter the NTN as 1234567-8 (8 digits) or a CNIC as 12345-1234567-1.";
                req.Ntn = ntn;
            }

            if (req.Address?.Length > MaxAddress) return $"Address can be at most {MaxAddress} characters.";

            if (req.Website != null && (req.Website.Length > MaxWebsite || !IsWebsite(req.Website)))
                return "Enter a valid website, e.g. www.example.com.";

            if (req.FooterNote?.Length > MaxFooterNote) return $"Footer note can be at most {MaxFooterNote} characters.";

            if (req.RemoveLogo && req.Logo != null) return "Choose either a new logo or Remove logo, not both.";
            return null;
        }

        // A web address, with or without http(s)://. Other schemes (javascript:, data:, ...) are refused.
        public static bool IsWebsite(string value)
        {
            if (Regex.IsMatch(value, @"\s")) return false;
            var hasScheme = value.Contains("://");
            if (hasScheme && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                          && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            if (!hasScheme && value.Contains(':')) return false;
            return Uri.TryCreate(hasScheme ? value : "https://" + value, UriKind.Absolute, out var uri)
                   && uri.Host.Contains('.') && !uri.Host.StartsWith('.') && !uri.Host.EndsWith('.');
        }

        // Logo sent as a data URL (data:image/png;base64,...) or plain base64. The type comes from the file's own
        // bytes, never from what the browser says: only PNG and JPEG, at most 200 KB.
        public static (byte[]? Bytes, string? Type, string? Error) ReadLogo(string input)
        {
            var base64 = input.Trim();
            var comma = base64.IndexOf(',');
            if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                if (comma < 0 || !base64[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                    return (null, null, "The logo file could not be read.");
                base64 = base64[(comma + 1)..];
            }

            // Reject before decoding anything far too large (base64 is 4/3 of the file size)
            if (base64.Length > (MaxLogoBytes + 2) / 3 * 4 + 4)
                return (null, null, "The logo must be 200 KB or smaller.");

            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); }
            catch (FormatException) { return (null, null, "The logo file could not be read."); }

            if (bytes.Length == 0) return (null, null, "The logo file is empty.");
            if (bytes.Length > MaxLogoBytes) return (null, null, "The logo must be 200 KB or smaller.");

            var type = ImageType(bytes);
            if (type == null) return (null, null, "The logo must be a PNG or JPG image.");
            return (bytes, type, null);
        }

        private static string? ImageType(byte[] b)
        {
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
                return "image/png";
            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return "image/jpeg";
            return null;
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
