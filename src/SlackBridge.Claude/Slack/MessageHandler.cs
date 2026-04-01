using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SlackBridge.Claude.Configuration;
using SlackNet;
using SlackNet.Events;
using SlackNet.WebApi;

namespace SlackBridge.Claude.Slack;

/// <summary>
/// Handles incoming Slack messages and forwards them as MCP channel notifications.
/// Filters out bot messages, messages from other channels, and bot-started threads.
/// </summary>
public class MessageHandler : IEventHandler<MessageEvent>
{
    private readonly McpServer _mcpServer;
    private readonly Lazy<ISlackApiClient> _apiClient;
    private readonly SlackOptions _options;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(
        McpServer mcpServer,
        Lazy<ISlackApiClient> apiClient,
        IOptions<SlackOptions> options,
        ILogger<MessageHandler> logger)
    {
        _mcpServer = mcpServer;
        _apiClient = apiClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Handle(MessageEvent message)
    {
        // Filter: only our assigned channel
        if (!string.Equals(message.Channel, _options.ChannelId, StringComparison.OrdinalIgnoreCase))
            return;

        // Filter: ignore bot messages (including our own)
        if (!string.IsNullOrEmpty(message.BotId))
            return;

        // Filter: ignore message subtypes (edits, joins, etc.) - only plain messages
        if (!string.IsNullOrEmpty(message.Subtype))
            return;

        // Filter: ignore our own messages by user ID
        if (string.Equals(message.User, _options.BotUserId, StringComparison.OrdinalIgnoreCase))
            return;

        _logger.LogInformation("Received message from {User} in #{Channel}: {Text}",
            message.User, _options.Channel, Truncate(message.Text, 100));

        // Add eyes reaction to acknowledge receipt
        try
        {
            await _apiClient.Value.Reactions.AddToMessage("eyes", _options.ChannelId!, message.Ts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add eyes reaction");
        }

        // Resolve user display name
        var userName = await ResolveUserName(message.User);

        // Send MCP channel notification
        var notification = new ChannelNotification
        {
            Content = message.Text ?? string.Empty,
            Meta = new ChannelMeta
            {
                ChannelId = message.Channel,
                MessageTs = message.Ts,
                User = message.User,
                UserName = userName,
                ThreadTs = message.ThreadTs ?? string.Empty
            }
        };

        await _mcpServer.SendNotificationAsync(
            "notifications/claude/channel",
            notification);

        // Write timestamp and thread info so the approval hook knows we're in remote mode
        // Use the thread_ts if replying in a thread, otherwise the message_ts (starts a new thread)
        TouchRemoteActivity(message.ThreadTs ?? message.Ts);

        _logger.LogInformation("Forwarded message to MCP as channel notification");
    }

    private async Task<string> ResolveUserName(string userId)
    {
        try
        {
            var user = await _apiClient.Value.Users.Info(userId);
            return user.Profile?.DisplayName
                ?? user.Profile?.RealName
                ?? user.Name
                ?? userId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve user name for {UserId}", userId);
            return userId;
        }
    }

    private static void TouchRemoteActivity(string threadTs)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".slackbridge-claude");
            var path = Path.Combine(dir, "remote-active");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow.ToString("o"),
                threadTs
            });
            System.IO.File.WriteAllText(path, json);
        }
        catch
        {
            // Best effort
        }
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "(empty)";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

public class ChannelNotification
{
    public string Content { get; set; } = string.Empty;
    public ChannelMeta Meta { get; set; } = new();
}

public class ChannelMeta
{
    public string ChannelId { get; set; } = string.Empty;
    public string MessageTs { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ThreadTs { get; set; } = string.Empty;
}
