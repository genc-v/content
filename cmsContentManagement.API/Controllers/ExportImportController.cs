using System.Text.Json;
using cmsContentManagement.API.Extensions;
using cmsContentManagement.Application.Common.Settings;
using cmsContentManagement.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace cmsContentManagement.API.Controllers;

[ApiController]
[Route("{organisationId:guid}")]
[Authorize]
public class ExportImportController(
    IContentExportImportService service,
    IHttpClientFactory httpClientFactory,
    IOptions<JwtSettings> jwtSettings) : ControllerBase
{
    private async Task<bool> IsAdmin(Guid organisationId)
    {
        var token = Request.Headers.Authorization.FirstOrDefault()?.Split(" ").Last();
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token ?? string.Empty);
        try
        {
            var response = await client.GetAsync($"{jwtSettings.Value.CmsOrgUrl}/organisations/{organisationId}/role");
            if (!response.IsSuccessStatusCode) return false;
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var role = doc.RootElement.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;
            return role == "Admin";
        }
        catch
        {
            return false;
        }
    }

    [HttpGet("entry/export")]
    public async Task<IActionResult> ExportEntries(Guid organisationId, [FromQuery] string format = "json")
    {
        if (!await IsAdmin(organisationId)) return Forbid();
        var (data, contentType, fileName) = await service.ExportEntries(organisationId, format);
        return File(data, contentType, fileName);
    }

    [HttpPost("entry/import")]
    public async Task<IActionResult> ImportEntries(Guid organisationId, IFormFile file)
    {
        if (!await IsAdmin(organisationId)) return Forbid();
        using var stream = file.OpenReadStream();
        var result = await service.ImportEntries(organisationId, User.GetUserId(), stream, Path.GetExtension(file.FileName));
        return Ok(result);
    }

    [HttpGet("category/export")]
    public async Task<IActionResult> ExportCategories(Guid organisationId, [FromQuery] string format = "json")
    {
        if (!await IsAdmin(organisationId)) return Forbid();
        var (data, contentType, fileName) = await service.ExportCategories(organisationId, format);
        return File(data, contentType, fileName);
    }

    [HttpGet("tag/export")]
    public async Task<IActionResult> ExportTags(Guid organisationId, [FromQuery] string format = "json")
    {
        if (!await IsAdmin(organisationId)) return Forbid();
        var (data, contentType, fileName) = await service.ExportTags(organisationId, format);
        return File(data, contentType, fileName);
    }
}
