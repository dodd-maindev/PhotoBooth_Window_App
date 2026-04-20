using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Photobooth.Desktop.Models;

namespace Photobooth.Desktop.Services;

public sealed class ImageProcessingClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly FileLogger _logger;

    public ImageProcessingClient(AppSettings settings, FileLogger logger)
    {
        _settings = settings;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(settings.ApiBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(settings.ApiTimeoutSeconds)
        };
    }

    public async Task<string> ProcessAsync(string imagePath, string filterType, string outputFolder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputFolder);

        var fileName = Path.GetFileNameWithoutExtension(imagePath);
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var outputPath = Path.Combine(outputFolder, $"{fileName}-{filterType}-{timestamp}.png");

        Exception? lastException = null;

        for (var attempt = 1; attempt <= Math.Max(1, _settings.ApiRetries); attempt++)
        {
            try
            {
                await _logger.DebugAsync($"Processing attempt {attempt}: {Path.GetFileName(imagePath)} -> {Path.GetFileName(outputPath)}").ConfigureAwait(false);
                
                using var content = new MultipartFormDataContent();
                await using var fileStream = File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(fileContent, "image_file", Path.GetFileName(imagePath));
                content.Add(new StringContent(filterType), "filter_type");

                await _logger.DebugAsync($"Sending POST /process-image for {Path.GetFileName(imagePath)}").ConfigureAwait(false);
                using var response = await _httpClient.PostAsync("/process-image", content, cancellationToken).ConfigureAwait(false);
                var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var responseText = System.Text.Encoding.UTF8.GetString(responseBytes);
                    throw new InvalidOperationException($"API returned {(int)response.StatusCode}: {responseText}");
                }

                await _logger.DebugAsync($"Received {responseBytes.Length} bytes from API").ConfigureAwait(false);
                await File.WriteAllBytesAsync(outputPath, responseBytes, cancellationToken).ConfigureAwait(false);
                await _logger.InfoAsync($"✓ Processed {Path.GetFileName(imagePath)} with filter '{filterType}' -> {outputPath}").ConfigureAwait(false);
                return outputPath;
            }
            catch (Exception ex) when (attempt < _settings.ApiRetries)
            {
                lastException = ex;
                await _logger.WarnAsync($"API attempt {attempt}/{_settings.ApiRetries} failed: {ex.Message}").ConfigureAwait(false);
                await Task.Delay(_settings.RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                await _logger.ErrorAsync($"Processing failed after {attempt} attempt(s): {ex.Message}", ex).ConfigureAwait(false);
                break;
            }
        }

        throw new InvalidOperationException($"Failed to process {imagePath}", lastException);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}