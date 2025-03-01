# AI Image Generator Project

## Local Development Setup

### Azure Storage
- Uses Azurite for local development
- Connection string: "UseDevelopmentStorage=true"
- Containers are created per group name
- Images stored as {guid}.jpg in group containers

### Configuration
- local.settings.json required for local development
- AZURE_STORAGE_CONNECTION_STRING must be set
- FUNCTIONS_WORKER_RUNTIME set to "dotnet-isolated"

## Testing
- Tests clean up containers after each run
- Each test uses unique container names to avoid conflicts
- Verifies both API responses and blob storage operations

## API Endpoints

### POST /api/generate
Generates an AI image based on input parameters:
- email (required): User's email
- group (required): Container/category for the image
- type (required): bw, color, sticker, or whisperframe
- sendEmail (required): Whether to email the result
- details (optional): Additional generation details
- name (optional): User's name

Returns:
- requestId: Unique GUID for the request
- status: "Accepted"
- message: Status message
- imageUrl: URL to access the generated image
