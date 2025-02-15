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

namespace ImageGenerator.Tests
{
    public class GenerateImageTests
    {
        // Helper method to create a fake HttpRequest with the given body
        private static HttpRequest CreateHttpRequest(string body)
        {
            var context = new DefaultHttpContext();
            var request = context.Request;
            // Set the request body stream to the provided JSON content
            request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return request;
        }

        [Fact]
        public async Task Run_WithValidRequest_ReturnsAcceptedResult()
        {
            // Arrange: valid JSON input
            string json = "{" +
                "\"Email\":\"test@example.com\"," +
                "\"Group\":\"newsletter\"," +
                "\"Type\":\"bw\"," +
                "\"SendEmail\":true," +
                "\"Details\":\"Some details\"," +
                "\"Name\":\"John Doe\"" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert: should be an AcceptedResult with the proper response properties
            var acceptedResult = Assert.IsType<AcceptedResult>(result);
            // Serialize the anonymous response for inspection
            string responseJson = JsonSerializer.Serialize(acceptedResult.Value);
            var responseData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseJson);
            Assert.NotNull(responseData);
            Assert.True(responseData.ContainsKey("RequestId"));
            Assert.True(responseData.ContainsKey("Status"));
            Assert.True(responseData.ContainsKey("Message"));
            Assert.Equal("Accepted", responseData["Status"].GetString());
            Assert.Equal("Image generation request received", responseData["Message"].GetString());
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

            // Act
            IActionResult result = await function.Run(request);

            // Assert: expecting a BadRequest result with proper error message
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid image type. Must be one of: bw, color, sticker, whisperframe", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithInvalidJSON_ReturnsBadRequest()
        {
            // Arrange: the provided JSON is not valid
            string invalidJson = "Not a JSON";
            HttpRequest request = CreateHttpRequest(invalidJson);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert: expecting a BadRequest result with error message "Invalid JSON format"
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid JSON format", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithMissingTypeField_ReturnsBadRequest()
        {
            // Arrange: missing the "Type" property should yield default empty string and fail validation
            string json = "{" +
                "\"Email\":\"test@example.com\"," +
                "\"Group\":\"newsletter\"," +
                "\"SendEmail\":true" +
            "}";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert: should return BadRequest for invalid image type because type defaults to empty string
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid image type. Must be one of: bw, color, sticker, whisperframe", badRequestResult.Value);
        }

        [Fact]
        public async Task Run_WithEmptyBody_ReturnsBadRequest()
        {
            // Arrange: empty request body should cause deserialization failure
            string json = "";
            HttpRequest request = CreateHttpRequest(json);
            var logger = NullLogger<GenerateImage>.Instance;
            var function = new GenerateImage(logger);

            // Act
            IActionResult result = await function.Run(request);

            // Assert: expecting BadRequest with "Invalid JSON format"
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid JSON format", badRequestResult.Value);
        }
    }
}
