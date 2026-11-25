using Microsoft.EntityFrameworkCore;
using System.Linq;
using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.BackgroundJobs;
using Trackwise.Application.Abstractions.Storage;
using Trackwise.Application.Modules.Common.Constants;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.GenerateQuotationPdf;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.RecordQuotationLinkAccess;
using Trackwise.Application.Modules.Operations.FreightForwarding.Services;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.ValueObjects;
using Trackwise.Shared.Models;
using Trackwise.Shared.Models.Quotations;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Queries;

internal sealed class GetPublicQuotationPdfHandler(ITenantUnitOfWork tenantUow, QuotationPdfBuilder builder)
    : IAppRequestHandler<GetPublicQuotationPdfQuery, Result<FileResponse>>
{
    public async Task<Result<FileResponse>> Handle(GetPublicQuotationPdfQuery req, CancellationToken ct)
    {
        try { await tenantUow.SetCurrentTenantByIdAsync(req.TenantId); }
        catch (InvalidOperationException) { return Result<FileResponse>.Fail("Invalid quotation link."); }

        var link = await tenantUow.Repository<QuotationLink>().GetAsync(l => l.Token == req.Token, ct);
        if (link is null || !link.IsValid)
            return Result<FileResponse>.Fail("This quotation link is invalid, expired or revoked.");

        // No access-log enqueue here: GetPublicQuotationHandler already records the visit.
        return await builder.BuildAsync(link.QuotationId, ct);
    }
}
