using cmsContentManagement.Application.DTO;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using cmsContentManagement.API.Extensions;

namespace cmsContentManagement.API.Controllers;

[ApiController]
[Route("{organisationId}/tag")]
[Authorize]
public class TagController : ControllerBase
{
    private readonly ITagService _tagService;

    public TagController(ITagService tagService)
    {
        _tagService = tagService;
    }

    [HttpGet]
    public async Task<List<TagDTO>> GetAllTags(Guid organisationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null)
    {
        return await _tagService.GetAllTags(organisationId, page, pageSize, search);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Tag>> GetTagById(Guid organisationId, Guid id)
    {
        var tag = await _tagService.GetTagById(id);
        if (tag == null || tag.OrganisationId != organisationId) return NotFound();
        return tag;
    }

    [HttpPost]
    public async Task<ActionResult<Tag>> CreateTag(Guid organisationId, CreateTagDTO tagDto)
    {
        try
        {
            return await _tagService.CreateTag(organisationId, User.GetUserId(), tagDto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateTag(Guid organisationId, TagDTO tagDto)
    {
        await _tagService.UpdateTag(organisationId, tagDto);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTag(Guid organisationId, Guid id)
    {
        await _tagService.DeleteTag(organisationId, id);
        return Ok();
    }
}
