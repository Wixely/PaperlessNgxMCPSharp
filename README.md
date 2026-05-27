# PaperlessNgxMCPSharp

A standalone C# **MCP (Model Context Protocol) server** for **[Paperless-ngx](https://docs.paperless-ngx.com/)** over Streamable HTTP. Talks to the official Paperless-ngx REST API.

## Features

- HTTP MCP server using the Streamable HTTP transport.
- **Read-only mode by default** — all write/update tools stay disabled until explicitly enabled. Destructive deletes need a second gate.
- Document tools: list/search/get/get-content/get-metadata, download (file, preview, thumbnail, inline base64), upload, update metadata, add/remove tags, notes, bulk edit.
- Metadata tools: list / create / update / delete for tags, correspondents, document types, storage paths, and custom fields.
- Search tools: autocomplete, similar documents (`more_like`), saved views, statistics, remote version.
- Configuration via `PaperlessNgxMCPSharp.json`, environment variables, or command line.
- Serilog logging to console and rolling files (daily + 50 MB rollover, 14-file retention).
- Runs as a console app, Windows Service, or Docker container.

## Configuration

Configure via `PaperlessNgxMCPSharp.json` or environment variables. Environment variables win over JSON; in Docker, use the `PAPERLESSMCP_` prefix and `__` for nested keys.

| Setting | Default | Description |
| --- | --- | --- |
| `Paperless:BaseUrl` | `http://localhost:8000/` | Paperless-ngx base URL (no `/api`). |
| `Paperless:ApiToken` | _(none)_ | API token from Paperless (`/api/token/` or the UI). Sent as `Authorization: Token <value>`. |
| `Paperless:Username` / `Password` | _(none)_ | Optional HTTP Basic fallback if `ApiToken` is blank. |
| `Paperless:ReadOnly` | `true` | When `true`, write/update tools are disabled. |
| `Paperless:AllowDelete` | `false` | Second gate required for `delete_document`, `delete_metadata`, `delete_document_note`, and `bulk_edit_documents:delete`. |
| `Paperless:EnableUploads` | `true` | Hides upload tools when `false`. |
| `Paperless:EnableDownloads` | `true` | Hides download tools when `false`. |
| `Paperless:EnableMetadataManagement` | `true` | Hides metadata CRUD tools when `false`. |
| `Paperless:DownloadDirectory` | _(temp)_ | Where `download_document*` writes files. Defaults to `<TEMP>/PaperlessNgxMCPSharp`. |
| `Paperless:MaxInlineContentBytes` | `65536` | Cap on inline OCR text returned by `get_document_content`. |
| `Paperless:MaxInlineBinaryBytes` | `2000000` | Cap on inline base64 bytes from `download_document_inline`; above this, the file is written to disk. |
| `Paperless:DefaultPageSize` | `25` | Page size for list operations (max 100). |
| `Paperless:MaxPages` | `5` | Max pages traversed when auto-paginating. |
| `Paperless:RequestTimeoutSeconds` | `100` | HTTP timeout. |
| `Paperless:UserAgent` | `PaperlessNgxMCPSharp` | UA header. |
| `Paperless:AllowInvalidCertificate` | `false` | Skip TLS verification (self-signed homelab only). |
| `Server:Host` | `localhost` | Host to bind. |
| `Server:Port` | `5708` | HTTP port. |
| `Server:Path` | `/mcp` | MCP endpoint path. |
| `Server:WindowsServiceName` | `PaperlessNgxMCPSharp` | Service name when running under SCM. |
| `Server:Password` | blank | Optional MCP endpoint password; blank disables password auth. |

When `Server:Password` is set, MCP requests must provide the password as `Authorization: Bearer <password>`, the Basic auth password, or `X-MCP-Password`.

Arrays use numeric indexes, for example `PAPERLESSMCP_Paperless__BaseUrl=https://paperless.example.com/`. Booleans use `true` or `false`.

## Tools

