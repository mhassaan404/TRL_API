using TRL_API.BLL;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Field checks on new payments (method list, date, amounts, discount %, notes) before anything is written
    public class PaymentFieldValidationTests
    {
        private static readonly DateTime Today = new(2026, 10, 12);

        private static Payments Pay(decimal cash = 1000, string? method = "Cash", DateTime? date = null, decimal disc = 0, decimal pct = 0, string? notes = null) =>
            new() { TenantId = 1, RentInvoiceId = 7, PaymentAmount = cash, PaymentMethod = method, PaymentDate = date ?? Today, DiscountAmount = disc, DiscountPercent = pct, Notes = notes };

        private static string? Check(params Payments[] p) => RentService.ValidatePaymentFields(p.ToList(), Today);

        [Theory]
        [InlineData("Cash")]
        [InlineData("Bank Transfer")]
        [InlineData("Cheque")]
        [InlineData(" Online ")]
        public void Cash_methods_are_accepted(string method) => Assert.Null(Check(Pay(method: method)));

        [Theory]
        [InlineData("Security Deposit")]
        [InlineData("Credit to Deposit")]
        [InlineData("Adjustment")]
        [InlineData("Gold")]
        [InlineData(null)]
        public void Other_methods_are_refused_for_cash(string? method) =>
            Assert.Equal("Payment method for invoice #7 must be one of: Cash, Bank Transfer, Cheque, Online.", Check(Pay(method: method)));

        [Fact]
        public void A_discount_or_waiver_only_row_is_stored_as_adjustment_but_never_as_a_settlement_method()
        {
            var p = Pay(cash: 0, method: null, disc: 500);
            Assert.Null(Check(p));
            Assert.Equal("Adjustment", p.PaymentMethod);
            Assert.NotNull(Check(Pay(cash: 0, method: "Security Deposit", disc: 500)));
        }

        [Fact]
        public void Payment_dates_must_be_real_and_not_in_the_future()
        {
            Assert.Null(Check(Pay(date: Today.AddDays(1)))); // one day of leeway: the server may run in UTC
            Assert.Equal("Payment date for invoice #7 can't be in the future.", Check(Pay(date: Today.AddDays(2))));
            Assert.Equal("Payment date for invoice #7 can't be in the future.", Check(Pay(date: default(DateTime))));
        }

        [Fact]
        public void Amount_discount_and_notes_limits()
        {
            Assert.Equal("Amount for invoice #7 is too large.", Check(Pay(cash: 1_000_000_001)));
            Assert.Null(Check(Pay(cash: 150_000_000))); // above the old decimal(10,2) limit
            Assert.Equal("Amounts for invoice #7 can have at most 2 decimal places.", Check(Pay(cash: 10.005m)));
            Assert.Equal("Discount % for invoice #7 must be between 0 and 100.", Check(Pay(pct: 150)));
            Assert.Equal("Notes for invoice #7 can be at most 500 characters.", Check(Pay(notes: new string('x', 501))));
            Assert.Equal("At most 100 payments can be recorded at once.", Check(Enumerable.Range(0, 101).Select(_ => Pay()).ToArray()));
            Assert.Equal("Invoice #7 appears more than once in this payment. Record it once.", Check(Pay(), Pay(cash: 5)));
        }
    }
}
