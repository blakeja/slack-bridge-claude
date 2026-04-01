# SlackBridge.Claude

Remote control Claude Code instances via Slack. Post prompts in a Slack channel from your phone, get responses back in threads.

## Architecture

```
You (Slack)  →  Socket Mode WebSocket  →  C# MCP Server (stdio)  →  Claude Code session
                                       ←  reply/react/edit tools  ←
```

- **Socket Mode** (no public URL, works on free Slack plan)
- **One channel per Claude Code instance** — channel name auto-derives from the working directory (e.g., `SlackBridge.Claude` → `#slack-bridge-claude`)
- **MCP server over stdio** — uses `ModelContextProtocol` SDK + `SlackNet` for Slack API
- Channel is auto-created if it doesn't exist

## Project Structure

```
src/SlackBridge.Claude/
├── Program.cs                          # Entry point, DI wiring, channel name derivation
├── Configuration/SlackOptions.cs       # Env var config (SLACK_BOT_TOKEN, SLACK_APP_TOKEN, SLACK_CHANNEL)
├── Slack/
│   ├── SlackConnectionService.cs       # Hosted service: Socket Mode connect, channel resolve/create, readiness gate
│   └── MessageHandler.cs              # Filters messages, forwards to MCP notifications
└── Mcp/
    ├── McpKeepAliveService.cs         # Periodic pings to prevent Claude Code from disconnecting
    └── Tools/
        ├── ReplyTool.cs               # Threaded replies with auto-chunking (3900 char limit)
        ├── ReactTool.cs               # Add/remove emoji reactions (handles already_reacted/no_reaction)
        └── EditMessageTool.cs         # Update bot messages
```

## Key Dependencies

- `ModelContextProtocol` 1.2.0 — .NET MCP SDK (stdio server, tool attributes, notifications)
- `SlackNet` 0.17.10 — Slack API + Socket Mode (built-in, no extra packages)
- `Microsoft.Extensions.Hosting` 10.0.5 — Generic host, DI, logging

## Environment Variables

- `SLACK_BOT_TOKEN` (required) — Bot User OAuth Token (`xoxb-...`)
- `SLACK_APP_TOKEN` (required) — App-Level Token (`xapp-...`) with `connections:write`
- `SLACK_CHANNEL` (optional) — Override channel name; defaults to slugified current directory name

## Slack App Configuration

Bot Token Scopes: `channels:history`, `channels:read`, `channels:manage`, `chat:write`, `reactions:read`, `reactions:write`, `files:write`, `users:read`
Event Subscriptions: `message.channels`
Socket Mode: enabled

## MCP SDK Notes

- `Capabilities.Experimental` values must be `JsonElement`, not anonymous types or `Dictionary<string, object>` — the source-generated serializer can't handle those
- Tools should await `SlackConnectionService.Ready` before accessing `SlackOptions.ChannelId` to avoid race conditions on startup

## SlackNet API Notes

- Reactions: `AddToMessage(emoji, channelId, ts)` / `RemoveFromMessage(emoji, channelId, ts)` — NOT `Add`/`Remove`
- MessageUpdate: uses `ChannelId` property, not `Channel`
- SlackServiceBuilder: `GetApiClient()` and `GetSocketModeClient()` are on the builder directly, `SlackServiceProvider` is internal
- RegisterEventHandler receives `SlackRequestContext`, not `IServiceProvider`

## Building & Running

```bash
dotnet build
# Launch as Claude Code channel plugin (local dev):
claude --dangerously-load-development-channels server:slack-bridge
```

## Development Preferences

- C# is the preferred language
- .NET 10 target
