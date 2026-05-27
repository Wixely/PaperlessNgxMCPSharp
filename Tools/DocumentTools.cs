using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaperlessNgxMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace PaperlessNgxMCPSharp.Tools;

[McpServerToolType]
public static class DocumentTools
{
    [McpServerTool(Name = "list_documents"),
     Description("List documents, optionally filtered. Returns paginated metadata. Use the document id with get_document for full detail.")]
    public static async Task<string> ListDocuments(
        PaperlessService svc,
        [Description("Free-text search across title and content (passed as `query`).")] string? query = null,
        [Description("Filter: tag id (single).")] int? tagId = null,
        [Description("Filter: correspondent id.")] int? correspondentId = null,
        [Description("Filter: document type id.")] int? documentTypeId = null,
        [Description("Filter: storage path id.")] int? storagePathId = null,
        [Description("Created on/after (ISO 8601 date).")] string? createdAfter = null,
        [Description("Created on/before (ISO 8601 date).")] string? createdBefore = null,
        [Description("Ordering, e.g. `-created`, `title`, `-added`. Prefix `-` for descending.")] string? ordering = null,
        [Description("Page number (1-based). Defaults to 1.")] int page = 1,
        CancellationToken ct = default)
    {
        var qs = new List<string> { $"page={Math.Max(1, page)}", $"page_size={svc.Options.DefaultPageSize}" };
        if (!string.IsNullOrWhiteSpace(query)) qs.Add($"query={Uri.EscapeDataString(query)}");
        if (tagId.HasValue) qs.Add($"tags__id__in={tagId.Value}");
        if (correspondentId.HasValue) qs.Add($"correspondent__id={correspondentId.Value}");
        if (documentTypeId.HasValue) qs.Add($"document_type__id={documentTypeId.Value}");
        if (storagePathId.HasValue) qs.Add($"storage_path__id={storagePathId.Value}");
        if (!string.IsNullOrWhiteSpace(createdAfter)) qs.Add($"created__date__gte={Uri.EscapeDataString(createdAfter)}");
        if (!string.IsNullOrWhiteSpace(createdBefore)) qs.Add($"created__date__lte={Uri.EscapeDataString(createdBefore)}");
        if (!string.IsNullOrWhiteSpace(ordering)) qs.Add($"ordering={Uri.EscapeDataString(ordering)}");

        var url = "api/documents/?" + string.Join('&', qs);
        var node = await svc.GetJsonAsync(url, ct);
        return JsonSerializer.Serialize(Summarize(node), JsonOpts.Default);
    }

    [McpServerTool(Name = "get_document"),
     Description("Get full metadata for one document by id. Includes title, tags, correspondent, custom fields, dates, and archive paths.")]
    public static async Task<string> GetDocument(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/documents/{id}/", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "null";
    }

