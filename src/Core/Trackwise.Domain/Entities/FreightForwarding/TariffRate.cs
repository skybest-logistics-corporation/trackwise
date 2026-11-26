using System.Text.Json;
using System.Text.Json.Serialization;
using Trackwise.Domain.Core;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.ValueObjects;

namespace Trackwise.Domain.Entities;

/// <summary>
/// Represents an individual rate definition within a tariff.
/// </summary>
public class TariffRate : AuditableEntity, ITenantEntity
{
    public Guid TariffId { get; set; }
    public Guid ChargeCodeId { get; set; }
    public virtual ChargeCode? ChargeCode { get; set; }

    public PricingModel PricingModel { get; set; }

    /// <summary>Client-facing Selling base rate.</summary>
    public decimal BaseAmount { get; set; }

    /// <summary>Estimated baseline Buying cost per unit (used for quote margin estimation and booking accruals).</summary>
    public decimal? EstimatedBuyingAmount { get; set; }

    public string Unit { get; set; } = string.Empty; // per kg, per cbm, per container, etc.
    public decimal? Minimum { get; set; }
    public decimal? Maximum { get; set; }

    // Demurrage / Detention / Storage dwell rules
    public int? FreeDays { get; set; }
    public decimal? RateAfterFreeDays { get; set; }
    public decimal? BuyingRateAfterFreeDays { get; set; }

    // Percentage pricing rules
    public decimal? Percentage { get; set; }    // e.g., 2.5 for 2.5%
    public string? PercentageBase { get; set; } // "BAS" (base freight), "CIF" (declared cargo value), or custom

    // Serialized tiers: [{"min":0,"rate":5,"buyingRate":4},{"min":101,"rate":4,"buyingRate":3.2}]
    public string? BreakpointJson { get; set; }

    /// <summary>FIX #5: AllUnits (default, previous behavior) or Incremental (marginal brackets).</summary>
    public BreakpointMode BreakpointMode { get; set; } = BreakpointMode.AllUnits;

    public virtual Tariff? Tariff { get; set; }

    public string SourceCurrency =>
        (ChargeCode?.Currency is { Length: > 0 } code ? code : Tariff?.Currency ?? "PHP").Trim().ToUpperInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Calculates the Selling Money amount (existing signature preserved for backward compatibility).
    /// </summary>
    public Money Apply(
        decimal quantity,
        decimal? fxRate = null,
        decimal? valuationBase = null,
        string? targetCurrency = null)
    {
        return CalculateSide(
            isBuying: false,
            quantity: quantity,
            fxRate: fxRate,
            valuationBase: valuationBase,
            targetCurrency: targetCurrency);
    }

    /// <summary>
    /// Calculates the Estimated Buying Money amount using the same currency, dwell, and breakpoint rules.
    /// </summary>
    /// <summary>
    /// Calculates the Estimated Buying Money amount directly from EstimatedBuyingAmount (or Breakpoint buying tiers).
    /// </summary>
    public Money ApplyBuying(
        decimal quantity,
        decimal? fxRate = null,
        string? targetCurrency = null)
    {
        return CalculateSide(
            isBuying: true,
            quantity: quantity,
            fxRate: fxRate,
            valuationBase: null,
            targetCurrency: targetCurrency);
    }

    /// <summary>
    /// Evaluates both Selling and Estimated Buying amounts in one call for Quotation line generation.
    /// </summary>
    public (Money Selling, Money EstimatedBuying) ApplyBoth(
        decimal quantity,
        decimal? fxRate = null,
        decimal? valuationBase = null,
        string? targetCurrency = null)
    {
        var selling = Apply(quantity, fxRate, valuationBase, targetCurrency);
        var buying = ApplyBuying(quantity, fxRate, targetCurrency);
        return (selling, buying);
    }

