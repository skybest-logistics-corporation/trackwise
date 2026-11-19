using Trackwise.Application.Abstractions;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.Trips.Queries;

public sealed class GetTripsQuery : PagedQuery, IQuery<PagedResult<TripDto>>
{
    public string? Name { get; set; }
    public TripStatus? Status { get; set; }
    public string? TruckNumber { get; set; }
    public Guid? TruckId { get; set; }

    /// <summary>The trip's own driver - crews rotate across trucks, so a truck doesn't say whose trip it is.</summary>
    public Guid? DriverId { get; set; }

    /// <summary>
    /// With <see cref="DriverId" />: also the trips where that driver runs a rescue, with every trip's
    /// rescues filled in - what the Driver App needs to show a rescue crew the drops they took over.
    /// </summary>
    public bool IncludeRescues { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool OnlyActiveTrips { get; set; }
}
