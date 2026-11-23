using Trackwise.Domain.Core;

namespace Trackwise.Domain.Entities;

/// <summary>
///     Represents a public, token-gated quotation link that allows unauthenticated access.
/// </summary>
public class QuotationLink : AuditableEntity, ITenantEntity
{
    /// <summary>Unique secure token for quotation link access.</summary>
    public required string Token { get; set; }

    /// <summary>The quotation this link provides access to.</summary>
    public required Guid QuotationId { get; set; }
    public virtual Quotation? Quotation { get; set; }

    /// <summary>When the quotation link expires (mirrors the quotation's ValidUntil).</summary>
    public required DateTime ExpiresAt { get; set; }

    /// <summary>Whether the quotation link is active (can be revoked).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The user who created this quotation link.</summary>
    public required Guid CreatedByUserId { get; set; }

    /// <summary>Number of times this quotation link has been accessed.</summary>
    public int AccessCount { get; set; }

    /// <summary>Last time this quotation link was accessed.</summary>
    public DateTime? LastAccessedAt { get; set; }

    /// <summary>Whether the quotation link is currently valid (active and not expired).</summary>
    public bool IsValid => IsActive && DateTime.UtcNow < ExpiresAt;

    /// <summary>Records an access to this quotation link.</summary>
    public void RecordAccess()
    {
        AccessCount++;
        LastAccessedAt = DateTime.UtcNow;
    }

    /// <summary>Revokes the quotation link.</summary>
    public void Revoke() => IsActive = false;
}