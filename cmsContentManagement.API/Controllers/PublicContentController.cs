using cmsContentManagement.Application.Common.ErrorCodes;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Application.DTO;
using Microsoft.AspNetCore.Mvc;

namespace cmsContentManagment.Controllers;

[ApiController]
[Route("api/public/content")]
public class PublicContentController : ControllerBase
{
    private readonly IContentManagmentService _contentManagmentService;
    private readonly ICategoryService _categoryService;
    private readonly ITagService _tagService;
    private readonly IApiKeyValidator _apiKeyValidator;

    public PublicContentController(
        IContentManagmentService contentManagmentService,
        ICategoryService categoryService,
        ITagService tagService,
        IApiKeyValidator apiKeyValidator)
    {
        _contentManagmentService = contentManagmentService;
        _categoryService = categoryService;
        _tagService = tagService;
        _apiKeyValidator = apiKeyValidator;
    }

    /// <summary>
    /// Resolves the organisation behind the public API key. The key may arrive in the
    /// <c>X-Api-Key</c> header (preferred) or, for backwards compatibility, in the request body.
    /// Throws if no key is supplied or the key is invalid/expired/disabled.
    /// </summary>
    private async Task<Guid> ResolveOrganisationAsync(string? headerKey, string? bodyKey = null)
    {
        var apiKey = !string.IsNullOrWhiteSpace(headerKey) ? headerKey : bodyKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw GeneralErrorCodes.InvalidInput;
        }

        return await _apiKeyValidator.ValidateAsync(apiKey);
    }

    /// <summary>General entry search/listing, scoped to the key's organisation (published only).</summary>
    [HttpGet]
    public async Task<List<PublicContentDTO>> GetPublicContents(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] string? query,
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] bool withElastic = true)
    {
        var organisationId = await ResolveOrganisationAsync(apiKey);
        return await _contentManagmentService.GetPublicContents(
            query, tag, category, fromDate, toDate, page, pageSize, withElastic, organisationId);
    }

    /// <summary>Fetch a single published entry by slug (e.g. for SEO/Google landing pages).</summary>
    [HttpPost]
    public async Task<PublicContentDTO> GetContentBySlug(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] PublicContentRequestDTO request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Slug))
        {
            throw GeneralErrorCodes.InvalidInput;
        }

        var organisationId = await ResolveOrganisationAsync(apiKey, request.ApiKey);
        return await _contentManagmentService.GetPublicContentBySlug(request.Slug, organisationId);
    }

    /// <summary>Advanced search over published entries (title, rich content, tag, category, dates).</summary>
    [HttpPost("/api/public/search")]
    public async Task<List<PublicContentDTO>> Search(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromBody] PublicSearchRequestDTO request)
    {
        if (request == null)
        {
            throw GeneralErrorCodes.InvalidInput;
        }

        var organisationId = await ResolveOrganisationAsync(apiKey, request.ApiKey);
        return await _contentManagmentService.GetPublicContents(
            request.Query, request.Tag, request.Category, request.FromDate, request.ToDate,
            request.Page, request.PageSize, request.WithElastic, organisationId);
    }

    /// <summary>List the organisation's categories (for category pages / navigation).</summary>
    [HttpGet("/api/public/categories")]
    public async Task<List<CategoryResponseDTO>> GetCategories(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        var organisationId = await ResolveOrganisationAsync(apiKey);
        return await _categoryService.GetAllCategories(organisationId, page, pageSize, search);
    }

    /// <summary>List the organisation's tags (for tag pages / navigation).</summary>
    [HttpGet("/api/public/tags")]
    public async Task<List<TagDTO>> GetTags(
        [FromHeader(Name = "X-Api-Key")] string? apiKey,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? search = null)
    {
        var organisationId = await ResolveOrganisationAsync(apiKey);
        return await _tagService.GetAllTags(organisationId, page, pageSize, search);
    }
}
