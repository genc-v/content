# cmsContentManagement — Service Documentation

## What this service does

A multi-tenant CMS backend built on .NET 8 (Clean Architecture). It manages:

- **Content entries** through a lifecycle: `New → Draft → Published → Unpublished → Deleted` (soft delete only)
- **Categories** and **Tags** scoped per organisation (tenant)
- **Public read-only endpoints** for published content (no auth)
- **Role-based access** — every write call checks the caller's role against the sibling `cmsOrg` service
- **Elasticsearch** for optional full-text fuzzy search (falls back to MySQL/EF Core when disabled)
- **Redis** for caching
- **Kafka** consumer (`files-uploaded` topic) — attaches uploaded asset URLs to content entries and can auto-promote a Draft to Published

---

## Authentication

| Type | How |
|---|---|
| Protected endpoints | `Authorization: Bearer <JWT>` |
| Public endpoints | No auth (`/api/public/content/*`) |

All routes with `{organisationId}` enforce the caller's role against the sibling `cmsOrg` service (`GET /organisations/{id}/role`), using the same hierarchy (`Admin` > `Editor` > `Viewer`):

| Operation | Minimum role |
|---|---|
| Reads (`GET` / `HEAD`) | **Viewer** |
| Writes (`POST` / `PUT` / `PATCH` / `DELETE`) | **Editor** |

Non-members and users below the required role receive `403`. If `cmsOrg` is unreachable or returns an unexpected payload, the request fails with `502`.

---

## Domain Models

### Content
| Field | Type | Notes |
|---|---|---|
| `ContentId` | `Guid` | PK |
| `Title` | `string?` | Unique per org (non-deleted) |
| `Slug` | `string?` | Auto-generated from title on first save; never changed again |
| `RichContent` | `string?` | |
| `AssetUrl` | `string?` | Set via Kafka event or save body |
| `Status` | `string` | `New` / `Draft` / `Published` / `Unpublished` / `Deleted` |
| `OrganisationId` | `Guid` | |
| `UserId` | `Guid` | Set from JWT claims |
| `CategoryId` | `Guid?` | FK → Category |
| `Tags` | `ICollection<Tag>` | Many-to-many |
| `CreatedOn` | `DateTime` | UTC |
| `UpdatedOn` | `DateTime` | UTC |

### Category
| Field | Type | Notes |
|---|---|---|
| `CategoryId` | `Guid` | PK |
| `Name` | `string` | Max 100; unique per org |
| `Description` | `string?` | |
| `OrganisationId` | `Guid` | |
| `UserId` | `Guid` | |

### Tag
| Field | Type | Notes |
|---|---|---|
| `TagId` | `Guid` | PK |
| `Name` | `string` | Max 50; unique per org |
| `OrganisationId` | `Guid` | |
| `UserId` | `Guid` | |

---

## DTOs

### `ContentDTO` — returned by protected content endpoints
```json
{
  "contentId": "guid",
  "assetUrl": "string?",
  "status": "New|Draft|Published|Unpublished|Deleted",
  "title": "string?",
  "slug": "string?",
  "richContent": "string?",
  "organisationId": "guid",
  "categoryId": "guid?",
  "categoryName": "string?",
  "createdOn": "datetime",
  "updatedOn": "datetime",
  "tags": [{ "tagId": "guid", "name": "string" }]
}
```

### `PublicContentDTO` — returned by public endpoints
```json
{
  "contentId": "guid",
  "assetUrl": "string?",
  "title": "string?",
  "slug": "string?",
  "richContent": "string?",
  "status": "Published",
  "createdOn": "datetime",
  "updatedOn": "datetime",
  "organisationId": "guid",
  "category": { "categoryId": "guid", "name": "string", "description": "string?" },
  "tags": [{ "tagId": "guid", "name": "string" }]
}
```

### `SaveContentDTO` — body for `PUT /{organisationId}/entry/{contentId}`
```json
{
  "assetUrl": "string?",
  "title": "string?",
  "richContent": "string?",
  "categoryId": "guid?",
  "categoryName": "string?",
  "tags": [{ "tagId": "guid", "name": "string" }]
}
```
> Note: `categoryName` is accepted but ignored — only `categoryId` is used for lookup.

### `CategoryResponseDTO` — returned by `GET /{organisationId}/category`
```json
{ "categoryId": "guid", "name": "string" }
```

### `CreateCategoryDTO` — body for `POST /{organisationId}/category`
```json
{ "name": "string", "description": "string?" }
```

