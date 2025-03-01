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
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var containerName in _containersToDelete)
            {
                try
                {
                    await _blobServiceClient.DeleteBlobContainerAsync(containerName);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }


        private static HttpRequest CreateEmptyRequest()
        {
            return new DefaultHttpContext().Request;
        }

        public async Task Run_WithValidRequest_ReturnsAcceptedResultAndStoresBlob()
            string groupName = "testgroup" + Guid.NewGuid().ToString("n");
            _containersToDelete.Add(groupName);

                $"\"Group\":\"{groupName}\"," +
using Azure.Storage.Blobs;
    public class GenerateImageTests : IAsyncDisposable
        private readonly BlobServiceClient _blobServiceClient;
        private readonly List<string> _containersToDelete = new();
        public GenerateImageTests()
        {
            _blobServiceClient = new BlobServiceClient("UseDevelopmentStorage=true");
        }

        public async ValueTask DisposeAsync()
        {
            // Cleanup any containers created during tests
            foreach (var containerName in _containersToDelete)
            {
                try
                {
                    await _blobServiceClient.DeleteBlobContainerAsync(containerName);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        // Helper method to create a fake HttpRequest with the given body
        private static HttpRequest CreateHttpRequest(string body)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return request;
        }

        [Fact]
        public async Task Run_WithValidRequest_ReturnsAcceptedResultAndStoresBlob()
        {
            // Arrange
            string groupName = "testgroup" + Guid.NewGuid().ToString("n"); // Ensure unique container name
            _containersToDelete.Add(groupName); // Mark for cleanup

            string json = "{" +
                "\"Email\":\"test@example.com\"," +
                $"\"Group\":\"{groupName}\"," +
                "\"Type\":\"bw\"," +
                "\"SendEmail\":true," +
                "\"Details\":\"Some details\"," +
                "\"Name\":\"John Doe\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            IActionResult result = await function.Run(request);

            var acceptedResult = Assert.IsType<AcceptedResult>(result);
            string responseJson = JsonSerializer.Serialize(acceptedResult.Value);
            var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            
            Assert.NotNull(responseData);
            Assert.True(responseData.ContainsKey("RequestId"));
            Assert.True(responseData.ContainsKey("Status"));
            Assert.True(responseData.ContainsKey("Message"));
            Assert.True(responseData.ContainsKey("ImageUrl"));
            
            string requestId = responseData["RequestId"].GetString();
            var containerClient = _blobServiceClient.GetBlobContainerClient(groupName);
            var blobClient = containerClient.GetBlobClient($"{requestId}.jpg");
            var exists = await blobClient.ExistsAsync();
            Assert.True(exists.Value, "Blob should exist in storage");

            var getResult = await function.GetImage(CreateEmptyRequest(), groupName, requestId);
            var fileResult = Assert.IsType<FileContentResult>(getResult);
            Assert.Equal("image/jpeg", fileResult.ContentType);
            Assert.NotEmpty(fileResult.FileContents);
            
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
            string groupName = "testgroup" + Guid.NewGuid().ToString("n");
            _containersToDelete.Add(groupName);
            
            var containerClient = _blobServiceClient.GetBlobContainerClient(groupName);
            await containerClient.CreateIfNotExistsAsync();

            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            var result = await function.GetImage(CreateEmptyRequest(), groupName, "nonexistentimage");

            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal($"Image 'nonexistentimage' not found in group '{groupName}'", notFoundResult.Value);
        }

        [Fact]
            // Assert
            Assert.True(responseData.ContainsKey("ImageUrl"));
            Assert.Equal("Accepted", responseData["Status"].GetString());
            
            // Verify blob was created
            string requestId = responseData["RequestId"].GetString();
            var containerClient = _blobServiceClient.GetBlobContainerClient(groupName);
            var blobClient = containerClient.GetBlobClient($"{requestId}.jpg");
            var exists = await blobClient.ExistsAsync();
            Assert.True(exists.Value, "Blob should exist in storage");
        }

        [Fact]
        public async Task Run_WithInvalidImageType_ReturnsBadRequest()
        {
            // Arrange: image type is not one of the allowed values
            string json = "{" +
                "\"Email\":\"test@example.com\"," +
                "\"Group\":\"newsletter\"," +
                "\"Type\":\"invalid\"," +
                "\"SendEmail\":true" +
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
                "\"Email\":\"test@example.com\"," +
                "\"Group\":\"newsletter\"," +
                "\"SendEmail\":true" +
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
    }
}
