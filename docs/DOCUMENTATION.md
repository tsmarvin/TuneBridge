# Documentation Guide

This project uses [DocFX](https://dotfx.github.io/) to generate API documentation from XML comments in the source code.

## Building Documentation Locally

To build the documentation locally:

```bash
# Restore the DocFX tool
dotnet tool restore

# Build the documentation
dotnet docfx docfx.json
```

The generated documentation will be in the `_site` directory. You can open `_site/index.html` in a browser to view it.

## Updating Documentation

The documentation is automatically generated from:

1. **XML comments** in the C# source code
2. **Markdown files** in the `docs/` directory

### Adding XML Comments

Add XML comments to your public classes, methods, and properties:

```csharp
/// <summary>
/// Brief description of what this does.
/// </summary>
/// <param name="paramName">Description of the parameter.</param>
/// <returns>Description of what is returned.</returns>
public string MyMethod(string paramName)
{
    // ...
}
```

### Adding Documentation Pages

Add new `.md` files to the `docs/` directory and update `docs/toc.yml` to include them in the navigation.

## Automatic Deployment

The documentation is automatically built and deployed to GitHub Pages when changes are pushed to the `main` branch via the `.github/workflows/docs.yml` workflow.

View the live documentation at: https://tsmarvin.github.io/TuneBridge/

## Configuration

The DocFX configuration is in `docfx.json`. Key settings:

- **metadata.src**: Specifies which projects to document
- **build.content**: Specifies which files to include in the output
- **build.template**: The DocFX template to use (default + modern)
- **build.globalMetadata**: Site-wide metadata like title and footer
