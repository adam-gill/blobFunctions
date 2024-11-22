using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace blobFunctions
{
    public class Misc
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public class BronResponse
        {
            public bool Success { get; set; }
            public List<Dictionary<string, object>>? Result { get; set; }
            public string? Message {get; set;}
        }

        public class QueryParams
        {
            public string? Table { get; set; }
            public string? Column { get; set; }
            public string? Condition { get; set; }
        }

        [Function("bron")]
        public static async Task<IActionResult> RunBron([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            try
            {
                // Map query parameters to the QueryParams type
                var queryParams = new QueryParams
                {
                    Table = req.Query["table"],
                    Column = req.Query["column"],
                    Condition = req.Query["condition"]
                };

                // Validate the parameters
                if (string.IsNullOrWhiteSpace(queryParams.Table) ||
                    string.IsNullOrWhiteSpace(queryParams.Column) ||
                    string.IsNullOrWhiteSpace(queryParams.Condition))
                {
                    return new BadRequestObjectResult(new BronResponse
                    {
                        Success = false,
                        Result = [],
                        Message = "All parameters (table, column, condition) are required.",
                    });
                }

                // Use the mapped parameters in your logic
                List<Dictionary<string, object>> result = await DatabaseHelper.RunQuery(queryParams.Table, queryParams.Column, queryParams.Condition);

                

                return new OkObjectResult(new BronResponse
                {
                    Success = true,
                    Result = result,
                    Message = result.Count == 0 ? "No results found" : "Result found",
                });
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(new BronResponse
                {
                    Success = false,
                    Result = [],
                    Message = ex.Message,
                });
            }



        }

        [Function("time")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
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
        public string? Please { get; set; }
    }

}

