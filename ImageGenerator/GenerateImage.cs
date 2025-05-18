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
using System.Net.Http.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Images;

namespace morehavoc.ai
{
    public class OpenAIApiException : Exception
    {
        public string OpenAIErrorResponseBody { get; }
        public System.Net.HttpStatusCode StatusCode { get; }

        public OpenAIApiException(string message, string openAIErrorResponseBody, System.Net.HttpStatusCode statusCode) : base(message)
        {
            OpenAIErrorResponseBody = openAIErrorResponseBody;
            StatusCode = statusCode;
        }
    }

    public class ImageGenerationRequest
    {
        [Required]
        public string Group { get; set; } = string.Empty;

        [Required]
        public string Type { get; set; } = string.Empty;

        public string? Details { get; set; }

        public string? Name { get; set; }

        public string? Size { get; set; }
    }

    public class GenerateImage
    {
        private readonly ILogger<GenerateImage> _logger;
        private readonly string _openAIApiKey;
        private readonly string _containerName;
        private readonly IHttpClientFactory _httpClientFactory;
        private OpenAIClient _openAIClient;

        public GenerateImage(ILogger<GenerateImage> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _openAIApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            _containerName = Environment.GetEnvironmentVariable("BLOB_CONTAINER_NAME") ?? "images";
            _httpClientFactory = httpClientFactory;
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
                    return new BadRequestObjectResult("Invalid image type. Must be one of: bw, color, sticker, whisperframe, raw");
                }

                if (request.Type.ToLower() == "raw" && string.IsNullOrWhiteSpace(request.Details))
                {
                    return new BadRequestObjectResult("For 'raw' type, 'details' field is required and cannot be empty.");
                }

                if (!System.Text.RegularExpressions.Regex.IsMatch(request.Group, "^[a-z][a-z0-9]*$"))
                {
                    return new BadRequestObjectResult("Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.");
                }

                if (!string.IsNullOrWhiteSpace(request.Size) && 
                    request.Size != "1024x1024" && request.Size != "1536x1024" && request.Size != "1024x1536")
                {
                    return new BadRequestObjectResult("Invalid size parameter for GPT Image 1. Must be one of: 1024x1024, 1536x1024, or 1024x1536. If not specified, defaults to 1024x1024.");
                }
                
                string requestId = Guid.NewGuid().ToString();
                
                string imagePrompt = await GenerateImagePromptAsync(request);
                _logger.LogInformation("Prompt is: {prompt}", imagePrompt);
                
                byte[] imageBytes = await GenerateAiImage(imagePrompt, request.Size);
                
                string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true";
                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                
                await containerClient.CreateIfNotExistsAsync();
                
                string blobPath = $"{request.Group.ToLowerInvariant()}/{requestId}.jpg";
                BlobClient blobClient = containerClient.GetBlobClient(blobPath);
                
                using (MemoryStream ms = new MemoryStream(imageBytes))
                {
                    await blobClient.UploadAsync(ms, overwrite: true);
                }
                
                var blobUrl = $"api/image/{request.Group.ToLowerInvariant()}/{requestId}";
                
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
            catch (OpenAIApiException ex)
            {
                _logger.LogError(ex, "OpenAI API error during image generation. OpenAI Response: {OpenAIErrorBody}", ex.OpenAIErrorResponseBody);
                return new ObjectResult(ex.OpenAIErrorResponseBody)
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
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
                
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_containerName);
                
                if (!await containerClient.ExistsAsync())
                {
                    return new NotFoundObjectResult($"Storage container not found");
                }

