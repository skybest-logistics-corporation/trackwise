using System;
using System.Collections.Generic;
using System.Text;

namespace Trackwise.Domain.Primitives.Enums.FreightForwarding.BookingEnums;

public enum BookingBuyingStatus
{
    /// <summary>Zero or negative buying (e.g., discounts, percentage charges, internal detention). No OR needed.</summary>
    NotRequired = 0,

    /// <summary>Estimated buying > 0 copied from quotation or added manually; awaiting disbursement & OR.</summary>
    Estimated = 1,

    /// <summary>Included in an RFP, Cash Advance, or Petty Cash request; awaiting vendor OR reconciliation.</summary>
    Requested = 2,

    /// <summary>Reconciled against an Official Receipt / Sales Invoice with ActualBuyingAmount locked.</summary>
    Verified = 3
}
