using System.Text.Json;
using cmsContentManagement.Application.Common.ErrorCodes;
using cmsContentManagement.Application.Common.Settings;
using cmsContentManagement.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace cmsContentManagement.API.Services;

public class ApiKeyValidator : IApiKeyValidator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JwtSettings _settings;
    private readonly ILogger<ApiKeyValidator> _logger;

    public ApiKeyValidator(IHttpClientFactory httpClientFactory, IOptions<JwtSettings> settings, ILogger<ApiKeyValidator> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Guid> ValidateAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw GeneralErrorCodes.InvalidInput;
        }

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.CmsOrgUrl}/api-keys/validate");
            request.Headers.Add("X-Api-Key", apiKey);
            response = await client.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "cmsorg service unreachable at {CmsOrgUrl}.", _settings.CmsOrgUrl);
            throw GeneralErrorCodes.ServiceUnavailable;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw GeneralErrorCodes.PermissionDenied;
        }

        var json = await response.Content.ReadAsStringAsync();
        if (!TryReadOrganisationId(json, out var organisationId))
        {
            _logger.LogError("cmsorg api-keys/validate returned an unexpected payload: {Payload}", json);
            throw GeneralErrorCodes.ServiceUnavailable;
        }

        return organisationId;
    }

    private static bool TryReadOrganisationId(string json, out Guid organisationId)
    {
        organisationId = Guid.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("organisationId", out var orgEl)
                && Guid.TryParse(orgEl.GetString(), out organisationId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
