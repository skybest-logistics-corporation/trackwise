using Trackwise.Application.Abstractions;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Shared.Models;
using Trackwise.Shared.Models.Quotations;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.CreateQuotation;

public class CreateQuotationCommand : ICommand<Result<Guid>>
{
    public Guid CustomerId { get; set; }
    public Guid BranchId { get; set; }
    public string Currency { get; set; } = default!;
    public TransportMode Mode { get; set; }
    public FreightMode FreightMode { get; set; } = FreightMode.Domestic;
    public ShipmentType ShipmentType { get; set; }
    public Incoterm Incoterm { get; set; }

    /// <summary>Route lane - required for Reefer/Trucking, ignored for every other freight mode.</summary>
    public LaneTransitDto? Lane { get; set; }

    /// <summary>Port of Loading - required for Domestic/Import/Export/Triangular freight modes.</summary>
    public Guid? PolId { get; init; }
    public Guid? PodId { get; init; }

    public CargoSpecificationDto? Cargo { get; set; } = default!;

    public TemperatureControlDto? TemperatureSpec { get; set; }
    public bool IsHazardous { get; set; }
    public bool RequiresCustomsBrokerage { get; set; }
    public string? Notes { get; set; }
}
