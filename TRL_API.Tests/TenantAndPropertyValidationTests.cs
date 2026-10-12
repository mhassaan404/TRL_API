using NSubstitute;
using TRL_API.BLL;
using TRL_API.DAL;
using TRL_API.Helpers;
using TRL_API.Models;
using Xunit;

namespace TRL_API.Tests
{
    // Tenant details (Helpers/TenantValidation, same rules as src/utils/validators.js) and the building / floor /
    // unit input checks in PropertiesService
    public class TenantAndPropertyValidationTests
    {
        private static Tenants Tenant(string? name = "Ali Traders", string? type = "Individual", string? phone = null, string? id = null, string? email = null) =>
            new() { Name = name, TenantType = type, Contact = phone, CnicNtn = id, Email = email };

        [Fact]
        public void Valid_tenant_is_cleaned_up_and_formatted()
        {
            var t = Tenant("  Ali Traders ", "company", " +92 300 1234567 ", "12345678", "  Ali@Example.COM ");
            Assert.Null(TenantValidation.Normalize(t));
            Assert.Equal("Ali Traders", t.Name);
            Assert.Equal("Company", t.TenantType);
            Assert.Equal("0300-1234567", t.Contact);
            Assert.Equal("1234567-8", t.CnicNtn);
            Assert.Equal("ali@example.com", t.Email);
        }

        [Theory]
        [InlineData(null, "Individual", null, null, null, "Tenant / Company name is required.")]
        [InlineData("   ", "Individual", null, null, null, "Tenant / Company name is required.")]
        [InlineData("A", "Robot", null, null, null, "Tenant type must be Company or Individual.")]
        [InlineData("A", null, null, null, null, "Tenant type must be Company or Individual.")]
        [InlineData("A", "Individual", "12345", null, null, "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.")]
        [InlineData("A", "Individual", "0300-12345ab", null, null, "Enter a valid Pakistani phone number, e.g. 0300-1234567 or 021-34567890.")]
        [InlineData("A", "Individual", null, "1234567-8", null, "Enter the CNIC as 12345-1234567-1 (13 digits).")]
        [InlineData("A", "Company", null, "123", null, "Enter the NTN as 1234567-8 (8 digits) or a CNIC as 12345-1234567-1.")]
        [InlineData("A", "Individual", null, null, "not-an-email", "Enter a valid email address, e.g. name@example.com.")]
        public void Invalid_tenant_details_are_refused(string? name, string? type, string? phone, string? id, string? email, string message) =>
            Assert.Equal(message, TenantValidation.Normalize(Tenant(name, type, phone, id, email)));

        [Fact]
        public void Length_limits()
        {
            Assert.Equal("Tenant / Company name can be at most 150 characters.", TenantValidation.Normalize(Tenant(new string('a', 151))));
            var t = Tenant();
            t.Address = new string('a', 501);
            Assert.Equal("Address can be at most 500 characters.", TenantValidation.Normalize(t));
        }

        [Theory]
        [InlineData("021-34567890", "021-34567890")]
        [InlineData("0092 300 1234567", "0300-1234567")]
        [InlineData("(042) 3456 7890", "042-34567890")]
        [InlineData("0300 123", null)]
        public void Phone_formatting(string input, string? expected) => Assert.Equal(expected, TenantValidation.FormatPhone(input));

