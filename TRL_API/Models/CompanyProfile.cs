namespace TRL_API.Models
{
    // The client's own details for printed documents (one stored row, edited on the Company Profile page).
    // CompanyName comes from the catalog (Clients.ClientName) and can't be changed here.
    public class CompanyProfile
    {
        public string CompanyName { get; set; } = "";
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Ntn { get; set; }
        public string? Address { get; set; }
        public string? Website { get; set; }
        public string? FooterNote { get; set; }
        public string? LogoDataUrl { get; set; } // data:image/png;base64,... or null
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SaveCompanyProfileRequest
    {
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Ntn { get; set; }
        public string? Address { get; set; }
        public string? Website { get; set; }
        public string? FooterNote { get; set; }
        // Logo: null = keep the current logo; a data URL or plain base64 = replace it; RemoveLogo = delete it
        public string? Logo { get; set; }
        public bool RemoveLogo { get; set; }
    }
}
