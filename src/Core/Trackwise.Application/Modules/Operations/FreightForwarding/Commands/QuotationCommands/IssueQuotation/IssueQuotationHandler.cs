using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Application.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using Trackwise.Application.Abstractions.Email;
using Trackwise.Application.Abstractions.CurrentUser;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.IssueQuotation;

internal sealed class IssueQuotationHandler(ITenantUnitOfWork tenantUow, IEmailSender emailSender, ICurrentUserService currentUserService)
    : IAppRequestHandler<IssueQuotationCommand, Result>
{
    public async Task<Result> Handle(IssueQuotationCommand req, CancellationToken ct)
    {
        var quotationRepo = tenantUow.Repository<Quotation>();

        // Eager-load Lines -> TariffRate -> ChargeCode so line.SnapshotPrices() computes successfully
        var quotation = await quotationRepo.Query()
            .Include(q => q.Lines)
                .ThenInclude(l => l.TariffRate)
                    .ThenInclude(tr => tr!.ChargeCode)
            .Include(q => q.Lines)
                .ThenInclude(l => l.TariffRate)
                    .ThenInclude(tr => tr!.Tariff)
            .FirstOrDefaultAsync(q => q.Id == req.Id, ct);

        if (quotation is null)
            return Result.Fail($"Quotation with ID '{req.Id}' not found.");

        if (quotation.Status != QuotationStatus.Approved)
            return Result.Fail($"Only Approved quotations can be issued. Current status: '{quotation.Status}'.");

        try
        {
            // Freezes buying and selling numbers on every line to prevent tariff-drift
            quotation.Issue(TimeSpan.FromDays(req.ValidityDays), currentUserService.GetUserName());

            // Send email to customer if email is provided
            if (!string.IsNullOrWhiteSpace(req.CustomerEmail))
            {
                var emailSubject = $"Quotation {quotation.ReferenceNumber} Issued";
                var emailBody = new StringBuilder();
                emailBody.AppendLine($"Dear Customer,");

                await emailSender.SendEmailAsync(req.CustomerEmail, emailSubject, emailBody.ToString());
            }
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ex.Message);
        }

        quotationRepo.Update(quotation);
        await tenantUow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
