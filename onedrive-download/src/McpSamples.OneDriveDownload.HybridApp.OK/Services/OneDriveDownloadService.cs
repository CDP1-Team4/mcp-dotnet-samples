using System.Text;

using Azure.Storage.Files.Shares;
using Azure.Storage.Sas;

using McpSamples.OnedriveDownload.HybridApp.Configurations;
using McpSamples.OnedriveDownload.HybridApp.Models;

using Microsoft.Graph;

using File = System.IO.File;

namespace McpSamples.OnedriveDownload.HybridApp.Services;

/// <summary>
/// This provides interfaces to <see cref="OneDriveDownloadService"/>.
/// </summary>
public interface IOneDriveDownloadService
{
    /// <summary>
    /// Downloads a file from a OneDrive sharing URL.
    /// </summary>
    /// <param name="sharingUrl">The OneDrive sharing URL of the file to download.</param>
    /// <returns>A <see cref="DownloadResult"/> containing information about the downloaded file.</returns>
    Task<DownloadResult> DownloadFileAsync(string sharingUrl);
}

/// <summary>
/// This represents the service to download files from OneDrive and save to Azure Storage.
/// </summary>
/// <param name="settings">The <see cref="OnedriveDownloadAppSettings"/> instance.</param>
/// <param name="graph">The <see cref="GraphServiceClient"/> instance.</param>
/// <param name="share">The <see cref="ShareClient"/> instance.</param>
/// <param name="accessor">The <see cref="IHttpContextAccessor"/> instance.</param>
/// <param name="logger">The <see cref="ILogger{T}"/> instance.</param>
public class OneDriveDownloadService(OnedriveDownloadAppSettings settings, GraphServiceClient graph, ShareClient share, IHttpContextAccessor accessor, ILogger<OneDriveDownloadService> logger) : IOneDriveDownloadService
{
    private readonly OnedriveDownloadAppSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly GraphServiceClient _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    private readonly ShareClient _share = share ?? throw new ArgumentNullException(nameof(share));
    private readonly IHttpContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    private readonly ILogger<OneDriveDownloadService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadFileAsync(string sharingUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharingUrl, nameof(sharingUrl));

        _logger.LogInformation("Downloading file from OneDrive sharing URL: {SharingUrl}", sharingUrl);

        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(sharingUrl));
        string encodedSharingUrl = $"u!{encoded.TrimEnd('=').Replace('/', '_').Replace('+', '-')}";

        var driveItem = await _graph.Shares[encodedSharingUrl].DriveItem.Request().GetAsync();

        var fileSize = driveItem.Size ?? 0;
        var fileName = driveItem.Name;

        using var contentStream = await _graph.Shares[encodedSharingUrl].DriveItem.Content.Request().GetAsync();
        if (settings.UseHttp == false)
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, Constants.DefaultDownloadFolder, fileName);
            using var fileStream = File.Create(filePath);
            await contentStream.CopyToAsync(fileStream);

            _logger.LogInformation("File '{FileName}' downloaded and saved to local path: {FilePath}", fileName, filePath);

            return new DownloadResult
            {
                FileName = fileName,
                DownloadUrl = filePath,
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.AzureStorage.ConnectionString) == true)
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, "wwwroot", Constants.DefaultDownloadFolder, fileName);
            using var fileStream = File.Create(filePath);
            await contentStream.CopyToAsync(fileStream);

            _logger.LogInformation("File '{FileName}' downloaded and saved to: {FilePath}", fileName, filePath);

            return new DownloadResult
            {
                FileName = fileName,
                DownloadUrl = $"http://localhost:7071/{Constants.DefaultDownloadFolder}?file={Uri.EscapeDataString(fileName)}",
            };
        }

        await _share.CreateIfNotExistsAsync();
        var directory = _share.GetRootDirectoryClient();
        var file = directory.GetFileClient(fileName);

        await file.CreateAsync(fileSize);
        await file.UploadAsync(contentStream);

        _logger.LogInformation("File '{FileName}' downloaded and saved to Azure File Share.", fileName);

        var host = string.IsNullOrWhiteSpace(_accessor.HttpContext?.Request.Host.Value) == true
                 ? "http://localhost:7071"
                 : $"{_accessor.HttpContext.Request.Scheme}://{_accessor.HttpContext.Request.Host.Value}";
        var downloadUrl = $"{host}/download?file={Uri.EscapeDataString(fileName)}";
        var sasUri = file.GenerateSasUri(ShareFileSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));

        return new DownloadResult
        {
            FileName = fileName,
            DownloadUrl = downloadUrl,
            SasUrl = sasUri.ToString()
        };
    }
}