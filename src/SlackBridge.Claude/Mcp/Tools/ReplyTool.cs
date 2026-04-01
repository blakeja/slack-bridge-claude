using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SlackBridge.Claude.Configuration;
using SlackBridge.Claude.Slack;
using SlackNet;
using SlackNet.WebApi;

namespace SlackBridge.Claude.Mcp.Tools;

[McpServerToolType]
public class ReplyTool
{
    private const int MaxSlackMessageLength = 3900;

    [McpServerTool(Name = "reply"), Description("Post a message to the Slack channel. Always pass thread_ts from the inbound message to reply in a thread.")]
    public async Task<string> Reply(
        [Description("Message text (supports Slack mrkdwn formatting)")] string text,
        [Description("Thread timestamp from the inbound message. Pass this to reply in a thread.")] string? thread_ts,
        ISlackApiClient apiClient,
        IOptions<SlackOptions> options,
        SlackConnectionService slackConnection,
        ILogger<ReplyTool> logger)
    {
        await slackConnection.Ready;
        var channelId = options.Value.ChannelId!;

        var chunks = ChunkMessage(text);
        string? lastTs = null;

        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
                await Task.Delay(1100); // Slack rate limit: ~1 msg/sec per channel

            var response = await apiClient.Chat.PostMessage(new Message
            {
                Channel = channelId,
                Text = chunks[i],
                ThreadTs = thread_ts,
                UnfurlLinks = false,
                UnfurlMedia = false
            });

            lastTs = response.Ts;

            // If first chunk created a thread, subsequent chunks go in the same thread
            thread_ts ??= lastTs;
        }

        // Add checkmark reaction to original message if we have a thread_ts
        if (!string.IsNullOrEmpty(thread_ts))
        {
            try
            {
                await apiClient.Reactions.AddToMessage("white_check_mark", channelId, thread_ts);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to add checkmark reaction");
            }

            try
            {
                await apiClient.Reactions.RemoveFromMessage("eyes", channelId, thread_ts);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to remove eyes reaction");
            }
        }

        return chunks.Count == 1
            ? $"Message sent (ts: {lastTs})"
            : $"Message sent in {chunks.Count} parts (ts: {lastTs})";
    }

    private static List<string> ChunkMessage(string text)
    {
        if (text.Length <= MaxSlackMessageLength)
            return [text];

        var chunks = new List<string>();
        var remaining = text.AsSpan();

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MaxSlackMessageLength)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            // Find a good break point (newline or space)
            var breakAt = MaxSlackMessageLength;
            var newlineIdx = remaining[..MaxSlackMessageLength].LastIndexOf('\n');
            if (newlineIdx > MaxSlackMessageLength / 2)
            {
                breakAt = newlineIdx + 1;
            }
            else
            {
                var spaceIdx = remaining[..MaxSlackMessageLength].LastIndexOf(' ');
                if (spaceIdx > MaxSlackMessageLength / 2)
                    breakAt = spaceIdx + 1;
            }

            chunks.Add(remaining[..breakAt].ToString());
            remaining = remaining[breakAt..];
        }

        return chunks;
    }
}
