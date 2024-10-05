using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace blobFunctions
{
    public class Time
    {
        [Function("time")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {

            Console.WriteLine("time request made");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (requestBody.Contains("please", StringComparison.InvariantCultureIgnoreCase))
            {
                return new OkObjectResult($"The current time is: {DateTime.Now.ToString("HH:mm:ss")}");
            }
            else
            {
                return new BadRequestObjectResult("Please say the magic word");
            }
        }
    }

}

