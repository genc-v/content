using cmsContentManagement.Application.DTO;
using cmsContentManagement.Application.Interfaces;
using cmsContentManagement.Domain.Entities;
using cmsContentManagment.Infrastructure.Persistance;
using Microsoft.EntityFrameworkCore;

namespace cmsContentManagment.Infrastructure.Repositories;

public class CategoryService : ICategoryService
{
    private readonly AppDbContext _dbContext;

    public CategoryService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<CategoryResponseDTO>> GetAllCategories(Guid organisationId, int page, int pageSize, string? searchTerm = null)
    {
        var query = _dbContext.Categories.Where(c => c.OrganisationId == organisationId).AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(c => c.Name.Contains(searchTerm));
        }

        return await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CategoryResponseDTO
            {
                CategoryId = c.CategoryId,
                Name = c.Name
            })
            .ToListAsync();
    }

    public async Task<Category?> GetCategoryById(Guid id)
    {
        return await _dbContext.Categories.FindAsync(id);
    }

    public async Task<Category> CreateCategory(Guid organisationId, Guid userId, CreateCategoryDTO categoryDto)
    {
        if (await _dbContext.Categories.AnyAsync(c => c.OrganisationId == organisationId && c.Name == categoryDto.Name))
        {
            throw new InvalidOperationException($"Category with name '{categoryDto.Name}' already exists.");
        }

        var category = new Category
        {
            CategoryId = Guid.NewGuid(),
            UserId = userId,
            OrganisationId = organisationId,
            Name = categoryDto.Name,
            Description = categoryDto.Description
        };
        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync();
        return category;
    }

    public async Task UpdateCategory(Guid organisationId, CategoryDTO categoryDto)
    {
        var category = await _dbContext.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryDto.CategoryId && c.OrganisationId == organisationId);
        if (category != null)
        {
            category.Name = categoryDto.Name;
            category.Description = categoryDto.Description;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task DeleteCategory(Guid organisationId, Guid id)
    {
        var category = await _dbContext.Categories.FirstOrDefaultAsync(c => c.CategoryId == id && c.OrganisationId == organisationId);
        if (category != null)
        {
            _dbContext.Categories.Remove(category);
            await _dbContext.SaveChangesAsync();
        }
    }
}
