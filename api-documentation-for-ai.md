# AI Image Generator API Documentation

## Base URL
`/api`

## Endpoints

### Generate Image
`POST /generate`

Generate an AI image based on provided parameters.

#### Request Body (JSON)
```json
{
    "group": "string (required)",     // Must start with lowercase letter, contain only lowercase letters and numbers
    "type": "string (required)",      // One of: "bw", "color", "sticker", "whisperframe", "raw"
    "details": "string (optional)",    // Additional generation details (required if type is "raw")
    "name": "string (optional)"       // User's name
}
```

#### Response (200 Accepted)
```json
{
    "requestId": "string (guid)",
    "status": "Complete",
    "message": "Image generation request complete",
    "imageUrl": "string",             // Format: "api/image/{group}/{requestId}"
    "prompt": "string"                // The generated prompt used for image creation
}
```

#### Error Responses
- 400 Bad Request
  - Invalid JSON format
  - Invalid group name (must start with lowercase letter, contain only lowercase letters and numbers)
  - Invalid image type (must be one of: bw, color, sticker, whisperframe, raw)
  - Missing or empty details field when type is "raw"
- 500 Internal Server Error

### Retrieve Generated Image
`GET /image/{group}/{id}`

Retrieve a previously generated image.

#### URL Parameters
- group: The group name used during generation
- id: The requestId returned from the generate endpoint

#### Response
- 200 OK: Image file (image/jpeg)
- 404 Not Found: If group or image doesn't exist
- 500 Internal Server Error

## Image Types
- `bw`: Black and white artistic image with strong contrast
- `color`: Vibrant, colorful image
- `sticker`: Simple, black and white cartoon-style sticker design
- `whisperframe`: Dreamy, ethereal, slightly abstract image
- `raw`: Uses the details field directly as the image generation prompt, bypassing any additional AI-driven prompt engineering

## Example Usage
```python
# Generate an image
response = await post("/api/generate", {
    "group": "newsletter",
    "type": "color",
    "details": "A sunset over mountains",
    "name": "John"
})

# Get the image URL from response
image_url = response.imageUrl

# Retrieve the image
image = await get(image_url)
```