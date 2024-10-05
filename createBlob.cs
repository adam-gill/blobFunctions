using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace blobFunctions
{
    public class CreateBlob
    {
        private readonly ILogger<CreateBlob> _logger;

        public CreateBlob(ILogger<CreateBlob> logger)
        {
            _logger = logger;
        }

        [Function("createBlob")]
        public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req) {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            return new OkObjectResult("Welcome to Azure Functions!");
        }
    }
}
