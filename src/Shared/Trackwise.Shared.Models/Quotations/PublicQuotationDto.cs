using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.ValueObjects;
using Trackwise.Shared.Models.Quotations;

namespace Trackwise.Shared.Models;

/// <summary>
///     Quotation information for public (unauthenticated) access via a tokenized link.
///     Selling-only pricing - buying and margin are never exposed to the customer.
/// </summary>
public class PublicQuotationDto
{
    public string ReferenceNumber { get; set; } = string.Empty;
    public QuotationStatus Status { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? ValidUntil { get; set; }

    // Route & logistics profile
    public TransportMode Mode { get; set; }
    public FreightMode FreightMode { get; set; }
    public ShipmentType ShipmentType { get; set; }
    public Incoterm Incoterm { get; set; }
    public List<RouteStopSummaryDto> Stops { get; set; } = [];
    public decimal? TotalDistanceKm { get; set; }

    // Cargo profile
    public string? Commodity { get; set; }
    public decimal? TotalWeightKg { get; set; }
    public int? PalletCount { get; set; }
    public bool IsTemperatureControlled { get; set; }

    // Selling-side line items (no buying, no margin)
    public List<PublicQuotationLineDto> Lines { get; set; } = [];
    public decimal TotalSellingAmount { get; set; }
    public string? Notes { get; set; }

    // Tenant branding
    public string CompanyName { get; set; } = string.Empty;
    public string? CompanyLogoUrl { get; set; }
}

public class PublicQuotationLineDto
{
    public string ChargeName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public OperationalServiceType ServiceType { get; set; }
    public decimal Quantity { get; set; }
    public decimal SellingAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}