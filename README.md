# CMS Content Management Service

The Content Management Service is the core writing and publishing microservice of the CMS platform. It owns every content entry that authors create — from first draft through publication — and exposes two distinct APIs: a private management API for authenticated editors and an unauthenticated public API for frontend consumers. It is one of three backend services in the platform:

| Service | Repo | Responsibility |
|---|---|---|
| **Auth** (`cmsUserManagment`) | `../auth` | User registration, login, JWT issuance, 2FA, roles, notifications |
| **Assets** (`assets`) | `../assets` | S3 file uploads, asset metadata, Kafka event publishing |
| **Content** (`cmsContentManagement`) | this repo | Entry authoring, search, caching, public delivery |

---

## Why this service exists

A CMS front-end needs a place to create, draft, and publish structured content (articles, landing pages, blog posts). Rather than bundling that into the auth service, content management is separated so it can scale independently, use its own database, and expose a clean public read API without leaking internal write surfaces. The public API is keyed separately from JWT so that external frontends or static-site generators can fetch published entries without user credentials.

---

## How the three services interact

```
Browser / Editor                Auth Service           Assets Service         Content Service
       |                             |                       |                      |
       |-- POST /api/auth/login ----->|                       |                      |
       |<-- JWT token ---------------||                       |                      |
       |                             |                       |                      |
       |-- GET /{orgId}/entry/new-id (JWT) ---------------------------------------->|
       |<-- contentId -----------------------------------------------------------------------|
       |                             |                       |                      |
       |-- POST /organisations/:orgId/assets (JWT + contentId) -->|                |
       |                             |                       |-- S3 upload          |
       |                             |                       |-- Kafka: files.uploaded event ->|
       |                             |                       |                      |-- update AssetUrl
       |                             |                       |                      |-- re-evaluate status
       |                             |                       |                      |
       |-- PUT /{orgId}/entry/{id} (JWT + body) ------------------------------------------>|
       |<-- 200 OK (entry saved, auto-published if complete) ----------------------------|
       |                             |                       |                      |
Public Consumer                      |                       |                      |
       |-- GET /api/public/content (X-Api-Key header) ----------------------------------->|
       |<-- published entries (from Redis or DB) -----------------------------------------|
```

**Key integration points:**

- The JWT the user gets from Auth is validated by the Content service on every private request. For org-scoped routes, Content also calls the Auth/Org service (`CmsOrgUrl`) to verify the user's role inside that organisation.
- The API key for public reads is also validated against the Auth/Org service (`/api-keys/validate`), which returns the `organisationId` the key belongs to.
- When a file is uploaded through Assets, it publishes a `files.uploaded` Kafka message containing `entryId` and `url`. Content consumes this and writes the asset URL back onto the entry, then re-checks whether the entry is now complete enough to auto-publish.

---

## Architecture

The project follows Clean Architecture with four layers:

```
cmsContentManagement.Domain          — entities, no dependencies
cmsContentManagement.Application     — DTOs, interfaces, settings
cmsContentManagement.Infrastructure  — EF Core, Elasticsearch, Redis, Kafka
cmsContentManagement.API             — controllers, middleware, DI wiring
cmsContentManagement.Tests           — unit tests
```

**Domain** owns the three entity types (Content, Category, Tag) with no framework dependencies. **Application** defines the service contracts and the settings classes that map to configuration. **Infrastructure** implements those contracts against real databases and message brokers. **API** wires everything together and handles HTTP concerns: routing, auth middleware, error handling, and Swagger.

---

## Database

**Engine:** MySQL 8 via Entity Framework Core (migrations in `cmsContentManagement.Infrastructure/Migrations`).

### Table: `Contents`

The central table. One row per content entry.

