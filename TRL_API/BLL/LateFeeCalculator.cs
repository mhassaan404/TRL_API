namespace TRL_API.BLL
{
    public static class LateFeeCalculator
    {
        /// <summary>
        /// Calculates the late fee for a tenant/company based on rules.
        /// </summary>
        /// <param name="totalRent">Total rent of the invoice</param>
        /// <param name="dueDate">Due date of the invoice</param>
        /// <param name="currentDate">Current date (usually DateTime.UtcNow)</param>
        /// <param name="lateFeeType">Type of late fee: "Fixed", "Percentage", "Daily"</param>
        /// <param name="lateFeeValue">Value of the late fee (amount or percentage)</param>
        /// <param name="graceDays">Number of grace days</param>
        /// <param name="maxCap">Maximum late fee allowed (0 = no cap)</param>
        /// <returns>Calculated late fee</returns>
        public static decimal Calculate(decimal companyId, decimal totalRent, DateTime dueDate, DateTime currentDate)
        {
            companyId = 0; // Temporary override for testing
            // ===== Tenant-specific late fee rules =====
            string lateFeeType;
            decimal lateFeeValue;
            int graceDays;
            decimal maxCap;

            switch (companyId) // Add new tenants/companies here
            {
                case 1: // Company A
                    lateFeeType = "Percentage";
                    lateFeeValue = 2; // 2%
                    graceDays = 5;
                    maxCap = 0;
                    break;

                case 2: // Company B
                    lateFeeType = "Daily";
                    lateFeeValue = 0.5m; // 0.5% per day
                    graceDays = 3;
                    maxCap = 1000;
                    break;

                default: // Default rule
                    lateFeeType = "Fixed";
                    lateFeeValue = 2000; //Fixed amount
                    graceDays = 5;
                    maxCap = 0;
                    break;
            }

            // ===== Calculate Late Fee =====
            int daysLate = (currentDate.Date - dueDate.Date).Days - graceDays;
            if (daysLate <= 0)
                return 0;

            decimal lateFee = 0;

            switch (lateFeeType.ToLower())
            {
                case "fixed":
                    lateFee = lateFeeValue;
                    break;
                case "percentage":
                    lateFee = totalRent * lateFeeValue / 100m;
                    break;
                case "daily":
                    lateFee = totalRent * lateFeeValue / 100m * daysLate;
                    break;
            }

            // Apply max cap if defined
            if (maxCap > 0 && lateFee > maxCap)
                lateFee = maxCap;

            return Math.Round(lateFee, 2);
        }
    }
}
