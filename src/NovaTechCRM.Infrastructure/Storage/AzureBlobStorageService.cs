using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using NovaTechCRM.Infrastructure.Pdf;

namespace NovaTechCRM.Infrastructure.Storage;

// Azure Blob Storage via REST API — intentionally not using the Azure.Storage.Blobs SDK
// because the last time we upgraded it it broke the ARM template deployment pipeline.
// Keep on raw HTTP until NOVA-71 is resolved.
// TODO: pre-signed URLs for direct browser downloads (NOVA-72)
public class AzureBlobStorageService : IStorageService
{
    private readonly HttpClient _http;
    private readonly AzureBlobOptions _opts;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        HttpClient http,
        IOptions<AzureBlobOptions> opts,
        ILogger<AzureBlobStorageService> logger)
    {
        _http   = http;
        _opts   = opts.Value;
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        string path, byte[] data, string contentType, CancellationToken ct = default)
    {
        var url     = $"{_opts.BaseUrl}/{_opts.Container}/{path}";
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Headers.Add("x-ms-blob-type", "BlockBlob");
        content.Headers.Add("x-ms-version", "2020-04-08");

        var response = await _http.PutAsync(url, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Azure Blob upload failed for {Path}: {Status} {Body}",
                path, response.StatusCode, err);
            throw new IOException($"Blob upload failed: {response.StatusCode}");
        }

        _logger.LogDebug("Uploaded blob: {Path} ({Bytes} bytes)", path, data.Length);
        return $"{_opts.PublicBaseUrl}/{_opts.Container}/{path}";
    }

    public async Task<byte[]> DownloadAsync(string path, CancellationToken ct = default)
    {
        var url      = $"{_opts.BaseUrl}/{_opts.Container}/{path}";
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            throw new FileNotFoundException($"Blob not found: {path}");

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var url      = $"{_opts.BaseUrl}/{_opts.Container}/{path}";
        var response = await _http.DeleteAsync(url, ct);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            _logger.LogWarning("Azure Blob delete returned {Status} for {Path}",
                response.StatusCode, path);
    }
}

public class AzureBlobOptions
{
    public string BaseUrl       { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string Container     { get; set; } = "novatech";
    public string AccountName   { get; set; } = string.Empty;
    public string AccountKey    { get; set; } = string.Empty;
}
