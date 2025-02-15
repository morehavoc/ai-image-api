using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using System;

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
                var request = JsonSerializer.Deserialize<ImageGenerationRequest>(requestBody, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (request == null)
                {
                    return new BadRequestObjectResult("Invalid request body");
                }

                // Validate the image type
                if (!IsValidImageType(request.Type))
                {
                    return new BadRequestObjectResult("Invalid image type. Must be one of: bw, color, sticker, whisperframe");
                }

                // TODO: Implement the image generation workflow
                // For now, return a mock response
                var response = new
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Status = "Accepted",
                    Message = "Image generation request received"
                };

                return new AcceptedResult("", response);
            }
            catch (JsonException)
            {
                return new BadRequestObjectResult("Invalid JSON format");
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
