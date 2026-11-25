using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AcceptPublicQuotation;

internal sealed class AcceptPublicQuotationHandler(ITenantUnitOfWork tenantUow)
    : IAppRequestHandler<AcceptPublicQuotationCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(AcceptPublicQuotationCommand req, CancellationToken ct)
    {
        var message = req.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 1000)
            return Result<Guid>.Fail("A message of up to 1000 characters is required.");

        try { await tenantUow.SetCurrentTenantByIdAsync(req.TenantId); }
        catch (InvalidOperationException) { return Result<Guid>.Fail("Invalid quotation link."); }

        var link = await tenantUow.Repository<QuotationLink>().GetAsync(l => l.Token == req.Token, ct);
        if (link is null || !link.IsValid)
            return Result<Guid>.Fail("This quotation link is invalid, expired or revoked.");

        var quotation = await tenantUow.Repository<Quotation>().GetByIdAsync(link.QuotationId, ct);
        if (quotation is null) return Result<Guid>.Fail("Quotation not found.");

        var customer = await tenantUow.Repository<Customer>().GetByIdAsync(quotation.CustomerId, ct);

        try
        {
            quotation.Accept($"{customer?.Name ?? "Customer"} (via link)", message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<Guid>.Fail(ex.Message);
        }

        await tenantUow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(quotation.Id);
    }
}
