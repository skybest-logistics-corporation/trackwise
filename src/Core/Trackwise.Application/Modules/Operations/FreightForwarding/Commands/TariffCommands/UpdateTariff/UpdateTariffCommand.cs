using Trackwise.Application.Abstractions;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.TariffCommands.UpdateTariff;

public class UpdateTariffCommand : ICommand<Result>
{
    public Guid Id { get; set; }
    public FreightMode? Mode { get; set; }
    public ServiceLevel? ServiceLevel { get; set; }
    public string? Name { get; set; }
    public string? Currency { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool? IsActive { get; set; }
    public string? OriginLane { get; set; }
    public string? DestinationLane { get; set; }
    public TruckType TruckType { get; set; }
    public TruckTonnageClass? TonnageClass { get; set; }
    public Guid? ContainerTypeId { get; set; }
    public Guid? CarrierId { get; set; }
    public string? ContractRef { get; set; }
}
