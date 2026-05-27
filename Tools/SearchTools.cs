using System.ComponentModel;
using System.Text.Json;
using PaperlessNgxMCPSharp.Services;
using ModelContextProtocol.Server;

namespace PaperlessNgxMCPSharp.Tools;

[McpServerToolType]
public static class SearchTools
{
    [McpServerTool(Name = "search_autocomplete"),
     Description("Return Paperless autocomplete suggestions for a partial term.")]
    public static async Task<string> Autocomplete(
        PaperlessService svc,
        [Description("Partial search term.")] string term,
        [Description("Maximum suggestions to return. Defaults to 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync(
            $"api/search/autocomplete/?term={Uri.EscapeDataString(term)}&limit={Math.Max(1, limit)}",
            ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "[]";
    }

    [McpServerTool(Name = "search_similar"),
     Description("Find documents similar to a given document id using Paperless's `more_like` query.")]
    public static async Task<string> Similar(
        PaperlessService svc,
        [Description("Reference document id.")] int id,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync($"api/documents/?more_like_id={id}&page_size={svc.Options.DefaultPageSize}", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    [McpServerTool(Name = "get_statistics"),
     Description("Return Paperless instance statistics (document count, character count, tag inbox counts).")]
    public static async Task<string> Statistics(
        PaperlessService svc,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync("api/statistics/", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "{}";
    }

    [McpServerTool(Name = "list_saved_views"),
     Description("List Paperless saved views (named filters).")]
    public static async Task<string> SavedViews(
        PaperlessService svc,
        CancellationToken ct = default)
    {
        var arr = await svc.ListAllAsync("api/saved_views/", ct);
        return arr.ToJsonString(JsonOpts.Default);
    }

    [McpServerTool(Name = "remote_version"),
     Description("Return the Paperless-ngx server version and update info.")]
    public static async Task<string> RemoteVersion(
        PaperlessService svc,
        CancellationToken ct = default)
    {
        var node = await svc.GetJsonAsync("api/remote_version/", ct);
        return node?.ToJsonString(JsonOpts.Default) ?? "{}";
    }
}
