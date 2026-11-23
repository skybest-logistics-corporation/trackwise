using Microsoft.Extensions.Logging;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.RecordQuotationLinkAccess;

internal sealed class RecordQuotationLinkAccessHandler(
    ITenantUnitOfWork tenantUow,
    ILogger<RecordQuotationLinkAccessHandler> logger)
    : IAppRequestHandler<RecordQuotationLinkAccessCommand, Result>
{
    public async Task<Result> Handle(RecordQuotationLinkAccessCommand req, CancellationToken ct)
    {
        try
        {
            await tenantUow.SetCurrentTenantByIdAsync(req.TenantId);
        }
        catch (InvalidOperationException)
        {
            return Result.Fail("Invalid tenant.");
        }

        var quotationLink = await tenantUow.Repository<QuotationLink>().GetByIdAsync(req.QuotationLinkId, ct);
        if (quotationLink is null)
        {
            logger.LogWarning(
                "RecordQuotationLinkAccess: quotation link {LinkId} not found for tenant {TenantId}.",
                req.QuotationLinkId, req.TenantId);
            return Result.Fail("Quotation link not found.");
        }

        quotationLink.RecordAccess();
        tenantUow.Repository<QuotationLink>().Update(quotationLink);
        await tenantUow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}