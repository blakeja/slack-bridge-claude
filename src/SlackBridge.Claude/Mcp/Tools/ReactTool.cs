using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SlackBridge.Claude.Configuration;
using SlackBridge.Claude.Slack;
using SlackNet;

namespace SlackBridge.Claude.Mcp.Tools;

[McpServerToolType]
public class ReactTool
{
    [McpServerTool(Name = "react"), Description("Add or remove an emoji reaction on a message.")]
    public async Task<string> React(
        [Description("Emoji name without colons (e.g., 'thumbsup', 'eyes', 'white_check_mark')")] string emoji,
        [Description("Timestamp of the message to react to")] string message_ts,
        [Description("Action to perform: 'add' or 'remove'")] string action,
        ISlackApiClient apiClient,
        IOptions<SlackOptions> options,
        SlackConnectionService slackConnection)
    {
        await slackConnection.Ready;
        var channelId = options.Value.ChannelId!;

        try
        {
            if (string.Equals(action, "remove", StringComparison.OrdinalIgnoreCase))
            {
                await apiClient.Reactions.RemoveFromMessage(emoji, channelId, message_ts);
                return $"Removed :{emoji}: reaction";
            }
            else
            {
                await apiClient.Reactions.AddToMessage(emoji, channelId, message_ts);
                return $"Added :{emoji}: reaction";
            }
        }
        catch (SlackException ex) when (ex.Message.Contains("already_reacted") || ex.Message.Contains("no_reaction"))
        {
            return action.Equals("remove", StringComparison.OrdinalIgnoreCase)
                ? $"No :{emoji}: reaction to remove"
                : $"Already reacted with :{emoji}:";
        }
    }
}