    private Money CalculateSide(
        bool isBuying,
        decimal quantity,
        decimal? fxRate,
        decimal? valuationBase,
        string? targetCurrency)
    {
        var sourceCurrency = SourceCurrency;
        var currency = string.IsNullOrWhiteSpace(targetCurrency)
            ? sourceCurrency
            : targetCurrency.Trim().ToUpperInvariant();

        decimal factor;
        if (!string.Equals(sourceCurrency, currency, StringComparison.Ordinal))
        {
            if (fxRate is not > 0m)
                throw new InvalidOperationException(
                    $"Rate '{ChargeCode?.Code ?? Id.ToString()}' is in {sourceCurrency} but the target is {currency}; a positive fxRate is required.");
            factor = fxRate.Value;
        }
        else
        {
            factor = 1m;
        }

        var rawBase = isBuying ? (EstimatedBuyingAmount ?? 0m) : BaseAmount;

        // If calculating Buying and no buying rate/tiers are configured, return 0 without applying Selling Min/Max clamps.
        if (isBuying && EstimatedBuyingAmount is null && string.IsNullOrWhiteSpace(BreakpointJson))
        {
            return new Money { Amount = 0m, Currency = currency };
        }

        var unitRate = isBuying ? rawBase : (valuationBase ?? rawBase);

        decimal amount = PricingModel switch
        {
            PricingModel.Flat => unitRate * factor,
            PricingModel.PerUnit => CalculatePerUnit(isBuying, quantity, unitRate, rawBase) * factor,
            PricingModel.Breakpoint => CalculateBreakpoint(isBuying, quantity, unitRate, rawBase) * factor,
            PricingModel.Percentage => CalculatePercentage(valuationBase * quantity),
            _ => rawBase * factor
        };

        // Min/Max clamps apply to Selling; for Buying, only clamp if a non-zero buying cost exists.
        if (!isBuying || amount > 0m)
        {
            if (Minimum.HasValue && !isBuying) amount = Math.Max(amount, Minimum.Value * factor);
            if (Maximum.HasValue && !isBuying) amount = Math.Min(amount, Maximum.Value * factor);
        }

        return new Money { Amount = amount, Currency = currency };
    }

    private decimal CalculatePerUnit(bool isBuying, decimal quantity, decimal unitRate, decimal fallbackBase)
    {
        if (FreeDays.HasValue)
        {
            var billable = Math.Max(0m, quantity - FreeDays.Value);
            var dwellRate = isBuying
                ? (BuyingRateAfterFreeDays ?? fallbackBase)
                : (RateAfterFreeDays ?? fallbackBase);

            return billable * dwellRate;
        }

        return unitRate * quantity;
    }

    private decimal CalculateBreakpoint(bool isBuying, decimal quantity, decimal unitRate, decimal fallbackBase)
    {
        if (string.IsNullOrWhiteSpace(BreakpointJson))
            return unitRate * quantity;

        List<BreakpointTier>? parsed;
        try
        {
            parsed = ParseBreakpointTiers(BreakpointJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Rate '{ChargeCode?.Code ?? Id.ToString()}' has invalid BreakpointJson.", ex);
        }

        if (parsed is null || parsed.Count == 0)
            return unitRate * quantity;

        var tiers = parsed.OrderBy(t => t.Min).ToList();

        return BreakpointMode == BreakpointMode.Incremental
            ? PriceIncremental(isBuying, tiers, quantity)
            : PriceAllUnits(isBuying, tiers, quantity, fallbackBase);
    }

    private static decimal PriceAllUnits(bool isBuying, List<BreakpointTier> tiers, decimal quantity, decimal fallbackBase)
    {
        var matched = tiers.LastOrDefault(t => t.Min <= quantity);
        if (matched is null)
            return fallbackBase * quantity;

        var tierRate = isBuying ? (matched.BuyingRate ?? 0m) : matched.Rate;
        return tierRate * quantity;
    }

    private static decimal PriceIncremental(bool isBuying, List<BreakpointTier> tiers, decimal quantity)
    {
        decimal total = 0m;

        for (var i = 0; i < tiers.Count; i++)
        {
            var lower = i == 0 ? 0m : tiers[i].Min;
            var upper = i + 1 < tiers.Count ? tiers[i + 1].Min : decimal.MaxValue;

            var units = Math.Max(0m, Math.Min(quantity, upper) - lower);
            var tierRate = isBuying ? (tiers[i].BuyingRate ?? 0m) : tiers[i].Rate;
            total += units * tierRate;
        }

        return total;
    }

    private decimal CalculatePercentage(decimal? valuationBase)
    {
        if (valuationBase is null)
            throw new InvalidOperationException(
                $"Percentage rate '{ChargeCode?.Code ?? Id.ToString()}' needs a valuation base ({PercentageBase ?? "none configured"}).");

        return valuationBase.Value * ((Percentage ?? 0m) / 100m);
    }

    private static List<BreakpointTier>? ParseBreakpointTiers(string json)
    {
        var trimmed = json.Trim();

        if (trimmed.StartsWith('['))
            return JsonSerializer.Deserialize<List<BreakpointTier>>(trimmed, JsonOptions);

        return JsonSerializer.Deserialize<BreakpointPayload>(trimmed, JsonOptions)?.Tiers;
    }
}

public class BreakpointPayload
{
    [JsonPropertyName("tiers")]
    public List<BreakpointTier> Tiers { get; set; } = new();
}

public class BreakpointTier
{
    [JsonPropertyName("min")]
    public decimal Min { get; set; }

    /// <summary>Informational. Pricing uses Min boundaries only.</summary>
    [JsonPropertyName("max")]
    public decimal? Max { get; set; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; }

    /// <summary>Optional estimated buying rate for this weight/volume bracket.</summary>
    [JsonPropertyName("buyingRate")]
    public decimal? BuyingRate { get; set; }
}
