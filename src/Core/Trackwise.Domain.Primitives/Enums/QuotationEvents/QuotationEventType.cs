using System;
using System.Collections.Generic;
using System.Text;

namespace Trackwise.Domain.Primitives.Enums.QuotationEvents;

public enum QuotationEventType
{
    Created, Edited, RevisionCreated, Submitted, Approved,
    Rejected, Issued, Accepted, Expired, ConvertedToBooking
}
