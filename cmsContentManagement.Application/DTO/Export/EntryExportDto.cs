namespace cmsContentManagement.Application.DTO.Export;

public class EntryExportDto
{
    public Guid ContentId { get; set; }
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public string? RichContent { get; set; }
    public string? AssetUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string TagNames { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
    public DateTime UpdatedOn { get; set; }
}
