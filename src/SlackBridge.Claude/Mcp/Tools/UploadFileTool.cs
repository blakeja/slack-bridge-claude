using System.ComponentModel;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using SlackBridge.Claude.Configuration;
using SlackBridge.Claude.Slack;
using SlackNet;
using SlackNet.WebApi;

namespace SlackBridge.Claude.Mcp.Tools;

[McpServerToolType]
public class UploadFileTool
{
    [McpServerTool(Name = "upload_file"), Description("Upload a file to the Slack channel. Use for sharing code, logs, or documents. Supports text content or reading from a file path.")]
    public async Task<string> UploadFile(
        [Description("File name with extension (e.g., 'readme.md', 'output.log')")] string filename,
        [Description("Text content of the file. Provide either this or file_path.")] string? content,
        [Description("Absolute path to a file to upload. Provide either this or content.")] string? file_path,
        [Description("Thread timestamp to upload as a reply. Pass null to post in the channel.")] string? thread_ts,
        [Description("Optional comment to accompany the file.")] string? comment,
        ISlackApiClient apiClient,
        IOptions<SlackOptions> options,
        SlackConnectionService slackConnection)
    {
        await slackConnection.Ready;
        var channelId = options.Value.ChannelId!;

        if (content == null && file_path == null)
            return "Error: provide either content or file_path";

        if (content == null)
        {
            if (!System.IO.File.Exists(file_path))
                return $"Error: file not found: {file_path}";
            content = await System.IO.File.ReadAllTextAsync(file_path);
        }

        using var httpContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        var upload = new FileUpload(filename, httpContent);

        await apiClient.Files.Upload(
            file: upload,
            channelId: channelId,
            threadTs: thread_ts,
            initialComment: comment);

        return $"Uploaded {filename} ({content.Length} chars)";
    }
}
