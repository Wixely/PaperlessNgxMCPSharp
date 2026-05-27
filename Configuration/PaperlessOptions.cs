namespace PaperlessNgxMCPSharp.Configuration;

public sealed class PaperlessOptions
{
    public const string SectionName = "Paperless";

    /// <summary>Base URL of the Paperless-ngx instance (no trailing /api). Example: https://paperless.example.com/.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000/";

    /// <summary>Paperless-ngx API token. Sent as `Authorization: Token <value>`.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Optional HTTP Basic auth username (used if ApiToken is blank).</summary>
    public string? Username { get; set; }

    /// <summary>Optional HTTP Basic auth password (used with Username).</summary>
    public string? Password { get; set; }

    /// <summary>When true, all write/update/delete tools are disabled. Default true.</summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>If false, document upload tools are hidden.</summary>
    public bool EnableUploads { get; set; } = true;

    /// <summary>If false, document download tools (binary file + preview + thumbnail) are hidden.</summary>
    public bool EnableDownloads { get; set; } = true;

    /// <summary>If false, tag/correspondent/document-type/storage-path/custom-field management tools are hidden.</summary>
    public bool EnableMetadataManagement { get; set; } = true;

    /// <summary>If false, deletion of documents and metadata is forbidden even when ReadOnly=false.</summary>
    public bool AllowDelete { get; set; } = false;

    /// <summary>Optional directory where downloaded documents are written. Falls back to the system temp directory.</summary>
    public string? DownloadDirectory { get; set; }

    /// <summary>Maximum bytes of textual document content returned inline by `get_document_content`. Larger content is truncated with a flag.</summary>
    public int MaxInlineContentBytes { get; set; } = 65_536;

    /// <summary>Maximum bytes of binary file returned inline (base64) by `download_document_inline`. Above this, the tool writes to disk and returns a path.</summary>
    public int MaxInlineBinaryBytes { get; set; } = 2_000_000;

    /// <summary>Default page size for list operations. Paperless caps at 100.</summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Max pages traversed when auto-paginating. Guards against runaway calls.</summary>
    public int MaxPages { get; set; } = 5;

    /// <summary>HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>User-Agent header sent to Paperless.</summary>
    public string UserAgent { get; set; } = "PaperlessNgxMCPSharp";

    /// <summary>If true, ignore TLS certificate validation errors. Use only for self-signed homelab instances.</summary>
    public bool AllowInvalidCertificate { get; set; } = false;
}

public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5708;
    public string Path { get; set; } = "/mcp";

    /// <summary>Service name when running as a Windows Service.</summary>
    public string WindowsServiceName { get; set; } = "PaperlessNgxMCPSharp";

    /// <summary>Optional MCP endpoint password. Blank disables MCP password auth.</summary>
    public string Password { get; set; } = string.Empty;
}
