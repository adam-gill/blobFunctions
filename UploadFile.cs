using Microsoft.AspNetCore.Mvc;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Storage.Blobs.Models;
using HttpTriggerAttribute = Microsoft.Azure.Functions.Worker.HttpTriggerAttribute;
using AuthorizationLevel = Microsoft.Azure.Functions.Worker.AuthorizationLevel;
using Azure;

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

        public class FileInfo
        {
            public string? Name { get; set; }
            public long SizeInBytes { get; set; }
            public string? ContentType { get; set; }
            public DateTimeOffset? LastModified { get; set; }
            public string? BlobUrl { get; set; }
            public IDictionary<string, string>? Metadata { get; set; }
            public string? MD5Hash { get; set; }
        }

        public class ListFilesResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public List<FileInfo>? Files { get; set; }
        }

        public class DeleteFileRequest
        {
            public string? UserId { get; set; }
            public string? BlobName { get; set; }
        }

        [Function("UploadFile")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
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
                })
                { StatusCode = 500 };
            }
        }

        [Function("ListUserFiles")]
        public static async Task<IActionResult> RunLsFile(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "files/{userId}")] HttpRequest req,
            string userId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId))
                {
                    return new BadRequestObjectResult(new ListFilesResponse
                    {
                        Success = false,
                        Message = "User ID is required.",
                        Files = []
                    });
                }

                // Create BlobServiceClient
                var blobServiceClient = new BlobServiceClient(connectionString);

                // Get container name (lowercase for Azure requirements)
                string containerName = $"user-{userId.ToLower()}";

                // Get container client
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                // Check if container exists
                if (!await containerClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new ListFilesResponse
                    {
                        Success = false,
                        Message = "No files found for this user.",
                        Files = []
                    });
                }

                var filesList = new List<FileInfo>();

                // List all blobs in the container
                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync())
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Name);
                    var properties = await blobClient.GetPropertiesAsync();

                    filesList.Add(new FileInfo
                    {
                        Name = blobItem.Name,
                        SizeInBytes = blobItem.Properties.ContentLength ?? 0,
                        ContentType = properties.Value.ContentType,
                        LastModified = blobItem.Properties.LastModified,
                        BlobUrl = blobClient.Uri.ToString(),
                        Metadata = properties.Value.Metadata,
                        MD5Hash = Convert.ToBase64String(blobItem.Properties.ContentHash)
                    });
                }

                return new OkObjectResult(new ListFilesResponse
                {
                    Success = true,
                    Message = $"Found {filesList.Count} files.",
                    Files = filesList
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");

                return new ObjectResult(new ListFilesResponse
                {
                    Success = false,
                    Message = "Error retrieving files: " + ex.Message,
                    Files = new List<FileInfo>()
                })
                { StatusCode = 500 };
            }
        }

        [Function("DeleteFile")]
        public static async Task<IActionResult> RunDeleteFile(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "deleteFile")] HttpRequest req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var data = JsonSerializer.Deserialize<DeleteFileRequest>(requestBody);

                if (string.IsNullOrEmpty(data?.UserId) || string.IsNullOrEmpty(data?.BlobName))
                {
                    return new BadRequestObjectResult(new
                    {
                        success = false,
                        message = "Container name and blob name are required"
                    });
                }

                var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient($"user-{data.UserId}");
                var blobClient = containerClient.GetBlobClient(data.BlobName);

                if (await blobClient.ExistsAsync())
                {
                    await blobClient.DeleteAsync();

                    return new OkObjectResult(new
                    {
                        success = true,
                        message = $"Successfully deleted file '{data.BlobName}'"
                    });
                }
                else
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = $"Blob '{data.BlobName}' not found in container user-{data.UserId}"
                    });
                }
            }
            catch (RequestFailedException ex)
            {
                return new ObjectResult(new
                {
                    success = false,
                    message = $"Azure Storage error: {ex.Message}",
                    errorCode = ex.ErrorCode,
                    status = ex.Status
                })
                {
                    StatusCode = ex.Status
                };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new
                {
                    success = false,
                    message = $"An error occurred: {ex.Message}"
                })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }
        }



    }
}