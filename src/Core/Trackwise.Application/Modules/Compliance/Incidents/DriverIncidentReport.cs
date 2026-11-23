using Trackwise.Application.Modules.Compliance.Incidents.Commands;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.Safety;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.Safety;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Compliance.Incidents;

/// <summary>
/// Turns a crew's report from the Driver App into an incident's details and parties. The reporter is
/// either the trip's own driver or the driver of a rescue on it, and the case names that crew and truck.
/// </summary>
internal static class DriverIncidentReport
{
    public const string NotYourTrip = "This trip isn't yours.";

    public static Result<(IncidentDetails Details, IncidentParties Parties)> Build(
        Trip trip, Guid callerId, string reporterName, ReportDriverIncidentCommand req, DateTime now)
    {
        if (trip.Status is not (TripStatus.Dispatched or TripStatus.InTransit or TripStatus.Completed))
        {
            return Result<(IncidentDetails, IncidentParties)>.Fail("This trip isn't under way.");
        }

        var crew = CrewOf(trip, callerId);
        if (crew is null)
        {
            return Result<(IncidentDetails, IncidentParties)>.Fail(NotYourTrip);
        }

        var details = new IncidentDetails
        {
            Kind = req.Kind,
            Severity = req.AnyInjuries ? IncidentSeverity.Moderate : IncidentSeverity.Minor,
            OccurredAt = now,
            Summary = Summary(req, reporterName),
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            AnyInjuries = req.AnyInjuries,
            // Unknown from the roadside; the office sets the count when it reviews the case.
            NumberOfInjuries = req.AnyInjuries ? 1 : null,
            VehicleDamaged = req.Kind == IncidentKind.Accident,
            VehicleDrivable = req.VehicleDrivable
        };

        var parties = new IncidentParties(
            trip.Id,
            IncidentPartiesResolver.SoleCustomerOf(trip),
            crew.Value.TruckId,
            callerId,
            crew.Value.HelperId,
            crew.Value.SecondHelperId);
        return Result<(IncidentDetails, IncidentParties)>.Ok((details, parties));
    }

    private static (Guid? TruckId, Guid? HelperId, Guid? SecondHelperId)? CrewOf(Trip trip, Guid callerId)
    {
        if (trip.DriverId == callerId)
        {
            return (trip.TruckId, trip.HelperId, trip.SecondHelperId);
        }

        var rescue = trip.Rescues.LastOrDefault(r => r.DriverId == callerId);
        return rescue is null ? null : (rescue.TruckId, rescue.HelperId, rescue.SecondHelperId);
    }

    private static string Summary(ReportDriverIncidentCommand req, string reporterName)
    {
        var source = $"Reported from the Driver App by {reporterName}.";
        return string.IsNullOrWhiteSpace(req.Note) ? source : $"{req.Note.Trim()}\n\n{source}";
    }
}
