using Trackwise.Application.Abstractions;
using Trackwise.Domain.Primitives.Enums.Safety;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Compliance.Incidents.Commands;

/// <summary>
/// The crew's "Problem" button in the Driver App: an accident, a breakdown or anything else that stops
/// the trip. It opens a case on the trip with the reporting crew and truck, and tells the office; the
/// office fills in the rest and decides on a rescue. A repeat of the same report within a few minutes
/// returns the case already opened.
/// </summary>
public class ReportDriverIncidentCommand : ICommand<Result<IncidentDto>>
{
    public Guid TripId { get; set; }
    public IncidentKind Kind { get; set; }

    /// <summary>What happened, in the crew's words; optional, since they may be on the roadside.</summary>
    public string? Note { get; set; }

    public bool VehicleDrivable { get; set; } = true;
    public bool AnyInjuries { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
