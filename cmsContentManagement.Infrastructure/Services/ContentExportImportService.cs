using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using cmsContentManagement.Application.DTO.Export;
using cmsContentManagement.Application.DTO.Import;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Domain.Entities;
using cmsContentManagment.Infrastructure.Persistance;
using CsvHelper;
using Microsoft.EntityFrameworkCore;

namespace cmsContentManagment.Infrastructure.Services;

public class ContentExportImportService(AppDbContext db) : IContentExportImportService
{
    public async Task<(byte[] Data, string ContentType, string FileName)> ExportEntries(Guid organisationId, string format)
    {
        var items = await db.Contents
            .Where(c => c.OrganisationId == organisationId && c.Status != "Deleted")
            .Include(c => c.Category)
            .Include(c => c.Tags)
            .Select(c => new EntryExportDto
            {
                ContentId = c.ContentId,
                Title = c.Title,
                Slug = c.Slug,
                RichContent = c.RichContent,
                AssetUrl = c.AssetUrl,
                Status = c.Status,
                CategoryName = c.Category != null ? c.Category.Name : null,
                TagNames = string.Join(",", c.Tags.Select(t => t.Name)),
                CreatedOn = c.CreatedOn,
                UpdatedOn = c.UpdatedOn
            })
            .ToListAsync();

        return Serialize(items, format, "entries");
    }

    public async Task<ImportResultDto> ImportEntries(Guid organisationId, Guid userId, Stream stream, string fileExtension)
    {
        var rows = await Deserialize<EntryImportDto>(stream, fileExtension);
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (string.IsNullOrWhiteSpace(row.Title))
            {
                errors.Add($"Row {i + 1}: Title is required.");
                skipped++;
                continue;
            }

            bool titleExists = await db.Contents.AnyAsync(c =>
                c.OrganisationId == organisationId && c.Title == row.Title && c.Status != "Deleted");
            if (titleExists)
            {
                skipped++;
                continue;
            }

            var slug = GenerateSlug(row.Title);
            bool slugExists = await db.Contents.AnyAsync(c =>
                c.OrganisationId == organisationId && c.Slug == slug && c.Status != "Deleted");
            if (slugExists)
                slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";

            Category? category = null;
            if (!string.IsNullOrWhiteSpace(row.CategoryName))
            {
                category = await db.Categories.FirstOrDefaultAsync(c =>
                    c.OrganisationId == organisationId && c.Name == row.CategoryName);

                if (category == null)
                {
                    category = new Category
                    {
                        CategoryId = Guid.NewGuid(),
                        Name = row.CategoryName,
                        OrganisationId = organisationId,
                        UserId = userId
                    };
                    db.Categories.Add(category);
                    await db.SaveChangesAsync();
                }
            }

            var tags = new List<Tag>();
            if (!string.IsNullOrWhiteSpace(row.TagNames))
            {
                var tagNames = row.TagNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var tagName in tagNames)
                {
                    var tag = await db.Tags.FirstOrDefaultAsync(t =>
                        t.OrganisationId == organisationId && t.Name == tagName);

                    if (tag == null)
                    {
                        tag = new Tag
                        {
                            TagId = Guid.NewGuid(),
                            Name = tagName,
                            OrganisationId = organisationId,
                            UserId = userId
                        };
                        db.Tags.Add(tag);
                        await db.SaveChangesAsync();
                    }

                    tags.Add(tag);
                }
            }

            bool isComplete = !string.IsNullOrWhiteSpace(row.RichContent)
                && category != null
                && !string.IsNullOrWhiteSpace(row.AssetUrl);

            db.Contents.Add(new Content
            {
                ContentId = Guid.NewGuid(),
                Title = row.Title,
                Slug = slug,
                RichContent = row.RichContent,
                AssetUrl = row.AssetUrl,
                Status = isComplete ? "Published" : "Draft",
                OrganisationId = organisationId,
                UserId = userId,
                CategoryId = category?.CategoryId,
                Tags = tags
            });

            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResultDto { Imported = imported, Skipped = skipped, Errors = errors };
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportCategories(Guid organisationId, string format)
    {
        var items = await db.Categories
            .Where(c => c.OrganisationId == organisationId)
            .Select(c => new CategoryExportDto
            {
                CategoryId = c.CategoryId,
                Name = c.Name,
                Description = c.Description
            })
            .ToListAsync();

        return Serialize(items, format, "categories");
    }

