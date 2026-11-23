using Riok.Mapperly.Abstractions;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Entities.FreightForwarding; // QuotationEvent (drop if it lives in Trackwise.Domain.Entities)
using Trackwise.Domain.Primitives.ValueObjects;
using Trackwise.Shared.Models;
using Trackwise.Shared.Models.QuotationEvents;
using Trackwise.Shared.Models.Quotations;

namespace Trackwise.Mappings;

[Mapper]
[UseStaticMapper(typeof(TerminalMapper))]
public static partial class QuotationMapper
{
    // =========================================================================
    // 1. Quotation -> QuotationDto
    // =========================================================================

    [MapperIgnoreSource(nameof(Quotation.DomainEvents))]
    [MapperIgnoreSource(nameof(Quotation.Lines))]
    // Ignore Lane & CargoSpec objects because we flatten their child properties below
    [MapperIgnoreSource(nameof(Quotation.Lane))]
    [MapperIgnoreSource(nameof(Quotation.CargoSpec))]
    // Target fields resolved asynchronously or computed via overloads
    [MapperIgnoreTarget(nameof(QuotationDto.CustomerName))]
    [MapperIgnoreTarget(nameof(QuotationDto.CustomerEmail))]
    [MapperIgnoreTarget(nameof(QuotationDto.BranchName))]
    [MapperIgnoreTarget(nameof(QuotationDto.Stops))]
    [MapperIgnoreTarget(nameof(QuotationDto.Lines))]
    [MapperIgnoreTarget(nameof(QuotationDto.TotalBuyingAmount))]
    [MapperIgnoreTarget(nameof(QuotationDto.TotalSellingAmount))]
    [MapperIgnoreTarget(nameof(QuotationDto.Revisions))]
    // UpdatedBy holds a user id on the entity. The overload below sets the display name,
    // so the id is never exposed by accident.
    [MapperIgnoreTarget(nameof(QuotationDto.UpdatedBy))]
    // Flatten Lane properties
    [MapProperty("Lane.TotalDistanceKm", nameof(QuotationDto.TotalDistanceKm))]
    // Flatten CargoSpec properties
    [MapProperty("CargoSpec.Commodity", nameof(QuotationDto.Commodity))]
    [MapProperty("CargoSpec.TotalWeightKg", nameof(QuotationDto.TotalWeightKg))]
    [MapProperty("CargoSpec.PalletCount", nameof(QuotationDto.PalletCount))]
    [MapProperty("CargoSpec.DeclaredCargoValue.Amount", nameof(QuotationDto.DeclaredCargoValueAmount))]
    // NEW: the currency was previously unmapped and always came back null
    [MapProperty("CargoSpec.DeclaredCargoValue.Currency", nameof(QuotationDto.DeclaredCargoValueCurrency))]
    // Lifecycle history: names differ (Events -> Timeline) and we force chronological order.
    // UpdatedAt maps by name and goes through ToUtcOffset below.
    [MapProperty(nameof(Quotation.Events), nameof(QuotationDto.Timeline), Use = nameof(MapTimeline))]
    // NEW: PolId, PodId, Pol and Pod map by name. Pol/Pod (Terminal -> TerminalSummaryDto)
    // use ToTerminalSummary below, so the handler must Include() both navigations.
    public static partial QuotationDto ToDto(this Quotation entity);

    /// <summary>
    /// Nested Value Object Mapper: Automatically utilized by Mapperly 
    /// when mapping Quotation.TemperatureSpec to QuotationDto.TemperatureSpec.
    /// </summary>
    public static partial TemperatureControlDto ToDto(this TemperatureControl entity);

    // =========================================================================
    // 2. Quotation -> QuotationSummaryDto
    // =========================================================================

