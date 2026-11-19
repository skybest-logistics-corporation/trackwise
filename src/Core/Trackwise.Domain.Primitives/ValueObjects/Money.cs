using System.ComponentModel.DataAnnotations.Schema;

namespace Trackwise.Domain.Primitives.ValueObjects;

[ComplexType]
public record Money
{
    public required decimal Amount { get; set; }
    /// <summary>ISO 4217 code, stored upper-case: a client sending the tenant's "php" still gets "PHP".</summary>
    public required string Currency
    {
        get;
        set => field = value?.Trim().ToUpperInvariant() ?? "";
    }
    public static Money Zero(string currency) => new() { Amount = 0M, Currency = currency };

    /// <summary>
    /// Implicitly converts a Money object to a decimal.
    /// </summary>
    /// <param name="entity">The Money object to convert.</param>
    /// <returns>The decimal amount of the Money object.</returns>
    public static implicit operator decimal(Money entity) => entity.Amount;
}
