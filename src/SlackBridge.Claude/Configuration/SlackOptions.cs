namespace SlackBridge.Claude.Configuration;

public class SlackOptions
{
    public const string SectionName = "Slack";

    public string BotToken { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? ChannelId { get; set; }
    public string? BotUserId { get; set; }
}
