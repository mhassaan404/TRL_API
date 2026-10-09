using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Security deposit input checks (SecurityDepositService.ValidateRecord / ValidateCorrection / ValidateAgreed)
    public class SecurityDepositValidationTests
    {
        private static readonly DateTime Today = new(2026, 10, 9);

        private static RecordDepositRequest Rec(Action<RecordDepositRequest>? change = null)
        {
            var r = new RecordDepositRequest { LeaseId = 5, Amount = 50000, EntryDate = Today, PaymentMethod = "Cash" };
            change?.Invoke(r);
            return r;
        }

        private static CorrectDepositRequest Cor(Action<CorrectDepositRequest>? change = null)
        {
            var r = new CorrectDepositRequest { TenantId = 1, UnitId = 2, Amount = 1000, EntryDate = Today, Reason = "Entered twice" };
            change?.Invoke(r);
            return r;
        }

        [Fact]
        public void Valid_record_is_cleaned()
        {
            var r = Rec(x => { x.PaymentMethod = " bank transfer "; x.Reference = "  CHQ-123  "; x.Notes = "   "; });
            Assert.Null(SecurityDepositService.ValidateRecord(r, Today));
            Assert.Equal("Bank Transfer", r.PaymentMethod);
            Assert.Equal("CHQ-123", r.Reference);
            Assert.Null(r.Notes);
        }

        [Fact]
        public void Tomorrow_is_allowed_for_time_zones_but_not_later()
        {
            Assert.Null(SecurityDepositService.ValidateRecord(Rec(x => x.EntryDate = Today.AddDays(1)), Today));
            Assert.Equal("The received date can't be in the future.", SecurityDepositService.ValidateRecord(Rec(x => x.EntryDate = Today.AddDays(2)), Today));
        }

        [Theory]
        [InlineData("lease", "Please choose a lease.")]
        [InlineData("noamount", "Please enter the deposit amount.")]
        [InlineData("zero", "The deposit amount must be greater than 0.")]
        [InlineData("negative", "The deposit amount must be greater than 0.")]
        [InlineData("huge", "The deposit amount can be at most 1,000,000,000.")]
        [InlineData("decimals", "The deposit amount can have at most 2 decimal places.")]
        [InlineData("nodate", "Please enter the received date.")]
        [InlineData("olddate", "The received date must be in 2000 or later.")]
        [InlineData("nomethod", "Please choose the payment method.")]
        [InlineData("badmethod", "Payment method must be one of: Cash, Bank Transfer, Cheque, Online.")]
        [InlineData("longref", "Reference can be at most 100 characters.")]
        [InlineData("longnotes", "Notes can be at most 500 characters.")]
        public void Record_errors(string c, string message)
        {
            var r = Rec(x =>
            {
                switch (c)
                {
                    case "lease": x.LeaseId = 0; break;
                    case "noamount": x.Amount = null; break;
                    case "zero": x.Amount = 0; break;
                    case "negative": x.Amount = -5; break;
                    case "huge": x.Amount = 1_000_000_001; break;
                    case "decimals": x.Amount = 10.123m; break;
                    case "nodate": x.EntryDate = null; break;
                    case "olddate": x.EntryDate = new DateTime(1999, 12, 31); break;
                    case "nomethod": x.PaymentMethod = " "; break;
                    case "badmethod": x.PaymentMethod = "Gold"; break;
                    case "longref": x.Reference = new string('r', 101); break;
                    case "longnotes": x.Notes = new string('n', 501); break;
                }
            });
            Assert.Equal(message, SecurityDepositService.ValidateRecord(r, Today));
        }

        [Fact]
        public void Record_limits_are_inclusive()
        {
            Assert.Null(SecurityDepositService.ValidateRecord(Rec(x => { x.Amount = 1_000_000_000; x.Reference = new string('r', 100); x.Notes = new string('n', 500); x.EntryDate = new DateTime(2000, 1, 1); }), Today));
            Assert.Null(SecurityDepositService.ValidateRecord(Rec(x => x.Amount = 0.01m), Today));
        }

        [Fact]
        public void Correction_rules()
        {
            Assert.Null(SecurityDepositService.ValidateCorrection(Cor(), Today));
            Assert.Equal("Please choose a tenancy.", SecurityDepositService.ValidateCorrection(Cor(x => x.UnitId = 0), Today));
            Assert.Equal("Please enter the reason for the correction.", SecurityDepositService.ValidateCorrection(Cor(x => x.Reason = "  "), Today));
            Assert.Equal("Reason can be at most 500 characters.", SecurityDepositService.ValidateCorrection(Cor(x => x.Reason = new string('x', 501)), Today));
            Assert.Equal("The correction amount must be greater than 0.", SecurityDepositService.ValidateCorrection(Cor(x => x.Amount = -1000), Today));
            Assert.Equal("The correction date can't be in the future.", SecurityDepositService.ValidateCorrection(Cor(x => x.EntryDate = Today.AddDays(5)), Today));
        }

        [Fact]
        public void Agreed_rules()
        {
            Assert.Null(SecurityDepositService.ValidateAgreed(new SetDepositAgreedRequest { TenantId = 1, UnitId = 2, AgreedAmount = 60000 }));
            Assert.Null(SecurityDepositService.ValidateAgreed(new SetDepositAgreedRequest { TenantId = 1, UnitId = 2, AgreedAmount = null }));
            Assert.Equal("The agreed deposit must be greater than 0.", SecurityDepositService.ValidateAgreed(new SetDepositAgreedRequest { TenantId = 1, UnitId = 2, AgreedAmount = 0 }));
            Assert.Equal("Please choose a tenancy.", SecurityDepositService.ValidateAgreed(new SetDepositAgreedRequest { TenantId = 0, UnitId = 2, AgreedAmount = 5 }));
            Assert.Equal("Please enter the deposit details.", SecurityDepositService.ValidateRecord(null, Today));
        }
    }
}
