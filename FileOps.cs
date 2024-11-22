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
using Azure.Storage.Sas;

namespace blobFunctions
{
    public static class FileOps
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

        public class ListFileResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public FileInfo? File { get; set; }
        }

        public class DeleteFileRequest
        {
            public string? UserId { get; set; }
            public string? BlobName { get; set; }
        }

        public class GetFileRequest
        {
            public string? UserId { get; set; }
            public string? FileName { get; set; }
        }

        [Function("UploadFile")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req)
        {
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
                string userId = uploadRequest.UserId.ToLower();
                string containerName = $"user-{userId}";

                // Get container client and create if it doesn't exist
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                // await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

                if (!await containerClient.ExistsAsync())
                {
                    await DatabaseHelper.InsertUser(userId, null, false);
                    await containerClient.CreateAsync();

                    // Generate SAS token
                    var startsOn = DateTimeOffset.UtcNow;
                    var expiresOn = startsOn.AddDays(360);

                    var sasBuilder = new BlobSasBuilder
                    {
                        BlobContainerName = containerName,
                        Resource = "c", // SAS for container
                        ExpiresOn = expiresOn,
                        StartsOn = startsOn,
                        Protocol = SasProtocol.HttpsAndHttp
                    };
                    sasBuilder.SetPermissions(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List);

                    var sasToken = containerClient.GenerateSasUri(sasBuilder).ToString();
                    if (sasToken.Contains('?')) {
                        sasToken = sasToken[sasToken.IndexOf('?')..];
                    }

                    await DatabaseHelper.InsertSASToken(userId, sasToken, startsOn, expiresOn);
                }

                // Generate unique blob name
                // string blobName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{file.FileName}";
                string blobName = file.FileName;
                var blobClient = containerClient.GetBlobClient(blobName);

                // Set Content-Type based on file content type
                var contentType = file.ContentType;
                // Set Content-Disposition to inline to display the file in the browser if possible
                var blobHttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,
                    ContentDisposition = "inline; filename=" + blobName
                };

                // Upload the file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobUploadOptions
                    {
                        HttpHeaders = blobHttpHeaders
                    });
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

        [Function("GetFile")]
        public static async Task<IActionResult> RunGetFile(
    [HttpTrigger(AuthorizationLevel.Function, "get", Route = "getFile")] HttpRequest req)
        {
            try
            {
                string? userId = req.Query["userId"];
                string? fileName = req.Query["fileName"];

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(fileName))
                {
                    return new BadRequestObjectResult(new
                    {
                        Success = false,
                        Message = "User ID and Filename are required as query parameters.",
                    });
                }

                var blobServiceClient = new BlobServiceClient(connectionString);
                string containerName = $"user-{userId.ToLower()}";
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                if (!await containerClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        Success = false,
                        Message = "File not found for this user.",
                    });
                }

                var blobClient = containerClient.GetBlobClient(fileName);
                if (!await blobClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        Success = false,
                        Message = $"File '{fileName}' not found for user {userId}.",
                    });
                }

                // Retrieve blob properties and metadata
                var properties = await blobClient.GetPropertiesAsync();
                var fileInfo = new FileInfo
                {
                    Name = fileName,
                    SizeInBytes = properties.Value.ContentLength,
                    ContentType = properties.Value.ContentType,
                    LastModified = properties.Value.LastModified,
                    BlobUrl = blobClient.Uri.ToString(),
                    Metadata = properties.Value.Metadata,
                    MD5Hash = properties.Value.ContentHash != null ? Convert.ToBase64String(properties.Value.ContentHash) : null
                };

                return new OkObjectResult(new
                {
                    Success = true,
                    Message = "File information retrieved successfully.",
                    File = fileInfo
                });
            }
            catch (Exception ex)
            {
                return new ObjectResult(new
                {
                    Success = false,
                    Message = "An error occurred while retrieving the file information.",
                    Error = ex.Message
                })
                {
                    StatusCode = 500
                };
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
                    return new OkObjectResult(new ListFilesResponse
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
                    Files = []
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

                string blobName = data.BlobName;
                string containerName = $"user-{data.UserId}";

                var blobServiceClient = new BlobServiceClient(connectionString);
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                var blobClient = containerClient.GetBlobClient(blobName);

                bool blobWasDeleted = await blobClient.DeleteIfExistsAsync();

                if (blobWasDeleted)
                {
                    return new OkObjectResult(new
                    {
                        success = true,
                        message = $"Successfully deleted file'{data.BlobName}'"
                    });
                }
                else
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = $"Blob '{blobName}' not found in container user-{data.UserId}"
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