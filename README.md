# SlackBridge.Claude

Remote control [Claude Code](https://claude.com/claude-code) from Slack. Post prompts in a Slack channel from your phone, get responses back in threads.

```
You (Slack)  →  Socket Mode WebSocket  →  C# MCP Server (stdio)  →  Claude Code session
                                       ←  reply/react/edit tools  ←
```

## How It Works

- Messages you send in Slack are forwarded to your Claude Code session as MCP channel notifications
- Claude Code processes the prompt and replies back via threaded Slack messages
- One Slack channel per Claude Code instance — channel name auto-derives from the working directory (e.g., project `MyApp` → `#my-app`)
- Uses Socket Mode (no public URL needed, works on free Slack plans)
- The channel is auto-created if it doesn't exist

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Claude Code](https://claude.com/claude-code) CLI installed
- A Slack workspace where you can create apps

## Setup

### 1. Create a Slack App

1. Go to [api.slack.com/apps](https://api.slack.com/apps) and click **Create New App** → **From scratch**
2. Name it whatever you like (e.g., "Claude Bridge") and select your workspace

### 2. Configure Bot Token Scopes

Under **OAuth & Permissions** → **Scopes** → **Bot Token Scopes**, add:

| Scope | Purpose |
|-------|---------|
| `channels:history` | Read messages in public channels |
| `channels:read` | List channels to find/resolve the target channel |
| `channels:manage` | Auto-create the channel if it doesn't exist |
| `chat:write` | Post replies and messages |
| `reactions:read` | Read reactions |
| `reactions:write` | Add/remove emoji reactions on messages |
| `files:write` | Upload files to Slack |
| `users:read` | Resolve user display names |

### 3. Enable Socket Mode

Under **Socket Mode**, toggle it **on**. This generates an **App-Level Token** — create one with the `connections:write` scope. Save this token (`xapp-...`).

### 4. Enable Event Subscriptions

Under **Event Subscriptions**, toggle it **on**, then under **Subscribe to bot events**, add:

- `message.channels` — triggers when a message is posted in a public channel

### 5. Install the App

Under **Install App**, click **Install to Workspace** and authorize. Save the **Bot User OAuth Token** (`xoxb-...`).

### 6. Set Environment Variables

Set these as persistent environment variables (not just shell exports — they need to be available to Claude Code):

```bash
# Linux/macOS
export SLACK_BOT_TOKEN="xoxb-your-bot-token"
export SLACK_APP_TOKEN="xapp-your-app-token"

# Windows (PowerShell — sets permanently for your user)
[System.Environment]::SetEnvironmentVariable('SLACK_BOT_TOKEN', 'xoxb-your-bot-token', 'User')
[System.Environment]::SetEnvironmentVariable('SLACK_APP_TOKEN', 'xapp-your-app-token', 'User')
```

Optionally set `SLACK_CHANNEL` to override the auto-derived channel name.

### 7. Build & Publish

```bash
git clone https://github.com/blakeja/slack-bridge-claude.git
cd slack-bridge-claude

# Development (requires .NET 10 SDK):
dotnet build -c Release

# Self-contained publish (no .NET runtime needed on target machine):
# Windows
dotnet publish src/SlackBridge.Claude -c Release -r win-x64
dotnet publish src/SlackBridge.Claude.Hook -c Release -r win-x64

# macOS (Apple Silicon)
dotnet publish src/SlackBridge.Claude -c Release -r osx-arm64
dotnet publish src/SlackBridge.Claude.Hook -c Release -r osx-arm64

# macOS (Intel)
dotnet publish src/SlackBridge.Claude -c Release -r osx-x64
dotnet publish src/SlackBridge.Claude.Hook -c Release -r osx-x64

# Linux
dotnet publish src/SlackBridge.Claude -c Release -r linux-x64
dotnet publish src/SlackBridge.Claude.Hook -c Release -r linux-x64
```

### 8. Run with Claude Code

```bash
claude --dangerously-load-development-channels server:slack-bridge
```

This starts Claude Code with the SlackBridge MCP server. The server will:
1. Connect to Slack via Socket Mode
2. Find or create a channel named after the current directory
3. Listen for messages and forward them to Claude Code
4. Provide tools for Claude Code to reply, react, and edit messages

### 9. Invite the Bot

The bot auto-creates the channel, but if the channel already exists, make sure the bot is a member. In Slack, go to the channel and run `/invite @YourBotName`.

## Usage

Once running, just send a message in the Slack channel. Claude Code will:

1. React with :eyes: to acknowledge receipt
2. Process the prompt
3. Reply in a thread with the response
4. React with :white_check_mark: when done

Long responses are automatically chunked to fit Slack's message limits.

## MCP Tools

The server exposes four tools to Claude Code:

| Tool | Description |
|------|-------------|
| `reply` | Post a threaded reply (auto-chunks long messages) |
| `react` | Add/remove emoji reactions on messages |
| `edit_message` | Update a previously posted bot message |
| `upload_file` | Upload a file to the channel (from content or file path) |

## Remote Approval Hook

When you're away from the terminal and interacting via Slack, Claude Code's permission prompts are automatically forwarded to the Slack thread. You can reply **yes** or **no** to approve or deny tool usage.

The system auto-detects whether you're local or remote:
- **Slack message received** → marks session as remote, approvals go to Slack
- **Terminal input detected** → marks session as local, approvals show in terminal

No manual mode switching required — it just works.

### How it works

The `SlackBridge.Claude.Hook` project is a `PreToolUse` hook that:
1. Checks if the last interaction came from Slack (via a timestamp file)
2. If remote: posts the tool details to the Slack thread and polls for a yes/no reply
3. If local: does nothing, normal terminal prompt appears

Configuration is in `.claude/settings.json` — the hook is pre-configured for `Bash`, `Write`, `Edit`, and MCP tool calls.

## Project Structure

```
src/
├── SlackBridge.Claude/                 # MCP server
│   ├── Program.cs                      # Entry point, DI wiring, channel name derivation
│   ├── Configuration/SlackOptions.cs   # Env var config
│   ├── Slack/
│   │   ├── SlackConnectionService.cs   # Socket Mode connect, channel resolve/create
│   │   └── MessageHandler.cs           # Message filtering, MCP notification forwarding
│   └── Mcp/Tools/
│       ├── ReplyTool.cs                # Threaded replies with auto-chunking
│       ├── ReactTool.cs                # Emoji reactions
│       ├── EditMessageTool.cs          # Message editing
│       └── UploadFileTool.cs           # File uploads
└── SlackBridge.Claude.Hook/            # PreToolUse approval hook
    ├── Program.cs                      # Hook entry point (PreToolUse + UserPromptSubmit)
    └── LocalActivity.cs                # Local vs remote activity tracking
```

## Configuration

The MCP server is configured via `.mcp.json` in the project root:

```json
{
  "mcpServers": {
    "slack-bridge": {
      "command": "dotnet",
      "args": ["run", "--no-build", "-c", "Release", "--project", "src/SlackBridge.Claude"],
      "env": {
        "SLACK_BOT_TOKEN": "${SLACK_BOT_TOKEN}",
        "SLACK_APP_TOKEN": "${SLACK_APP_TOKEN}"
      }
    }
  }
}
```

## Troubleshooting

- **"Channel ID not resolved yet"** — The tool fired before Slack connected. This is a race condition that should be handled automatically, but if it persists, check that your tokens are valid.
- **MCP server disconnects** — Check `~/.slackbridge-claude/server.log` for errors. Ensure the MCP server process isn't being killed by antivirus or system policies.
- **Bot doesn't respond** — Make sure the bot is in the channel and that `message.channels` event subscription is enabled.
- **Build fails with file lock** — The MCP server process is still running. Kill it with `taskkill /F /IM SlackBridge.Claude.exe` (Windows) or `pkill SlackBridge.Claude` (Linux/macOS), then rebuild.
- **Remote approvals not appearing** — Ensure the MCP server has written `~/.slackbridge-claude/hook-config.json` (happens on first Slack connection).

## License

MIT
