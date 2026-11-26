using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.UpdateQuotationLine;

internal sealed class UpdateQuotationLineHandler(ITenantUnitOfWork tenantUow)
    : IAppRequestHandler<UpdateQuotationLineCommand, Result>
{
    public async Task<Result> Handle(UpdateQuotationLineCommand req, CancellationToken ct)
    {
        if (req.Quantity <= 0)
            return Result.Fail("Quantity must be greater than zero.");

        if (req.MarkupValue < 0)
            return Result.Fail("Markup value cannot be negative.");

        if (req.BaseAmount is < 0)
            return Result.Fail("Base amount cannot be negative.");

        if (req.FxRate is <= 0)
            return Result.Fail("FX rate must be greater than zero.");

        var quotation = await tenantUow.Repository<Quotation>().GetByIdAsync(req.QuotationId, ct);

        if (quotation is null)
            return Result.Fail($"Quotation with ID '{req.QuotationId}' not found.");

        if (quotation.Status != QuotationStatus.Draft)
            return Result.Fail("Lines can only be edited on a Draft quotation.");

        if (!quotation.Lines.Any(l => l.Id == req.LineId))
            return Result.Fail($"Quotation line with ID '{req.LineId}' not found on this quotation.");

        quotation.UpdateLine(
            req.LineId,
            req.Quantity,
            req.BaseAmount,
            req.MarkupType,
            req.MarkupValue,
            req.FxRate,
            req.Notes);

        await tenantUow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
