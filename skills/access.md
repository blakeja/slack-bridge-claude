---
name: slack:access
description: Manage access control for SlackBridge.Claude
user_invocable: true
---

# /slack:access

Manage which Slack users can send commands to this Claude Code instance.

## Usage

```
/slack:access pair <code>        # Complete pairing with a code shown in Slack
/slack:access policy allowlist   # Enforce allowlist (only paired users can send)
/slack:access policy open        # Disable allowlist (anyone in the channel can send)
/slack:access list               # Show current allowlist
```

## Pairing flow

1. An unauthorized user sends a message in the Slack channel
2. The bot replies with a pairing code (expires in 5 minutes)
3. Run `/slack:access pair <code>` in Claude Code to authorize that user
4. The user can now send commands

## Policy modes

- **open** (default): Any user in the channel can send commands
- **allowlist**: Only paired/authorized users can send commands
