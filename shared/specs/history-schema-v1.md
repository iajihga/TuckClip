# Logical history schema v1

The two clients use the same logical fields even though each platform owns its
local storage and encryption envelope.

| Field | Type | Notes |
|---|---|---|
| `id` | UUID | Stable item identity |
| `kind` | string | `text`, `link`, `image`, or `files` |
| `plainText` | string? | Text/link payload only |
| `filePaths` | string[] | References only; files are not copied |
| `imageFileName` | string? | Safe relative encrypted blob name |
| `createdAt` | timestamp | UTC |
| `updatedAt` | timestamp | UTC, used for recency order |
| `sourceAppName` | string? | Best-effort display metadata |
| `sourceIdentifier` | string? | Bundle ID or Windows process name |
| `fingerprint` | string | Lowercase SHA-256 hex |
| `isPinned` | bool | Exempt from automatic pruning |
| `copyCount` | integer | Saturating positive counter |

Changing this shape requires a versioned migration and fixtures that both
implementations can decode. A failed migration must preserve the original bytes.
