using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using morehavoc.ai;
using Azure.Storage.Blobs;

namespace ImageGenerator.Tests
{
    public class GenerateImageTests : IAsyncDisposable
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly List<string> _containersToDelete = new();

        public GenerateImageTests()
        {
            _blobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true");
            
            // Set up environment variables for testing
            Environment.SetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING", "UseDevelopmentStorage=true");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-fake-key");
            Environment.SetEnvironmentVariable("BLOB_CONTAINER_NAME", "test-images");
        }

        public async ValueTask DisposeAsync()
        {
            // Cleanup any containers created during tests
            try
            {
                await _blobServiceClient.DeleteBlobContainerAsync("test-images");
            }
            catch
            {
                // Ignore cleanup errors
            }
            
            // Clean up environment variables
            Environment.SetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            Environment.SetEnvironmentVariable("BLOB_CONTAINER_NAME", null);
        }

        // Helper method to create a fake HttpRequest with the given body
        private static HttpRequest CreateHttpRequest(string body)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return request;
        }

        private static HttpRequest CreateEmptyRequest()
        {
            return new DefaultHttpContext().Request;
        }

        [Fact]
        public async Task Run_WithValidRequest_ReturnsAcceptedResultAndStoresBlob()
        {
            // Arrange
            string groupName = "testgroup";

            string json = "{" +
                $"\"Group\":\"{groupName}\"," +
                "\"Type\":\"bw\"," +
                "\"Details\":\"Some details\"," +
                "\"Name\":\"John Doe\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert
            var acceptedResult = Assert.IsType<AcceptedResult>(result);
            string responseJson = JsonSerializer.Serialize(acceptedResult.Value);
            var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            
            Assert.NotNull(responseData);
            Assert.True(responseData.ContainsKey("RequestId"));
            Assert.True(responseData.ContainsKey("Status"));
            Assert.True(responseData.ContainsKey("Message"));
            Assert.True(responseData.ContainsKey("ImageUrl"));
            Assert.True(responseData.ContainsKey("Prompt"));
            Assert.Equal("Complete", responseData["Status"].GetString());
            
            // Verify blob was created
            string requestId = responseData["RequestId"].GetString();
            var containerClient = _blobServiceClient.GetBlobContainerClient("test-images");
            var blobClient = containerClient.GetBlobClient($"{groupName}/{requestId}.jpg");
            var exists = await blobClient.ExistsAsync();
            Assert.True(exists.Value, "Blob should exist in storage");
        }

        [Fact]
        public async Task GenerateAndRetrieveImage_FullFlow_Success()
        {
            // Arrange
            string groupName = "testgroup";
            _containersToDelete.Add(groupName);

            string json = "{" +
                $"\"Group\":\"{groupName}\"," +
                "\"Type\":\"bw\"," +
                "\"Details\":\"A simple test image\"," +
                "\"Name\":\"Test User\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act - Generate the image
            IActionResult generateResult = await function.Run(request);
            
            // Assert - Verify generation result
            var acceptedResult = Assert.IsType<AcceptedResult>(generateResult);
            string responseJson = JsonSerializer.Serialize(acceptedResult.Value);
            var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            
            Assert.NotNull(responseData);
            string requestId = responseData["RequestId"].GetString();
            
            // Act - Retrieve the generated image
            IActionResult getResult = await function.GetImage(CreateEmptyRequest(), groupName, requestId);
            
            // Assert - Verify retrieved image
            var fileResult = Assert.IsType<FileContentResult>(getResult);
            Assert.Equal("image/jpeg", fileResult.ContentType);
            Assert.NotEmpty(fileResult.FileContents);
            
            // Verify the image data is valid (at least check it's not empty and has a reasonable size)
            Assert.True(fileResult.FileContents.Length > 100, "Image data should have a reasonable size");
        }

        [Fact]
        public async Task GetImage_NonexistentGroup_ReturnsNotFound()
        {
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            var result = await function.GetImage(CreateEmptyRequest(), "nonexistentgroup", "someid");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Group 'nonexistentgroup' not found", notFoundResult.Value);
        }

        [Fact]
        public async Task GetImage_NonexistentImage_ReturnsNotFound()
        {
            string groupName = "testgroup";
            _containersToDelete.Add(groupName);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient("test-images");
            await containerClient.CreateIfNotExistsAsync();

            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            var result = await function.GetImage(CreateEmptyRequest(), groupName, "nonexistentimage");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal($"Image 'nonexistentimage' not found in group '{groupName}'", notFoundResult.Value);
        }

        [Fact]
        public async Task Run_WithInvalidImageType_ReturnsBadRequest()
        {
            // Arrange: image type is not one of the allowed values
            string json = "{" +
                "\"Group\":\"newsletter\"," +
                "\"Type\":\"invalid\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            IActionResult result = await function.Run(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid image type. Must be one of: bw, color, sticker, whisperframe", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithInvalidJSON_ReturnsBadRequest()
        {
            string invalidJson = "Not a JSON";
            HttpRequest request = CreateHttpRequest(invalidJson);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            IActionResult result = await function.Run(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid JSON format", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithMissingTypeField_ReturnsBadRequest()
        {
            string json = "{" +
                "\"Group\":\"newsletter\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            IActionResult result = await function.Run(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid image type. Must be one of: bw, color, sticker, whisperframe", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithEmptyBody_ReturnsBadRequest()
        {
            string json = "";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            IActionResult result = await function.Run(request);

            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid JSON format", badRequestResult.Value);
        }

        [Theory]
        [InlineData("123group", "Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.")]
        [InlineData("Group", "Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.")]
        [InlineData("group-name", "Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.")]
        [InlineData("group_name", "Invalid group name. Must start with a lowercase letter and contain only lowercase letters and numbers.")]
        public async Task Run_WithInvalidGroupName_ReturnsBadRequest(string groupName, string expectedMessage)
        {
            // Arrange
            string json = "{" +
                $"\"Group\":\"{groupName}\"," +
                "\"Type\":\"bw\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(expectedMessage, badRequestResult.Value);
        }
    }
}
