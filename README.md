# Personal Image Generator

This application provides a series of rest endpoints that generate an image for a person using an AI model. Mostly this gets used as part of a larger process, like for a sticker printer at a conference or for my newsletter where people that sign up get a new image for them.

Each image generation request requires:
- email address (required) - The email of the person, this email will receive a copy of this image when the generation is complete!
- group (required) - This is a tag that groups the images together in some way, for example we might have one for a conference or one for my newsletter. The group will determine what "folder" the image is stored within inside the blob store.
- type (required) - tells us the type of image we will generate can be:
	- bw - black and white
	- color - full color image
	- sticker - black and white, but designed for the sticker printer
	- whisperframe - Generate a full image directly, specific to the whisperframe app.
- send_email (required, default false) - Indicates if the process should send the email to the end user or not.
- details (optional) - A string provided by the user to help target the image generation
- name (optional) - The person's name

Each group defined above will be registered in the settings file and will have the following properties:
- prompt - The AI prompt used to generate the image. This is specific to the group so that you can taylor it for each conference or purpose. It can have the following tags that are replaced dynamically:
	- {name} - the name of the user, if provided
	- {email} - the email of the user
- email - The email html that will be sent to the user. It can have the following tags that are replaced dynamically:
	- {name}
	- {email}
	- {prompt} - The image prompt created for the user
	- {url} - Public URL for the image


When a request is made the workflow looks like this:
1. Request is made with parameters
2. Generate a unique and random GUID for this request.
2. Extract the prompt text for the specified group
3. Replace variables within the prompt text.
4. Add additional prompt text based on `type`
5. Send prompt to Claude to get image generation prompt
6. Add additional image generation prompt details based on `type`
7. Generate image using Dalle2
8. Save the Image to the blob store using the group and GUID
9. Send email to the email address (if instructed to)
10. Return a result that contains the prompt and url of the image


The service will have the following rest endpoints

- `/api/generate` (POST) - Main entrypoint, generates the image using the workflow above
- `/api/image/{group}/{guid}` (GET) - The URL of an image, extracts it from the blob store and returns it.