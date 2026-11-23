using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Application.Abstractions.Email;
using Trackwise.Application.Abstractions.Email.Models;
using Trackwise.Application.Utilities;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Options;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.IssueQuotation;

internal sealed class IssueQuotationHandler(
    ITenantUnitOfWork tenantUow,
    IEmailSender emailSender,
    IEmailTemplateService emailTemplateService,
    ICurrentUserService currentUserService,
    IOptions<CustomerPortalOptions> portalOptions)
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
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(ex.Message);
        }

        QuotationLink? quotationLink = null;

        // Send email to customer if email is provided, carrying a tokenized link to the issued offer
        if (!string.IsNullOrWhiteSpace(req.CustomerEmail))
        {
            quotationLink = new QuotationLink
            {
                Token = TokenGenerator.GenerateSecureToken(64),
                QuotationId = quotation.Id,
                ExpiresAt = quotation.ValidUntil!.Value.UtcDateTime,
                CreatedByUserId = currentUserService.GetUserId() ?? Guid.Empty
            };

            var sent = await SendQuotationIssuedEmailAsync(quotation, quotationLink, req.CustomerEmail, ct);
            if (!sent)
                return Result.Fail("Failed to send the quotation email. Please try again.");
        }

        quotationRepo.Update(quotation);
        if (quotationLink is not null)
            await tenantUow.Repository<QuotationLink>().AddAsync(quotationLink, ct);
        await tenantUow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    private async Task<bool> SendQuotationIssuedEmailAsync(
        Quotation quotation,
        QuotationLink quotationLink,
        string customerEmail,
        CancellationToken ct)
    {
        var tenant = tenantUow.GetCurrentTenant();
        var customer = await tenantUow.Repository<Customer>().GetByIdAsync(quotation.CustomerId, ct);

        var model = new QuotationIssuedEmailModel
        {
            CompanyName = tenant.CompanyName ?? tenant.Name,
            CustomerName = customer?.Name ?? "Customer",
            ReferenceNumber = quotation.ReferenceNumber,
            QuotationUrl = $"{portalOptions.Value.BaseUrl}/quotations/{tenant.Id}/{quotationLink.Token}",
            ExpiresAt = quotationLink.ExpiresAt.ToString("MMMM dd, yyyy"),
            PersonalMessage = null
        };

        var subject = $"Quotation {quotation.ReferenceNumber} Issued | {model.CompanyName}";
        var body = await emailTemplateService.RenderAsync("QuotationIssued", model);

        return await emailSender.SendEmailAsync(customerEmail, subject, body);
    }
}