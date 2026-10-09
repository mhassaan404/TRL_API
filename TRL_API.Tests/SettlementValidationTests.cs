using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Move-out settlement input checks (SettlementService.ValidateFinalize / ValidateRefund)
    public class SettlementValidationTests
    {
        private static readonly DateTime Today = new(2026, 10, 9);

        private static FinalizeSettlementRequest Req(Action<FinalizeSettlementRequest>? change = null)
        {
            var r = new FinalizeSettlementRequest
            {
                TenantId = 1, UnitId = 2, LeaseId = 3, ExpectedOutstanding = 1000, ExpectedCredit = 0, ExpectedHeld = 5000, SettlementDate = Today,
            };
            change?.Invoke(r);
            return r;
        }

        [Fact]
        public void Minimal_settlement_is_valid()
        {
            Assert.Null(SettlementService.ValidateFinalize(Req(), Today));
        }

        [Fact]
        public void Deductions_and_money_are_cleaned()
        {
            var r = Req(x =>
            {
                x.Deductions = new() { new() { ChargeType = " damage ", Amount = 1500, Reason = "  Broken window  " } };
                x.FinalPayment = new() { Amount = 0, PaymentMethod = "Cash", Reference = "x" };   // 0 = nothing received
                x.Refund = new() { Amount = 500, PaymentMethod = "bank transfer", Reference = "  TRX-1 " };
                x.Notes = "   ";
            });
            Assert.Null(SettlementService.ValidateFinalize(r, Today));
            Assert.Equal("Damage", r.Deductions![0].ChargeType);
            Assert.Equal("Broken window", r.Deductions[0].Reason);
            Assert.Null(r.FinalPayment!.Amount);
            Assert.Null(r.FinalPayment.PaymentMethod);
            Assert.Equal("Bank Transfer", r.Refund!.PaymentMethod);
            Assert.Equal("TRX-1", r.Refund.Reference);
            Assert.Null(r.Notes);
        }

        [Theory]
        [InlineData("tenancy", "Please choose the tenancy to settle.")]
        [InlineData("expected", "The reviewed figures are missing. Please open the settlement again.")]
        [InlineData("nodate", "Please enter the settlement date.")]
        [InlineData("future", "The settlement date can't be in the future.")]
        [InlineData("notes", "Notes can be at most 500 characters.")]
        [InlineData("dtype", "Deduction 1: type must be one of: Damage, Cleaning, Maintenance, Utility, Other.")]
        [InlineData("damount", "The Deduction 1 amount must be greater than 0.")]
        [InlineData("dreason", "Deduction 1: please enter the reason for the deduction.")]
        [InlineData("dlong", "Deduction 1: the reason can be at most 255 characters.")]
        [InlineData("dmany", "At most 20 deductions can be added.")]
        [InlineData("finalmethod", "Please choose the payment method for the payment received (Cash, Bank Transfer, Cheque, Online).")]
        [InlineData("finalneg", "The payment received amount must be greater than 0.")]
        [InlineData("refundmethod", "Please choose the payment method for the refund paid (Cash, Bank Transfer, Cheque, Online).")]
        [InlineData("refunddec", "The refund paid amount can have at most 2 decimal places.")]
        public void Errors(string c, string message)
        {
            var r = Req(x =>
            {
                switch (c)
                {
                    case "tenancy": x.LeaseId = 0; break;
                    case "expected": x.ExpectedHeld = null; break;
                    case "nodate": x.SettlementDate = null; break;
                    case "future": x.SettlementDate = Today.AddDays(2); break;
                    case "notes": x.Notes = new string('n', 501); break;
                    case "dtype": x.Deductions = new() { new() { ChargeType = "Rent", Amount = 5, Reason = "x" } }; break;
                    case "damount": x.Deductions = new() { new() { ChargeType = "Damage", Amount = 0, Reason = "x" } }; break;
                    case "dreason": x.Deductions = new() { new() { ChargeType = "Damage", Amount = 5, Reason = " " } }; break;
                    case "dlong": x.Deductions = new() { new() { ChargeType = "Damage", Amount = 5, Reason = new string('r', 256) } }; break;
                    case "dmany": x.Deductions = Enumerable.Range(0, 21).Select(_ => new SettlementDeductionInput { ChargeType = "Other", Amount = 1, Reason = "x" }).ToList(); break;
                    case "finalmethod": x.FinalPayment = new() { Amount = 100 }; break;
                    case "finalneg": x.FinalPayment = new() { Amount = -100, PaymentMethod = "Cash" }; break;
                    case "refundmethod": x.Refund = new() { Amount = 100, PaymentMethod = "Gold" }; break;
                    case "refunddec": x.Refund = new() { Amount = 1.005m, PaymentMethod = "Cash" }; break;
                }
            });
            Assert.Equal(message, SettlementService.ValidateFinalize(r, Today));
        }

        [Fact]
        public void Refund_rules()
        {
            var ok = new RecordSettlementRefundRequest { SettlementId = 4, Amount = 2500, RefundDate = Today, PaymentMethod = "cheque", Reference = " CHQ-9 " };
            Assert.Null(SettlementService.ValidateRefund(ok, Today));
            Assert.Equal("Cheque", ok.PaymentMethod);
            Assert.Equal("CHQ-9", ok.Reference);
            Assert.Equal("Please choose a settlement.", SettlementService.ValidateRefund(new() { Amount = 1, RefundDate = Today, PaymentMethod = "Cash" }, Today));
            Assert.Equal("Please enter the refund amount.", SettlementService.ValidateRefund(new() { SettlementId = 1, RefundDate = Today, PaymentMethod = "Cash" }, Today));
            Assert.Equal("Please choose the payment method for the refund (Cash, Bank Transfer, Cheque, Online).",
                SettlementService.ValidateRefund(new() { SettlementId = 1, Amount = 5, RefundDate = Today }, Today));
            Assert.Equal("The refund date can't be in the future.", SettlementService.ValidateRefund(new() { SettlementId = 1, Amount = 5, RefundDate = Today.AddDays(3), PaymentMethod = "Cash" }, Today));
        }
    }
}
