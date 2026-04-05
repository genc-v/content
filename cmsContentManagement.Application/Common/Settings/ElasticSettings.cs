namespace cmsContentManagement.Application.Common.Settings;

public class ElasticSettings
{
    public string Url { get; set; } = string.Empty;
    public string DefaultIndex { get; set; } = "content";
    public string? Username { get; set; }
    public string? Password { get; set; }
}
