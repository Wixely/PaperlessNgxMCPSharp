using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaperlessNgxMCPSharp.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace PaperlessNgxMCPSharp.Services;

/// <summary>
/// Thin client over the Paperless-ngx REST API (https://docs.paperless-ngx.com/api/).
/// All higher-level conveniences are layered on top so tool classes stay declarative.
/// </summary>
public sealed class PaperlessService : IDisposable
{
    private readonly PaperlessOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private bool _disposed;

    public PaperlessService(IOptions<PaperlessOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new InvalidOperationException("Paperless:BaseUrl is not configured.");

        var baseUrl = _options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/";
        _baseUri = new Uri(baseUrl, UriKind.Absolute);

        var handler = new HttpClientHandler();
        if (_options.AllowInvalidCertificate)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }

        _http = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.RequestTimeoutSeconds)),
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", _options.ApiToken);
        }
        else if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            var credential = $"{_options.Username}:{_options.Password ?? string.Empty}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        }
    }

    public PaperlessOptions Options => _options;
    public bool IsReadOnly => _options.ReadOnly;
    public HttpClient Http => _http;

    public void EnsureWriteAllowed(string operation)
    {
        if (_options.ReadOnly)
        {
            throw new McpException(
                $"MCP tool '{operation}' is blocked by server configuration. " +
                "Set Paperless:ReadOnly=false to allow writes.");
        }
    }

    public void EnsureDeleteAllowed(string operation)
    {
        EnsureWriteAllowed(operation);
        if (!_options.AllowDelete)
        {
            throw new McpException(
                $"MCP tool '{operation}' requires Paperless:AllowDelete=true (in addition to Paperless:ReadOnly=false).");
        }
    }

    public void EnsureFeature(bool flag, string feature)
    {
        if (!flag)
        {
            throw new McpException($"{feature} tools are disabled by server configuration.");
        }
    }

    public string ResolveDownloadDirectory()
    {
        var dir = string.IsNullOrWhiteSpace(_options.DownloadDirectory)
            ? Path.Combine(Path.GetTempPath(), "PaperlessNgxMCPSharp")
            : _options.DownloadDirectory!;
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<JsonNode?> GetJsonAsync(string relativePath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(relativePath, ct);
        await EnsureSuccessAsync(response, ct);
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct);
    }

    public async Task<JsonNode?> SendJsonAsync(HttpMethod method, string relativePath, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return null;

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonNode.ParseAsync(stream, cancellationToken: ct);
    }

    /// <summary>Auto-paginate `?page=N` results capped by Options.MaxPages and merge into a flat list.</summary>
    public async Task<JsonArray> ListAllAsync(string relativePath, CancellationToken ct)
    {
        var aggregate = new JsonArray();
        var url = AppendPageSize(relativePath, _options.DefaultPageSize);
        var pageCount = 0;
        while (!string.IsNullOrEmpty(url) && pageCount < Math.Max(1, _options.MaxPages))
        {
            var node = await GetJsonAsync(url, ct);
            if (node is not JsonObject obj) break;

            if (obj["results"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    aggregate.Add(item?.DeepClone());
                }
            }

            var next = obj["next"]?.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(next))
                break;

            // The API returns absolute next URLs. Strip the host so HttpClient can use BaseAddress.
            url = TrimToRelative(next!);
            pageCount++;
        }
        return aggregate;
    }

    public async Task<(byte[] Bytes, string? ContentType, string? FileName)> DownloadBytesAsync(string relativePath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(relativePath, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        return (bytes, contentType, fileName);
    }

    public async Task<JsonNode?> PostMultipartAsync(string relativePath, MultipartFormDataContent content, CancellationToken ct)
    {
        using var response = await _http.PostAsync(relativePath, content, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
            return null;
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        // post_document/ returns a quoted task UUID string. Either JSON object or raw string.
        try { return JsonNode.Parse(text); }
        catch (JsonException) { return JsonValue.Create(text.Trim('"')); }
    }

    private string TrimToRelative(string absolute)
    {
        if (Uri.TryCreate(absolute, UriKind.Absolute, out var abs) && _baseUri.IsBaseOf(abs))
        {
            return _baseUri.MakeRelativeUri(abs).ToString();
        }
        return absolute;
    }

    private static string AppendPageSize(string url, int pageSize)
    {
        if (url.Contains("page_size=", StringComparison.OrdinalIgnoreCase))
            return url;
        var sep = url.Contains('?') ? '&' : '?';
        return $"{url}{sep}page_size={pageSize}";
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string body;
        try { body = await response.Content.ReadAsStringAsync(ct); }
        catch { body = string.Empty; }
        if (body.Length > 2000) body = body[..2000] + "…(truncated)";
        throw new McpException(
            $"Paperless API returned {(int)response.StatusCode} {response.ReasonPhrase} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}. Body: {body}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
