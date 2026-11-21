using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Application.Abstractions.Storage;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.Safety;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.Safety;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

internal sealed class AddDriverIncidentPhotosHandler(
    ITenantUnitOfWork tenantUow,
    IBlobStorageService blobStorage,
    ICurrentUserService currentUser)
    : IAppRequestHandler<AddDriverIncidentPhotosCommand, Result>
{
    public async Task<Result> Handle(AddDriverIncidentPhotosCommand req, CancellationToken ct)
    {
        var incident = await tenantUow.Repository<Incident>().GetByIdAsync(req.IncidentId, ct);
        var callerId = currentUser.GetUserId();

        // Every driver holds the driver endpoints' permission, so the case must name this one.
        if (incident is null || callerId is null || incident.DriverId != callerId)
        {
            return Result.Fail("That case isn't one you reported.");
        }

        if (incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed)
        {
            return Result.Fail("The office has already settled this case - send the photos to them directly.");
        }

        var driver = await tenantUow.Repository<Employee>().GetByIdAsync(callerId.Value, ct);
        if (driver is null)
        {
            return Result.Fail("That case isn't one you reported.");
        }

        await IncidentAttachmentWriter.AddAsync(
            tenantUow, blobStorage, incident, driver, DocumentType.Photo, req.Files, ct);
        await tenantUow.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
