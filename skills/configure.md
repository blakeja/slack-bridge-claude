---
name: slack:configure
description: Configure Slack tokens for SlackBridge.Claude
user_invocable: true
---

# /slack:configure

Configure Slack API tokens for the SlackBridge.Claude channel plugin.

## Usage

```
/slack:configure <bot-token> <app-token>
```

## Parameters

- `bot-token`: Slack Bot User OAuth Token (starts with `xoxb-`)
- `app-token`: Slack App-Level Token (starts with `xapp-`)

## What it does

Sets the `SLACK_BOT_TOKEN` and `SLACK_APP_TOKEN` environment variables for the current session.

To make these persistent, add them to your shell profile or a `.env` file.

## Setup instructions

1. Create a Slack app at https://api.slack.com/apps
2. Enable Socket Mode and generate an App-Level Token with `connections:write` scope
3. Add Bot Token Scopes: `channels:history`, `channels:read`, `chat:write`, `reactions:read`, `reactions:write`, `files:write`, `users:read`
4. Subscribe to Events: `message.channels`
5. Install the app to your workspace
6. Run `/slack:configure <bot-token> <app-token>`
