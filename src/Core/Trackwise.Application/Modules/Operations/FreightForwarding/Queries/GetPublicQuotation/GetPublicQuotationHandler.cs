using System.Linq;
using Microsoft.EntityFrameworkCore;
using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.BackgroundJobs;
using Trackwise.Application.Modules.Common.Constants;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.ValueObjects;
using Trackwise.Application.Abstractions.Storage;
using Trackwise.Shared.Models;
using Trackwise.Shared.Models.Quotations;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.RecordQuotationLinkAccess;
using Trackwise.Application.Modules.Operations.FreightForwarding.Queries.GetPublicQuotation;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Queries;

internal sealed class GetPublicQuotationHandler(
    ITenantUnitOfWork tenantUow,
    IBlobStorageService blobStorageService,
    ICommandEnqueuer commandEnqueuer)
    : IAppRequestHandler<GetPublicQuotationQuery, Result<PublicQuotationDto>>
{
    public async Task<Result<PublicQuotationDto>> Handle(GetPublicQuotationQuery req, CancellationToken ct)
    {
        Tenant tenant;
        try
        {
            tenant = await tenantUow.SetCurrentTenantByIdAsync(req.TenantId);
        }
        catch (InvalidOperationException)
        {
            return Result<PublicQuotationDto>.Fail("Invalid quotation link.");
        }

        var quotationLink = await tenantUow.Repository<QuotationLink>()
            .GetAsync(l => l.Token == req.Token, ct);

        if (quotationLink is null)
        {
            return Result<PublicQuotationDto>.Fail("Quotation link not found.");
        }

        if (!quotationLink.IsValid)
        {
            return Result<PublicQuotationDto>.Fail("This quotation link has expired or been revoked.");
        }

        // Defer access bookkeeping so the read path stays write-free.
        commandEnqueuer.Enqueue(new RecordQuotationLinkAccessCommand
        {
            TenantId = req.TenantId,
            QuotationLinkId = quotationLink.Id
        });

        var quotation = await tenantUow.Repository<Quotation>().GetByIdAsync(quotationLink.QuotationId, ct);
        if (quotation is null)
        {
            return Result<PublicQuotationDto>.Fail("Quotation not found.");
        }

        // Batch-load route stop location names (Lane is a value object, so stops are loaded with the quotation).
        var stops = quotation.Lane?.Stops ?? [];
        var stopLocationIds = stops.Select(s => s.LocationId).Distinct().ToList();
        var locations = stopLocationIds.Count > 0
            ? await tenantUow.Repository<Location>().Query()
                .Where(loc => stopLocationIds.Contains(loc.Id))
                .ToDictionaryAsync(loc => loc.Id, loc => new { loc.Code, loc.Name }, ct)
            : [];

        var stopsDto = stops
            .OrderBy(s => s.Sequence)
            .Select(s => new RouteStopSummaryDto(
                Sequence: s.Sequence,
                LocationId: s.LocationId,
                LocationCode: locations.TryGetValue(s.LocationId, out var loc) ? loc.Code : "UNKNOWN",
                LocationName: loc?.Name ?? "Unknown Location",
                Type: s.Type,
                WindowStart: s.WindowStart,
                WindowEnd: s.WindowEnd))
            .ToList();

        var sellingTotals = quotation.Lines.Sum(l => l.SnapshotSellingAmount ?? l.Price().Selling.Amount);

        var dto = new PublicQuotationDto
        {
            ReferenceNumber = quotation.ReferenceNumber,
            Status = quotation.Status,
            Currency = quotation.Currency,
            IssuedAt = quotation.IssuedAt,
            ValidUntil = quotation.ValidUntil,
            Mode = quotation.Mode,
            FreightMode = quotation.FreightMode,
            ShipmentType = quotation.ShipmentType,
            Incoterm = quotation.Incoterm,
            Stops = stopsDto,
            TotalDistanceKm = quotation.Lane?.TotalDistanceKm,
            Commodity = quotation.CargoSpec?.Commodity,
            TotalWeightKg = quotation.CargoSpec?.TotalWeightKg > 0 ? quotation.CargoSpec?.TotalWeightKg : null,
            PalletCount = quotation.CargoSpec?.PalletCount > 0 ? quotation.CargoSpec?.PalletCount : null,
            IsTemperatureControlled = quotation.IsTemperatureControlled,
            Lines = quotation.Lines.Select(l => new PublicQuotationLineDto
            {
                ChargeName = l.TariffRate?.ChargeCode?.Name ?? l.TariffRate?.ChargeCode?.Code ?? "Charge",
                Unit = l.TariffRate?.Unit ?? l.TariffRate?.ChargeCode?.DefaultUnit ?? string.Empty,
                ServiceType = l.ServiceType,
                Quantity = l.Quantity,
                SellingAmount = l.SnapshotSellingAmount ?? l.Price().Selling.Amount,
                Currency = quotation.Currency
            }).ToList(),
            TotalSellingAmount = sellingTotals,
            Notes = quotation.Notes,
            CompanyName = tenant.CompanyName ?? tenant.Name,
            CompanyLogoUrl = GetLogoUrl(tenant)
        };

        return Result<PublicQuotationDto>.Ok(dto);
    }

    private string? GetLogoUrl(Tenant tenant)
    {
        if (string.IsNullOrEmpty(tenant.LogoPath))
        {
            return null;
        }

        if (tenant.LogoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return tenant.LogoPath;
        }

        return blobStorageService.GetPublicUrl(BlobConstants.LogosContainerName, tenant.LogoPath, tenant.Id);
    }
}
