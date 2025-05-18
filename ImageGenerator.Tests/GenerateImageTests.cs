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
    public class GenerateImageTests
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly List<string> _containersToDelete = new();
        private readonly IHttpClientFactory _httpClientFactory;

        public GenerateImageTests()
        {

        }

        
    }
}