    [McpServerTool(Name = "get_document_content"),
     Description("Return the OCR/extracted text of a document. Content larger than Paperless:MaxInlineContentBytes is truncated with a flag.")]
    public static async Task<string> GetDocumentContent(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/documents/{id}/", ct);
        var content = node?["content"]?.GetValue<string?>() ?? string.Empty;
        var truncated = false;
        var limit = svc.Options.MaxInlineContentBytes;
        if (limit > 0 && content.Length > limit)
        {
            content = content[..limit];
            truncated = true;
        }
        return JsonSerializer.Serialize(new
        {
            id,
            title = node?["title"]?.GetValue<string?>(),
            length = node?["content"]?.GetValue<string?>()?.Length ?? 0,
            truncated,
            content,
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_document_metadata"),
     Description("Return Paperless metadata (file type, page count, archive checksum, original filename, etc.) for a document.")]
    public static async Task<string> GetDocumentMetadata(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/documents/{id}/metadata/", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "null";
    }

    [McpServerTool(Name = "get_document_notes"),
     Description("List notes attached to a document.")]
    public static async Task<string> GetDocumentNotes(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/documents/{id}/notes/", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "[]";
    }

    [McpServerTool(Name = "add_document_note"),
     Description("Add a note to a document. Requires write mode.")]
    public static async Task<string> AddDocumentNote(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("Note text.")] string note,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("add_document_note");
        var result = await svc.SendJsonAsync(HttpMethod.Post, $"api/documents/{id}/notes/", new { note }, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "null";
    }

    [McpServerTool(Name = "delete_document_note"),
     Description("Delete a note from a document. Requires Paperless:ReadOnly=false and Paperless:AllowDelete=true.")]
    public static async Task<string> DeleteDocumentNote(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("Note id.")] int noteId,
        CancellationToken ct = default)
    {
        svc.EnsureDeleteAllowed("delete_document_note");
        await svc.SendJsonAsync(HttpMethod.Delete, $"api/documents/{id}/notes/?id={noteId}", null, ct);
        return JsonSerializer.Serialize(new { deleted = true, id, noteId }, JsonOpts.Default);
    }

    [McpServerTool(Name = "update_document"),
     Description("Update a document's mutable fields (title, tags, correspondent, document_type, storage_path, archive_serial_number, created, custom_fields). PATCH semantics. Requires write mode.")]
    public static async Task<string> UpdateDocument(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("New title (optional).")] string? title = null,
        [Description("Replacement tag id list (e.g. [1,2,3]) as JSON array. If omitted, tags are not changed.")] string? tagIdsJson = null,
        [Description("Correspondent id, or null to clear.")] int? correspondentId = null,
        [Description("Set to true to explicitly clear correspondent.")] bool clearCorrespondent = false,
        [Description("Document type id, or null to clear.")] int? documentTypeId = null,
        [Description("Set to true to explicitly clear document type.")] bool clearDocumentType = false,
        [Description("Storage path id, or null to clear.")] int? storagePathId = null,
        [Description("Set to true to explicitly clear storage path.")] bool clearStoragePath = false,
        [Description("Archive serial number (string or numeric).")] string? archiveSerialNumber = null,
        [Description("Created date (ISO 8601).")] string? created = null,
        [Description("Custom fields as JSON array, e.g. `[{\"field\":1,\"value\":\"x\"}]`.")] string? customFieldsJson = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("update_document");

        var patch = new JsonObject();
        if (title is not null) patch["title"] = title;
        if (tagIdsJson is not null)
        {
            var tagArray = ParseIntArray(tagIdsJson, nameof(tagIdsJson));
            patch["tags"] = tagArray;
        }
        if (correspondentId.HasValue) patch["correspondent"] = correspondentId.Value;
        else if (clearCorrespondent) patch["correspondent"] = null;
        if (documentTypeId.HasValue) patch["document_type"] = documentTypeId.Value;
        else if (clearDocumentType) patch["document_type"] = null;
        if (storagePathId.HasValue) patch["storage_path"] = storagePathId.Value;
        else if (clearStoragePath) patch["storage_path"] = null;
        if (archiveSerialNumber is not null) patch["archive_serial_number"] = archiveSerialNumber;
        if (created is not null) patch["created"] = created;
        if (customFieldsJson is not null)
        {
            var parsed = JsonNode.Parse(customFieldsJson)
                ?? throw new McpException("customFieldsJson is not valid JSON.");
            patch["custom_fields"] = parsed;
        }

        if (patch.Count == 0)
            throw new McpException("update_document: at least one field must be supplied.");

        var result = await svc.SendJsonAsync(HttpMethod.Patch, $"api/documents/{id}/", patch, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "null";
    }

    [McpServerTool(Name = "add_document_tag"),
     Description("Add a single tag to a document without touching other tags. Requires write mode.")]
    public static async Task<string> AddDocumentTag(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("Tag id to add.")] int tagId,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("add_document_tag");
        var node = await svc.GetJsonAsync($"api/documents/{id}/", ct) as JsonObject
            ?? throw new McpException($"Document {id} not found.");
        var tags = (node["tags"] as JsonArray) ?? new JsonArray();
        var existing = tags.Select(t => t?.GetValue<int>() ?? -1).ToHashSet();
        if (existing.Add(tagId))
        {
            var patch = new JsonObject { ["tags"] = new JsonArray(existing.Select(i => (JsonNode)JsonValue.Create(i)).ToArray()) };
            var result = await svc.SendJsonAsync(HttpMethod.Patch, $"api/documents/{id}/", patch, ct);
            return result?.ToJsonString(JsonOpts.Default) ?? "null";
        }
        return JsonSerializer.Serialize(new { id, tagId, alreadyPresent = true }, JsonOpts.Default);
    }

