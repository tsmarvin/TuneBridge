# TuneBridge API Documentation

Welcome to the TuneBridge API documentation!

## Overview

**TuneBridge** is a cross-platform music link converter and lookup service that bridges Apple Music and Spotify. It provides both a web interface and a Discord bot for seamless music sharing across different streaming platforms.

The application uses official APIs from both services to ensure accurate matching through standardized identifiers (ISRC for tracks, UPC for albums). When matches cannot be found via external IDs, the application performs fuzzy matching using metadata to find equivalent content.

## Features

- 🎵 **Music Link Conversion**: Convert music links between Apple Music and Spotify
- 🔍 **Multiple Lookup Methods**: Search by URL, ISRC, UPC, or title/artist
- 🤖 **Discord Bot Integration**: Automatically detect and convert music links in Discord messages
- 🌐 **Web API**: RESTful API endpoints for programmatic access
- 🖥️ **Web Interface**: Simple browser-based UI for manual lookups
- 🐳 **Docker Support**: Easy deployment with Docker containers

## Key Components

### Core Interfaces

- **`IMusicLookupService`**: Common interface for provider-specific music lookup operations (Apple Music, Spotify)
- **`IMediaLinkService`**: Aggregates results from multiple providers and handles cross-platform lookups

### Service Implementations

- **`AppleMusicLookupService`**: Apple Music provider implementation using MusicKit API
- **`SpotifyLookupService`**: Spotify provider implementation using Web API
- **`DefaultMediaLinkService`**: Default implementation that queries all enabled providers in parallel

### Controllers

- **`MusicLookupController`**: REST API endpoints for cross-platform music lookup and link translation
  - POST `/music/lookup/url` - Streaming URL lookup
  - POST `/music/lookup/urlList` - Batch URL lookup
  - POST `/music/lookup/isrc` - Exact track lookup by ISRC
  - POST `/music/lookup/upc` - Exact album lookup by UPC
  - POST `/music/lookup/title` - Search by title and artist

### Discord Integration

- **`MessageCreateGatewayHandler`**: Handles Discord messages and automatically converts music links

## Lookup Methods

TuneBridge supports multiple ways to find music:

1. **URL-based**: Paste Apple Music or Spotify URLs to find equivalents
2. **ISRC (tracks)**: Most reliable - uses International Standard Recording Code
3. **UPC (albums)**: Exact album matching using Universal Product Code
4. **Title/Artist**: Fuzzy search when standardized IDs aren't available

## Technology Stack

- **.NET 9.0** - Cross-platform framework
- **ASP.NET Core** - Web framework and API
- **NetCord** - Discord bot library
- **Apple MusicKit API** - Apple Music integration
- **Spotify Web API** - Spotify integration

## Getting Started

For setup instructions, API credentials, and deployment options, see the [README](https://github.com/tsmarvin/TuneBridge#readme).

Browse the API reference sections for detailed documentation of classes, methods, and types.
