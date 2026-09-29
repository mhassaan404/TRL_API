using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TRL_API.Models;

namespace TRL_API.Helpers
{
    // Makes every ApiResponse consistent before it is sent:
    //  - a failed ApiResponse returned with 200 OK is sent as 400 Bad Request, so status codes always match isSuccess
    //  - Message and ErrorMessage are both filled on failures, so clients can read either one
    public class ApiResponseResultFilter : IResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not ObjectResult { Value: ApiResponse response } result)
                return;

            if (!response.IsSuccess)
            {
                response.ErrorMessage ??= response.Message ?? "The request could not be completed.";
                response.Message ??= response.ErrorMessage;
                if (result.StatusCode is null or StatusCodes.Status200OK)
                    result.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        public void OnResultExecuted(ResultExecutedContext context) { }
    }
}