    [McpServerTool(Name = "remove_document_tag"),
     Description("Remove a single tag from a document without touching other tags. Requires write mode.")]
    public static async Task<string> RemoveDocumentTag(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("Tag id to remove.")] int tagId,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("remove_document_tag");
        var node = await svc.GetJsonAsync($"api/documents/{id}/", ct) as JsonObject
            ?? throw new McpException($"Document {id} not found.");
        var tags = (node["tags"] as JsonArray) ?? new JsonArray();
        var existing = tags.Select(t => t?.GetValue<int>() ?? -1).ToList();
        if (!existing.Remove(tagId))
        {
            return JsonSerializer.Serialize(new { id, tagId, notPresent = true }, JsonOpts.Default);
        }
        var patch = new JsonObject { ["tags"] = new JsonArray(existing.Select(i => (JsonNode)JsonValue.Create(i)).ToArray()) };
        var result = await svc.SendJsonAsync(HttpMethod.Patch, $"api/documents/{id}/", patch, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "null";
    }

    [McpServerTool(Name = "delete_document"),
     Description("Permanently delete a document. Requires Paperless:ReadOnly=false and Paperless:AllowDelete=true.")]
    public static async Task<string> DeleteDocument(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        svc.EnsureDeleteAllowed("delete_document");
        await svc.SendJsonAsync(HttpMethod.Delete, $"api/documents/{id}/", null, ct);
        return JsonSerializer.Serialize(new { deleted = true, id }, JsonOpts.Default);
    }

