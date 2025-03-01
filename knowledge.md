# AI Image Generator Project

## Local Development Setup

### Azure Storage
- Uses Azurite for local development
- Connection string: "UseDevelopmentStorage=true"
- Containers are created per group name
- Images stored as {guid}.jpg in group containers

### OpenAI Integration
- Uses direct HTTP calls to OpenAI API for generating image prompts
- Requires OPENAI_API_KEY environment variable to be set in local.settings.json
- Uses GPT-4 for generating image prompts (configurable via OPENAI_MODEL env var)
- Currently uses a dummy image generator, but can be extended to use DALL-E
- Fallback mechanisms in place if API keys are missing or API calls fail

### Configuration
- local.settings.json required for local development
- AZURE_STORAGE_CONNECTION_STRING must be set
- OPENAI_API_KEY must be set for AI image generation
- OPENAI_MODEL can be set to specify which GPT model to use (defaults to "gpt-4")
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
- prompt: The generated prompt used for image creation

### GET /api/image/{group}/{id}
Retrieves a generated image:
- group: The container/category of the image
- id: The unique identifier (GUID) of the image

Returns:
- The image file with content-type image/jpeg
- 404 if the group or image doesn't exist