### `CreateTagDTO` — body for `POST /{organisationId}/tag`
```json
{ "name": "string" }
```

### `TagDTO` — returned by tag endpoints and embedded in content
```json
{ "tagId": "guid", "name": "string" }
```

---

## API Endpoints

### Content — `/{organisationId}/entry`

---

#### `GET /{organisationId}/entry/new-id`

Returns an existing `New`-status entry for the caller+org, or creates a new blank one. Idempotent per (user, org).

**Returns:** `200 OK` — bare `Guid`

---

#### `GET /{organisationId}/entry`

List content entries for an organisation with optional filtering, search, and pagination.

**Query params:**
| Param | Type | Default | Description |
|---|---|---|---|
| `query` | `string?` | — | Full-text search on title + richContent |
| `tag` | `string?` | — | Exact tag name filter |
| `category` | `string?` | — | Exact category name filter |
| `status` | `string?` | — | `New` / `Draft` / `Published` / `Unpublished` |
| `fromDate` | `DateTime?` | — | `CreatedOn >= value` |
| `toDate` | `DateTime?` | — | `CreatedOn <= value` |
| `page` | `int` | `1` | |
| `pageSize` | `int` | `25` | |
| `withElastic` | `bool` | `true` | Use Elasticsearch (fuzziness=2) instead of DB; falls back to DB if Elastic is unavailable |

**Returns:** `200 OK` — `List<ContentDTO>` ordered by `CreatedOn` descending. `Deleted` entries are never included.

---

#### `GET /{organisationId}/entry/{contentId}`

Get a single content entry by ID. The caller must be the owner of the entry (`UserId` match).

**Returns:** `200 OK` — `ContentDTO`

**Errors:** `404` not found · `403` caller is not the owner

---

#### `PUT /{organisationId}/entry/{contentId}`

Save (create or update) a content entry. Status is auto-calculated:
- All four fields (`Title`, `RichContent`, `CategoryId`, `AssetUrl`) non-empty AND category + tags resolve → `Published`
- Otherwise → `Draft`

Slug is auto-generated from title on the first save and never changed after that.

**Body:** `SaveContentDTO`

**Returns:** `200 OK` (empty body)

**Errors:** `404` not found · `409` duplicate title or slug · `502` cmsOrg unreachable

---

#### `POST /{organisationId}/entry/{contentId}/unpublish`

Set a content entry's status to `Unpublished`. Also updates the Elasticsearch index.

**Returns:** `200 OK`

**Errors:** `404` not found

---

#### `DELETE /{organisationId}/entry/{contentId}`

Soft-delete a content entry (sets status to `Deleted`). Removes from Elasticsearch index.

**Returns:** `200 OK`

**Errors:** `404` not found

---

### Categories — `/{organisationId}/category`

---

#### `GET /{organisationId}/category`

List categories for an organisation.

**Query params:**
| Param | Type | Default |
|---|---|---|
| `page` | `int` | `1` |
| `pageSize` | `int` | `10` |
| `search` | `string?` | — (substring match on name) |

**Returns:** `200 OK` — `List<CategoryResponseDTO>` ordered alphabetically

---

#### `GET /{organisationId}/category/{id}`

Get a single category by ID.

**Returns:** `200 OK` — full `Category` entity (includes `UserId`, `OrganisationId`)

**Errors:** `404` not found or belongs to a different org

---

#### `POST /{organisationId}/category`

Create a new category.

**Body:** `CreateCategoryDTO`

**Returns:** `201` — full `Category` entity

**Errors:** `409 Conflict` `{ "message": "..." }` if name already exists in the org

---

#### `PUT /{organisationId}/category`

Update an existing category.

**Body:** `CategoryDTO` — `{ "categoryId": "guid", "name": "string", "description": "string?" }`

**Returns:** `200 OK` (empty body). Silently no-ops if category is not found.

---

#### `DELETE /{organisationId}/category/{id}`

Delete a category.

**Returns:** `200 OK`. Silently no-ops if not found.

---

### Tags — `/{organisationId}/tag`

---

#### `GET /{organisationId}/tag`

List tags for an organisation.

**Query params:**
| Param | Type | Default |
|---|---|---|
| `page` | `int` | `1` |
| `pageSize` | `int` | `10` |
| `search` | `string?` | — (substring match on name) |

**Returns:** `200 OK` — `List<TagDTO>` ordered alphabetically

---

#### `GET /{organisationId}/tag/{id}`

