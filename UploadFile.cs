using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;

namespace blobFunctions
{
    public static class UploadFile
    {
        private static readonly string? connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

        public class UploadResponse
        {
            public bool Success { get; set; }
            public required string Message { get; set; }
        }

        [Function("UploadFile")]
        public static async Task<IActionResult> Run([Microsoft.Azure.Functions.Worker.HttpTrigger(Microsoft.Azure.Functions.Worker.AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processing file upload request.");

            try
            {
                // Get the B2C UID from the request
                string? userId = req.Query["userId"];
                if (string.IsNullOrEmpty(userId))
                {
                    return new BadRequestObjectResult(new UploadResponse 
                    { 
                        Success = false, 
                        Message = "User ID is required." 
                    });
                }

                // Get the file from the request
                var formData = await req.ReadFormAsync();
                var file = formData.Files["file"];
                if (file == null)
                {
                    return new BadRequestObjectResult(new UploadResponse 
                    { 
                        Success = false, 
                        Message = "No file was uploaded." 
                    });
                }

                // Create BlobServiceClient
                var blobServiceClient = new BlobServiceClient(connectionString);

                // Create container name (lowercase for Azure requirements)
                string containerName = $"user-{userId.ToLower()}";
                
                // Get container client and create if it doesn't exist
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync();

                // Generate unique blob name
                string blobName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{file.FileName}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload the file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, true);
                }

                log.LogInformation($"File {blobName} uploaded successfully to container {containerName}");

                return new OkObjectResult(new UploadResponse 
                { 
                    Success = true, 
                    Message = $"File {file.FileName} uploaded successfully." 
                });
            }
            catch (Exception ex)
            {
                log.LogError($"Error uploading file: {ex.Message}");
                return new ObjectResult(new UploadResponse 
                { 
                    Success = false, 
                    Message = "Error uploading file: " + ex.Message 
                }) { StatusCode = 500 };
            }
        }
    }
}