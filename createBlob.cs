using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FileGilla.Function
{
    public class createBlob
    {
        private readonly ILogger<createBlob> _logger;

        public createBlob(ILogger<createBlob> logger)
        {
            _logger = logger;
        }

        [Function("createBlob")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}
