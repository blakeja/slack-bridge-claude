// Helpers for tracking whether the user is interacting via Slack (remote) or terminal (local).
// The MCP server writes remote-active as JSON: { timestamp, threadTs }
// The UserPromptSubmit hook writes local-active as a plain timestamp string.

using System.Text.Json;

static class LocalActivity
{
    public static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".slackbridge-claude", "local-active");

    public static readonly string RemotePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".slackbridge-claude", "remote-active");

    public static void Touch()
    {
        try
        {
            // Don't overwrite local-active if a Slack message arrived in the last 5 seconds.
            // Channel notifications trigger UserPromptSubmit too, so we need to ignore those.
            var (remoteTime, _) = ReadRemoteState();
            if (remoteTime != null && DateTimeOffset.UtcNow - remoteTime.Value < TimeSpan.FromSeconds(5))
                return; // Slack message just arrived — this prompt is from the channel, not the keyboard

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, DateTimeOffset.UtcNow.ToString("o"));
        }
        catch
        {
            // Best effort
        }
    }

    public static (bool isRemote, string? threadTs) CheckRemoteStatus(TimeSpan window)
    {
        try
        {
            var (remoteTime, threadTs) = ReadRemoteState();
            if (remoteTime == null)
                return (false, null);

            if (DateTimeOffset.UtcNow - remoteTime.Value > window)
                return (false, null); // Too old

            // Check if local activity is more recent
            if (File.Exists(Path))
            {
                var localTime = DateTimeOffset.Parse(File.ReadAllText(Path).Trim());
                if (localTime > remoteTime.Value)
                    return (false, null); // User came back to the terminal
            }

            return (true, threadTs);
        }
        catch
        {
            return (false, null);
        }
    }

    private static (DateTimeOffset? timestamp, string? threadTs) ReadRemoteState()
    {
        try
        {
            if (!File.Exists(RemotePath))
                return (null, null);

            var content = File.ReadAllText(RemotePath).Trim();

            // Try JSON format first: { timestamp, threadTs }
            if (content.StartsWith('{'))
            {
                var doc = JsonSerializer.Deserialize<JsonElement>(content);
                var ts = DateTimeOffset.Parse(doc.GetProperty("timestamp").GetString()!);
                var threadTs = doc.GetProperty("threadTs").GetString();
                return (ts, threadTs);
            }

            // Fall back to plain timestamp string
            return (DateTimeOffset.Parse(content), null);
        }
        catch
        {
            return (null, null);
        }
    }
}