    [McpServerTool(Name = "download_document"),
     Description("Download a document (original or archived PDF) to the server's DownloadDirectory. Returns the local file path.")]
    public static async Task<string> DownloadDocument(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("If true, request the original ingested file instead of the archived PDF.")] bool original = false,
        [Description("Optional override file name (without directory). Defaults to the server-provided name.")] string? fileName = null,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableDownloads, "Download");
        var url = $"api/documents/{id}/download/" + (original ? "?original=true" : string.Empty);
        var (bytes, contentType, serverName) = await svc.DownloadBytesAsync(url, ct);
        var name = string.IsNullOrWhiteSpace(fileName) ? (serverName ?? $"document-{id}") : fileName!;
        var dir = svc.ResolveDownloadDirectory();
        var path = Path.Combine(dir, SanitizeFileName(name));
        await File.WriteAllBytesAsync(path, bytes, ct);
        return JsonSerializer.Serialize(new { id, path, bytes = bytes.Length, contentType, original }, JsonOpts.Default);
    }

    [McpServerTool(Name = "download_document_inline"),
     Description("Return a document's bytes inline as base64. Falls back to writing to disk and returning the path if it would exceed Paperless:MaxInlineBinaryBytes.")]
    public static async Task<string> DownloadDocumentInline(
        PaperlessService svc,
        [Description("Document id.")] int id,
        [Description("If true, request the original file instead of the archived PDF.")] bool original = false,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableDownloads, "Download");
        var url = $"api/documents/{id}/download/" + (original ? "?original=true" : string.Empty);
        var (bytes, contentType, serverName) = await svc.DownloadBytesAsync(url, ct);
        if (bytes.Length > svc.Options.MaxInlineBinaryBytes)
        {
            var dir = svc.ResolveDownloadDirectory();
            var name = SanitizeFileName(serverName ?? $"document-{id}");
            var path = Path.Combine(dir, name);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return JsonSerializer.Serialize(new
            {
                id,
                inline = false,
                reason = "size_exceeds_MaxInlineBinaryBytes",
                bytes = bytes.Length,
                contentType,
                path,
            }, JsonOpts.Default);
        }
        return JsonSerializer.Serialize(new
        {
            id,
            inline = true,
            bytes = bytes.Length,
            contentType,
            fileName = serverName,
            base64 = Convert.ToBase64String(bytes),
        }, JsonOpts.Default);
    }

    [McpServerTool(Name = "download_document_thumbnail"),
     Description("Download a document thumbnail PNG to disk and return the local path.")]
    public static async Task<string> DownloadDocumentThumbnail(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableDownloads, "Download");
        var (bytes, contentType, serverName) = await svc.DownloadBytesAsync($"api/documents/{id}/thumb/", ct);
        var dir = svc.ResolveDownloadDirectory();
        var name = SanitizeFileName(serverName ?? $"document-{id}-thumb.png");
        var path = Path.Combine(dir, name);
        await File.WriteAllBytesAsync(path, bytes, ct);
        return JsonSerializer.Serialize(new { id, path, bytes = bytes.Length, contentType }, JsonOpts.Default);
    }

    [McpServerTool(Name = "download_document_preview"),
     Description("Download a document preview rendering (usually PDF) to disk and return the local path.")]
    public static async Task<string> DownloadDocumentPreview(
        PaperlessService svc,
        [Description("Document id.")] int id,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableDownloads, "Download");
        var (bytes, contentType, serverName) = await svc.DownloadBytesAsync($"api/documents/{id}/preview/", ct);
        var dir = svc.ResolveDownloadDirectory();
        var name = SanitizeFileName(serverName ?? $"document-{id}-preview");
        var path = Path.Combine(dir, name);
        await File.WriteAllBytesAsync(path, bytes, ct);
        return JsonSerializer.Serialize(new { id, path, bytes = bytes.Length, contentType }, JsonOpts.Default);
    }

    [McpServerTool(Name = "upload_document"),
     Description("Upload a file to Paperless for consumption. Returns the consumption task id; use get_task to follow completion. Requires write mode.")]
    public static async Task<string> UploadDocument(
        PaperlessService svc,
        [Description("Absolute path to a file on the server's filesystem to upload.")] string filePath,
        [Description("Optional override title (otherwise Paperless infers from filename or OCR).")] string? title = null,
        [Description("Optional ISO 8601 created date.")] string? created = null,
        [Description("Optional correspondent id.")] int? correspondentId = null,
        [Description("Optional document type id.")] int? documentTypeId = null,
        [Description("Optional storage path id.")] int? storagePathId = null,
        [Description("Optional tag ids as JSON array, e.g. `[1,2]`.")] string? tagIdsJson = null,
        [Description("Optional archive serial number.")] string? archiveSerialNumber = null,
        CancellationToken ct = default)
    {
        svc.EnsureWriteAllowed("upload_document");
        svc.EnsureFeature(svc.Options.EnableUploads, "Upload");

        if (!File.Exists(filePath))
            throw new McpException($"File not found: {filePath}");

        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();

        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessContentType(filePath));
        content.Add(fileContent, "document", Path.GetFileName(filePath));

        if (!string.IsNullOrWhiteSpace(title)) content.Add(new StringContent(title), "title");
        if (!string.IsNullOrWhiteSpace(created)) content.Add(new StringContent(created), "created");
        if (correspondentId.HasValue) content.Add(new StringContent(correspondentId.Value.ToString()), "correspondent");
        if (documentTypeId.HasValue) content.Add(new StringContent(documentTypeId.Value.ToString()), "document_type");
        if (storagePathId.HasValue) content.Add(new StringContent(storagePathId.Value.ToString()), "storage_path");
        if (!string.IsNullOrWhiteSpace(archiveSerialNumber)) content.Add(new StringContent(archiveSerialNumber), "archive_serial_number");
        if (!string.IsNullOrWhiteSpace(tagIdsJson))
        {
            foreach (var t in ParseIntArray(tagIdsJson, nameof(tagIdsJson)))
            {
                content.Add(new StringContent(t!.GetValue<int>().ToString()), "tags");
            }
        }

        var result = await svc.PostMultipartAsync("api/documents/post_document/", content, ct);
        return JsonSerializer.Serialize(new { taskId = result?.ToString(), fileName = Path.GetFileName(filePath) }, JsonOpts.Default);
    }

    [McpServerTool(Name = "get_task"),
     Description("Get the status of a consumption (or other) task by id or UUID.")]
    public static async Task<string> GetTask(
        PaperlessService svc,
        [Description("Task UUID returned by upload_document (preferred) or integer task id.")] string taskId,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/tasks/?task_id={Uri.EscapeDataString(taskId)}", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "[]";
    }

    [McpServerTool(Name = "bulk_edit_documents"),
     Description("Run a Paperless bulk_edit operation across multiple documents. Methods: set_correspondent, set_document_type, set_storage_path, add_tag, remove_tag, modify_tags, delete, redo_ocr, set_permissions. Requires write mode (and AllowDelete for `delete`).")]
    public static async Task<string> BulkEditDocuments(
        PaperlessService svc,
        [Description("Document ids as JSON array, e.g. `[1,2,3]`.")] string documentIdsJson,
        [Description("Bulk method name.")] string method,
        [Description("Optional parameters object as JSON, e.g. `{\"tag\":3}` or `{\"add_tags\":[1],\"remove_tags\":[2]}`.")] string? parametersJson = null,
        CancellationToken ct = default)
    {
        if (string.Equals(method, "delete", StringComparison.OrdinalIgnoreCase))
            svc.EnsureDeleteAllowed("bulk_edit_documents:delete");
        else
            svc.EnsureWriteAllowed("bulk_edit_documents");

        var docIds = ParseIntArray(documentIdsJson, nameof(documentIdsJson));
        var body = new JsonObject
        {
            ["documents"] = docIds,
            ["method"] = method,
        };
        if (!string.IsNullOrWhiteSpace(parametersJson))
        {
            body["parameters"] = JsonNode.Parse(parametersJson)
                ?? throw new McpException("parametersJson is not valid JSON.");
        }
        else
        {
            body["parameters"] = new JsonObject();
        }
        var result = await svc.SendJsonAsync(HttpMethod.Post, "api/documents/bulk_edit/", body, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    private static JsonArray ParseIntArray(string json, string paramName)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new McpException($"{paramName} is not valid JSON: {ex.Message}"); }
        if (node is not JsonArray arr)
            throw new McpException($"{paramName} must be a JSON array of integers.");
        var result = new JsonArray();
        foreach (var item in arr)
        {
            if (item is null || item is not JsonValue v || !v.TryGetValue<int>(out var i))
                throw new McpException($"{paramName} must contain only integers.");
            result.Add(i);
        }
        return result;
    }

    private static object? Summarize(JsonNode? node)
    {
        if (node is not JsonObject obj) return node;
        var results = obj["results"] as JsonArray;
        if (results is null) return obj;
        var slim = results.Select(r => r is JsonObject d ? new
        {
            id = d["id"]?.GetValue<int?>(),
            title = d["title"]?.GetValue<string?>(),
            created = d["created"]?.GetValue<string?>(),
            added = d["added"]?.GetValue<string?>(),
            modified = d["modified"]?.GetValue<string?>(),
            correspondent = d["correspondent"]?.GetValue<int?>(),
            document_type = d["document_type"]?.GetValue<int?>(),
            storage_path = d["storage_path"]?.GetValue<int?>(),
            tags = (d["tags"] as JsonArray)?.Select(t => t?.GetValue<int?>()),
            archive_serial_number = d["archive_serial_number"]?.ToString(),
            original_file_name = d["original_file_name"]?.GetValue<string?>(),
            archived_file_name = d["archived_file_name"]?.GetValue<string?>(),
        } : (object?)r);
        return new
        {
            count = obj["count"]?.GetValue<int?>(),
            next = obj["next"]?.GetValue<string?>(),
            previous = obj["previous"]?.GetValue<string?>(),
            results = slim,
        };
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return string.IsNullOrWhiteSpace(name) ? "download.bin" : name;
    }

    private static string GuessContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".eml" => "message/rfc822",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" => "application/msword",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".odt" => "application/vnd.oasis.opendocument.text",
            _ => "application/octet-stream",
        };
}
