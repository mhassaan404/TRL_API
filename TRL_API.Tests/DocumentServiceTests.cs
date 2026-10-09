using TRL_API.BLL;
using Xunit;

namespace TRL_API.Tests
{
    // Receipt ids from the print link (DocumentService.ParseReceiptIds)
    public class DocumentServiceTests
    {
        private static readonly DocumentService Service = new(null!);

        [Theory]
        [InlineData("12", new[] { 12 })]
        [InlineData("12,13", new[] { 12, 13 })]
        [InlineData(" 12 , 13 ,12", new[] { 12, 13 })] // spaces and duplicates
        [InlineData("5,,6,", new[] { 5, 6 })]
        public void Valid(string ids, int[] expected)
        {
            var (list, error) = Service.ParseReceiptIds(ids);
            Assert.Null(error);
            Assert.Equal(expected, list);
        }

        [Theory]
        [InlineData(null, "Please choose a payment.")]
        [InlineData("", "Please choose a payment.")]
        [InlineData(" , ", "Please choose a payment.")]
        [InlineData("abc", "Invalid payment number.")]
        [InlineData("0", "Invalid payment number.")]
        [InlineData("-3", "Invalid payment number.")]
        [InlineData("1.5", "Invalid payment number.")]
        [InlineData("1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21", "At most 20 receipts can be printed at once.")]
        public void Invalid(string? ids, string message) => Assert.Equal(message, Service.ParseReceiptIds(ids).Error);

        [Fact]
        public void Twenty_is_allowed() =>
            Assert.Equal(20, Service.ParseReceiptIds(string.Join(",", Enumerable.Range(1, 20))).Ids!.Count);
    }
}
