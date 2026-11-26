using Trackwise.Domain.Core;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.ValueObjects;

namespace Trackwise.Domain.Entities;

/// <summary>
/// Represents a single priced line on a Quotation, pointing at a live TariffRate.
/// </summary>
public class QuotationLine : AuditableEntity, ITenantEntity
{
    public Guid QuotationId { get; private set; }
    public Guid TariffRateId { get; private set; }
    public virtual TariffRate? TariffRate { get; private set; }

    /// <summary>
    /// FIX #2: persisted charge category (previously the constructor argument was dropped).
    /// Lets validation and discount handling work without loading TariffRate/ChargeCode.
    /// </summary>
    public ChargeCategory Category { get; private set; }

    /// <summary>FIX #4: the quotation's currency; Price() converts into this.</summary>
    public string QuotationCurrency { get; private set; } = default!;

    // Operational Classification for Cold Chain Validation
    public OperationalServiceType ServiceType { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>
    /// Explicit valuation base for percentage-based lines (e.g. an override of CIF value).
    /// Wins over the quotation-derived base. Must be in the quotation currency.
    /// </summary>
    public decimal? BaseAmount { get; private set; }

    public decimal? FxRate { get; private set; }

    public MarkupType MarkupType { get; private set; }
    public decimal MarkupValue { get; private set; }

    // Price Snapshots (persisted once quote is Issued to prevent drift)
    public decimal? SnapshotBuyingAmount { get; private set; }
    public decimal? SnapshotSellingAmount { get; private set; }
    public string? Currency { get; private set; }

    public string? Notes { get; private set; }
    public virtual Quotation? Quotation { get; private set; }

    protected QuotationLine() { }

    internal static QuotationLine Create(
        Guid quotationId,
        string quotationCurrency,
        TariffRate tariffRate,
        ChargeCategory category,
        OperationalServiceType serviceType,
        decimal quantity,
        MarkupType markupType,
        decimal markupValue,
        decimal? fxRate,
        string? notes,
        decimal? baseAmount = null)
    {
        ArgumentNullException.ThrowIfNull(tariffRate);

        var line = Create(
            quotationId, quotationCurrency, tariffRate.Id, category, serviceType,
            quantity, markupType, markupValue, fxRate, notes, baseAmount);

        line.TariffRate = tariffRate;
        return line;
    }

    internal QuotationLine CopyTo(Guid quotationId, string quotationCurrency) =>
        Create(
            quotationId,
            quotationCurrency,
            TariffRateId,
            Category,
            ServiceType,
            Quantity,
            MarkupType,
            MarkupValue,
            FxRate,
            Notes,
            BaseAmount);

    internal static QuotationLine Create(
        Guid quotationId,
        string quotationCurrency,
        Guid tariffRateId,
        ChargeCategory category,
        OperationalServiceType serviceType,
        decimal quantity,
        MarkupType markupType,
        decimal markupValue,
        decimal? fxRate,
        string? notes,
        decimal? baseAmount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quotationCurrency);

        var line = new QuotationLine
        {
            QuotationId = quotationId,
            QuotationCurrency = quotationCurrency.Trim().ToUpperInvariant(),
            TariffRateId = tariffRateId,
            Category = category,
            ServiceType = serviceType,
            Quantity = quantity,
            BaseAmount = baseAmount,
            MarkupType = markupType,
            MarkupValue = markupValue,
            FxRate = fxRate,
            Notes = notes
        };

        line.DomainEvents.Add(new QuotationLineAdded(line.Id, quotationId, tariffRateId));
        return line;
    }

    /// <param name="dynamicValuationBase">
    /// Base for Percentage rates, in the quotation currency. Normally resolved by
    /// <see cref="Quotation.PriceLine"/>; an explicit line BaseAmount takes precedence.
    /// </param>
    public (Money Buying, Money Selling) Price(decimal? dynamicValuationBase = null)
    {
        if (SnapshotBuyingAmount.HasValue && SnapshotSellingAmount.HasValue && Currency is not null)
        {
            return (
                new Money { Amount = SnapshotBuyingAmount.Value, Currency = Currency },
                new Money { Amount = SnapshotSellingAmount.Value, Currency = Currency }
            );
        }

        if (TariffRate is null)
        {
            throw new InvalidOperationException(
                $"TariffRate must be Include()'d before pricing quotation line '{Id}'.");
        }

        var valuation = BaseAmount ?? dynamicValuationBase;

        // Selling uses TariffRate.BaseAmount (or valuationBase for Percentage);
        // Buying comes strictly from TariffRate.EstimatedBuyingAmount.
        var (tariffSelling, tariffBuying) = TariffRate.ApplyBoth(
            quantity: Quantity,
            fxRate: FxRate,
            valuationBase: valuation,
            targetCurrency: QuotationCurrency);

        var buyingAmount = tariffBuying.Amount;

        var sellingAmount = MarkupType switch
        {
            MarkupType.Percentage when MarkupValue != 0m => tariffSelling.Amount * (1 + MarkupValue / 100m),
            MarkupType.Flat when MarkupValue != 0m => tariffSelling.Amount + MarkupValue,
            _ => tariffSelling.Amount
        };

        if (Category == ChargeCategory.Discount)
        {
            if (buyingAmount > 0) buyingAmount = -buyingAmount;
            if (sellingAmount > 0) sellingAmount = -sellingAmount;
        }

        return (
            new Money { Amount = buyingAmount, Currency = tariffBuying.Currency },
            new Money { Amount = sellingAmount, Currency = tariffSelling.Currency });
    }

    internal void SnapshotPrices(decimal? dynamicValuationBase = null)
    {
        var (buying, selling) = Price(dynamicValuationBase);
        SnapshotBuyingAmount = buying.Amount;
        SnapshotSellingAmount = selling.Amount;
        Currency = buying.Currency;
    }

    internal void Update(
        decimal quantity,
        decimal? baseAmount,
        MarkupType markupType,
        decimal markupValue,
        decimal? fxRate,
        string? notes)
        {
            // Snapshots are written when the quote is issued; changing inputs afterwards would
            // leave Price() returning stale snapshot values.
            if (SnapshotBuyingAmount.HasValue || SnapshotSellingAmount.HasValue)
                throw new InvalidOperationException("Cannot modify a line that has been price-snapshotted.");

            Quantity = quantity;
            BaseAmount = baseAmount;
            MarkupType = markupType;
            MarkupValue = markupValue;
            FxRate = fxRate;
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        }
    }

public record QuotationLineAdded(Guid Id, Guid QuotationId, Guid TariffRateId) : IDomainEvent;
