# Copilot Instructions

This project is a C# application that automates poster overlays for Plex media collections managed by Maintainerr. Please follow these guidelines:

- Use C# best practices and .NET 8 or later.
- Structure code for maintainability and extensibility.
- All configuration should be loaded from a JSON file and environment variables.
- Support both Windows and Unix-style paths and file operations.
- Output the full configuration at startup.
- Use async/await for all I/O and API calls.
- Handle errors gracefully and log meaningful messages.
- Do not hardcode sensitive information; use config and environment variables.
- Ensure the asset directory structure mirrors Plex library and media folder hierarchy.
- Overlay appearance must be configurable (text, color, font, transparency, etc.).
- Maintain state between runs to track overlays and restore originals as needed.
- Prefer NuGet packages for HTTP, image processing, and JSON handling.
- Write clear comments and documentation for all major functions and classes.
