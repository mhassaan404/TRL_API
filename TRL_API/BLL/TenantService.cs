using Microsoft.Data.SqlClient;
using System.Data;
using TRL_API.DAL;
using TRL_API.Models;

namespace TRL_API.BLL
{
    public class TenantService
    {
        private readonly TenantRepository _dal;

        public TenantService(TenantRepository dal)
        {
            _dal = dal;
        }

        // =========================
        // Data Retrieval Methods
        // =========================

        public async Task<DataTable> GetTenants()
            => await _dal.GetTenants();

        public async Task<DataTable> GetBuildings()
            => await _dal.GetBuildings();

        public async Task<DataTable> GetFloors(int? buildingId)
            => await _dal.GetFloors(buildingId);

        public async Task<DataTable> GetUnits(int? floorId)
            => await _dal.GetUnits(floorId);

        public async Task<DataTable> GetCities()
            => await _dal.GetCities();


        // =========================
        // CRUD Methods
        // =========================

        public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        {
            try
            {
                // ADDED: auto-fill rent from the unit if not provided
                //if (tenant.MonthlyRent <= 0 && tenant.UnitId > 0)
                //    tenant.MonthlyRent = await _dal.GetUnitBaseRent(tenant.UnitId);

                var result = await _dal.SaveTenantAsync(tenant);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant saved successfully." };

                return new ApiResponse { IsSuccess = false, Message = "No record saved." };
            }
            // ADDED: duplicate active tenant on the same unit
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = "This unit already has an active tenant." };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Error occurred while saving tenant. " + ex.Message
                };
            }
        }

        public async Task<ApiResponse> UpdateTenantAsync(Tenants tenant)
        {
            try
            {
                // ADDED: auto-fill rent from the unit if not provided
                //if (tenant.MonthlyRent <= 0 && tenant.UnitId > 0)
                //    tenant.MonthlyRent = await _dal.GetUnitBaseRent(tenant.UnitId);

                var result = await _dal.UpdateTenantAsync(tenant);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant updated successfully." };

                return new ApiResponse { IsSuccess = false, Message = "No record updated." };
            }
            // ADDED: duplicate active tenant on the same unit (checked before the existing FK catch)
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                return new ApiResponse { IsSuccess = false, Message = "This unit already has an active tenant." };
            }
            catch (SqlException ex) when (ex.Number == 547) // foreign key violation
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Cannot update tenant because it is referenced in another record."
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Error occurred while updating tenant. " + ex.Message
                };
            }
        }


        //public async Task<ApiResponse> SaveTenantAsync(Tenants tenant)
        //{
        //    try
        //    {
        //        var result = await _dal.SaveTenantAsync(tenant);

        //        if (result.IsSuccess)
        //            return new ApiResponse { IsSuccess = true, Message = "Tenant saved successfully." };

        //        return new ApiResponse { IsSuccess = false, Message = "No record saved." };
        //    }
        //    catch (Exception ex)
        //    {
        //        return new ApiResponse
        //        {
        //            IsSuccess = false,
        //            Message = "Error occurred while saving tenant. " + ex.Message
        //        };
        //    }
        //}

        //public async Task<ApiResponse> UpdateTenantAsync(Tenants tenant)
        //{
        //    try
        //    {
        //        var result = await _dal.UpdateTenantAsync(tenant);

        //        if (result.IsSuccess)
        //            return new ApiResponse { IsSuccess = true, Message = "Tenant updated successfully." };

        //        return new ApiResponse { IsSuccess = false, Message = "No record updated." };
        //    }
        //    catch (SqlException ex) when (ex.Number == 547) // foreign key violation
        //    {
        //        return new ApiResponse
        //        {
        //            IsSuccess = false,
        //            Message = "Cannot update tenant because it is referenced in another record."
        //        };
        //    }
        //    catch (Exception ex)
        //    {
        //        return new ApiResponse
        //        {
        //            IsSuccess = false,
        //            Message = "Error occurred while updating tenant. " + ex.Message
        //        };
        //    }
        //}

        public async Task<ApiResponse> DeleteTenantAsync(int tenantId)
        {
            try
            {
                var result = await _dal.DeleteTenantAsync(tenantId);

                if (result.IsSuccess)
                    return new ApiResponse { IsSuccess = true, Message = "Tenant deleted successfully." };

                return new ApiResponse { IsSuccess = false, Message = "Tenant not found or already deleted." };
            }
            catch (SqlException ex) when (ex.Number == 547) // foreign key violation
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Cannot delete tenant because it is referenced in another record or there are related invoices."
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse
                {
                    IsSuccess = false,
                    Message = "Error occurred while deleting tenant. " + ex.Message
                };
            }
        }
    }
}
