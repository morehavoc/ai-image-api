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
        public string Group { get; set; } = string.Empty;

        [Required]
        public string Type { get; set; } = string.Empty;

        public string? Details { get; set; }

        public string? Name { get; set; }
    }

    public class GenerateImage
    {
        private readonly ILogger<GenerateImage> _logger;
        private readonly string _openAIApiKey;

        private OpenAIClient _openAIClient;
        public GenerateImage(ILogger<GenerateImage> logger)
        {
            _logger = logger;
            _openAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
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

                if (!IsValidImageType(request.Type))
                {
                    return new BadRequestObjectResult("Invalid image type. Must be one of: bw, color, sticker, whisperframe");
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(request.Group, "^[a-z][a-z0-9]*$"))
                {
                    return new BadRequestObjectResult("Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.");
                }
                
                string requestId = Guid.NewGuid().ToString();
                
                string imagePrompt = await GenerateImagePromptAsync(request);
                
                byte[] imageBytes = await GenerateAiImage(imagePrompt);
                
                string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";
                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                
                string containerName = request.Group.ToLowerInvariant();
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                
                await containerClient.CreateIfNotExistsAsync();
                
                string blobName = requestId + ".jpg";
                BlobClient blobClient = containerClient.GetBlobClient(blobName);
                
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    await blobClient.UploadAsync(ms, overwrite: true);
                }
                
                var blobUrl = $"api/image/{containerName}/{requestId}";
                
                var response = new
                {
                    RequestId = requestId,
                    Status = "Complete",
                    Message = "Image generation request complete",
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

        private string GetSystemMessage(string imageType)
        {
            return imageType.ToLower() switch
            {
                "bw" => @"
                    You are an expert at creating detailed black and white image generation prompts.
                    Your task is to create a vivid, detailed prompt for a monochromatic image with strong contrast and mood.
                    Focus on dramatic lighting, shadows, textures, and emotional impact.
                    Your prompt should be 1-3 sentences long and highly descriptive.",
                
                "color" => @"
                    You are an expert at creating detailed color image generation prompts.
                    Your task is to create a vibrant, colorful image prompt with rich details.
                    Focus on color harmony, saturation, lighting, and visual impact.
                    Your prompt should be 1-3 sentences long and highly descriptive.",
                
                "sticker" => @"
                    You are an expert at creating cute sticker design prompts.
                    Your task is to create a simple, black and white cartoon-style sticker design prompt.
                    Focus on simplicity, bold outlines, cute characters, and playful elements. This should be a simple
                    illustration, not a detailed drawing. It will be printed on a small sticker using a thermal printer.
                    High levels of detail will not be visible.
                    Your prompt should be 1-2 sentences long and clearly describe the sticker concept.",
                
                "whisperframe" => @"
                    You are an expert at converting audio transcripts into art. Your job is to read the provided
                    auio transcript and create an image that represents a topic in the audio. Focus on
                    one topic. Do not draw people sitting around a table having a conversation. Instead, create
                    a representation of the topic. Do not draw bycicles. Never. Never draw bicycles.
                    Your prompt should be 1-3 sentences long and highly descriptive. Make the art in a style related to
                    the topic or tone or subject of the audio.",
                
                _ => @"
                    You are an expert at creating detailed image generation prompts. 
                    Your task is to create a vivid, detailed prompt for image generation based on the user's request.
                    Your prompt should be 1-3 sentences long and highly descriptive."
            };
        }
        
        private string GetUserMessagePrefix(string imageType)
        {
            return imageType.ToLower() switch
            {
                "bw" => "Create a black and white artistic image with strong contrast based on: ",
                "color" => "Create a vibrant, colorful image based on: ",
                "sticker" => "Create a cute, simple sticker design based on: ",
                "whisperframe" => "Create a dreamy, ethereal, slightly abstract image based on: ",
                _ => $"Create a {imageType} image based on: "
            };
        }
        
        private async Task<string> GenerateImagePromptAsync(ImageGenerationRequest request)
        {               
            try
            {
                string systemMessage = GetSystemMessage(request.Type);
                
                string userMessage = GetUserMessagePrefix(request.Type);
                
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
                return $"A {request.Type} image based on: {request.Details ?? "abstract art"}";
            }
        }
        
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
