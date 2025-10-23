namespace TRL_API.Models
{
    public class ApiResponse
    {
        public bool Success { get; set; }   // true/false instead of Action
        public string? Message { get; set; } // success message
        public string? ErrorMessage { get; set; } // optional error
    }
}
