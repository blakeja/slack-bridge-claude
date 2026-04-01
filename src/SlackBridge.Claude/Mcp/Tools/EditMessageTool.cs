using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SlackBridge.Claude.Configuration;
using SlackBridge.Claude.Slack;
using SlackNet;
using SlackNet.WebApi;

namespace SlackBridge.Claude.Mcp.Tools;

[McpServerToolType]
public class EditMessageTool
{
    [McpServerTool(Name = "edit_message"), Description("Update a previously posted message. Only works on messages posted by the bot.")]
    public async Task<string> EditMessage(
        [Description("New message text")] string text,
        [Description("Timestamp of the message to edit")] string message_ts,
        ISlackApiClient apiClient,
        IOptions<SlackOptions> options,
        SlackConnectionService slackConnection)
    {
        await slackConnection.Ready;
        var channelId = options.Value.ChannelId!;

        await apiClient.Chat.Update(new MessageUpdate
        {
            ChannelId = channelId,
            Ts = message_ts,
            Text = text
        });

        return $"Message updated (ts: {message_ts})";
    }
}