### Documents
- `list_documents` — paginated metadata, with filters (query, tag, correspondent, type, storage path, created date, ordering).
- `get_document` — full Paperless document object.
- `get_document_content` — OCR/extracted text, capped by `MaxInlineContentBytes`.
- `get_document_metadata` — Paperless metadata (file type, page count, archive checksum, …).
- `get_document_notes`, `add_document_note`, `delete_document_note`.
- `update_document` — PATCH title, tags, correspondent, document type, storage path, archive serial, created, custom fields.
- `add_document_tag`, `remove_document_tag` — single-tag mutations without touching others.
- `delete_document` — hard delete (requires `AllowDelete=true`).
- `download_document` — write the archived PDF (or `original=true`) to `DownloadDirectory`.
- `download_document_inline` — base64-encoded bytes, falls back to disk above `MaxInlineBinaryBytes`.
- `download_document_preview`, `download_document_thumbnail`.
- `upload_document` — multipart POST to `/api/documents/post_document/`, returns the consumption task id.
- `get_task` — poll a consumption task by UUID or id.
- `bulk_edit_documents` — `set_correspondent`, `set_document_type`, `set_storage_path`, `add_tag`, `remove_tag`, `modify_tags`, `delete`, `redo_ocr`, `set_permissions`.

### Metadata (tags / correspondents / document types / storage paths / custom fields)
- `list_tags`, `list_correspondents`, `list_document_types`, `list_storage_paths`, `list_custom_fields`.
- `create_metadata`, `update_metadata`, `delete_metadata` — generic CRUD by collection name.
- `create_tag` — convenience wrapper with common fields.

### Search & introspection
- `search_autocomplete` — Paperless autocomplete suggestions.
- `search_similar` — find documents similar to a reference document (`more_like`).
- `list_saved_views` — Paperless saved views.
- `get_statistics` — instance statistics.
- `remote_version` — server version and update info.

## Running

```sh
dotnet run
```

Then point your MCP client at `http://localhost:5708/mcp`.

## Docker

Tagged releases publish a multi-arch image to GitHub Container Registry:

```sh
docker pull ghcr.io/wixely/paperlessngxmcpsharp:<version>
docker run --rm -p 5708:5708 \
  -e PAPERLESSMCP_Paperless__BaseUrl=https://paperless.example.com/ \
  -e PAPERLESSMCP_Paperless__ApiToken=<token> \
  -e PAPERLESSMCP_Server__Password=change-me \
  -v paperlessmcp-downloads:/downloads \
  -e PAPERLESSMCP_Paperless__DownloadDirectory=/downloads \
  ghcr.io/wixely/paperlessngxmcpsharp:<version>
```

The image supports `linux/amd64` and `linux/arm64`. Read-only mode is on by default; set `PAPERLESSMCP_Paperless__ReadOnly=false` (and `PAPERLESSMCP_Paperless__AllowDelete=true` for deletes) only when you want write tools.

## Running as a Windows Service

The host detects when it's launched by the Service Control Manager and switches to service mode automatically (config and logs resolve from the executable directory, not the SCM's `C:\Windows\System32` working directory).

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o C:\Services\PaperlessNgxMCPSharp

sc.exe create PaperlessNgxMCPSharp `
    binPath= "C:\Services\PaperlessNgxMCPSharp\PaperlessNgxMCPSharp.exe" `
    start= auto `
    DisplayName= "Paperless-ngx MCP (C#)"
sc.exe description PaperlessNgxMCPSharp "MCP server for Paperless-ngx."
sc.exe start PaperlessNgxMCPSharp
```

Put credentials in `C:\Services\PaperlessNgxMCPSharp\PaperlessNgxMCPSharp.Local.json` (or set `PAPERLESSMCP_Paperless__ApiToken` as a machine-level env var) — never in `PaperlessNgxMCPSharp.json`, which is checked in.

To remove:

```powershell
sc.exe stop PaperlessNgxMCPSharp
sc.exe delete PaperlessNgxMCPSharp
```

Logs land in `<install-dir>\logs\paperlessmcp-*.log`.

## Safety model

- **Read-only by default.** All write tools call `EnsureWriteAllowed` and fail with a clear error naming the config key to flip.
- **Delete double-gate.** Destructive tools also require `Paperless:AllowDelete=true`.
- **Feature toggles.** Uploads, downloads, and metadata management can each be hidden.
- **Inline size caps.** Both extracted text and binary downloads are capped; oversized payloads spill to disk instead of being returned through the MCP channel.