    [MapperIgnoreSource(nameof(Quotation.DomainEvents))]
    [MapperIgnoreSource(nameof(Quotation.Lines))]
    [MapperIgnoreSource(nameof(Quotation.Events))]
    [MapperIgnoreSource(nameof(Quotation.Lane))]
    [MapperIgnoreSource(nameof(Quotation.CargoSpec))]
    [MapperIgnoreSource(nameof(Quotation.TemperatureSpec))]
    [MapperIgnoreSource(nameof(Quotation.Notes))]
    [MapperIgnoreSource(nameof(Quotation.ApprovedBy))]
    [MapperIgnoreSource(nameof(Quotation.ApprovedAt))]
    [MapperIgnoreSource(nameof(Quotation.RejectedBy))]
    [MapperIgnoreSource(nameof(Quotation.RejectedAt))]
    [MapperIgnoreSource(nameof(Quotation.RejectionReason))]
    [MapperIgnoreSource(nameof(Quotation.ApprovalEscalationReason))]
    // NEW: the summary has no port fields
    [MapperIgnoreSource(nameof(Quotation.PolId))]
    [MapperIgnoreSource(nameof(Quotation.Pol))]
    [MapperIgnoreSource(nameof(Quotation.PodId))]
    [MapperIgnoreSource(nameof(Quotation.Pod))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.CustomerName))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.CustomerEmail))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.BranchName))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.ReferenceNumber))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.LineCount))]
    [MapperIgnoreTarget(nameof(QuotationSummaryDto.TotalSellingAmount))]
    public static partial QuotationSummaryDto ToSummaryDto(this Quotation entity);

    // =========================================================================
    // 3. QuotationLine -> QuotationLineDto
    // =========================================================================

    [MapperIgnoreSource(nameof(QuotationLine.DomainEvents))]
    [MapperIgnoreSource(nameof(QuotationLine.TariffRate))]
    [MapperIgnoreSource(nameof(QuotationLine.Quotation))]
    [MapperIgnoreSource(nameof(QuotationLine.SnapshotBuyingAmount))]
    [MapperIgnoreSource(nameof(QuotationLine.SnapshotSellingAmount))]
    // Target fields populated via TariffRate or pricing computation
    [MapperIgnoreTarget(nameof(QuotationLineDto.TariffId))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.TariffName))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.TariffTruckType))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.TariffTonnageClass))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.ChargeCode))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.ChargeName))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.Unit))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.BuyingAmount))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.SellingAmount))]
    [MapperIgnoreTarget(nameof(QuotationLineDto.IsPriceSnapshotted))]
    public static partial QuotationLineDto ToDto(this QuotationLine entity);

    // =========================================================================
    // 4. Overloads for Computed & Aggregated Fields
    // =========================================================================

    /// <summary>
    /// Full Quotation overload: Injects customer name, resolved route stops, 
    /// and aggregated buying/selling line amounts.
    /// </summary>
    /// <param name="updatedByName">
    /// Display name for entity.UpdatedBy (a user id). Resolve it in the handler; null shows no name.
    /// </param>
    public static QuotationDto ToDto(
        this Quotation entity,
        string customerName,
        string branchName,
        List<QuotationLineDto> lines,
        List<RouteStopSummaryDto>? stops = null,
        string? updatedByName = null)
    {
        var dto = entity.ToDto();
        dto.CustomerName = customerName;
        dto.BranchName = branchName;
        dto.ReferenceNumber = entity.ReferenceNumber;
        dto.Lines = lines;
        dto.Stops = stops ?? [];
        dto.TotalBuyingAmount = lines.Sum(l => l.BuyingAmount);
        dto.TotalSellingAmount = lines.Sum(l => l.SellingAmount);
        dto.UpdatedBy = updatedByName;
        return dto;
    }

    /// <summary>
    /// Summary overload: Injects customer name and pre-computed line summaries.
    /// </summary>
    public static QuotationSummaryDto ToSummaryDto(
        this Quotation entity,
        string customerName,
        string branchName,
        int lineCount,
        decimal totalSellingAmount)
    {
        var dto = entity.ToSummaryDto();
        dto.CustomerName = customerName;
        dto.BranchName = branchName;
        dto.ReferenceNumber = entity.ReferenceNumber;
        dto.LineCount = lineCount;
        dto.FreightMode = entity.FreightMode;
        dto.TotalSellingAmount = totalSellingAmount;
        return dto;
    }

    /// <summary>
    /// Line overload: Computes (or retrieves snapshotted) prices and applies 
    /// TariffRate/ChargeCode metadata.
    /// </summary>
    public static QuotationLineDto ToDto(
        this QuotationLine entity,
        string chargeCode,
        string chargeName,
        string unit)
    {
        var (buying, selling) = entity.Price();
        var dto = entity.ToDto();

        dto.ChargeCode = chargeCode;
        dto.ChargeName = chargeName;
        dto.Unit = unit;
        dto.BuyingAmount = buying.Amount;
        dto.SellingAmount = selling.Amount;
        dto.Currency = buying.Currency;
        dto.IsPriceSnapshotted = entity.SnapshotBuyingAmount.HasValue;

        return dto;
    }

    // =========================================================================
    // 5. Custom conversions used by the generated mappings
    // =========================================================================

    /// <summary>Oldest first, so the frontend can render the list as-is.</summary>
    private static List<QuotationEventDto> MapTimeline(IReadOnlyCollection<QuotationEvent> events) =>
        events
            .OrderBy(e => e.OccurredAt)
            .Select(e => new QuotationEventDto(e.Id, e.Type, e.Actor, e.OccurredAt, e.Details, e.RelatedId))
            .ToList();

    // Entities store UTC DateTime, but EF returns Kind = Unspecified, and a plain
    // (DateTimeOffset) cast would apply the server's local offset. Mapperly uses these for every
    // DateTime -> DateTimeOffset property (CreatedAt, UpdatedAt).
    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue ? ToUtcOffset(value.Value) : null;
}
