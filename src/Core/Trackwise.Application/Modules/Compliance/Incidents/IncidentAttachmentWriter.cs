using Trackwise.Application.Abstractions.Storage;
using Trackwise.Application.Modules.Common.Constants;
using Trackwise.Application.Utilities;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.Safety;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Compliance.Incidents;

/// <summary>Uploads files to an incident, for the office's attachments and the crew's photos alike.</summary>
internal static class IncidentAttachmentWriter
{
    public static async Task AddAsync(
        ITenantUnitOfWork uow,
        IBlobStorageService blobStorage,
        Incident incident,
        Employee uploadedBy,
        DocumentType type,
        IEnumerable<FileUpload> files,
        CancellationToken ct)
    {
        var index = 0;
        foreach (var file in files)
        {
            var fileName = BlobPathHelper.GenerateUniqueFileName(file.FileName, index++);
            var blobPath = BlobPathHelper.GetOwnerDocumentBlobPath("incidents", incident.Id, fileName);
            await blobStorage.UploadAsync(
                BlobConstants.DocumentsContainerName, blobPath, file.Content, file.ContentType, ct);

            var document = IncidentDocument.Create(
                fileName,
                file.FileName,
                file.ContentType,
                file.FileSizeBytes,
                blobPath,
                BlobConstants.DocumentsContainerName,
                type,
                incident.Id,
                uploadedBy.Id);
            await uow.Repository<IncidentDocument>().AddAsync(document, ct);
        }
    }
}
