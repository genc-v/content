using cmsContentManagement.Application.DTO.Import;

namespace cmsContentManagement.Application.Interfaces;

public interface IContentExportImportService
{
    Task<(byte[] Data, string ContentType, string FileName)> ExportEntries(Guid organisationId, string format);
    Task<ImportResultDto> ImportEntries(Guid organisationId, Guid userId, Stream stream, string fileExtension);
    Task<(byte[] Data, string ContentType, string FileName)> ExportCategories(Guid organisationId, string format);
    Task<(byte[] Data, string ContentType, string FileName)> ExportTags(Guid organisationId, string format);
}
