using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Domain.Primitives.Enums.QuotationEvents;

namespace Trackwise.Shared.Models.QuotationEvents;

public record QuotationEventDto(
    Guid Id,
    QuotationEventType Type,
    string Actor,
    DateTimeOffset OccurredAt,
    string? Details,
    Guid? RelatedId);
