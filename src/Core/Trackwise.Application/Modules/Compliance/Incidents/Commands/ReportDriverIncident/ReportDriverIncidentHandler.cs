using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Application.Abstractions.Modules.MasterData;
using Trackwise.Application.Abstractions.Notifications;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.Safety;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.Safety;
using Trackwise.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

internal sealed class ReportDriverIncidentHandler(
    ITenantUnitOfWork tenantUow,
    IMasterDataCodeGenerator codes,
    ICurrentUserService currentUser,
    INotificationService notifications,
    ILogger<ReportDriverIncidentHandler> logger)
    : IAppRequestHandler<ReportDriverIncidentCommand, Result<IncidentDto>>
{
    /// <summary>A phone that lost signal resends the report; within this window it is the same case.</summary>
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromMinutes(10);

    public async Task<Result<IncidentDto>> Handle(ReportDriverIncidentCommand req, CancellationToken ct)
    {
        if (currentUser.GetUserId() is not { } callerId)
        {
            return Result<IncidentDto>.Fail(DriverIncidentReport.NotYourTrip);
        }

        var trip = await tenantUow.Repository<Trip>().GetByIdAsync(req.TripId, ct);
        if (trip is null)
        {
            return Result<IncidentDto>.Fail($"Could not find the trip with ID '{req.TripId}'");
        }

        var now = DateTime.UtcNow;
        var since = now - RepeatWindow;
        var repeat = await tenantUow.Repository<Incident>().Query()
            .Where(i => i.TripId == trip.Id && i.DriverId == callerId && i.Kind == req.Kind
                        && i.Status == IncidentStatus.Open && i.CreatedAt >= since)
            .FirstOrDefaultAsync(ct);
        if (repeat is not null)
        {
            return Result<IncidentDto>.Ok(await IncidentReader.ToDetailsDtoAsync(tenantUow, repeat, ct));
        }

        var reporter = await tenantUow.Repository<Employee>().GetByIdAsync(callerId, ct);
        var reporterName = reporter?.GetFullName() ?? currentUser.GetUserName();
        var report = DriverIncidentReport.Build(trip, callerId, reporterName, req, now);
        if (!report.IsSuccess)
        {
            return Result<IncidentDto>.Fail(report.Error!);
        }

        var caseNumber = await codes.NextAsync(
            Incident.CaseNumberSeries, Incident.DefaultCaseNumberPrefix, Incident.CaseNumberPadWidth, ct);

        Incident incident;
        try
        {
            var (details, parties) = report.Value;
            incident = Incident.Record(caseNumber, details, parties, now);
        }
        catch (InvalidOperationException ex)
        {
            return Result<IncidentDto>.Fail(ex.Message);
        }

        await tenantUow.Repository<Incident>().AddAsync(incident, ct);
        await tenantUow.SaveChangesAsync(ct);

        logger.LogInformation(
            "Driver app reported {Kind} {CaseNumber} on trip {TripNumber}", req.Kind, caseNumber, trip.Number);
        await AlertOfficeAsync(incident, trip, reporterName);

        return Result<IncidentDto>.Ok(await IncidentReader.ToDetailsDtoAsync(tenantUow, incident, ct));
    }

    /// <summary>The case is on record either way; a failed alert must not make the crew report it again.</summary>
    private async Task AlertOfficeAsync(Incident incident, Trip trip, string reporterName)
    {
        var truck = incident.TruckId is { } truckId
            ? (await tenantUow.Repository<Truck>().GetByIdAsync(truckId))?.Number
            : null;
        var state = new List<string> { incident.VehicleDrivable ? "truck can still drive" : "truck can't go on" };
        if (incident.AnyInjuries)
        {
            state.Add("someone is hurt");
        }

        try
        {
            await notifications.SendNotificationAsync(
                $"{incident.Kind.GetDescription()} reported - {incident.CaseNumber}",
                $"{reporterName} on trip #{trip.Number}{(truck is null ? "" : $" ({truck})")}: {string.Join(", ", state)}.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not alert the office about incident {CaseNumber}", incident.CaseNumber);
        }
    }
}
