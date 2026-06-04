namespace cmsContentManagement.Application.Interfaces;

public interface IApiKeyValidator
{
    /// <summary>
    /// Validates the API key against the cmsorg service and returns the organisation
    /// it belongs to. Throws if the key is missing, invalid, or cmsorg is unreachable.
    /// </summary>
    Task<Guid> ValidateAsync(string apiKey);
}
