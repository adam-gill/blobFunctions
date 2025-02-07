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

        public class ShareFileRequest
        {
            [JsonPropertyName("userId")]
            public string? UserId { get; set; }

            [JsonPropertyName("blobURL")]
            public string? BlobURL { get; set; }

            [JsonPropertyName("shareName")]
            public string? ShareName { get; set; }

            [JsonPropertyName("operation")]
            public string? Operation { get; set; }

            [JsonPropertyName("uuid")]
            public string? UUID { get; set; }
        }

        public class RenameFileRequest
        {
            [JsonPropertyName("userId")]
            public required string UserId { get; set; }

            [JsonPropertyName("oldFileName")]
            public required string OldFileName { get; set; }

            [JsonPropertyName("newFileName")]
            public required string NewFileName { get; set; }
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

        private static string GetFileNameFromBlobUrl(string blobUrl)
        {
            // Remove any query parameters
            string urlWithoutParams = blobUrl.Split('?')[0];

            // Get the last segment of the URL
            string fileName = urlWithoutParams.Split('/').Last();

            return fileName;
        }

        private static string GetFileExtension(string blobUrl)
        {
            // Remove any query parameters
            string urlWithoutParams = blobUrl.Split('?')[0];

            // Get the last segment and extract extension
            string fileName = urlWithoutParams.Split('/').Last();
            string extension = Path.GetExtension(fileName);

            return extension;
        }

        [Function("RenameFile")]
        public static async Task<IActionResult> RunRenameFile(
    [HttpTrigger(AuthorizationLevel.Function, "put", Route = "renameFile")] HttpRequest req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var data = JsonSerializer.Deserialize<RenameFileRequest>(requestBody);

                if (data == null || string.IsNullOrEmpty(data.UserId) ||
                    string.IsNullOrEmpty(data.OldFileName) || string.IsNullOrEmpty(data.NewFileName))
                {
                    return new BadRequestObjectResult(new
                    {
                        Success = false,
                        Message = "UserId, old file name, and new file name are required"
                    });
                }

                var blobServiceClient = new BlobServiceClient(connectionString);
                string containerName = $"user-{data.UserId.ToLower()}";
                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);

                if (!await containerClient.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        Success = false,
                        Message = "User container not found"
                    });
                }

                var sourceBlob = containerClient.GetBlobClient(data.OldFileName);
                var destinationBlob = containerClient.GetBlobClient(data.NewFileName);

                if (!await sourceBlob.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        Success = false,
                        Message = $"Source file '{data.OldFileName}' not found"
                    });
                }

                // Copy the source blob to the destination
                await destinationBlob.StartCopyFromUriAsync(sourceBlob.Uri);

                // Delete the source blob
                await sourceBlob.DeleteAsync();

                return new OkObjectResult(new
                {
                    Success = true,
                    Message = $"Successfully renamed '{data.OldFileName}' to '{data.NewFileName}'"
                });
            }
            catch (RequestFailedException ex)
            {
                return new ObjectResult(new
                {
                    Success = false,
                    Message = $"Azure Storage error: {ex.Message}",
                    ErrorCode = ex.ErrorCode
                })
                { StatusCode = ex.Status };
            }
            catch (Exception ex)
            {
                return new ObjectResult(new
                {
                    Success = false,
                    Message = $"An error occurred: {ex.Message}"
                })
                { StatusCode = StatusCodes.Status500InternalServerError };
            }
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
                    if (sasToken.Contains('?'))
                    {
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

                var sasToken = await DatabaseHelper.GetSASToken(userId);

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
                        BlobUrl = blobClient.Uri.ToString() + sasToken,
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

        [Function("ShareOperation")]
        public static async Task<IActionResult> RunValidateShareRequest(
    [HttpTrigger(AuthorizationLevel.Function, "post", Route = "shareOperation")] HttpRequest req)
        {
            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var data = JsonSerializer.Deserialize<ShareFileRequest>(requestBody);

                bool isValid = !string.IsNullOrEmpty(data?.UserId) &&
                              !string.IsNullOrEmpty(data?.BlobURL) &&
                              !string.IsNullOrEmpty(data?.Operation) &&
                              !string.IsNullOrEmpty(data?.UUID) &&
                              !string.IsNullOrEmpty(data?.ShareName);

                if (!isValid)
                {
                    return new BadRequestObjectResult(new
                    {
                        success = isValid,
                        message = "Missing required parameters",
                    });
                }

                string UserId = data?.UserId!;
                string BlobURL = data?.BlobURL!;
                string Operation = data?.Operation!;
                string ShareName = data?.ShareName!;
                string UUID = data?.UUID!;


                BlobServiceClient blobServiceClient = new(connectionString);
                string sourceContainerName = $"user-{UserId}";

                BlobContainerClient sourceContainer = blobServiceClient.GetBlobContainerClient(sourceContainerName);
                BlobContainerClient destinationContainer = blobServiceClient.GetBlobContainerClient("shares");

                if (!await sourceContainer.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = "Source container not found"
                    });
                }

                string BlobName = GetFileNameFromBlobUrl(BlobURL);
                string FileExtension = GetFileExtension(BlobURL);
                BlobClient sourceBlob = sourceContainer.GetBlobClient(BlobName);
                var sourceBlobProperties = await sourceBlob.GetPropertiesAsync();
                var SourceETAG = sourceBlobProperties.Value.ETag.ToString().Trim('"');
                if (!await sourceBlob.ExistsAsync())
                {
                    return new NotFoundObjectResult(new
                    {
                        success = false,
                        message = "Source file not found"
                    });
                }

                BlobClient destinationBlob = destinationContainer.GetBlobClient(ShareName + FileExtension);
                await destinationBlob.StartCopyFromUriAsync(sourceBlob.Uri);
                await DatabaseHelper.ShareFileDBOperation(UserId, UUID, ShareName, destinationBlob.Uri.ToString(), Operation, SourceETAG);

                return new OkObjectResult(new
                {
                    success = true,
                    message = $"Successfully shared file '{BlobURL}' as '{destinationBlob.Uri}'",
                    shareUrl = destinationBlob.Uri.ToString()
                });
            }
            catch (Exception ex)
            {
                return new ObjectResult(new
                {
                    success = false,
                    message = $"An error occurred: {ex.Message}"
                })
                { StatusCode = StatusCodes.Status500InternalServerError };
            }
        }

    }
}