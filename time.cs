using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace blobFunctions
{
    public class Time
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        [Function("time")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {

            Console.WriteLine("time request made");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrEmpty(requestBody))
            {
                return new JsonResult(new { success = false, error = "Request body is empty" })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

            try
            {

                var requestData = JsonSerializer.Deserialize<RequestModel>(requestBody, _jsonOptions);

                if (requestData?.Message?.Contains("please", StringComparison.InvariantCultureIgnoreCase) == true)
                {
                    var response = new
                    {
                        success = true,
                        message = $"The current time is: {DateTime.Now:HH:mm:ss}",
                        timestamp = DateTime.Now
                    };
                    return new JsonResult(response);
                }
                else
                {
                    return new JsonResult(new { success = false, error = "Please say the magic word", please = requestData?.Please })
                    {
                        StatusCode = StatusCodes.Status400BadRequest
                    };
                }

            }
            catch (JsonException)
            {
                return new JsonResult(new { success = false, error = "Invalid JSON in request body" })
                {
                    StatusCode = StatusCodes.Status400BadRequest
                };
            }

        }

        
    }

    public class RequestModel
    {
        public string? Message { get; set; }
        public string? Please {get; set;}
    }

}

