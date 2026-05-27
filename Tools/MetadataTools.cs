using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaperlessNgxMCPSharp.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace PaperlessNgxMCPSharp.Tools;

/// <summary>
/// CRUD over Paperless metadata collections: tags, correspondents, document types,
/// storage paths, and custom fields. Every collection follows the same shape, so a
/// generic helper keeps the surface tight.
/// </summary>
[McpServerToolType]
public static class MetadataTools
{
    private record Endpoint(string Collection, string Singular);

    private static readonly Dictionary<string, Endpoint> Endpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tags"]            = new("api/tags/", "tag"),
        ["correspondents"]  = new("api/correspondents/", "correspondent"),
        ["document_types"]  = new("api/document_types/", "document type"),
        ["storage_paths"]   = new("api/storage_paths/", "storage path"),
        ["custom_fields"]   = new("api/custom_fields/", "custom field"),
    };

    [McpServerTool(Name = "list_tags"),
     Description("List all tags (id, name, colour, matching algorithm, document counts).")]
    public static Task<string> ListTags(PaperlessService svc, CancellationToken ct = default)
        => ListAll(svc, "tags", ct);

    [McpServerTool(Name = "list_correspondents"),
     Description("List all correspondents.")]
    public static Task<string> ListCorrespondents(PaperlessService svc, CancellationToken ct = default)
        => ListAll(svc, "correspondents", ct);

    [McpServerTool(Name = "list_document_types"),
     Description("List all document types.")]
    public static Task<string> ListDocumentTypes(PaperlessService svc, CancellationToken ct = default)
        => ListAll(svc, "document_types", ct);

    [McpServerTool(Name = "list_storage_paths"),
     Description("List all storage paths.")]
    public static Task<string> ListStoragePaths(PaperlessService svc, CancellationToken ct = default)
        => ListAll(svc, "storage_paths", ct);

    [McpServerTool(Name = "list_custom_fields"),
     Description("List all custom fields defined in Paperless.")]
    public static Task<string> ListCustomFields(PaperlessService svc, CancellationToken ct = default)
        => ListAll(svc, "custom_fields", ct);

    [McpServerTool(Name = "create_metadata"),
     Description("Create an entry in a Paperless metadata collection. Collection must be one of: tags, correspondents, document_types, storage_paths, custom_fields. Body is a JSON object matching the Paperless schema (e.g. tags accept name/colour/matching_algorithm/match/is_insensitive). Requires write mode.")]
    public static async Task<string> CreateMetadata(
        PaperlessService svc,
        [Description("Collection name.")] string collection,
        [Description("JSON object body matching the Paperless schema.")] string bodyJson,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableMetadataManagement, "Metadata management");
        svc.EnsureWriteAllowed($"create_metadata:{collection}");
        var endpoint = Resolve(collection);
        var body = JsonNode.Parse(bodyJson) as JsonObject
            ?? throw new McpException("bodyJson must be a JSON object.");
        var result = await svc.SendJsonAsync(HttpMethod.Post, endpoint.Collection, body, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    [McpServerTool(Name = "update_metadata"),
     Description("PATCH-update an entry in a Paperless metadata collection. Requires write mode.")]
    public static async Task<string> UpdateMetadata(
        PaperlessService svc,
        [Description("Collection name (tags, correspondents, document_types, storage_paths, custom_fields).")] string collection,
        [Description("Entry id.")] int id,
        [Description("JSON object body with fields to update.")] string bodyJson,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableMetadataManagement, "Metadata management");
        svc.EnsureWriteAllowed($"update_metadata:{collection}");
        var endpoint = Resolve(collection);
        var body = JsonNode.Parse(bodyJson) as JsonObject
            ?? throw new McpException("bodyJson must be a JSON object.");
        var result = await svc.SendJsonAsync(HttpMethod.Patch, $"{endpoint.Collection}{id}/", body, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    [McpServerTool(Name = "delete_metadata"),
     Description("Delete an entry from a Paperless metadata collection. Requires Paperless:ReadOnly=false AND Paperless:AllowDelete=true.")]
    public static async Task<string> DeleteMetadata(
        PaperlessService svc,
        [Description("Collection name.")] string collection,
        [Description("Entry id.")] int id,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableMetadataManagement, "Metadata management");
        svc.EnsureDeleteAllowed($"delete_metadata:{collection}");
        var endpoint = Resolve(collection);
        await svc.SendJsonAsync(HttpMethod.Delete, $"{endpoint.Collection}{id}/", null, ct);
        return JsonSerializer.Serialize(new { deleted = true, collection, id }, JsonOpts.Default);
    }

    [McpServerTool(Name = "create_tag"),
     Description("Convenience: create a tag with the most common fields. Requires write mode.")]
    public static async Task<string> CreateTag(
        PaperlessService svc,
        [Description("Tag name.")] string name,
        [Description("Optional hex colour (e.g. `#aabbcc`).")] string? colour = null,
        [Description("Optional matching algorithm: any, all, literal, regex, fuzzy, auto. Defaults to auto.")] string? matchingAlgorithm = null,
        [Description("Optional match string used by the matching algorithm.")] string? match = null,
        [Description("Case-insensitive match. Defaults to true.")] bool isInsensitive = true,
        CancellationToken ct = default)
    {
        svc.EnsureFeature(svc.Options.EnableMetadataManagement, "Metadata management");
        svc.EnsureWriteAllowed("create_tag");
        var body = new JsonObject { ["name"] = name, ["is_insensitive"] = isInsensitive };
        if (!string.IsNullOrWhiteSpace(colour)) body["color"] = colour;
        if (!string.IsNullOrWhiteSpace(match)) body["match"] = match;
        body["matching_algorithm"] = MatchingAlgorithmToInt(matchingAlgorithm);
        var result = await svc.SendJsonAsync(HttpMethod.Post, "api/tags/", body, ct);
        return result?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    private static int MatchingAlgorithmToInt(string? value) =>
        (value ?? "auto").ToLowerInvariant() switch
        {
            "any" => 1,
            "all" => 2,
            "literal" => 3,
            "regex" or "regular" => 4,
            "fuzzy" => 5,
            _ => 6, // auto
        };

    private static async Task<string> ListAll(PaperlessService svc, string collection, CancellationToken ct)
    {
        var endpoint = Resolve(collection);
        var arr = await svc.ListAllAsync(endpoint.Collection, ct);
        return arr.ToJsonString(JsonOpts.Default);
    }

    private static Endpoint Resolve(string collection)
    {
        if (Endpoints.TryGetValue(collection, out var ep)) return ep;
        throw new McpException(
            $"Unknown metadata collection '{collection}'. Expected one of: {string.Join(", ", Endpoints.Keys)}.");
    }
}