    public async Task<(byte[] Data, string ContentType, string FileName)> ExportTags(Guid organisationId, string format)
    {
        var items = await db.Tags
            .Where(t => t.OrganisationId == organisationId)
            .Select(t => new TagExportDto
            {
                TagId = t.TagId,
                Name = t.Name
            })
            .ToListAsync();

        return Serialize(items, format, "tags");
    }

    private static string GenerateSlug(string title)
    {
        var slug = title.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"[^a-z0-9-]", string.Empty);
        return $"/{slug}";
    }

    private static (byte[] Data, string ContentType, string FileName) Serialize<T>(List<T> items, string format, string name)
    {
        return format.ToLowerInvariant() switch
        {
            "csv" => (ToCsv(items), "text/csv", $"{name}.csv"),
            "excel" => (ToExcel(items, name), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{name}.xlsx"),
            _ => (ToJson(items), "application/json", $"{name}.json")
        };
    }

    private static byte[] ToJson<T>(List<T> items)
        => JsonSerializer.SerializeToUtf8Bytes(items, new JsonSerializerOptions { WriteIndented = true });

    private static byte[] ToCsv<T>(List<T> items)
    {
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(items);
        writer.Flush();
        return ms.ToArray();
    }

    private static byte[] ToExcel<T>(List<T> items, string sheetName)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        var props = typeof(T).GetProperties();

        for (int c = 0; c < props.Length; c++)
            ws.Cell(1, c + 1).Value = props[c].Name;

        for (int r = 0; r < items.Count; r++)
            for (int c = 0; c < props.Length; c++)
                ws.Cell(r + 2, c + 1).Value = props[c].GetValue(items[r])?.ToString() ?? string.Empty;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static async Task<List<T>> Deserialize<T>(Stream stream, string fileExtension)
    {
        return fileExtension.ToLowerInvariant() switch
        {
            ".csv" => FromCsv<T>(stream),
            ".xlsx" => FromExcel<T>(stream),
            _ => await FromJson<T>(stream)
        };
    }

    private static async Task<List<T>> FromJson<T>(Stream stream)
    {
        var result = await JsonSerializer.DeserializeAsync<List<T>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? [];
    }

    private static List<T> FromCsv<T>(Stream stream)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return [.. csv.GetRecords<T>()];
    }

    private static List<T> FromExcel<T>(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var props = typeof(T).GetProperties().ToDictionary(p => p.Name.ToLowerInvariant(), p => p);
        var result = new List<T>();

        var headers = ws.Row(1).Cells()
            .Select((c, i) => (Header: c.Value.ToString().ToLowerInvariant(), Index: i + 1))
            .ToList();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var obj = Activator.CreateInstance<T>();
            foreach (var (header, colIndex) in headers)
            {
                if (!props.TryGetValue(header, out var prop)) continue;
                var cellValue = row.Cell(colIndex).Value.ToString();
                if (string.IsNullOrWhiteSpace(cellValue)) continue;
                SetProperty(obj!, prop, cellValue);
            }
            result.Add(obj);
        }

        return result;
    }

    private static void SetProperty(object obj, PropertyInfo prop, string value)
    {
        var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        object? parsed = type switch
        {
            _ when type == typeof(Guid) => Guid.TryParse(value, out var g) ? g : Guid.Empty,
            _ when type == typeof(DateTime) => DateTime.TryParse(value, out var dt) ? dt : DateTime.UtcNow,
            _ when type == typeof(int) => int.TryParse(value, out var n) ? n : 0,
            _ when type == typeof(bool) => bool.TryParse(value, out var b) && b,
            _ => value
        };
        prop.SetValue(obj, parsed);
    }
}