        private static (PropertiesService svc, IPropertiesRepository dal) Properties()
        {
            var dal = Substitute.For<IPropertiesRepository>();
            dal.SaveBuilding(default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, Id = 9 });
            dal.SaveFloor(default, default!).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, Id = 9 });
            dal.SaveUnit(default, default, default!, default, default, default, default).ReturnsForAnyArgs(new ApiResponse { IsSuccess = true, Id = 9 });
            dal.FloorBelongsToBuildingAsync(default, default).ReturnsForAnyArgs(true);
            return (new PropertiesService(dal), dal);
        }

        [Fact]
        public async Task Building_floor_and_unit_inputs_are_checked_before_the_database()
        {
            var (svc, dal) = Properties();
            Assert.Equal("Building name is required.", (await svc.SaveBuilding("  ", 1, 1, null)).Message);
            Assert.Equal("Floor number is required.", (await svc.SaveFloor(1, " ")).Message);
            Assert.Equal("Floor number can be at most 50 characters.", (await svc.SaveFloor(1, new string('1', 51))).Message);
            Assert.Equal("Unit number is required.", (await svc.SaveUnit(1, 1, "", 1, 100, null, null)).Message);
            Assert.Equal("Base rent can't be negative.", (await svc.SaveUnit(1, 1, "101", 1, -1, null, null)).Message);
            await dal.DidNotReceiveWithAnyArgs().SaveBuilding(default, default, default, default);
            await dal.DidNotReceiveWithAnyArgs().SaveFloor(default, default!);
            await dal.DidNotReceiveWithAnyArgs().SaveUnit(default, default, default!, default, default, default, default);
        }

        [Theory]
        [InlineData("OK", true, "deleted successfully")]
        [InlineData("IN_USE", false, "Cannot delete")]
        [InlineData("NOT_FOUND", false, "not found or already deleted")]
        public async Task Deletes_report_in_use_and_not_found(string code, bool success, string text)
        {
            var (svc, dal) = Properties();
            dal.DeleteBuilding(default).ReturnsForAnyArgs(code);
            dal.DeleteFloor(default).ReturnsForAnyArgs(code);
            dal.DeleteUnit(default).ReturnsForAnyArgs(code);
            foreach (var r in new[] { await svc.DeleteBuilding(1), await svc.DeleteFloor(1), await svc.DeleteUnitAsync(1) })
            {
                Assert.Equal(success, r.IsSuccess);
                Assert.Contains(text, r.Message);
            }
        }

        [Theory]
        [InlineData("OK", true, "Tenant deleted successfully.")]
        [InlineData("HAS_LEASE", false, "active lease")]
        [InlineData("OPEN_BALANCE", false, "owes money or has a credit")]
        [InlineData("DEPOSIT_HELD", false, "security deposit is still held")]
        [InlineData("NOT_FOUND", false, "not found or already deleted")]
        public async Task Tenant_delete_results(string code, bool success, string text)
        {
            var dal = Substitute.For<ITenantRepository>();
            dal.DeleteTenantAsync(default, default).ReturnsForAnyArgs(code);
            var r = await new TenantService(dal).DeleteTenantAsync(5, 1);
            Assert.Equal(success, r.IsSuccess);
            Assert.Contains(text, r.Message);
        }

        [Fact]
        public async Task Unit_floor_must_belong_to_the_building()
        {
            var (svc, dal) = Properties();
            dal.FloorBelongsToBuildingAsync(4, 3).Returns(false);
            var msg = "The selected floor doesn't belong to the selected building (or has been deleted).";
            Assert.Equal(msg, (await svc.SaveUnit(4, 3, "101", 1, 100.50m, null, null)).Message);
            Assert.Equal(msg, (await svc.UpdateUnit(7, 4, 3, "101", 1, 100.50m, null, null)).Message);
            await dal.DidNotReceiveWithAnyArgs().SaveUnit(default, default, default!, default, default, default, default);
        }

        [Fact]
        public async Task Valid_inputs_are_trimmed_and_return_the_new_id()
        {
            var (svc, dal) = Properties();
            Assert.Equal(9, (await svc.SaveFloor(3, "  G ")).Id);
            await dal.Received(1).SaveFloor(3, "G");
            Assert.Equal(9, (await svc.SaveUnit(4, 3, " A-101 ", 1, 50000, null, null)).Id);
            await dal.Received(1).SaveUnit(4, 3, "A-101", 1, 50000, null, null);
        }
    }
}