| Column | Type | Notes |
|---|---|---|
| `ContentId` | `GUID` PK | Auto-generated |
| `Title` | `string?` | Display title; must be unique within the org (non-deleted entries) |
| `Slug` | `string?` | URL-safe identifier, e.g. `/my-article`. Auto-generated from Title on first save; never regenerated after that |
| `RichContent` | `string?` | HTML or rich text body |
| `AssetUrl` | `string?` (URL) | Set either manually or by the Kafka consumer when Assets uploads a file |
| `Status` | `string` | `New` → `Draft` → `Published` / `Unpublished` → `Deleted` |
| `CreatedOn` | `DateTime` | UTC, set on insert |
| `UpdatedOn` | `DateTime` | UTC, updated on every save |
| `OrganisationId` | `GUID` | Tenant scope — every query filters by this |
| `UserId` | `GUID` | The user who created the entry |
| `CategoryId` | `GUID?` FK | Optional reference to `Categories` |

### Table: `Categories`

Used to group entries. Scoped to an organisation.

| Column | Type | Notes |
|---|---|---|
| `CategoryId` | `GUID` PK | |
| `Name` | `string` max 100 | Required; unique per org |
| `Description` | `string?` | Optional |
| `OrganisationId` | `GUID` | Tenant scope |
| `UserId` | `GUID` | Creator |

### Table: `Tags`

Free-form labels for cross-cutting classification. Scoped to an organisation.

| Column | Type | Notes |
|---|---|---|
| `TagId` | `GUID` PK | |
| `Name` | `string` max 50 | Required; unique per org |
| `OrganisationId` | `GUID` | Tenant scope |
| `UserId` | `GUID` | Creator |

### Relationships

- `Content` → `Category`: **Many-to-One** (`CategoryId` FK, nullable). One entry has at most one category; a category can contain many entries.
- `Content` ↔ `Tag`: **Many-to-Many** via an EF Core shadow join table (`ContentTag`). One entry can carry multiple tags; one tag can appear on multiple entries.

---

## Content lifecycle (status machine)

Status is set automatically every time an entry is saved; it is never set manually by the caller.

```
  GenerateNewContentId()
         |
         v
       [New]  ──── UpdateContent() with incomplete fields ────> [Draft]
                                                                    |
         UpdateContent() / UpdateContentAssetUrl()                  |
         (Title + RichContent + Category + AssetUrl all present)    |
                                                                    v
                                                              [Published]
                                                                    |
                                                     UnpublishContent()
                                                                    |
                                                                    v
                                                            [Unpublished]
                                                                    |
                                              DeleteContent() (soft delete)
                                                                    |
                                                                    v
                                                              [Deleted]
```

- **New** — stub row created by `GET /{orgId}/entry/new-id`. The caller uses this ID when uploading a file via Assets so the Kafka event can reference it before any content is saved.
- **Draft** — entry has been saved but at least one of (Title, RichContent, Category, AssetUrl) is missing or points to a non-existent org resource.
- **Published** — all four required fields are present and valid. Only Published entries are returned by the public API.
- **Unpublished** — manually pulled from the public feed but still intact. Can be re-published by saving it again with complete fields.
- **Deleted** — soft delete. The row stays in MySQL and Elasticsearch index is removed. Deleted entries are excluded from all queries.

---

## Search

Every write (create, update, delete, unpublish, asset attach) calls `IndexContentAsync`, which pushes the entry to Elasticsearch. The indexed document includes nested `Category` and `Tags` objects so that tag and category filters work without joins.

When Elasticsearch is available (`withElastic=true`, the default), search and list queries are served from the index with fuzzy full-text matching (fuzziness 2) across `title` and `richContent`. Pagination is applied at the Elasticsearch layer (`from` / `size`).

If Elasticsearch returns an invalid response, the service falls back to a MySQL query automatically. Callers can also force MySQL by passing `withElastic=false`.

The Elasticsearch index name defaults to `"content"` and is configurable via `ElasticSettings:DefaultIndex`. Basic auth is optional (`ElasticSettings:Username` / `Password`).

---

## Caching

Public-facing read paths go through a Redis read-through cache (`ContentCache`):

- **Slug lookup** — key `public:content:{orgId}:slug:{slug}`
- **List/search** — key `public:content:{orgId}:list:{query}:{tag}:{category}:{fromDate}:{toDate}:{page}:{pageSize}`

Cache entries expire by TTL (configured via `Cache:ContentTtlSeconds`, default 60 s). The slug entry is also invalidated explicitly whenever the underlying content changes (update, unpublish, delete, asset attach). List keys are only invalidated by TTL.

