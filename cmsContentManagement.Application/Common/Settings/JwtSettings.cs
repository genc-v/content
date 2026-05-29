namespace cmsContentManagement.Application.Common.Settings;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; }
    public string AuthUrl { get; set; } = string.Empty;
    public string CmsOrgUrl { get; set; } = string.Empty;
}
