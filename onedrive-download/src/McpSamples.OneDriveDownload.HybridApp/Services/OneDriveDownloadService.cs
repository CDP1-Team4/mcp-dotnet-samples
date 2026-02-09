using System.Text;

using Azure.Storage.Files.Shares;
using Azure.Storage.Sas;

using McpSamples.OneDriveDownload.HybridApp.Configurations;
using McpSamples.OneDriveDownload.HybridApp.Models;

using Microsoft.Graph;

using Directory = System.IO.Directory;
using File = System.IO.File;

namespace McpSamples.OneDriveDownload.HybridApp.Services;

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
/// <param name="settings">The <see cref="OneDriveDownloadAppSettings"/> instance.</param>
/// <param name="logger">The <see cref="ILogger{T}"/> instance.</param>
/// <param name="accessor">The <see cref="IHttpContextAccessor"/> instance.</param>
/// <param name="graph">The <see cref="GraphServiceClient"/> instance.</param>
/// <param name="share">The <see cref="ShareClient"/> instance.</param>
/// <param name="hostEnvironment">The <see cref="IHostEnvironment"/> instance.</param>
/// <param name="webHost">The <see cref="IWebHostEnvironment"/> instance.</param>
public class OneDriveDownloadService(
    OneDriveDownloadAppSettings settings,
    ILogger<OneDriveDownloadService> logger,
    IHttpContextAccessor accessor,
    GraphServiceClient graph,
    ShareClient? share = null,
    IHostEnvironment? host = null,
    IWebHostEnvironment? webHost = null) : IOneDriveDownloadService
{
    private readonly OneDriveDownloadAppSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<OneDriveDownloadService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IHttpContextAccessor _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    private readonly GraphServiceClient _graph = graph ?? throw new ArgumentNullException(nameof(graph));
    private readonly ShareClient? _share = share;
    private readonly IHostEnvironment? _host = host;
    private readonly IWebHostEnvironment? _webHost = webHost;

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadFileAsync(string sharingUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sharingUrl, nameof(sharingUrl));

        _logger.LogInformation("Downloading file from OneDrive sharing URL: {SharingUrl}", sharingUrl);

        var encodedSharingUrl = GetEncodedSharingUrl(sharingUrl);

        var driveItem = await GetDriveItemAsync(encodedSharingUrl);

        var fileSize = driveItem.Size ?? 0;
        var fileName = driveItem.Name;

        using var contentStream = await GetDriveItemContentStreamAsync(encodedSharingUrl);
        if (_settings.UseHttp == false)
        {
            var filePath = GetDownloadFilePath(fileName);

            using var fileStream = File.Create(filePath);
            await contentStream.CopyToAsync(fileStream);

            _logger.LogInformation("File '{FileName}' downloaded and saved to local path: {FilePath}", fileName, filePath);

            return new DownloadResult
            {
                FileName = fileName,
                DownloadUrl = filePath,
            };
        }

        var downloadUrl = default(string);
        if (_settings.UseAzureStorage == false)
        {
            var filePath = GetDownloadFilePath(fileName);

            using var fileStream = File.Create(filePath);
            await contentStream.CopyToAsync(fileStream);

            downloadUrl = GetDownloadUrl(fileName);

            _logger.LogInformation("File '{FileName}' downloaded to: {Url}", fileName, downloadUrl);

            return new DownloadResult
            {
                FileName = fileName,
                DownloadUrl = downloadUrl,
            };
        }

        await _share!.CreateIfNotExistsAsync();
        var directory = _share.GetRootDirectoryClient();
        var file = directory.GetFileClient(fileName);

        await file.CreateAsync(fileSize);
        await file.UploadAsync(contentStream);

        _logger.LogInformation("File '{FileName}' downloaded and saved to Azure File Share.", fileName);

        var sasUri = file.GenerateSasUri(ShareFileSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
        downloadUrl = GetDownloadUrl(fileName, sasUri);

        return new DownloadResult
        {
            FileName = fileName,
            DownloadUrl = downloadUrl,
            SasUrl = sasUri.ToString()
        };
    }

    private string GetEncodedSharingUrl(string sharingUrl)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(sharingUrl));
        var encodedSharingUrl = $"u!{encoded.TrimEnd('=').Replace('/', '_').Replace('+', '-')}";

        return encodedSharingUrl;
    }

    private async Task<DriveItem> GetDriveItemAsync(string encodedSharingUrl)
    {
        try
        {
            var driveItem = await _graph.Shares[encodedSharingUrl].DriveItem.Request().GetAsync();
            return driveItem;
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Graph request failed for share {EncodedSharingUrl}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}, ResponseBody={ResponseBody}",
                encodedSharingUrl,
                ex.StatusCode,
                ex.Error?.Code,
                ex.Error?.Message,
                ex.RawResponseBody);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Graph request failed for share {EncodedSharingUrl}. InnerException={InnerExceptionType}: {InnerExceptionMessage}",
                encodedSharingUrl,
                ex.InnerException?.GetType().FullName ?? "<none>",
                ex.InnerException?.Message ?? "<none>");

            throw;
        }
    }

    private async Task<Stream> GetDriveItemContentStreamAsync(string encodedSharingUrl)
    {
        try
        {
            var contentStream = await _graph.Shares[encodedSharingUrl].DriveItem.Content.Request().GetAsync();
            return contentStream;
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Graph content request failed for share {EncodedSharingUrl}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}, ResponseBody={ResponseBody}",
                encodedSharingUrl,
                ex.StatusCode,
                ex.Error?.Code,
                ex.Error?.Message,
                ex.RawResponseBody);

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Graph content request failed for share {EncodedSharingUrl}. InnerException={InnerExceptionType}: {InnerExceptionMessage}",
                encodedSharingUrl,
                ex.InnerException?.GetType().FullName ?? "<none>",
                ex.InnerException?.Message ?? "<none>");

            throw;
        }
    }

    private string GetDownloadUrl(string filename, Uri? sasUri = null)
    {
        var host = string.Format(
            "{0}://{1}",
            _accessor.HttpContext!.Request.Scheme,
            _accessor.HttpContext!.Request.Host);

        var downloadUrl = $"{host}/{Constants.DefaultDownloadFolder}/{Uri.EscapeDataString(filename)}";
        if (sasUri != null)
        {
            downloadUrl += $"?{sasUri.Query.TrimStart('?')}";
        }

        return downloadUrl;
    }

    private string GetDownloadFilePath(string filename)
    {
        var downloads = Path.Combine(GetWebRootPath(), Constants.DefaultDownloadFolder);
        Directory.CreateDirectory(downloads);

        var filePath = Path.Combine(downloads, filename);

        return filePath;
    }

    private string GetWebRootPath()
    {
        var wwwroot = string.IsNullOrWhiteSpace(_webHost?.WebRootPath)
                    ? Path.Combine(_host?.ContentRootPath ?? string.Empty, Constants.DefaultWebRootFolder)
                    : _webHost!.WebRootPath;

        return wwwroot;
    }
}