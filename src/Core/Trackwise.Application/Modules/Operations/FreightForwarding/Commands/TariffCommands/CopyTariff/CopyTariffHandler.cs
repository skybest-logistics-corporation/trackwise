using Microsoft.EntityFrameworkCore;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.TariffCommands.CopyTariff;

internal sealed class CopyTariffHandler(ITenantUnitOfWork tenantUow)
    : IAppRequestHandler<CopyTariffCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CopyTariffCommand req, CancellationToken ct)
    {
        var source = await tenantUow.Repository<Tariff>()
            .Query()
            .FirstOrDefaultAsync(i => i.Id == req.Id, ct);

        if (source is null)
        {
            return Result<Guid>.Fail($"Tariff with ID '{req.Id}' not found.");
        }

        var duplicate = await tenantUow.Repository<Tariff>().Query()
            .AnyAsync(i => i.Name == req.Name &&
                           i.Mode == source.Mode &&
                           i.ServiceLevel == source.ServiceLevel &&
                           i.ContractRef == source.ContractRef &&
                           i.CustomerId == source.CustomerId &&
                           i.IsActive, ct);

        if (duplicate)
        {
            return Result<Guid>.Fail($"A tariff with name '{req.Name}' already exists.");
        }

        var replacement = Tariff.Create(
            mode: source.Mode,
            serviceLevel: source.ServiceLevel,
            name: req.Name,
            currency: source.Currency,
            originLane: source.OriginLane,
            destinationLane: source.DestinationLane,
            truckType: source.TruckType,
            tonnageClass: source.TonnageClass,
            containerTypeId: source.ContainerTypeId,
            carrierId: source.CarrierId,
            effectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow.Date),
            contractRef: source.ContractRef,
            customerId: source.CustomerId);

        foreach (var sourceRate in source.Rates)
        {
            replacement.AddRate(
                chargeCodeId: sourceRate.ChargeCodeId,
                pricingModel: sourceRate.PricingModel,
                baseAmount: sourceRate.BaseAmount,
                unit: sourceRate.Unit,
                minimum: sourceRate.Minimum,
                maximum: sourceRate.Maximum,
                freeDays: sourceRate.FreeDays,
                rateAfterFreeDays: sourceRate.RateAfterFreeDays,
                percentage: sourceRate.Percentage,
                percentageBase: sourceRate.PercentageBase,
                breakpointJson: sourceRate.BreakpointJson);
        }

        await tenantUow.Repository<Tariff>().AddAsync(replacement, ct);
        await tenantUow.SaveChangesAsync(ct);

        return Result<Guid>.Ok(replacement.Id);
    }
}
