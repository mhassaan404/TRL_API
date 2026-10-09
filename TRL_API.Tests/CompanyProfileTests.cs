using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Company profile input checks (CompanyProfileService.Normalize / IsWebsite / ReadLogo)
    public class CompanyProfileTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 };

        private static SaveCompanyProfileRequest Req(Action<SaveCompanyProfileRequest>? change = null)
        {
            var r = new SaveCompanyProfileRequest { Phone = "0300 1234567" };
            change?.Invoke(r);
            return r;
        }

        [Fact]
        public void Phone_only_is_valid_and_formatted()
        {
            var r = Req();
            Assert.Null(CompanyProfileService.Normalize(r));
            Assert.Equal("0300-1234567", r.Phone);
        }

        [Fact]
        public void All_fields_are_cleaned_and_formatted()
        {
            var r = Req(x =>
            {
                x.Phone = " +92 21 34567890 "; x.Email = " Info@Example.COM "; x.Ntn = "12345678";
                x.Address = "  Office 5, Main Road  "; x.Website = " www.example.com "; x.FooterNote = "   ";
            });
            Assert.Null(CompanyProfileService.Normalize(r));
            Assert.Equal("021-34567890", r.Phone);
            Assert.Equal("info@example.com", r.Email);
            Assert.Equal("1234567-8", r.Ntn);
            Assert.Equal("Office 5, Main Road", r.Address);
            Assert.Equal("www.example.com", r.Website);
            Assert.Null(r.FooterNote);
        }

        [Theory]
        [InlineData(null, "Please enter the company phone number.")]
        [InlineData("   ", "Please enter the company phone number.")]
        [InlineData("12345", "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.")]
        [InlineData("0300-123456a", "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.")]
        public void Bad_phone(string? phone, string message) =>
            Assert.Equal(message, CompanyProfileService.Normalize(Req(x => x.Phone = phone)));

        [Fact]
        public void Other_errors()
        {
            Assert.Equal("Please enter the company details.", CompanyProfileService.Normalize(null));
            Assert.Equal("Enter a valid email address, e.g. info@example.com.", CompanyProfileService.Normalize(Req(x => x.Email = "info@")));
            Assert.Equal("Enter the NTN as 1234567-8 (8 digits) or a CNIC as 12345-1234567-1.", CompanyProfileService.Normalize(Req(x => x.Ntn = "123")));
            Assert.Equal("Address can be at most 300 characters.", CompanyProfileService.Normalize(Req(x => x.Address = new string('a', 301))));
            Assert.Equal("Footer note can be at most 200 characters.", CompanyProfileService.Normalize(Req(x => x.FooterNote = new string('a', 201))));
            Assert.Equal("Enter a valid website, e.g. www.example.com.", CompanyProfileService.Normalize(Req(x => x.Website = "javascript:alert(1)")));
            Assert.Equal("Choose either a new logo or Remove logo, not both.", CompanyProfileService.Normalize(Req(x => { x.Logo = "x"; x.RemoveLogo = true; })));
            Assert.Null(CompanyProfileService.Normalize(Req(x => { x.Address = new string('a', 300); x.FooterNote = new string('b', 200); x.Ntn = "12345-1234567-1"; })));
        }

        [Theory]
        [InlineData("example.com", true)]
        [InlineData("www.example.com.pk", true)]
        [InlineData("https://example.com/about", true)]
        [InlineData("HTTP://example.com", true)]
        [InlineData("javascript:alert(1)", false)]
        [InlineData("ftp://example.com", false)]
        [InlineData("data:text/html,x", false)]
        [InlineData("example", false)]
        [InlineData("exa mple.com", false)]
        [InlineData("https://.com", false)]
        public void Website(string value, bool valid) => Assert.Equal(valid, CompanyProfileService.IsWebsite(value));

        [Fact]
        public void Logo_png_and_jpg_as_data_url_or_plain_base64()
        {
            var (b, t, e) = CompanyProfileService.ReadLogo("data:image/png;base64," + Convert.ToBase64String(Png));
            Assert.Null(e); Assert.Equal("image/png", t); Assert.Equal(Png, b);
            (b, t, e) = CompanyProfileService.ReadLogo(Convert.ToBase64String(Jpg));
            Assert.Null(e); Assert.Equal("image/jpeg", t); Assert.Equal(Jpg, b);
        }

        [Fact]
        public void Logo_type_comes_from_the_bytes_not_the_label()
        {
            // A JPEG labelled as PNG is stored as JPEG; an SVG/HTML labelled as PNG is refused
            var (_, t, _) = CompanyProfileService.ReadLogo("data:image/png;base64," + Convert.ToBase64String(Jpg));
            Assert.Equal("image/jpeg", t);
            var svg = System.Text.Encoding.UTF8.GetBytes("<svg onload=alert(1)>");
            Assert.Equal("The logo must be a PNG or JPG image.", CompanyProfileService.ReadLogo("data:image/png;base64," + Convert.ToBase64String(svg)).Error);
        }

        [Fact]
        public void Logo_size_and_format_errors()
        {
            var max = new byte[CompanyProfileService.MaxLogoBytes];
            Png.CopyTo(max, 0);
            Assert.Null(CompanyProfileService.ReadLogo(Convert.ToBase64String(max)).Error);
            var tooBig = new byte[CompanyProfileService.MaxLogoBytes + 1];
            Png.CopyTo(tooBig, 0);
            Assert.Equal("The logo must be 200 KB or smaller.", CompanyProfileService.ReadLogo(Convert.ToBase64String(tooBig)).Error);
            Assert.Equal("The logo file could not be read.", CompanyProfileService.ReadLogo("data:image/png;base64,@@@").Error);
            Assert.Equal("The logo file could not be read.", CompanyProfileService.ReadLogo("data:image/png,abc").Error);
            Assert.Equal("The logo file is empty.", CompanyProfileService.ReadLogo("data:image/png;base64,").Error);
        }
    }
}
