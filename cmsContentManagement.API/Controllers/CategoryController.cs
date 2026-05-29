using cmsContentManagement.Application.DTO;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using cmsContentManagement.API.Extensions;

namespace cmsContentManagement.API.Controllers;

[ApiController]
[Route("{organisationId}/category")]
[Authorize]
public class CategoryController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoryController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    [HttpGet]
    public async Task<List<CategoryResponseDTO>> GetAllCategories(Guid organisationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null)
    {
        return await _categoryService.GetAllCategories(organisationId, page, pageSize, search);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Category>> GetCategoryById(Guid organisationId, Guid id)
    {
        var category = await _categoryService.GetCategoryById(id);
        if (category == null || category.OrganisationId != organisationId) return NotFound();
        return category;
    }

    [HttpPost]
    public async Task<ActionResult<Category>> CreateCategory(Guid organisationId, CreateCategoryDTO categoryDto)
    {
        try
        {
            return await _categoryService.CreateCategory(organisationId, User.GetUserId(), categoryDto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> UpdateCategory(Guid organisationId, CategoryDTO categoryDto)
    {
        await _categoryService.UpdateCategory(organisationId, categoryDto);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(Guid organisationId, Guid id)
    {
        await _categoryService.DeleteCategory(organisationId, id);
        return Ok();
    }
}
