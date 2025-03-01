using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using Azure.Storage.Blobs;

namespace morehavoc.ai
{
    public class ImageGenerationRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Group { get; set; } = string.Empty;

        [Required]
        public string Type { get; set; } = string.Empty;

        [Required]
        public bool SendEmail { get; set; } = false;

        public string? Details { get; set; }

        public string? Name { get; set; }
    }

    public class GenerateImage
    {
        private readonly ILogger<GenerateImage> _logger;

        public GenerateImage(ILogger<GenerateImage> logger)
        {
            _logger = logger;
        }

        [Function("GenerateImage")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
        {
            _logger.LogInformation("Processing image generation request");

            string requestBody;
            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            try
            {
                var request = JsonSerializer.Deserialize<ImageGenerationRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return new BadRequestObjectResult("Invalid request body");
                }

                // Validate the image type
                if (!IsValidImageType(request.Type))
                {
                    return new BadRequestObjectResult("Invalid image type. Must be one of: bw, color, sticker, whisperframe");
                }
                
                // Generate a new GUID for this request
                string requestId = Guid.NewGuid().ToString();
                
                // Simulate image generation by creating dummy image data
                byte[] dummyImageBytes = Encoding.UTF8.GetBytes("Fake image generated for request " + requestId);
                
                // Retrieve the blob storage connection string from environment variables.
                // For local development with Azurite, set AZURE_STORAGE_CONNECTION_STRING in local.settings.json
                string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";
                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                
                // Use the 'Group' parameter (lowercased) as the container name.
                string containerName = request.Group.ToLowerInvariant();
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                
                // Create the container if it doesn't already exist.
                await containerClient.CreateIfNotExistsAsync();
                
                // Use the generated GUID as the blob name with a .jpg extension.
                string blobName = requestId + ".jpg";
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                
                // Upload the dummy image data to the blob.
                using (MemoryStream ms = new MemoryStream(dummyImageBytes))
                {
                    await blobClient.UploadAsync(ms, overwrite: true);
                }
                
                // Construct the URL of the uploaded blob.
                string blobUrl = blobClient.Uri.ToString();
                
                var response = new
                {
                    RequestId = requestId,
                    Status = "Accepted",
                    Message = "Image generation request received",
                    ImageUrl = blobUrl
                };

                return new AcceptedResult("", response);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON format");
            }
        }

        [Function("GetImage")]
        public async Task<IActionResult> GetImage(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "image/{group}/{id}")] HttpRequest req,
            string group,
            string id)
        {
            _logger.LogInformation($"Retrieving image from group {group} with id {id}");

            try
            {
                string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";
                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                
                string containerName = group.ToLowerInvariant();
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                
                if (!await containerClient.ExistsAsync())
                {
                    return new NotFoundObjectResult($"Group '{group}' not found");
                }

                string blobName = $"{id}.jpg";
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                
                if (!await blobClient.ExistsAsync())
                {
                    return new NotFoundObjectResult($"Image '{id}' not found in group '{group}'");
                }

                var response = await blobClient.DownloadContentAsync();
                return new FileContentResult(response.Value.Content.ToArray(), "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
            }
        }

        private bool IsValidImageType(string type)
        {
            return type.ToLower() switch
            {
                "bw" or "color" or "sticker" or "whisperframe" => true,
                _ => false
            };
        }
    }
}
