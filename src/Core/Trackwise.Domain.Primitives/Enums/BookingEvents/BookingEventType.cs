using System;
using System.Collections.Generic;
using System.Text;

namespace Trackwise.Domain.Primitives.Enums.BookingEvents;

public enum BookingEventType
{
    Created, Updated,
    ChargeAdded, ChargeUpdated, ChargeRemoved, ChargesInvoiced, ChargesReleased,
    ChargesBuyingReconciled, ChargesExpenseReleased, ChargesExpenseRequested,
    DocumentAdded, DocumentVerified, DocumentRejected, DocumentWaived,
    CreditApproved, CreditRejected,
    Confirmed, Started, Rescheduled, Completed, Cancelled
}
