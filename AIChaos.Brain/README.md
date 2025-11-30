# AI Chaos Brain (C# Edition)

The C# version of the AI Chaos Brain - a server that receives chaos commands and generates Lua code for Garry's Mod.

## Features

- 🤖 **AI Code Generation** - Uses OpenRouter API to generate Lua code from natural language requests
- 📺 **Twitch Integration** - OAuth login and chat listener for Twitch commands
- 🎬 **YouTube Integration** - OAuth login and Super Chat listener for YouTube Live
- 🎮 **Web Control Panel** - Easy-to-use public interface for sending commands
- 📜 **Command History** - Track, repeat, and undo previous commands (password protected)
- 🔧 **Setup Wizard** - Easy configuration with model selection (password protected)
- 🌐 **Built-in Tunnel Support** - Start ngrok or LocalTunnel directly from the UI
- 🔒 **Password Protection** - Setup and History pages are protected by admin password

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or later
- An [OpenRouter](https://openrouter.ai/) API key
- (Optional) ngrok or LocalTunnel for public access

### Running the Brain

```bash
cd AIChaos.Brain
dotnet run
```

The server will start on `http://localhost:5000`

Open your browser to:
- `http://localhost:5000/` - Control Panel (Public)
- `http://localhost:5000/setup` - Setup & Configuration (Password Protected)
- `http://localhost:5000/history` - Command History (Password Protected)

## Setup Guide

### 1. Set Admin Password

On first visit to `/setup`, you'll be prompted to set an admin password. This protects the Setup and History pages from public access.

### 2. OpenRouter API (Required)

1. Go to [openrouter.ai/keys](https://openrouter.ai/keys)
2. Create an account and generate an API key
3. Enter the API key in the Setup page
4. Choose your preferred AI model

### 3. Tunnel Configuration (For GMod Access)

The Setup page includes built-in tunnel management:

**ngrok:**
1. (Optional) Get an auth token from [ngrok.com](https://ngrok.com/download)
2. Click "Start ngrok" in the Setup page
3. The public URL will be displayed and auto-configured in the Lua file

**LocalTunnel:**
1. Install: `npm install -g localtunnel`
2. Click "Start LocalTunnel" in the Setup page
3. Note: LocalTunnel requires entering your public IP as a password on first visit

### 4. Twitch Integration (Optional)

1. Go to [dev.twitch.tv/console](https://dev.twitch.tv/console)
2. Create a new application
3. Set the OAuth Redirect URL to: `http://localhost:5000/api/setup/twitch/callback`
4. Copy the Client ID and Client Secret to the Setup page
5. Click "Login with Twitch" to authenticate
6. Click "Start Listening" to begin receiving chat commands

### 5. YouTube Integration (Optional)

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project
3. Enable the YouTube Data API v3
4. Create OAuth 2.0 credentials (Web Application)
5. Set the redirect URI to: `http://localhost:5000/api/setup/youtube/callback`
6. Copy the Client ID and Client Secret to the Setup page
7. Click "Login with YouTube" to authenticate
8. Enter your live stream's Video ID and click "Start Listening"

## GMod Setup

The GMod addon in the `lua/` folder needs to connect to this brain. When you start a tunnel, the Lua file is automatically updated with the correct URL.

Manually update `lua/autorun/ai_chaos_controller.lua` if needed.

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Public control panel |
| `/setup` | GET | Setup page (password protected) |
| `/history` | GET | History page (password protected) |
| `/poll` | POST | GMod polls for commands |
| `/trigger` | POST | Send a chaos command |
| `/api/history` | GET | Get command history |
| `/api/setup/status` | GET | Get setup status |
| `/api/setup/models` | GET | Get available AI models |
| `/api/setup/tunnel/ngrok/start` | POST | Start ngrok tunnel |
| `/api/setup/tunnel/localtunnel/start` | POST | Start LocalTunnel |
| `/api/setup/tunnel/stop` | POST | Stop current tunnel |

## Development

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run in development mode
dotnet run

# Publish for production
dotnet publish -c Release
```

## Troubleshooting

### "Failed to connect to brain"
- Make sure the brain is running on port 5000
- Check firewall settings

### Twitch not receiving messages
- Verify OAuth token is valid (re-authenticate if needed)
- Make sure channel name is correct
- Check cooldown settings

### YouTube "Invalid video ID"
- The stream must be actively live
- Use the video ID from the URL, not the channel ID
- Make sure you're authenticated with the correct account

### Tunnel not starting
- ngrok: Make sure ngrok is installed and in PATH
- LocalTunnel: Make sure Node.js and `localtunnel` are installed (`npm install -g localtunnel`)

## License

See the main repository for license information.
