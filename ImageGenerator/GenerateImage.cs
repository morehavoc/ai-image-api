using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using Azure.Storage.Blobs;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;

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
        private readonly string _openAIApiKey;
        private readonly string _openAIModel;

        private OpenAIClient _openAIClient;
        public GenerateImage(ILogger<GenerateImage> logger)
        {
            _logger = logger;
            _openAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            _openAIModel = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4";
            _openAIClient = new OpenAIClient(_openAIApiKey);
            
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
                
                // Generate image prompt using GPT-4
                string imagePrompt = await GenerateImagePromptAsync(request);
                
                // Generate a dummy image (in a real implementation, this would call the OpenAI API)
                byte[] imageBytes = await GenerateAiImage(imagePrompt);
                
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
                
                // Upload the image data to the blob.
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    await blobClient.UploadAsync(ms, overwrite: true);
                }
                
                // Construct the URL of the uploaded blob.
                var blobUrl = $"api/image/{containerName}/{requestId}";
                
                var response = new
                {
                    RequestId = requestId,
                    Status = "Accepted",
                    Message = "Image generation request received",
                    ImageUrl = blobUrl,
                    Prompt = imagePrompt
                };

                return new AcceptedResult("", response);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image generation request");
                return new StatusCodeResult(StatusCodes.Status500InternalServerError);
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
        
        /// <summary>
        /// Generates an image prompt using GPT-4 based on the request details
        /// </summary>
        private async Task<string> GenerateImagePromptAsync(ImageGenerationRequest request)
        {               
            try
            {
                // Create a system message that instructs GPT-4 how to generate image prompts
                string systemMessage = @"
                You are an expert at creating detailed image generation prompts. 
                Your task is to create a vivid, detailed prompt for image generation based on the user's request.
                For different image types, adjust your prompt style:
                - bw: Create a black and white artistic image prompt with strong contrast and mood
                - color: Create a vibrant, colorful image prompt with rich details
                - sticker: Create a cute, simple, cartoon-style sticker design prompt
                - whisperframe: Create a dreamy, ethereal, slightly abstract image prompt
                
                Your prompt should be 1-3 sentences long and highly descriptive.";
                
                // Create a user message that includes the request details
                string userMessage = $"Create an image prompt for type: {request.Type}. ";
                
                if (!string.IsNullOrEmpty(request.Details))
                {
                    userMessage += $"Details: {request.Details}. ";
                }
                
                if (!string.IsNullOrEmpty(request.Name))
                {
                    userMessage += $"User name: {request.Name}. ";
                }
                var chat = _openAIClient.GetChatClient("gpt-4");
                List<ChatMessage> messages = 
                [
                    new SystemChatMessage(systemMessage),
                    new UserChatMessage(userMessage)
                ];
                
                var response = await chat.CompleteChatAsync(messages, new ChatCompletionOptions
                {
                    Temperature = 0.7f, MaxOutputTokenCount = 300,
                });
                // Check if the request was successful
                if (response.Value.Content[0].Text != null)
                {
                    return response.Value.Content[0].Text;
                }
                else
                {
                    _logger.LogError($"Error calling OpenAI API: {response} ");
                    return $"A {request.Type} image based on: {request.Details ?? "abstract art"}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating image prompt with GPT-4");
                // Fallback to a simple prompt if GPT-4 fails
                return $"A {request.Type} image based on: {request.Details ?? "abstract art"}";
            }
        }
        
        /// <summary>
        /// Generates a dummy image (in a real implementation, this would call the OpenAI API)
        /// </summary>
        private async Task<byte[]> GenerateAiImage(string prompt)
        {
            ImageGenerationOptions options = new()
            {
                Quality = GeneratedImageQuality.High,
                Size = GeneratedImageSize.W1792xH1024,
                Style = GeneratedImageStyle.Vivid,
                ResponseFormat = GeneratedImageFormat.Bytes
            };
            var imgClient = _openAIClient.GetImageClient("dall-e-3");
            GeneratedImage image = await imgClient.GenerateImageAsync(prompt, options);
            BinaryData bytes = image.ImageBytes;
            return bytes.ToArray();
        }
    }
}
