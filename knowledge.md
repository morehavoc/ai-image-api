# AI Image Generator Project

## Local Development Setup

### Azure Storage
- Uses Azurite for local development
- Connection string: "UseDevelopmentStorage=true"
- Default container name: "images" (configurable via BLOB_CONTAINER_NAME env var)
- Images stored as {group}/{guid}.jpg in container
- Groups are virtual folders within the container

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
- BLOB_CONTAINER_NAME can be set to specify the container name (defaults to "images")
- FUNCTIONS_WORKER_RUNTIME set to "dotnet-isolated"

## Testing
- Tests use a separate container named "test-images"
- Tests clean up container after each run
- Verifies both API responses and blob storage operations

## API Endpoints

### POST /api/generate
Generates an AI image based on input parameters:
- group (required): Virtual folder path for the image
- type (required): bw, color, sticker, whisperframe, or raw
- details (optional): Additional generation details. Required if type is "raw".
- name (optional): User's name

Returns:
- requestId: Unique GUID for the request
- status: "Complete"
- message: Status message
- imageUrl: URL to access the generated image
- prompt: The generated prompt used for image creation

### GET /api/image/{group}/{id}
Retrieves a generated image:
- group: The virtual folder path of the image
- id: The unique identifier (GUID) of the image

Returns:
- The image file with content-type image/jpeg
- 404 if the image doesn't exist