Get a single tag by ID.

**Returns:** `200 OK` — full `Tag` entity (includes `UserId`, `OrganisationId`)

**Errors:** `404` not found or belongs to a different org

---

#### `POST /{organisationId}/tag`

Create a new tag.

**Body:** `CreateTagDTO`

**Returns:** full `Tag` entity

**Errors:** `409 Conflict` `{ "message": "..." }` if name already exists in the org

---

#### `PUT /{organisationId}/tag`

Update an existing tag.

**Body:** `TagDTO` — `{ "tagId": "guid", "name": "string" }`

**Returns:** `200 OK` (empty body). Silently no-ops if tag is not found.

---

#### `DELETE /{organisationId}/tag/{id}`

Delete a tag.

**Returns:** `200 OK`. Silently no-ops if not found.

---

### Public Content — `api/public/content` (no auth)

---

#### `GET api/public/content`

List all published content across all organisations.

**Query params:**
| Param | Type | Default | Description |
|---|---|---|---|
| `query` | `string?` | — | Full-text search on title + richContent |
| `tag` | `string?` | — | Exact tag name filter |
| `category` | `string?` | — | Exact category name filter |
| `fromDate` | `DateTime?` | — | |
| `toDate` | `DateTime?` | — | |
| `page` | `int` | `1` | |
| `pageSize` | `int` | `10` | |
| `withElastic` | `bool` | `true` | Use Elasticsearch; falls back to DB if Elastic is unavailable |

**Returns:** `200 OK` — `List<PublicContentDTO>` (only `Published` entries, ordered by `CreatedOn` desc)

---

#### `POST api/public/content`

Get a single published content entry by slug, scoped to the organisation that owns the API key.

The API key is validated against cmsOrg (`GET {CmsOrgUrl}/api-keys/validate` with header `X-Api-Key`), which returns the owning `organisationId`; the slug is then resolved within that organisation.

**Body:**
```json
{
  "slug": "string",
  "apiKey": "string"
}
```

**Returns:** `200 OK` — `PublicContentDTO`

**Errors:** `400` missing slug or apiKey · `403` invalid API key · `404` no published entry matches the slug in that organisation · `503` cmsOrg unreachable

---

#### `POST api/public/search`

Filtered search over published content, scoped to the organisation that owns the API key.

The API key is validated against cmsOrg (`GET {CmsOrgUrl}/api-keys/validate` with header `X-Api-Key`), which returns the owning `organisationId`; results are filtered to that organisation only.

**Body:**
```json
{
  "apiKey": "string",
  "query": "string?",
  "tag": "string?",
  "category": "string?",
  "fromDate": "DateTime?",
  "toDate": "DateTime?",
  "page": 1,
  "pageSize": 10,
  "withElastic": true
}
```

**Returns:** `200 OK` — `List<PublicContentDTO>` (only `Published` entries for the organisation, ordered by `CreatedOn` desc)

**Errors:** `400` missing apiKey · `403` invalid API key · `503` cmsOrg unreachable

---

## Error Response Shape

All middleware-level errors use:
```json
{ "code": 0, "message": "string" }
```

| HTTP | Code | Condition |
|---|---|---|
| `400` | `3` | InvalidInput |
| `400` | `9` | ValidationError |
| `400` | `15` | ContentIsNew |
| `400` | `16` | TagNotFound |
| `401` | — | Missing / expired / invalid JWT |
| `403` | `8` | PermissionDenied |
| `403` | — | Role is not Editor or Admin |
| `404` | `1` | NotFound |
| `409` | `2` | UserAlreadyExists |
| `409` | `6` | Conflict (duplicate title or slug) |
| `409` | `14` | ContentAlreadyExists |
| `500` | `4` | OperationFailed |
| `500` | `5` | DatabaseError |
| `500` | `-1` | Unhandled exception |
| `502` | — | cmsOrg service unreachable |
| `503` | `7` | ServiceUnavailable |

---

## Background Services

### Kafka Consumer

- **Topic:** `files-uploaded`
- **Group ID:** `cms-content-group-v2`
- **Message shape:** `{ "entryId": "guid", "assetId": "string", "key": "string", "url": "string" }`
- **Effect:** Sets `AssetUrl` on the matching content entry. If all four publication fields (`Title`, `RichContent`, `CategoryId`, `AssetUrl`) are now complete, status is auto-promoted from `Draft` to `Published`. Re-indexes in Elasticsearch. Offsets are committed per-message; unparseable messages are skipped.
