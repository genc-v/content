using cmsContentManagement.Application.DTO;
using cmsContentManagement.Domain.Entities;

namespace cmsContentManagement.Application.Interfaces;

public interface ICategoryService
{
    Task<List<CategoryResponseDTO>> GetAllCategories(Guid organisationId, int page, int pageSize, string? searchTerm = null);
    Task<Category?> GetCategoryById(Guid id);
    Task<Category> CreateCategory(Guid organisationId, Guid userId, CreateCategoryDTO categoryDto);
    Task UpdateCategory(Guid organisationId, CategoryDTO categoryDto);
    Task DeleteCategory(Guid organisationId, Guid id);
}