                string blobPath = $"{group.ToLowerInvariant()}/{id}.jpg";
                BlobClient blobClient = containerClient.GetBlobClient(blobPath);
                
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
                "bw" or "color" or "sticker" or "whisperframe" or "raw" => true,
                _ => false
            };
        }

        private string GetSystemMessage(string imageType)
        {
            string envVarName = $"PROMPT_CONFIG_SYSTEM_{imageType.ToUpperInvariant()}";
            string? config = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(config))
            {
                return config;
            }

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
                
                "raw" => @"
                    You are an expert at creating detailed image generation prompts. 
                    Your task is to create a vivid, detailed prompt for image generation based on the user's request.
                    Your prompt should be 1-3 sentences long and highly descriptive.",
                
                _ => Environment.GetEnvironmentVariable("PROMPT_CONFIG_SYSTEM_DEFAULT") ?? @"
                    You are an expert at creating detailed image generation prompts. 
                    Your task is to create a vivid, detailed prompt for image generation based on the user's request.
                    Your prompt should be 1-3 sentences long and highly descriptive."
            };
        }
        
        private string GetUserMessagePrefix(string imageType)
        {
            string envVarName = $"PROMPT_CONFIG_USER_PREFIX_{imageType.ToUpperInvariant()}";
            string? config = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(config))
            {
                return config;
            }

            return imageType.ToLower() switch
            {
                "bw" => "Create a black and white artistic image with strong contrast based on: ",
                "color" => "Create a vibrant, colorful image based on: ",
                "sticker" => "Create a cute, simple sticker design based on: ",
                "whisperframe" => "Create an image the represents the topic of the audio transcript, draw a single topic: ",
                "raw" => "Create a detailed image based on: ",
                _ => Environment.GetEnvironmentVariable("PROMPT_CONFIG_USER_PREFIX_DEFAULT") ?? $"Create a {imageType} image based on: "
            };
        }

        private string GetFinalPromptPrefix(string imageType)
        {
            string envVarName = $"PROMPT_CONFIG_FINAL_PREFIX_{imageType.ToUpperInvariant()}";
            string? config = Environment.GetEnvironmentVariable(envVarName);
            if (!string.IsNullOrEmpty(config))
            {
                return config;
            }

            return imageType.ToLower() switch
            {
                "bw" => "Create a black and white artistic image with strong contrast based on: ",
                "color" => "Create a vibrant, colorful image based on: ",
                "sticker" => "Create a simple sticker in black and white that can be easily printed on a thermal printer. Do not use words or phrases, letters are ok. Keep the lines simple and clean. ",
                "whisperframe" => "Create an image that represents the topic of the audio transcript. Do not draw people sitting around a table, do not draw bicycles. Draw a single topic. ",
                "raw" => "Create a detailed image based on: ",
                _ => Environment.GetEnvironmentVariable("PROMPT_CONFIG_FINAL_PREFIX_DEFAULT") ?? $"Create a {imageType} image based on: "
            };
        }
        
        private async Task<string> GenerateImagePromptAsync(ImageGenerationRequest request)
        {               
            if (request.Type.ToLower() == "raw")
            {
                _logger.LogInformation("Raw image type requested, using details directly as prompt.");
                return request.Details!;
            }

            try
            {
                string prompt = GetFinalPromptPrefix(request.Type);

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
                    Temperature = 0.7f, MaxOutputTokenCount = 1000,
                });
                if (response.Value.Content[0].Text != null)
                {
                    return $"{prompt} {response.Value.Content[0].Text}";
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
        
        private async Task<byte[]> GenerateAiImage(string prompt, string? requestedSize)
        {
            HttpResponseMessage? response = null;
            string responseBody = string.Empty;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_openAIApiKey}");

                var requestBodyPayload = new
                {
                    model = "gpt-image-1",
                    prompt = prompt,
                    quality = "high",
                    size = string.IsNullOrWhiteSpace(requestedSize) ? "1024x1024" : requestedSize,
                };

                response = await client.PostAsJsonAsync("https://api.openai.com/v1/images/generations", requestBodyPayload);
                responseBody = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenAI API call failed. Status: {StatusCode}. Response: {ErrorBody}", response.StatusCode, responseBody);
                    throw new OpenAIApiException($"OpenAI API request failed with status {response.StatusCode}", responseBody, response.StatusCode);
                }

                var result = JsonSerializer.Deserialize<ImageGenerationResponse>(responseBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.data == null || result.data.Length == 0 || string.IsNullOrEmpty(result.data[0].b64_json))
                {
                    _logger.LogError("No image data (b64_json) received from OpenAI API despite successful status. Full response: {ResponseBody}", responseBody);
                    throw new OpenAIApiException("No image data (b64_json) received from OpenAI API despite successful status.", responseBody, response.StatusCode);
                }

                return Convert.FromBase64String(result.data[0].b64_json);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "Error parsing JSON response from OpenAI API. Response body attempt: {ResponseBody}", responseBody);
                throw new OpenAIApiException($"Error parsing JSON response from OpenAI API: {jsonEx.Message}. Response body: {responseBody}", responseBody, response?.StatusCode ?? System.Net.HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                if (ex is OpenAIApiException) throw;
                _logger.LogError(ex, "Unexpected error in GenerateAiImage. Response body if available: {ResponseBody}", responseBody);
                throw;
            }
        }

        private class ImageGenerationResponse
        {
            public ImageData[] data { get; set; } = Array.Empty<ImageData>();
        }

        private class ImageData
        {
            public string? b64_json { get; set; }
        }
    }
}
