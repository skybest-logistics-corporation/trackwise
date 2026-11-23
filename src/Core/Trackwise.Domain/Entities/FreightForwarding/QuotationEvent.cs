using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Domain.Core;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.QuotationEvents;

namespace Trackwise.Domain.Entities.FreightForwarding;

public class QuotationEvent : Entity, ITenantEntity
{
    public Guid QuotationId { get; private set; }
    public QuotationEventType Type { get; private set; }
    public QuotationStatus? FromStatus { get; private set; }
    public QuotationStatus ToStatus { get; private set; }

    /// <summary>Display name snapshot, so history survives a user rename or deletion.</summary>
    public string Actor { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Details { get; private set; }

    /// <summary>E.g. the booking id for ConvertedToBooking, so the UI can link to it.</summary>
    public Guid? RelatedId { get; private set; }

    protected QuotationEvent() { }

    internal static QuotationEvent Create(
        Guid quotationId, QuotationEventType type, QuotationStatus? from, QuotationStatus to,
        string? actor, string? details, Guid? relatedId) => new()
        {
            QuotationId = quotationId,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            Actor = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
            OccurredAt = DateTimeOffset.UtcNow,
            Details = details,
            RelatedId = relatedId
        };
}
