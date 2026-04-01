using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Two modes:
//   "local-touch" — UserPromptSubmit hook: marks user as locally active
//   (default)     — PreToolUse hook: forwards permission prompts to Slack when remote

if (args.Length > 0 && args[0] == "local-touch")
{
    LocalActivity.Touch();
    return 0;
}

// Check if user is actively using Slack (and hasn't returned to the terminal)
var (isRemote, threadTs) = LocalActivity.CheckRemoteStatus(TimeSpan.FromMinutes(5));
if (!isRemote)
    return 0; // Not remote — fall through to normal local prompt

// Read Slack config written by the MCP server
var hookConfigPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".slackbridge-claude", "hook-config.json");

string? botToken = null;
string? channelId = null;

try
{
    if (File.Exists(hookConfigPath))
    {
        var config = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(hookConfigPath));
        botToken = config.GetProperty("botToken").GetString();
        channelId = config.GetProperty("channelId").GetString();
    }
}
catch
{
    // Fall through
}

if (string.IsNullOrEmpty(botToken) || string.IsNullOrEmpty(channelId))
{
    // No Slack config — fall through to normal prompt
    return 0;
}

// Read hook input from stdin
string input;
try
{
    input = await Console.In.ReadToEndAsync();
}
catch
{
    return 0;
}

HookInput? hookInput;
try
{
    hookInput = JsonSerializer.Deserialize<HookInput>(input);
    if (hookInput == null) return 0;
}
catch
{
    return 0;
}

// Format the approval message
var toolDisplay = FormatToolForSlack(hookInput);

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", botToken);

// Post approval request to Slack
string? messageTs;
try
{
    var postBody = JsonSerializer.Serialize(new
    {
        channel = channelId,
        thread_ts = threadTs,
        text = $":warning: *Permission Required*\n\n{toolDisplay}\n\nReply *yes* or *no*"
    });

    var postResponse = await http.PostAsync(
        "https://slack.com/api/chat.postMessage",
        new StringContent(postBody, Encoding.UTF8, "application/json"));

    var postResult = JsonSerializer.Deserialize<SlackPostResponse>(
        await postResponse.Content.ReadAsStringAsync());

    if (postResult?.Ok != true)
    {
        // Can't reach Slack — fall through to local prompt
        return 0;
    }

    messageTs = postResult.Ts;
}
catch
{
    return 0;
}

// Poll for thread replies (timeout after 2 minutes)
// Poll the parent thread, looking for replies that came AFTER our approval message
var timeout = TimeSpan.FromMinutes(2);
var pollInterval = TimeSpan.FromSeconds(2);
var deadline = DateTime.UtcNow + timeout;
string decision = "ask"; // Default: fall back to local prompt
var pollTs = threadTs ?? messageTs; // Poll parent thread if available

while (DateTime.UtcNow < deadline)
{
    await Task.Delay(pollInterval);

    try
    {
        var repliesUrl = $"https://slack.com/api/conversations.replies?channel={channelId}&ts={pollTs}";
        var repliesResponse = await http.GetAsync(repliesUrl);
        var repliesResult = JsonSerializer.Deserialize<SlackRepliesResponse>(
            await repliesResponse.Content.ReadAsStringAsync());

        if (repliesResult?.Ok == true && repliesResult.Messages != null)
        {
            // Check replies that came after our approval message, from humans only
            foreach (var reply in repliesResult.Messages
                .Where(m => string.Compare(m.Ts, messageTs, StringComparison.Ordinal) > 0
                            && string.IsNullOrEmpty(m.BotId)))
            {
                var text = reply.Text?.Trim().ToLowerInvariant();
                if (text is "yes" or "y" or "yep" or "approve" or "ok" or "allow")
                {
                    decision = "allow";
                    goto decided;
                }
                if (text is "no" or "n" or "nope" or "deny" or "reject" or "block")
                {
                    decision = "deny";
                    goto decided;
                }
            }
        }
    }
    catch
    {
        // Poll failure — keep trying
    }
}

decided:

// Update the Slack message with the decision
try
{
    var emoji = decision switch
    {
        "allow" => ":white_check_mark:",
        "deny" => ":no_entry_sign:",
        _ => ":hourglass:"
    };
    var statusText = decision switch
    {
        "allow" => "Approved",
        "deny" => "Denied",
        _ => "Timed out (asking locally)"
    };

    var updateBody = JsonSerializer.Serialize(new
    {
        channel = channelId,
        ts = messageTs,
        text = $"{emoji} *{statusText}*\n\n{toolDisplay}"
    });

    await http.PostAsync(
        "https://slack.com/api/chat.update",
        new StringContent(updateBody, Encoding.UTF8, "application/json"));
}
catch
{
    // Best effort
}

// Return decision to Claude Code
var output = new HookOutput
{
    HookSpecificOutput = new HookDecision
    {
        HookEventName = "PreToolUse",
        PermissionDecision = decision,
        PermissionDecisionReason = decision switch
        {
            "allow" => "Approved via Slack",
            "deny" => "Denied via Slack",
            _ => "No response from Slack, asking locally"
        }
    }
};

Console.Write(JsonSerializer.Serialize(output));
return 0;

static string FormatToolForSlack(HookInput hook)
{
    var sb = new StringBuilder();
    sb.AppendLine($"*Tool:* `{hook.ToolName}`");

    if (hook.ToolInput != null)
    {
        var input = hook.ToolInput.Value;

        if (input.TryGetProperty("command", out var cmd))
            sb.AppendLine($"*Command:*\n```{cmd.GetString()}```");

        if (input.TryGetProperty("description", out var desc))
            sb.AppendLine($"*Description:* {desc.GetString()}");

        if (input.TryGetProperty("file_path", out var fp))
            sb.AppendLine($"*File:* `{fp.GetString()}`");

        if (input.TryGetProperty("content", out _))
            sb.AppendLine("*Action:* Write file content");

        if (input.TryGetProperty("old_string", out _))
            sb.AppendLine("*Action:* Edit file");

        if (input.TryGetProperty("url", out var url))
            sb.AppendLine($"*URL:* {url.GetString()}");

        if (input.TryGetProperty("prompt", out var prompt))
            sb.AppendLine($"*Prompt:* {prompt.GetString()}");
    }

    return sb.ToString().TrimEnd();
}

class HookInput
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; set; }

    [JsonPropertyName("tool_name")]
    public string? ToolName { get; set; }

    [JsonPropertyName("tool_input")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }
}

class HookOutput
{
    [JsonPropertyName("hookSpecificOutput")]
    public HookDecision? HookSpecificOutput { get; set; }
}

class HookDecision
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = "PreToolUse";

    [JsonPropertyName("permissionDecision")]
    public string PermissionDecision { get; set; } = "ask";

    [JsonPropertyName("permissionDecisionReason")]
    public string? PermissionDecisionReason { get; set; }
}

class SlackPostResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }
}

class SlackRepliesResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("messages")]
    public List<SlackReplyMessage>? Messages { get; set; }
}

class SlackReplyMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("bot_id")]
    public string? BotId { get; set; }
}