Redis is treated as best-effort: any Redis failure (read, write, or remove) is caught and logged, and the request falls through to the database. A cache outage cannot take down a request.

`null` results are never cached — if a slug lookup returns nothing from the DB, the cache stays empty so a subsequent publish is visible immediately.

---

## Kafka consumer

The service runs a `KafkaConsumerService` background worker (topic: `files.uploaded`). When Assets finishes uploading a file to S3, it publishes:

```json
{
  "entryId": "<uuid>",
  "assetId": "<string>",
  "key": "<s3-key>",
  "url": "<public-cdn-url>"
}
```

The consumer calls `UpdateContentAssetUrl(entryId, url)`, which:
1. Writes the URL to `Content.AssetUrl`
2. Re-evaluates status (the entry may auto-publish if it was already complete except for the asset)
3. Re-indexes the entry in Elasticsearch
4. Invalidates the public slug cache

The consumer commits the offset after a successful update. Deserialization failures commit anyway to avoid blocking the partition on a malformed message.

---

## Authentication and authorisation

### Private management API (JWT)

`JwtValidationMiddleware` runs before every request. It:
1. Skips `/swagger`, `[AllowAnonymous]` endpoints, and `/api/public/*`.
2. Validates the `Authorization: Bearer <token>` header against the shared JWT secret (HS256, issuer + audience checked, zero clock skew).
3. For routes that contain an `{organisationId}` segment, calls `{CmsOrgUrl}/organisations/{orgId}/role` to fetch the user's role in that org.
4. Applies the role gate: **GET/HEAD** require at least `Viewer`; **POST/PUT/DELETE/PATCH** require at least `Editor`.

Role hierarchy: `Admin (3) > Editor (2) > Viewer (1)`. A user not in the org resolves to weight 0 and is rejected.

### Public API (API key)

`/api/public/*` routes skip JWT validation entirely. Each request must supply an API key in the `X-Api-Key` header (preferred) or in the request body (`ApiKey` field, kept for backwards compatibility). The key is forwarded to `{CmsOrgUrl}/api-keys/validate`, which returns the `organisationId` the key belongs to. Content results are then scoped to that organisation.

### Export/Import (Admin only)

`ExportImportController` additionally calls the org service to confirm the user holds the `Admin` role before any export or import is executed.

---

## API reference

All private routes are prefixed with `/{organisationId:guid}` and require a valid JWT with the appropriate org role.

### Entries — private (`/{organisationId}/entry`)

| Method | Path | Min role | Description |
|---|---|---|---|
| `GET` | `/new-id` | Viewer | Reserve a new entry ID (creates a stub with status `New`) |
| `GET` | `/` | Viewer | List/search entries. Query params: `query`, `tag`, `category`, `status`, `fromDate`, `toDate`, `page` (default 1), `pageSize` (default 25), `withElastic` (default true) |
| `GET` | `/{contentId}` | Viewer | Fetch a single entry with category and tags |
| `PUT` | `/{contentId}` | Editor | Save (create or update) entry fields. Status is auto-computed |
| `POST` | `/{contentId}/unpublish` | Editor | Move entry from Published → Unpublished |
| `DELETE` | `/{contentId}` | Editor | Soft-delete (status → Deleted) |

**Save payload (`SaveContentDTO`):**
```json
{
  "title": "string",
  "richContent": "string (HTML)",
  "assetUrl": "string (URL, optional — usually set by Kafka)",
  "categoryId": "uuid (optional)",
  "categoryName": "string (informational, not used to set category)",
  "tags": [{ "tagId": "uuid", "name": "string" }]
}
```

### Categories — private (`/{organisationId}/category`)

| Method | Path | Min role | Description |
|---|---|---|---|
| `GET` | `/` | Viewer | List categories. Params: `page`, `pageSize`, `search` |
| `GET` | `/{id}` | Viewer | Get by ID |
| `POST` | `/` | Editor | Create. 409 if name already exists in org |
| `PUT` | `/` | Editor | Update name/description |
| `DELETE` | `/{id}` | Editor | Delete |

### Tags — private (`/{organisationId}/tag`)

