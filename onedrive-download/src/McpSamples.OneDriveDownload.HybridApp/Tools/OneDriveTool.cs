using System.ComponentModel;

using ModelContextProtocol.Server;

using McpSamples.OneDriveDownload.HybridApp.Models;
using McpSamples.OneDriveDownload.HybridApp.Services;

namespace McpSamples.OneDriveDownload.HybridApp.Tools;

/// <summary>
/// This provides interfaces to <see cref="OneDriveTool"/>.
/// </summary>
public interface IOneDriveTool
{
    /// <summary>
    /// Downloads a file from OneDrive sharing URL.
    /// </summary>
    /// <param name="sharingUrl">The OneDrive sharing URL of the file to download.</param>
    /// <returns>A <see cref="DownloadResult"/> containing information about the downloaded file.</returns>
    Task<DownloadResult> DownloadFileFromUrlAsync(string sharingUrl);
}

/// <summary>
/// This represents the MCP tool to download files from OneDrive.
/// </summary>
/// <param name="service">The <see cref="IOneDriveDownloadService"/> instance.</param>
/// <param name="logger">The <see cref="ILogger{T}"/> instance.</param>
[McpServerToolType]
public class OneDriveTool(IOneDriveDownloadService service, ILogger<OneDriveTool> logger) : IOneDriveTool
{
    private readonly IOneDriveDownloadService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly ILogger<OneDriveTool> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    [McpServerTool(Name = "download_file", Title = "Download File from OneDrive URL")]
    [Description("Downloads a file from OneDrive")]
    public async Task<DownloadResult> DownloadFileFromUrlAsync(
        [Description("The OneDrive sharing URL")] string sharingUrl
    )
    {
        var result = default(DownloadResult);
        try
        {
            result = await _service.DownloadFileAsync(sharingUrl);

            _logger.LogInformation("File downloaded successfully from OneDrive URL: {SharingUrl}", sharingUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from OneDrive URL: {SharingUrl}", sharingUrl);

            result = new DownloadResult() { ErrorMessage = ex.Message };
        }

        return result;
    }
}
