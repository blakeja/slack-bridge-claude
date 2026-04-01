using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SlackBridge.Claude.Configuration;
using SlackNet;

namespace SlackBridge.Claude.Slack;

/// <summary>
/// Hosted service that manages the Slack Socket Mode WebSocket connection.
/// Resolves the channel name to an ID on startup and keeps the connection alive.
/// Uses IServiceProvider for lazy resolution to avoid blocking MCP server startup.
/// </summary>
public class SlackConnectionService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SlackConnectionService> _logger;
    private readonly TaskCompletionSource _ready = new();
    private ISlackSocketModeClient? _socketClient;

    /// <summary>
    /// Completes when Slack is connected and the channel ID is resolved.
    /// Tools should await this before accessing SlackOptions.ChannelId.
    /// </summary>
    public Task Ready => _ready.Task;

    public SlackConnectionService(
        IServiceProvider serviceProvider,
        ILogger<SlackConnectionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Connect on a background thread so we don't block MCP server startup.
        // Must use Task.Run because GetSocketModeClient() blocks synchronously.
        _ = Task.Run(() => ConnectAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Disconnecting from Slack Socket Mode");
        _socketClient?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Resolve Slack clients lazily to avoid blocking MCP server startup
            var options = _serviceProvider.GetRequiredService<IOptions<SlackOptions>>().Value;
            var apiClient = _serviceProvider.GetRequiredService<ISlackApiClient>();
            _socketClient = _serviceProvider.GetRequiredService<ISlackSocketModeClient>();

            // Resolve channel name to ID
            await ResolveChannelId(apiClient, options, cancellationToken);

            // Resolve bot user ID
            await ResolveBotUserId(apiClient, options, cancellationToken);

            // Connect Socket Mode
            await _socketClient.Connect();
            _logger.LogInformation(
                "Connected to Slack Socket Mode. Monitoring channel #{Channel} ({ChannelId})",
                options.Channel, options.ChannelId);

            // Write connection info for the approval hook to read
            WriteHookConfig(options);

            _ready.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Slack");
            _ready.TrySetException(ex);
        }
    }

    private async Task ResolveChannelId(ISlackApiClient apiClient, SlackOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.ChannelId))
            return;

        string? cursor = null;
        do
        {
            var response = await apiClient.Conversations.List(cursor: cursor, limit: 200);
            foreach (var channel in response.Channels)
            {
                if (string.Equals(channel.Name, options.Channel, StringComparison.OrdinalIgnoreCase))
                {
                    options.ChannelId = channel.Id;
                    _logger.LogInformation("Resolved #{Channel} to {ChannelId}", options.Channel, channel.Id);
                    return;
                }
            }
            cursor = response.ResponseMetadata?.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        // Channel not found — create it
        _logger.LogInformation("Channel #{Channel} not found, creating it...", options.Channel);
        var created = await apiClient.Conversations.Create(options.Channel, isPrivate: false);
        options.ChannelId = created.Id;
        _logger.LogInformation("Created #{Channel} ({ChannelId})", options.Channel, created.Id);
    }

    private static void WriteHookConfig(SlackOptions options)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".slackbridge-claude");
            Directory.CreateDirectory(dir);
            var configPath = Path.Combine(dir, "hook-config.json");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                channelId = options.ChannelId,
                botToken = options.BotToken
            });
            System.IO.File.WriteAllText(configPath, json);
        }
        catch
        {
            // Best effort
        }
    }

    private async Task ResolveBotUserId(ISlackApiClient apiClient, SlackOptions options, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(options.BotUserId))
            return;

        var auth = await apiClient.Auth.Test();
        options.BotUserId = auth.UserId;
        _logger.LogInformation("Bot user ID: {BotUserId}", options.BotUserId);
    }
}
