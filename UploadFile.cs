using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace blobFunctions
{
    public static class UploadFile
    {
        private static readonly string? connectionString = Environment.GetEnvironmentVariable("AzureBlobStorage");

        public class UploadRequest
        {
            [JsonPropertyName("userId")]
            public required string UserId { get; set; }
        }

        public class UploadResponse
        {
            public bool Success { get; set; }
            public required string Message { get; set; }
        }

        [Function("UploadFile")]
        public static async Task<IActionResult> Run([Microsoft.Azure.Functions.Worker.HttpTrigger(Microsoft.Azure.Functions.Worker.AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
        {
            Console.WriteLine("request started");

            try
            {
                // Get the form data including both file and userId
                var formData = await req.ReadFormAsync();

                // Get userId from form data
                var userIdJson = formData["userId"].ToString();
                if (string.IsNullOrEmpty(userIdJson))
                {
                    return new BadRequestObjectResult(new UploadResponse 
                    { 
                        Success = false, 
                        Message = "User ID is required in the form data." 
                    });
                }

                // Parse the JSON string to get the userId
                var uploadRequest = JsonSerializer.Deserialize<UploadRequest>(userIdJson);
                if (uploadRequest == null || string.IsNullOrEmpty(uploadRequest.UserId))
                {
                    return new BadRequestObjectResult(new UploadResponse 
                    { 
                        Success = false, 
                        Message = "Invalid User ID format." 
                    });
                }

                // Get the file from the request
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
                string containerName = $"user-{uploadRequest.UserId.ToLower()}";
                
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

                return new OkObjectResult(new UploadResponse 
                { 
                    Success = true, 
                    Message = $"File {file.FileName} uploaded successfully." 
                });
            }
            catch (JsonException jex)
            {
                return new BadRequestObjectResult(new UploadResponse 
                { 
                    Success = false, 
                    Message = "Invalid userId format: " + jex.Message 
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                return new ObjectResult(new UploadResponse 
                { 
                    Success = false, 
                    Message = "Error uploading file: " + ex.Message 
                }) { StatusCode = 500 };
            }
        }
    }
}