namespace cmsContentManagement.Application.DTO.Export;

public class CategoryExportDto
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
