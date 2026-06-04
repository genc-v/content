using cmsContentManagement.Application.DTO;
using cmsContentManagement.Domain.Entities;

namespace cmsContentManagement.Application.Interfaces;

public interface IContentManagmentService
{
    public     Task<Guid> GenerateNewContentId(Guid organisationId, Guid userId);
    public Task<Content> getContentById(Guid organisationId, Guid contentId, Guid userId);
    public Task<List<ContentDTO>> FilterContents(Guid organisationId, string? query, string? tag, string? category, string? status, DateTime? fromDate, DateTime? toDate, int page, int pageSize, bool withElastic = true);
    public Task DeleteContent(Guid organisationId, Guid contentId);
    public Task UnpublishContent(Guid organisationId, Guid contentId);
    public Task UpdateContent(Guid organisationId, Guid contentId, SaveContentDTO content);
    public Task AddAssetUrlToContent(Guid organisationId, Guid contentId, string assetUrl);
    public Task<List<PublicContentDTO>> GetPublicContents(string? query, string? tag, string? category, DateTime? fromDate, DateTime? toDate, int page, int pageSize, bool withElastic = true, Guid? organisationId = null);
    public Task<PublicContentDTO> GetPublicContentBySlug(string slug, Guid? organisationId = null);
    public Task UpdateContentAssetUrl(Guid contentId, string assetUrl);
}
