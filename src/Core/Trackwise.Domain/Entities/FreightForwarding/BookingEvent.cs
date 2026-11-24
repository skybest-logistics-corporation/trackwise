using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Domain.Core;
using Trackwise.Domain.Primitives.Enums.BookingEvents;
using Trackwise.Domain.Primitives.Enums.FreightForwarding.BookingEnums;

namespace Trackwise.Domain.Entities.FreightForwarding;

public class BookingEvent : Entity, ITenantEntity
{
    public Guid BookingId { get; private set; }
    public BookingEventType Type { get; private set; }
    public BookingStatus? FromStatus { get; private set; }
    public BookingStatus ToStatus { get; private set; }

    /// <summary>Display name snapshot, so history survives a user rename or deletion.</summary>
    public string Actor { get; private set; } = default!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? Details { get; private set; }

    /// <summary>E.g. the charge, document or invoice id, so the UI can link to it.</summary>
    public Guid? RelatedId { get; private set; }

    protected BookingEvent() { }

    internal static BookingEvent Create(
        Guid bookingId, BookingEventType type, BookingStatus? from, BookingStatus to,
        string? actor, string? details, Guid? relatedId) => new()
        {
            BookingId = bookingId,
            Type = type,
            FromStatus = from,
            ToStatus = to,
            Actor = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
            OccurredAt = DateTimeOffset.UtcNow,
            Details = details,
            RelatedId = relatedId
        };
}
