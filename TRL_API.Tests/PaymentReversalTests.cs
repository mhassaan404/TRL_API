using System.Data;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using TRL_API.BLL;
using TRL_API.Controllers;
using TRL_API.DAL;
using Xunit;

namespace TRL_API.Tests
{
    // Payment reversal: request checks, the refusal messages, what the service does with the repository's result,
    // and that the endpoint is Admin-only while the old in-place edit route is gone.
    public class PaymentReversalTests
    {
        private static DataTable Result(string result, int? reversalId = null, int invoiceId = 7, decimal? balance = null, string? status = null)
        {
            var dt = new DataTable();
            dt.Columns.Add("Result", typeof(string));
            dt.Columns.Add("ReversalId", typeof(int));
            dt.Columns.Add("InvoiceId", typeof(int));
            dt.Columns.Add("Balance", typeof(decimal));
            dt.Columns.Add("Status", typeof(string));
            dt.Rows.Add(result, (object?)reversalId ?? DBNull.Value, invoiceId, (object?)balance ?? DBNull.Value, (object?)status ?? DBNull.Value);
            return dt;
        }

        private static (RentService svc, IRentRepository rent) Create(DataTable result)
        {
            var rent = Substitute.For<IRentRepository>();
            rent.ReversePaymentAsync(default, default!, default).ReturnsForAnyArgs(result);
            return (new RentService(rent, Substitute.For<ILateFeeSettingsRepository>()), rent);
        }

        [Theory]
        [InlineData(0, "Wrong invoice", "Payment is required.")]
        [InlineData(-3, "Wrong invoice", "Payment is required.")]
        [InlineData(5, null, "Please enter a reason for the reversal.")]
        [InlineData(5, "", "Please enter a reason for the reversal.")]
        [InlineData(5, "   ", "Please enter a reason for the reversal.")]
        [InlineData(5, "bad\u0001reason", "The reason contains invalid characters.")]
        public void Request_is_checked(int paymentId, string? reason, string expected) =>
            Assert.Equal(expected, RentService.ValidateReversalRequest(paymentId, reason));

        [Fact]
        public void Reason_length_limit_is_the_notes_column()
        {
            Assert.Null(RentService.ValidateReversalRequest(5, new string('x', 500)));
            Assert.Null(RentService.ValidateReversalRequest(5, "  " + new string('x', 500) + "  ")); // trimmed first
            Assert.Equal("The reason can be at most 500 characters.", RentService.ValidateReversalRequest(5, new string('x', 501)));
            Assert.Null(RentService.ValidateReversalRequest(5, "line one\r\nline two\tok"));
        }

        [Fact]
        public async Task Invalid_request_never_reaches_the_database()
        {
            var (svc, rent) = Create(Result("OK", 9, balance: 0, status: "Paid"));
            var r = await svc.ReversePaymentAsync(5, " ", 1);
            Assert.False(r.IsSuccess);
            await rent.DidNotReceiveWithAnyArgs().ReversePaymentAsync(default, default!, default);
        }

        [Fact]
        public async Task Success_passes_trimmed_reason_and_reports_new_state()
        {
            var (svc, rent) = Create(Result("OK", reversalId: 42, invoiceId: 7, balance: 50000m, status: "Unpaid"));
            var r = await svc.ReversePaymentAsync(5, "  Wrong invoice ", 3);
            Assert.True(r.IsSuccess);
            Assert.Equal(42, r.Id);
            Assert.Contains("Payment #5 reversed", r.Message);
            Assert.Contains("Invoice #7 is now Unpaid", r.Message);
            await rent.Received(1).ReversePaymentAsync(5, "Wrong invoice", 3);
        }

        // Every reason code the SQL rule can return has its own message (the UI shows it as the refusal)
        [Theory]
        [InlineData("NOT_FOUND", "was not found")]
        [InlineData("IS_REVERSAL", "itself a reversal")]
        [InlineData("ALREADY_REVERSED", "already been reversed")]
        [InlineData("SETTLEMENT", "move-out settlement")]
        [InlineData("ADJUSTMENT", "Adjustments can't be reversed")]
        [InlineData("NOTHING", "no amount, discount or waiver")]
        [InlineData("CANCELLED", "cancelled")]
        [InlineData("NEGATIVE", "negative total")]
        public async Task Refusal_codes_map_to_messages(string code, string text)
        {
            var (svc, _) = Create(Result(code));
            var r = await svc.ReversePaymentAsync(5, "reason", 1);
            Assert.False(r.IsSuccess);
            Assert.Contains(text, r.ErrorMessage);
            Assert.Equal(r.ErrorMessage, r.Message);
        }

        [Fact]
        public void Every_sql_reason_code_has_a_message()
        {
            foreach (var code in new[] { "IS_REVERSAL", "ALREADY_REVERSED", "SETTLEMENT", "ADJUSTMENT", "NOTHING", "CANCELLED", "NEGATIVE" })
            {
                Assert.Contains($"'{code}'", InvoiceSql.ReversalBlock);
                Assert.NotEqual("The payment could not be reversed.", RentService.ReversalBlockMessage(code, 1));
            }
        }

        [Fact]
        public void Endpoint_is_admin_only_post_and_edit_route_is_gone()
        {
            var type = typeof(RentController);
            Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(), a => a.Roles == "Admin");
            var reverse = type.GetMethod(nameof(RentController.ReversePayment))!;
            Assert.Equal("ReversePayment", reverse.GetCustomAttribute<HttpPostAttribute>()?.Template);
            Assert.Null(type.GetMethod("UpdatePayments"));
            Assert.Null(typeof(IRentService).GetMethod("UpdatePaymentsAsync"));
            Assert.Null(typeof(IRentRepository).GetMethod("UpdatePaymentAsync"));
        }
    }
}
