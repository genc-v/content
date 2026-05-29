using System;
using System.Linq;
using System.Threading.Tasks;
using cmsContentManagement.Application.DTO;
using cmsContentManagement.Domain.Entities;
using cmsContentManagment.Infrastructure.Persistance;
using cmsContentManagment.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace cmsContentManagement.Tests;

public class CategoryServiceTests
{
    [Fact]
    public async Task CreateCategory_AddsCategoryToDatabase()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new AppDbContext(options);
        var service = new CategoryService(context);

        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var dto = new CreateCategoryDTO { Name = "TestCategory", Description = "Desc" };

        var created = await service.CreateCategory(orgId, userId, dto);

        Assert.Equal(dto.Name, created.Name);
        Assert.Equal(dto.Description, created.Description);
        Assert.Equal(userId, created.UserId);
        Assert.NotEqual(Guid.Empty, created.CategoryId);
        Assert.Equal(1, context.Categories.Count());
    }

    [Fact]
    public async Task CreateCategory_DuplicateNameForSameUser_ThrowsInvalidOperationException()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new AppDbContext(options);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        context.Categories.Add(new Category { CategoryId = Guid.NewGuid(), UserId = userId, OrganisationId = orgId, Name = "Dup" });
        await context.SaveChangesAsync();

        var service = new CategoryService(context);
        var dto = new CreateCategoryDTO { Name = "Dup", Description = "x" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateCategory(orgId, userId, dto));
    }

    [Fact]
    public async Task GetAllCategories_FiltersByUserAndSearchTermAndReturnsPagedResults()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new AppDbContext(options);
        var userId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var otherUser = Guid.NewGuid();

        context.Categories.AddRange(
            new Category { CategoryId = Guid.NewGuid(), UserId = userId, OrganisationId = orgId, Name = "Alpha" },
            new Category { CategoryId = Guid.NewGuid(), UserId = userId, OrganisationId = orgId, Name = "AlphaBeta" },
            new Category { CategoryId = Guid.NewGuid(), UserId = userId, OrganisationId = orgId, Name = "Gamma" },
            new Category { CategoryId = Guid.NewGuid(), UserId = otherUser, OrganisationId = orgId, Name = "Alpha" }
        );
        await context.SaveChangesAsync();

        var service = new CategoryService(context);

        var results = await service.GetAllCategories(orgId, page: 1, pageSize: 10, searchTerm: "Alpha");

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Name == "Alpha");
        Assert.Contains(results, r => r.Name == "AlphaBeta");
    }
}