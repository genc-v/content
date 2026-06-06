using System.Security.Claims;
using System.Linq;
using cmsContentManagement.API.Extensions;
using cmsContentManagement.Application.DTO;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace cmsContentManagement.API.Controllers;

[ApiController]
[Route("{organisationId}/entry")]
[Authorize]
public class ContentManagementController : ControllerBase
{
    private readonly IContentManagmentService _contentManagmentService;

    public ContentManagementController(IContentManagmentService contentManagmentService)
    {
        _contentManagmentService = contentManagmentService;
    }

    [HttpGet("{contentId}")]
    public async Task<ActionResult<ContentDTO>> GetContent(Guid organisationId, Guid contentId)
    {
        var content = await _contentManagmentService.getContentById(organisationId, contentId);
        return Ok(MapToDto(content));
    }

    [HttpGet]
    public async Task<List<ContentDTO>> FilterContents(
        Guid organisationId,
        [FromQuery] string? query,
        [FromQuery] string? tag,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool withElastic = true
    )
    {
        return await _contentManagmentService.FilterContents(organisationId, query, tag, category, status, fromDate, toDate, page, pageSize, withElastic);
    }

    [HttpPut("{contentId}")]
    public async Task<IActionResult> UpdateContent(Guid organisationId, Guid contentId, [FromBody] SaveContentDTO content)
    {
        await _contentManagmentService.UpdateContent(organisationId, contentId, content);
        return Ok();
    }

    [HttpPost("{contentId}/unpublish")]
    public async Task<IActionResult> UnpublishContent(Guid organisationId, Guid contentId)
    {
        await _contentManagmentService.UnpublishContent(organisationId, contentId);
        return Ok();
    }

    [HttpDelete("{contentId}")]
    public async Task<IActionResult> DeleteContent(Guid organisationId, Guid contentId)
    {
        await _contentManagmentService.DeleteContent(organisationId, contentId);
        return Ok();
    }

    [HttpGet("new-id")]
    public async Task<ActionResult<Guid>> GenerateNewContentId(Guid organisationId)
    {
        var id = await _contentManagmentService.GenerateNewContentId(organisationId, User.GetUserId());
        return Ok(id);
    }

    private ContentDTO MapToDto(Content content)
    {
        return new ContentDTO
        {
            ContentId = content.ContentId,
            AssetUrl = content.AssetUrl,
            Status = content.Status,
            Title = content.Title,
            Slug = content.Slug,
            RichContent = content.RichContent,
            OrganisationId = content.OrganisationId,
            UserId = content.UserId,
            CategoryId = content.CategoryId,
            CategoryName = content.Category?.Name,
            CreatedOn = content.CreatedOn,
            UpdatedOn = content.UpdatedOn,
            Tags = content.Tags?.Select(t => new TagDTO { TagId = t.TagId, Name = t.Name }).ToList() ?? new List<TagDTO>()
        };
    }
}
