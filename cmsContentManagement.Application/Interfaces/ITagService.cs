using cmsContentManagement.Application.DTO;
using cmsContentManagement.Domain.Entities;

namespace cmsContentManagement.Application.Interfaces;

public interface ITagService
{
    Task<List<TagDTO>> GetAllTags(Guid organisationId, int page, int pageSize, string? searchTerm = null);
    Task<Tag?> GetTagById(Guid id);
    Task<Tag> CreateTag(Guid organisationId, Guid userId, CreateTagDTO tagDto);
    Task UpdateTag(Guid organisationId, TagDTO tagDto);
    Task DeleteTag(Guid organisationId, Guid id);
}
