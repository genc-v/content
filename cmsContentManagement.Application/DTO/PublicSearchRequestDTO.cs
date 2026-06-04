namespace cmsContentManagement.Application.DTO;

public class PublicSearchRequestDTO
{
    public string ApiKey { get; set; } = string.Empty;
    public string? Query { get; set; }
    public string? Tag { get; set; }
    public string? Category { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public bool WithElastic { get; set; } = true;
}