| Method | Path | Min role | Description |
|---|---|---|---|
| `GET` | `/` | Viewer | List tags. Params: `page`, `pageSize`, `search` |
| `GET` | `/{id}` | Viewer | Get by ID |
| `POST` | `/` | Editor | Create. 409 if name already exists in org |
| `PUT` | `/` | Editor | Update name |
| `DELETE` | `/{id}` | Editor | Delete |

### Export / Import — private (`/{organisationId}`, Admin only)

| Method | Path | Description |
|---|---|---|
| `GET` | `/entry/export?format=json\|csv\|excel` | Export all non-deleted entries |
| `POST` | `/entry/import` | Import entries from JSON / CSV / XLSX file |
| `GET` | `/category/export?format=json\|csv\|excel` | Export all categories |
| `GET` | `/tag/export?format=json\|csv\|excel` | Export all tags |

Import rules: rows missing a `Title` are skipped. Duplicate titles (within the org, non-deleted) are skipped. Missing categories and tags are created automatically. Slug collisions get a 6-char UUID suffix appended. Status is auto-computed on import (Published if Title + RichContent + Category + AssetUrl are all present, otherwise Draft).

### Public API (`/api/public`, API key required)

| Method | Path | Description |
|---|---|---|
| `GET` | `/content` | List published entries. Params: `query`, `tag`, `category`, `fromDate`, `toDate`, `page` (default 1), `pageSize` (default 10), `withElastic` (default true) |
| `POST` | `/content` | Fetch one entry by slug. Body: `{ "slug": "...", "apiKey": "..." }` (apiKey in body as fallback) |
| `POST` | `/search` | Advanced search. Body: `PublicSearchRequestDTO` with `query`, `tag`, `category`, `fromDate`, `toDate`, `page`, `pageSize`, `withElastic`, `apiKey` |
| `GET` | `/categories` | List org categories. Params: `page`, `pageSize`, `search` |
| `GET` | `/tags` | List org tags. Params: `page`, `pageSize`, `search` |

---

## Configuration

All settings come from `appsettings.json` / environment variables (double-underscore notation for env vars, e.g. `JwtSettings__Secret`).

| Section / Key | Required | Description |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | Yes | MySQL connection string |
| `Redis:Connection` | Yes | Redis connection string (e.g. `redis:6379`) |
| `JwtSettings:Secret` | Yes | HS256 signing key (must match Auth service) |
| `JwtSettings:Issuer` | Yes | JWT issuer claim (e.g. `cms`) |
| `JwtSettings:Audience` | Yes | JWT audience claim (e.g. `account`) |
| `JwtSettings:CmsOrgUrl` | Yes | Base URL of the Auth/Org service for role and API-key validation |
| `ElasticSettings:Url` | Yes | Elasticsearch base URL |
| `ElasticSettings:DefaultIndex` | No | Index name (default: `content`) |
| `ElasticSettings:Username` | No | Basic auth username for Elasticsearch |
| `ElasticSettings:Password` | No | Basic auth password for Elasticsearch |
| `Cache:ContentTtlSeconds` | No | Redis entry TTL in seconds (default: 60) |
| `KafkaSettings:BootstrapServers` | Yes | Kafka broker addresses (e.g. `kafka:9092`) |
| `KafkaSettings:GroupId` | Yes | Kafka consumer group ID |
| `KafkaSettings:Topic` | Yes | Topic to consume (e.g. `files.uploaded`) |

---

## Running locally

**Prerequisites:** Docker, .NET 8 SDK.

```bash
# Start MySQL, Redis, and the service itself
docker compose up -d

# Or run the API directly (needs MySQL + Redis already running)
dotnet run --project cmsContentManagement.API
```

Swagger UI is available at `http://localhost:5054/swagger`.

The compose file maps:
- API → `localhost:5054`
- MySQL → `localhost:33062`
- Redis → `localhost:6380`

Elasticsearch is commented out in the compose file; the service falls back to MySQL queries when it is not configured.

To connect the service to Kafka, the compose file joins the `nest-s3-kafka_default` external Docker network (created by the Assets service compose). Start Assets first, then Content.

---

## Running tests

```bash
dotnet test cmsContentManagement.Tests
```

The test project covers `CategoryService` with in-memory EF Core.
