using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AddQuotationLines;

internal sealed class AddQuotationLinesHandler(ITenantUnitOfWork tenantUow)
    : IAppRequestHandler<AddQuotationLinesCommand, Result<IReadOnlyList<Guid>>>
{
    public async Task<Result<IReadOnlyList<Guid>>> Handle(AddQuotationLinesCommand req, CancellationToken ct)
    {
        // 1. Quotation must exist and be a draft.
        var quotation = await tenantUow.Repository<Quotation>().GetByIdAsync(req.QuotationId, ct);
        if (quotation is null)
            return Fail($"Quotation with ID '{req.QuotationId}' not found.");

        if (quotation.Status != QuotationStatus.Draft)
            return Fail("Lines can only be added to a Draft quotation.");

        // 2. Load every referenced rate once (with ChargeCode + Tariff, as the domain overload needs them).
        var rateIds = req.Lines.Select(l => l.TariffRateId).Distinct().ToList();
        var rates = await tenantUow.Repository<TariffRate>().Query()
            .Include(r => r.ChargeCode)
            .Include(r => r.Tariff)
            .Where(r => rateIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        // 3. Validate ALL lines before touching the aggregate, so nothing is half-applied.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var checkedTariffs = new HashSet<Guid>();

        for (var i = 0; i < req.Lines.Count; i++)
        {
            var item = req.Lines[i];

            if (!rates.TryGetValue(item.TariffRateId, out var rate) || rate.ChargeCode is null || rate.Tariff is null)
                return Fail($"Line {i + 1}: tariff rate '{item.TariffRateId}' not found.");

            var tariff = rate.Tariff;

            // Tariff-level checks run once per tariff, not once per line.
            if (checkedTariffs.Add(tariff.Id))
            {
                if (!tariff.IsActive || tariff.EffectiveFrom > today || (tariff.EffectiveTo.HasValue && tariff.EffectiveTo < today))
                    return Fail($"Tariff '{tariff.Name}' is not active or not currently effective.");

                if (tariff.Mode != quotation.FreightMode)
                    return Fail($"Tariff '{tariff.Name}' is a {tariff.Mode} tariff but the quotation is {quotation.FreightMode}.");
            }

            var baseError = ValidatePercentageBase(quotation, rate, item.BaseAmount);
            if (baseError is not null)
                return Fail($"Line {i + 1} ({rate.ChargeCode.Code}): {baseError}");
        }

        // 4. Add through the domain overload (it validates category and FX rules).
        //    Nothing has been saved yet, so a domain exception here still means "no lines added".
        var newLines = new List<QuotationLine>(req.Lines.Count);
        for (var i = 0; i < req.Lines.Count; i++)
        {
            var item = req.Lines[i];
            var rate = rates[item.TariffRateId];

            try
            {
                newLines.Add(quotation.AddLine(
                    tariffRate: rate,
                    category: rate.ChargeCode!.Category,
                    serviceType: item.ServiceType,
                    quantity: item.Quantity,
                    markupType: item.MarkupType,
                    markupValue: item.MarkupValue,
                    fxRate: item.FxRate,
                    notes: item.Notes,
                    baseAmount: item.BaseAmount));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Fail($"Line {i + 1} ({rate.ChargeCode!.Code}): {ex.Message}");
            }
        }

        // 5. quotation is an EXISTING tracked entity, so each new line must be added explicitly
        //    (see .claude/rules/backend/ef-persistence.md). One SaveChanges = one transaction.
        var lineRepo = tenantUow.Repository<QuotationLine>();
        foreach (var line in newLines)
            await lineRepo.AddAsync(line, ct);

        await tenantUow.SaveChangesAsync(ct);

        return Result<IReadOnlyList<Guid>>.Ok(newLines.Select(l => l.Id).ToList());
    }

    /// <summary>Same percentage-base rules as AddQuotationLineHandler, returning a message instead of a Result.</summary>
    private static string? ValidatePercentageBase(Quotation quotation, TariffRate rate, decimal? baseAmount)
    {
        if (rate.PricingModel != PricingModel.Percentage || baseAmount is not null)
            return null;

        var baseKey = rate.PercentageBase?.Trim().ToUpperInvariant();

        if (baseKey == "CIF")
        {
            var cargoValue = quotation.CargoSpec?.DeclaredCargoValue;
            if (cargoValue is null || cargoValue.Amount <= 0m)
                return "this rate is a percentage of CIF value, but the quotation has no declared cargo value.";

            if (!string.Equals(cargoValue.Currency, quotation.Currency, StringComparison.OrdinalIgnoreCase))
                return $"cargo is declared in {cargoValue.Currency} but the quotation is in {quotation.Currency}. Provide a BaseAmount in {quotation.Currency}.";

            return null;
        }

        return baseKey == "BAS"
            ? null
            : $"percentage base '{rate.PercentageBase ?? "(none)"}' cannot be derived automatically. Provide a BaseAmount.";
    }

    private static Result<IReadOnlyList<Guid>> Fail(string message) =>
        Result<IReadOnlyList<Guid>>.Fail(message);
}
