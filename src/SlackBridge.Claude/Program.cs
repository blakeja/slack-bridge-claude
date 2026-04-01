using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using SlackBridge.Claude.Configuration;
using SlackBridge.Claude.Slack;
using SlackNet;
using SlackNet.Events;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr (stdout is reserved for MCP stdio transport)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// Also log to file for debugging MCP server issues
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".slackbridge-claude");
Directory.CreateDirectory(logDir);
var logPath = Path.Combine(logDir, "server.log");
builder.Logging.AddProvider(new FileLoggerProvider(logPath));

// Load Slack configuration from environment variables
builder.Services.Configure<SlackOptions>(options =>
{
    options.BotToken = Environment.GetEnvironmentVariable("SLACK_BOT_TOKEN")
        ?? throw new InvalidOperationException("SLACK_BOT_TOKEN environment variable is required");
    options.AppToken = Environment.GetEnvironmentVariable("SLACK_APP_TOKEN")
        ?? throw new InvalidOperationException("SLACK_APP_TOKEN environment variable is required");
    options.Channel = Environment.GetEnvironmentVariable("SLACK_CHANNEL")
        ?? ToSlackChannelName(Environment.CurrentDirectory);
});

// Register SlackNet services via a shared builder instance.
// MessageHandler uses Lazy<ISlackApiClient> to break the circular dependency:
// SlackServiceBuilder → MessageHandler → ISlackApiClient → SlackServiceBuilder
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<SlackOptions>>().Value;
    var handler = sp.GetRequiredService<MessageHandler>();
    return new SlackServiceBuilder()
        .UseApiToken(options.BotToken)
        .UseAppLevelToken(options.AppToken)
        .RegisterEventHandler<MessageEvent>(_ => handler);
});

builder.Services.AddSingleton<ISlackApiClient>(sp =>
    sp.GetRequiredService<SlackServiceBuilder>().GetApiClient());

builder.Services.AddSingleton(sp =>
    new Lazy<ISlackApiClient>(() => sp.GetRequiredService<ISlackApiClient>()));

builder.Services.AddSingleton<ISlackSocketModeClient>(sp =>
    sp.GetRequiredService<SlackServiceBuilder>().GetSocketModeClient());

// Register our services
builder.Services.AddSingleton<MessageHandler>();
// SlackConnectionService starts Slack in background after MCP is ready
builder.Services.AddSingleton<SlackConnectionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SlackConnectionService>());

// Configure MCP server
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "SlackBridge.Claude",
        Version = "0.1.0"
    };
    options.Capabilities ??= new();
    options.Capabilities.Experimental = new Dictionary<string, object>
    {
        ["claude/channel"] = System.Text.Json.JsonSerializer.SerializeToElement(new { })
    };
    options.ServerInstructions = """
        Messages from Slack arrive as notifications. Each notification includes:
        - content: the message text
        - meta.channel_id: Slack channel ID
        - meta.message_ts: message timestamp (use as thread_ts when replying)
        - meta.user: Slack user ID
        - meta.user_name: display name of the sender

        When replying to a message, ALWAYS pass the message_ts as thread_ts to the reply tool.
        This creates a threaded reply under the user's original message, keeping the channel clean.

        For long responses, the reply tool automatically chunks the message.
        Use react to add emoji reactions (e.g., 'thinking_face' while working, 'white_check_mark' when done).
        Use edit_message to update a previously posted bot message.
        """;
})
.WithStdioServerTransport()
.WithToolsFromAssembly();

var host = builder.Build();
await host.RunAsync();

/// <summary>
/// Converts a directory name into a valid Slack channel name.
/// "SlackBridge.Claude" → "slack-bridge-claude"
/// "MyProject" → "my-project"
/// </summary>
static string ToSlackChannelName(string directoryPath)
{
    var name = Path.GetFileName(directoryPath);

    // Insert hyphens before uppercase letters (PascalCase → pascal-case)
    name = Regex.Replace(name, "(?<=.)([A-Z])", "-$1");

    // Replace dots, underscores, spaces with hyphens
    name = Regex.Replace(name, @"[._\s]+", "-");

    // Lowercase, strip invalid chars, collapse multiple hyphens, trim hyphens
    name = name.ToLowerInvariant();
    name = Regex.Replace(name, @"[^a-z0-9-]", "");
    name = Regex.Replace(name, @"-{2,}", "-");
    name = name.Trim('-');

    // Slack channel names max 80 chars
    if (name.Length > 80)
        name = name[..80].TrimEnd('-');

    return name;
}
