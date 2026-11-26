using System.Collections.ObjectModel;
using Trackwise.Domain.Core;
using Trackwise.Domain.Entities.FreightForwarding;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.BookingEvents;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.Enums.FreightForwarding.BookingEnums;
using Trackwise.Domain.Primitives.ValueObjects;

namespace Trackwise.Domain.Entities;

// ── Booking number ───────────────────────────────────────────────────────────

public static class BookingNumbering
{
    public const string Prefix = "BK";
    private const string QuotationPrefix = "QT-";
    private const int SequenceWidth = 2;

    public static string FromQuotationReference(string quotationReference, int bookingSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quotationReference);
        ArgumentOutOfRangeException.ThrowIfLessThan(bookingSequence, 1);

        var reference = quotationReference.Trim().ToUpperInvariant();

        if (!reference.StartsWith(QuotationPrefix, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Quotation reference '{quotationReference}' must start with '{QuotationPrefix}'.",
                nameof(quotationReference));

        var body = reference[QuotationPrefix.Length..];
        return $"{Prefix}-{body}-{bookingSequence.ToString($"D{SequenceWidth}")}";
    }

    public static string FromBookingId(Guid bookingId)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("Booking ID is required.", nameof(bookingId));

        return $"{Prefix}-{bookingId:N}".ToUpperInvariant();
    }
}

/// <summary>
/// A confirmed-intent shipment order. Created as a Draft from an Accepted Quotation,
/// carries a locked copy of the quoted charges, and is the source of truth for
/// Trips (operations), Billing (AR invoicing), and Disbursements (AP / OR reconciliation).
/// </summary>
public partial class Booking : AuditableEntity, ITenantEntity
{
    // Header
    public string BookingNumber { get; private set; } = default!;
    public BookingStatus Status { get; private set; }
    public Guid CustomerId { get; private set; }
    public virtual Customer? Customer { get; private set; }
    public string Currency { get; private set; } = default!;
    public string? CustomerReference { get; private set; }
    public string? Notes { get; private set; }

    // Traceability to the originating quotation
    public Guid? QuotationId { get; private set; }
    public virtual Quotation? Quotation { get; private set; }
    public string? QuotationReference { get; private set; }

    public TruckType? TruckType { get; set; }
    public TruckTonnageClass? TonnageClass { get; set; }
    public string ContainerType { get; set; } = default!;

    // Logistics context
    public TransportMode Mode { get; private set; }
    public FreightMode FreightMode { get; private set; }
    public ShipmentType ShipmentType { get; private set; }
    public Incoterm Incoterm { get; private set; }
    public LaneTransit? Lane { get; private set; }
    public Guid? PolId { get; private set; }
    public virtual Terminal? Pol { get; private set; }
    public Guid? PodId { get; private set; }
    public virtual Terminal? Pod { get; private set; }

    public DateTimeOffset? RequestedPickupStart =>
        Lane?.Stops?.FirstOrDefault(s => s.Type == StopType.Pickup)?.WindowStart;

    public DateTimeOffset? RequestedDeliveryEnd =>
        Lane?.Stops?.LastOrDefault(s => s.Type == StopType.Dropoff)?.WindowEnd;

    public CargoSpecification? CargoSpec { get; private set; }
    public decimal? DeclaredCargoValuePhp { get; private set; }

    // Specialized handling
    public TemperatureControl? TemperatureSpec { get; private set; }
    public bool IsTemperatureControlled => TemperatureSpec is not null;
    public bool IsHazardous { get; private set; }
    public bool RequiresCustomsBrokerage { get; private set; }

    // Billing terms
    public int PaymentTermsDays { get; private set; }
    public decimal? DepositRequiredPercent { get; private set; }

    // Credit & approval
    public CreditCheckStatus CreditCheckStatus { get; private set; }
    public string? CreditCheckedBy { get; private set; }
    public DateTimeOffset? CreditCheckedAt { get; private set; }
    public string? CreditCheckNotes { get; private set; }

    // Lifecycle
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public string? ConfirmedBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }

    private readonly List<BookingChargeLine> _charges = [];
    public virtual IReadOnlyCollection<BookingChargeLine> Charges => new ReadOnlyCollection<BookingChargeLine>(_charges);

    private readonly List<BookingEvent> _events = [];
    public virtual IReadOnlyCollection<BookingEvent> Events => new ReadOnlyCollection<BookingEvent>(_events);

    private void Record(BookingEventType type, string? actor, BookingStatus? from = null,
        string? details = null, Guid? relatedId = null) =>
        _events.Add(BookingEvent.Create(Id, type, from, Status, actor, details, relatedId));

    private readonly List<BookingDocument> _documents = [];
    public virtual IReadOnlyCollection<BookingDocument> Documents => new ReadOnlyCollection<BookingDocument>(_documents);

    protected Booking() { }

    // ── Creation from quotation ──────────────────────────────────────────────

    public static Booking CreateDraftFromQuotation(
        Quotation quotation,
        int bookingSequence,
        int paymentTermsDays = 30,
        bool customerCreditCheckRequired = false,
        DateTime? requestedPickupDate = null,
        DateTime? requestedDeliveryDate = null,
        string? notes = null,
        string? createdBy = null)
    {
        ArgumentNullException.ThrowIfNull(quotation);

        if (quotation.Status != QuotationStatus.Accepted)
            throw new InvalidOperationException("A booking can only be created from an Accepted quotation.");

        var bookingId = Guid.NewGuid();
        var booking = new Booking
        {
            Id = bookingId,
            BookingNumber = BookingNumbering.FromQuotationReference(quotation.ReferenceNumber, bookingSequence),
            Status = BookingStatus.Draft,
            CustomerId = quotation.CustomerId,
            Currency = quotation.Currency,
            QuotationId = quotation.Id,
            QuotationReference = quotation.ReferenceNumber,
            TruckType = quotation.Lines.FirstOrDefault()?.TariffRate?.Tariff?.TruckType,
            TonnageClass = quotation.Lines.FirstOrDefault()?.TariffRate?.Tariff?.TonnageClass,
            ContainerType = quotation.Lines.FirstOrDefault()?.TariffRate?.Tariff?.ContainerType?.Name ?? string.Empty,
            Mode = quotation.Mode,
            FreightMode = quotation.FreightMode,
            ShipmentType = quotation.ShipmentType,
            Incoterm = quotation.Incoterm,
            Lane = IsLaneMode(quotation.FreightMode)
                ? ApplyRequestedDates(quotation.Lane, requestedPickupDate, requestedDeliveryDate)
                : null,
            PolId = quotation.PolId,
            PodId = quotation.PodId,
            CargoSpec = quotation.CargoSpec,
            DeclaredCargoValuePhp = quotation.DeclaredCargoValuePhp,
            TemperatureSpec = quotation.TemperatureSpec,
            IsHazardous = quotation.IsHazardous,
            RequiresCustomsBrokerage = quotation.RequiresCustomsBrokerage,
            PaymentTermsDays = paymentTermsDays,
            CreditCheckStatus = customerCreditCheckRequired ? CreditCheckStatus.Pending : CreditCheckStatus.NotRequired,
            Notes = notes ?? quotation.Notes
        };

        foreach (var line in quotation.Lines)
            booking._charges.Add(BookingChargeLine.FromQuotationLine(booking.Id, line));

        booking.DomainEvents.Add(new BookingDraftCreated(booking.Id, quotation.Id, quotation.CustomerId));
        booking.Record(BookingEventType.Created, createdBy,
            details: $"From quotation {quotation.ReferenceNumber}", relatedId: quotation.Id);
        return booking;
    }

    private static bool IsLaneMode(FreightMode freightMode) =>
        freightMode == FreightMode.Reefer || freightMode == FreightMode.Trucking;

    private static LaneTransit? ApplyRequestedDates(
        LaneTransit? lane,
        DateTime? requestedPickupDate,
        DateTime? requestedDeliveryDate)
    {
        if (lane is null || (requestedPickupDate is null && requestedDeliveryDate is null))
            return lane;

        var pickupStart = requestedPickupDate.HasValue
            ? new DateTimeOffset(requestedPickupDate.Value.ToUniversalTime())
            : (DateTimeOffset?)null;
        var deliveryEnd = requestedDeliveryDate.HasValue
            ? new DateTimeOffset(requestedDeliveryDate.Value.ToUniversalTime())
            : (DateTimeOffset?)null;

        var stops = lane.Stops.Select(stop => RouteStop.Create(
            stop.Sequence,
            stop.LocationId,
            stop.Type,
            stop.Type == StopType.Pickup && pickupStart.HasValue ? pickupStart : stop.WindowStart,
            stop.Type == StopType.Dropoff && deliveryEnd.HasValue ? deliveryEnd : stop.WindowEnd,
            stop.Instructions));

        return new LaneTransit(stops, lane.TotalDistanceKm);
    }

    // ── Header / cargo edits (Draft only) ────────────────────────────────────

    public void UpdateHeader(string? customerReference, string? notes, int paymentTermsDays, decimal? depositRequiredPercent, string? updatedBy = null)
    {
        EnsureDraft();

        if (paymentTermsDays < 0)
            throw new ArgumentOutOfRangeException(nameof(paymentTermsDays));
        if (depositRequiredPercent is < 0m or > 100m)
            throw new ArgumentOutOfRangeException(nameof(depositRequiredPercent), "Deposit must be between 0 and 100.");

        CustomerReference = customerReference?.Trim();
        Notes = notes;
        PaymentTermsDays = paymentTermsDays;
        DepositRequiredPercent = depositRequiredPercent;
        Record(BookingEventType.Updated, updatedBy, details: "Header updated");
    }

    public void UpdateLane(LaneTransit? lane, string? updatedBy = null)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(lane);
        Lane = lane;
        DomainEvents.Add(new BookingUpdated(Id));
        Record(BookingEventType.Updated, updatedBy, details: "Route/schedule updated");
    }

    public void Reschedule(LaneTransit newLane, string reason, string requestedBy)
    {
        if (Status is not (BookingStatus.Confirmed or BookingStatus.InProgress))
            throw new InvalidOperationException(
                "Reschedule applies to Confirmed or InProgress bookings. Use UpdateLane while Draft.");

        ArgumentNullException.ThrowIfNull(newLane);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);

        EnsureSameRoute(Lane, newLane);

        var oldPickup = RequestedPickupStart;
        var oldDelivery = RequestedDeliveryEnd;

        var windowsChanged = !Lane!.Stops.OrderBy(s => s.Sequence)
            .Select(s => (s.WindowStart, s.WindowEnd))
            .SequenceEqual(newLane.Stops.OrderBy(s => s.Sequence).Select(s => (s.WindowStart, s.WindowEnd)));

        if (!windowsChanged) return;

        Lane = newLane;

        DomainEvents.Add(new BookingRescheduled(
            Id, oldPickup, RequestedPickupStart, oldDelivery, RequestedDeliveryEnd, reason, requestedBy));
        Record(BookingEventType.Rescheduled, requestedBy, details: reason);
    }

    private static void EnsureSameRoute(LaneTransit? current, LaneTransit? proposed)
    {
        if (current is null || proposed is null)
            throw new InvalidOperationException(
                "Reschedule requires both the booking and the proposed lane to be set. Stops, order, and locations must remain the same.");

        var a = current.Stops.OrderBy(s => s.Sequence).ToList();
        var b = proposed.Stops.OrderBy(s => s.Sequence).ToList();

        var same = a.Count == b.Count &&
                   a.Zip(b).All(p => p.First.LocationId == p.Second.LocationId &&
                                     p.First.Type == p.Second.Type &&
                                     p.First.Sequence == p.Second.Sequence);

        if (!same)
            throw new InvalidOperationException(
                "Reschedule cannot change the route. Stops, order, and locations must remain the same.");
    }

    public void UpdateCargo(CargoSpecification cargoSpec, string? updatedBy = null)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(cargoSpec);
        CargoSpec = cargoSpec;
        DomainEvents.Add(new BookingUpdated(Id));
        Record(BookingEventType.Updated, updatedBy, details: "Cargo updated");
    }

    // ── Charges (Selling / Billing & Buying / OR Reconciliation) ─────────────

    public BookingChargeLine AddAdditionalCharge(
        ChargeCode ChargeCode,
        decimal quantity,
        decimal unitBuying,
        decimal unitSelling,
        string reason,
        string? addedBy = null)
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
            throw new InvalidOperationException($"Charges cannot be added to a {Status} booking.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var charge = BookingChargeLine.CreateAdditional(Id, ChargeCode, quantity, unitBuying, unitSelling, Currency, reason);
        _charges.Add(charge);
        DomainEvents.Add(new BookingChargeAdded(Id, charge.Id));
        Record(BookingEventType.ChargeAdded, addedBy,
            details: $"{charge.Description}: {reason.Trim()}", relatedId: charge.Id);
        return charge;
    }

    public void RemoveAdditionalCharge(Guid chargeId, string? removedBy = null)
        => RemoveAdditionalCharge(chargeId, allowInvoicedDraft: false, removedBy);

    public void RemoveAdditionalCharge(Guid chargeId, bool allowInvoicedDraft, string? removedBy = null)
    {
        var charge = _charges.FirstOrDefault(c => c.Id == chargeId);
        if (charge is null) return;

        if (charge.Source == BookingChargeSource.Quoted)
            throw new InvalidOperationException("Quoted charges are locked and cannot be removed.");
        if (charge.BillingStatus == BillingStatus.Invoiced && !allowInvoicedDraft)
            throw new InvalidOperationException("An invoiced charge cannot be removed.");
        if (charge.BuyingStatus is BookingBuyingStatus.Requested or BookingBuyingStatus.Verified)
            throw new InvalidOperationException("A charge with an active expense request or verified Official Receipt cannot be removed.");

        _charges.Remove(charge);
        DomainEvents.Add(new BookingChargeRemoved(Id, chargeId));
        Record(BookingEventType.ChargeRemoved, removedBy, details: charge.Description, relatedId: chargeId);
    }

    public void UpdateAdditionalCharge(
        Guid chargeId,
        decimal quantity,
        decimal unitBuying,
        decimal unitSelling,
        string reason,
        bool allowInvoicedDraft,
        string? updatedBy = null)
    {
        var charge = _charges.FirstOrDefault(c => c.Id == chargeId)
            ?? throw new InvalidOperationException($"Charge '{chargeId}' not found on this booking.");

        if (charge.Source == BookingChargeSource.Quoted)
            throw new InvalidOperationException("Quoted charges are locked and cannot be changed.");
        if (charge.BillingStatus == BillingStatus.Invoiced && !allowInvoicedDraft)
            throw new InvalidOperationException("An invoiced charge cannot be changed.");
        if (charge.BuyingStatus is BookingBuyingStatus.Requested or BookingBuyingStatus.Verified)
            throw new InvalidOperationException("A charge with an active expense request or verified Official Receipt cannot be edited; reconcile its actual buying instead.");

        charge.Update(quantity, unitBuying, unitSelling, reason);
        DomainEvents.Add(new BookingChargeUpdated(Id, chargeId));
        Record(BookingEventType.ChargeUpdated, updatedBy,
            details: $"{charge.Description}: {reason.Trim()}", relatedId: chargeId);
    }

    public BookingTotals GetTotals() =>
        new(
            Currency,
            Buying: _charges.Sum(c => c.BuyingAmount),
            Selling: _charges.Sum(c => c.SellingAmount),
            EstimatedBuying: _charges.Sum(c => c.EstimatedBuyingAmount));

    public IReadOnlyList<BookingChargeLine> GetUnbilledCharges() =>
        _charges.Where(c => c.BillingStatus == BillingStatus.Unbilled && c.IsBillable).ToList();

    public IReadOnlyList<BookingChargeLine> GetUnrequestedBuyingCharges() =>
        _charges.Where(c => c.BuyingStatus == BookingBuyingStatus.Estimated).ToList();

    public IReadOnlyList<BookingChargeLine> GetPendingReceiptCharges() =>
        _charges.Where(c => !c.IsBuyingSatisfied).ToList();

    // ── Selling / AR Invoicing Handoff ───────────────────────────────────────

    public void MarkChargesInvoiced(Guid invoiceId, IEnumerable<Guid> chargeIds, string? actor = null)
    {
        var ids = chargeIds.ToHashSet();
        var count = 0;
        foreach (var charge in _charges.Where(c => ids.Contains(c.Id)))
        {
            charge.MarkInvoiced(invoiceId);
            count++;
        }

        if (count > 0)
            Record(BookingEventType.ChargesInvoiced, actor, details: $"{count} charge(s) invoiced", relatedId: invoiceId);
    }

    public void ReleaseChargesFromInvoice(Guid invoiceId, string? actor = null)
    {
        var count = 0;
        foreach (var charge in _charges.Where(c => c.InvoiceId == invoiceId))
        {
            charge.MarkUnbilled(invoiceId);
            count++;
        }

        if (count > 0)
            Record(BookingEventType.ChargesReleased, actor, details: $"{count} charge(s) released", relatedId: invoiceId);
    }

    // ── Buying / AP Disbursement & Official Receipt Reconciliation ───────────

    /// <summary>
    /// Called when one or more charges are bundled into an RFP, Cash Advance, or Petty Cash request.
    /// Prevents duplicate disbursement requests on the same charge line.
    /// </summary>
    public void MarkChargesExpenseRequested(
        Guid expenseRequestId,
        string? disbursementReference,
        IEnumerable<Guid> chargeIds,
        string? actor = null)
    {
        EnsureNotClosed();
        if (expenseRequestId == Guid.Empty)
            throw new ArgumentException("Expense request ID is required.", nameof(expenseRequestId));

        var ids = chargeIds.ToHashSet();
        var matched = _charges.Where(c => ids.Contains(c.Id)).ToList();

        if (matched.Count != ids.Count)
            throw new InvalidOperationException("One or more selected charges do not belong to this booking.");

        foreach (var charge in matched)
            charge.MarkExpenseRequested(expenseRequestId, disbursementReference);

        if (matched.Count > 0)
        {
            var refLabel = string.IsNullOrWhiteSpace(disbursementReference) ? expenseRequestId.ToString() : disbursementReference.Trim();
            Record(BookingEventType.ChargesExpenseRequested, actor,
                details: $"{matched.Count} charge(s) linked to {refLabel}", relatedId: expenseRequestId);
        }
    }

    /// <summary>
    /// Releases charges back to Estimated if an RFP, Cash Advance, or Petty Cash request is rejected or cancelled.
    /// </summary>
    public void ReleaseChargesFromExpenseRequest(Guid expenseRequestId, string? actor = null)
    {
        var count = 0;
        foreach (var charge in _charges.Where(c => c.ExpenseRequestId == expenseRequestId && c.BuyingStatus == BookingBuyingStatus.Requested))
        {
            charge.ReleaseExpenseRequest(expenseRequestId);
            count++;
        }

        if (count > 0)
            Record(BookingEventType.ChargesExpenseReleased, actor,
                details: $"{count} charge(s) released from cancelled/rejected expense request", relatedId: expenseRequestId);
    }

    /// <summary>
    /// Reconciles actual buying amounts against a vendor Official Receipt (OR) or Sales Invoice (SI).
    /// Supports bulk reconciliation where a single OR covers multiple booking charges.
    /// </summary>
    public void ReconcileChargesBuying(
        IReadOnlyCollection<ChargeBuyingAllocation> allocations,
        string officialReceiptNumber,
        string reconciledBy,
        Guid? expenseRequestId = null)
    {
        EnsureNotClosed();
        ArgumentNullException.ThrowIfNull(allocations);
        if (allocations.Count == 0)
            throw new ArgumentException("At least one charge allocation is required.", nameof(allocations));
        ArgumentException.ThrowIfNullOrWhiteSpace(officialReceiptNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciledBy);

        var normalizedOr = officialReceiptNumber.Trim();

        foreach (var item in allocations)
        {
            var charge = _charges.FirstOrDefault(c => c.Id == item.ChargeId)
                ?? throw new InvalidOperationException($"Charge '{item.ChargeId}' not found on booking '{BookingNumber}'.");

            charge.ReconcileBuying(item.ActualBuyingAmount, normalizedOr, reconciledBy, expenseRequestId);
        }

        var totalActual = allocations.Sum(a => a.ActualBuyingAmount);
        DomainEvents.Add(new BookingChargesReconciled(Id, normalizedOr, allocations.Select(a => a.ChargeId).ToList(), totalActual));
        Record(BookingEventType.ChargesBuyingReconciled, reconciledBy,
            details: $"OR #{normalizedOr} reconciled across {allocations.Count} charge(s) ({totalActual:N2} {Currency})",
            relatedId: expenseRequestId);
    }

    // ── Documents ────────────────────────────────────────────────────────────

    public BookingDocument AddDocument(BookingDocumentType type, string? reference = null, DateTimeOffset? expiresAt = null, string? addedBy = null)
    {
        EnsureNotClosed();

        var doc = BookingDocument.Create(Id, type, BookingDocumentStatus.Submitted, reference, expiresAt);
        _documents.Add(doc);
        Record(BookingEventType.DocumentAdded, addedBy, details: type.ToString(), relatedId: doc.Id);
        return doc;
    }

    public void VerifyDocument(Guid documentId, string verifiedBy)
    {
        EnsureNotClosed();
        var doc = GetDocument(documentId);
        doc.Verify(verifiedBy);
        DomainEvents.Add(new BookingDocumentVerified(Id, documentId));
        Record(BookingEventType.DocumentVerified, verifiedBy, details: doc.Type.ToString(), relatedId: documentId);
    }

    public void RejectDocument(Guid documentId, string rejectedBy, string reason)
    {
        EnsureNotClosed();
        var doc = GetDocument(documentId);
        doc.Reject(rejectedBy, reason);
        Record(BookingEventType.DocumentRejected, rejectedBy, details: $"{doc.Type}: {reason}", relatedId: documentId);
    }

    public void WaiveDocument(Guid documentId, string waivedBy, string reason)
    {
        EnsureNotClosed();
        var doc = GetDocument(documentId);
        doc.Waive(waivedBy, reason);
        Record(BookingEventType.DocumentWaived, waivedBy, details: $"{doc.Type}: {reason}", relatedId: documentId);
    }

    private BookingDocument GetDocument(Guid id) =>
        _documents.FirstOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"Document '{id}' not found on booking '{BookingNumber}'.");

    private void GenerateDocumentRequirements()
    {
        Require(BookingDocumentType.Kyc);
        Require(BookingDocumentType.CommercialInvoice);
        Require(BookingDocumentType.PackingList);

        if (FreightMode == FreightMode.Import) Require(BookingDocumentType.ImportPermit);
        if (FreightMode == FreightMode.Export) Require(BookingDocumentType.ExportPermit);
        if (IsHazardous) Require(BookingDocumentType.DangerousGoodsDeclaration);
        if (IsTemperatureControlled) Require(BookingDocumentType.ColdChainCertificate);
        if (DeclaredCargoValuePhp > Quotation.HighValueCargoThresholdPhp) Require(BookingDocumentType.InsuranceCertificate);

        void Require(BookingDocumentType type) =>
            _documents.Add(BookingDocument.Create(Id, type, BookingDocumentStatus.Required));
    }

    // ── Credit check ─────────────────────────────────────────────────────────

    public void ApproveCredit(string checkedBy, string? notes = null)
    {
        EnsureNotClosed();
        CreditCheckStatus = CreditCheckStatus.Approved;
        CreditCheckedBy = checkedBy;
        CreditCheckedAt = DateTimeOffset.UtcNow;
        CreditCheckNotes = notes;
        Record(BookingEventType.CreditApproved, checkedBy, details: notes);
    }

    public void RejectCredit(string checkedBy, string reason)
    {
        EnsureNotClosed();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        CreditCheckStatus = CreditCheckStatus.Rejected;
        CreditCheckedBy = checkedBy;
        CreditCheckedAt = DateTimeOffset.UtcNow;
        CreditCheckNotes = reason;
        Record(BookingEventType.CreditRejected, checkedBy, details: reason);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public IReadOnlyList<string> GetConfirmationBlockers()
    {
        var blockers = new List<string>();

        if (_charges.Count == 0)
            blockers.Add("Booking has no charges.");

        if (CreditCheckStatus is CreditCheckStatus.Pending)
            blockers.Add("Credit check is pending.");
        else if (CreditCheckStatus is CreditCheckStatus.Rejected)
            blockers.Add("Credit check was rejected.");

        foreach (var d in _documents.Where(d => !d.IsSatisfied))
            blockers.Add($"Document '{d.Type}' is {d.Status}.");

        return blockers;
    }

    /// <summary>
    /// Returns the reasons this booking cannot be completed yet (every charge with Buying > 0 must have a verified OR).
    /// </summary>
    public IReadOnlyList<string> GetCompletionBlockers()
    {
        var blockers = new List<string>();

        foreach (var c in _charges.Where(c => !c.IsBuyingSatisfied))
        {
            blockers.Add($"Charge '{c.Description}' ({c.EstimatedBuyingAmount:N2} {c.Currency}) is missing a verified Official Receipt.");
        }

        return blockers;
    }

    public void Confirm(string confirmedBy)
    {
        if (Status != BookingStatus.Draft)
            throw new InvalidOperationException("Only Draft bookings can be confirmed.");

        var blockers = GetConfirmationBlockers();
        if (blockers.Count > 0)
            throw new InvalidOperationException("Booking cannot be confirmed: " + string.Join(" ", blockers));

        var from = Status;
        Status = BookingStatus.Confirmed;
        ConfirmedAt = DateTimeOffset.UtcNow;
        ConfirmedBy = confirmedBy;

        DomainEvents.Add(new BookingConfirmed(Id, CustomerId, QuotationId, BookingNumber));
        Record(BookingEventType.Confirmed, confirmedBy, from);
    }

    public void MarkInProgress(string? startedBy = null)
    {
        if (Status == BookingStatus.InProgress) return;
        if (Status != BookingStatus.Confirmed)
            throw new InvalidOperationException("Only Confirmed bookings can move to InProgress.");

        var from = Status;
        Status = BookingStatus.InProgress;
        DomainEvents.Add(new BookingStarted(Id));
        Record(BookingEventType.Started, startedBy, from);
    }

    public void Complete(string? completedBy = null)
    {
        var expectedStatus = FreightMode is FreightMode.Reefer or FreightMode.Trucking
            ? BookingStatus.InProgress
            : BookingStatus.Confirmed;

        if (Status != expectedStatus)
            throw new InvalidOperationException($"Only {expectedStatus} bookings can be completed.");

        var completionBlockers = GetCompletionBlockers();
        if (completionBlockers.Count > 0)
            throw new InvalidOperationException("Booking cannot be completed: " + string.Join(" ", completionBlockers));

        var from = Status;
        Status = BookingStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        DomainEvents.Add(new BookingCompleted(Id, CustomerId));
        Record(BookingEventType.Completed, completedBy, from);
    }

    public void Cancel(string cancelledBy, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
            throw new InvalidOperationException($"A {Status} booking cannot be cancelled.");

        if (Status == BookingStatus.InProgress)
            throw new InvalidOperationException("An in-progress booking cannot be cancelled. Cancel its trips first.");

        var wasConfirmed = Status == BookingStatus.Confirmed;
        var from = Status;

        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancelledBy = cancelledBy;
        CancellationReason = reason;

        DomainEvents.Add(new BookingCancelled(Id, reason, wasConfirmed));
        Record(BookingEventType.Cancelled, cancelledBy, from, reason);
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    private void EnsureDraft()
    {
        if (Status != BookingStatus.Draft)
            throw new InvalidOperationException("This change is only allowed while the booking is in Draft status.");
    }

    private void EnsureNotClosed()
    {
        if (Status is BookingStatus.Completed or BookingStatus.Cancelled)
            throw new InvalidOperationException($"Booking is {Status}.");
    }
}

// ── Child entity: charge line (Billing & AP Reconciliation handoff) ──────────

public class BookingChargeLine : AuditableEntity, ITenantEntity
{
    public Guid BookingId { get; private set; }
    public virtual Booking? Booking { get; private set; }

    public BookingChargeSource Source { get; private set; }

    // Traceability (null for additional charges)
    public Guid? QuotationLineId { get; private set; }
    public Guid? TariffRateId { get; private set; }

    public Guid? ChargeCodeId { get; set; }
    public virtual ChargeCode? ChargeCode { get; private set; }
    public ChargeCategory? Category { get; private set; }
    public OperationalServiceType ServiceType { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Quantity { get; private set; }

    public string Currency { get; private set; } = default!;

    /// <summary>Locked initial buying estimate copied from the quotation or additional charge creation.</summary>
    public decimal EstimatedBuyingAmount { get; private set; }

    /// <summary>Reconciled actual vendor cost from the Official Receipt / Sales Invoice (null until verified).</summary>
    public decimal? ActualBuyingAmount { get; private set; }

    /// <summary>
    /// Effective buying cost used across totals, margin queries, and existing projections:
    /// equals <see cref="ActualBuyingAmount"/> once reconciled, otherwise <see cref="EstimatedBuyingAmount"/>.
    /// </summary>
    public decimal BuyingAmount { get; private set; }

    /// <summary>What the customer pays (AR).</summary>
    public decimal SellingAmount { get; private set; }

    /// <summary>Positive = cost overrun against estimate; Negative = cost savings against estimate.</summary>
    public decimal CostVariance => BuyingAmount - EstimatedBuyingAmount;

    public bool IsBillable { get; private set; } = true;
    public string? Reason { get; private set; }

    // Selling / AR status
    public BillingStatus BillingStatus { get; private set; }
    public Guid? InvoiceId { get; private set; }

    // Buying / AP & Official Receipt reconciliation status
    public BookingBuyingStatus BuyingStatus { get; private set; }
    public Guid? ExpenseRequestId { get; private set; }
    public string? DisbursementReference { get; private set; }
    public string? OfficialReceiptNumber { get; private set; }
    public DateTimeOffset? ReconciledAt { get; private set; }
    public string? ReconciledBy { get; private set; }

    /// <summary>True when no vendor OR is required (0 buying / discount) or when an OR has been verified.</summary>
    public bool IsBuyingSatisfied =>
        BuyingStatus is BookingBuyingStatus.NotRequired or BookingBuyingStatus.Verified;

    protected BookingChargeLine() { }

    internal static BookingChargeLine FromQuotationLine(Guid bookingId, QuotationLine line)
    {
        if (line.SnapshotSellingAmount is null || line.SnapshotBuyingAmount is null || line.Currency is null)
            throw new InvalidOperationException(
                $"Quotation line '{line.Id}' has no issued price snapshot; the quotation must be Issued before booking.");

        var estimatedBuying = line.SnapshotBuyingAmount.Value;

        return new BookingChargeLine
        {
            BookingId = bookingId,
            Source = BookingChargeSource.Quoted,
            QuotationLineId = line.Id,
            TariffRateId = line.TariffRateId,
            ChargeCodeId = line.TariffRate?.ChargeCodeId,
            Category = line.Category,
            ServiceType = line.ServiceType,
            Description = $"{line.TariffRate?.ChargeCode?.Code}",
            Reason = $"{line.TariffRate?.Tariff?.OriginLane} - {line.TariffRate?.Tariff?.DestinationLane}",
            Quantity = line.Quantity,
            Currency = line.Currency,
            EstimatedBuyingAmount = estimatedBuying,
            ActualBuyingAmount = null,
            BuyingAmount = estimatedBuying,
            SellingAmount = line.SnapshotSellingAmount.Value,
            BillingStatus = BillingStatus.Unbilled,
            BuyingStatus = estimatedBuying > 0m ? BookingBuyingStatus.Estimated : BookingBuyingStatus.NotRequired
        };
    }

    internal static BookingChargeLine CreateAdditional(
        Guid bookingId, ChargeCode chargeCode,
        decimal quantity, decimal unitBuying, decimal unitSelling, string currency, string reason)
    {
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitBuying < 0m || unitSelling < 0m) throw new ArgumentOutOfRangeException(nameof(unitSelling), "Unit amounts cannot be negative.");

        var sign = chargeCode.Category == ChargeCategory.Discount ? -1m : 1m;
        var estimatedBuying = sign * unitBuying * quantity;

        return new BookingChargeLine
        {
            BookingId = bookingId,
            Source = BookingChargeSource.Additional,
            Category = chargeCode.Category,
            ChargeCodeId = chargeCode.Id,
            ServiceType = default,
            Description = $"{chargeCode.Code}-{chargeCode.Name}",
            Quantity = quantity,
            Currency = currency,
            EstimatedBuyingAmount = estimatedBuying,
            ActualBuyingAmount = null,
            BuyingAmount = estimatedBuying,
            SellingAmount = sign * unitSelling * quantity,
            Reason = reason,
            BillingStatus = BillingStatus.Unbilled,
            BuyingStatus = estimatedBuying > 0m ? BookingBuyingStatus.Estimated : BookingBuyingStatus.NotRequired
        };
    }

    internal void MarkInvoiced(Guid invoiceId)
    {
        if (BillingStatus == BillingStatus.Invoiced)
            throw new InvalidOperationException($"Charge '{Id}' is already invoiced.");

        BillingStatus = BillingStatus.Invoiced;
        InvoiceId = invoiceId;
    }

    internal void MarkUnbilled(Guid invoiceId)
    {
        if (BillingStatus != BillingStatus.Invoiced || InvoiceId != invoiceId)
            throw new InvalidOperationException($"Charge '{Id}' is not assigned to invoice '{invoiceId}'.");

        BillingStatus = BillingStatus.Unbilled;
        InvoiceId = null;
    }

    internal void MarkExpenseRequested(Guid expenseRequestId, string? disbursementReference)
    {
        if (BuyingStatus == BookingBuyingStatus.NotRequired)
            throw new InvalidOperationException($"Charge '{Description}' has no estimated buying cost and does not require disbursement.");
        if (BuyingStatus == BookingBuyingStatus.Requested)
            throw new InvalidOperationException($"Charge '{Description}' is already linked to an active expense request ({DisbursementReference ?? ExpenseRequestId.ToString()}).");
        if (BuyingStatus == BookingBuyingStatus.Verified)
            throw new InvalidOperationException($"Charge '{Description}' is already reconciled with OR #{OfficialReceiptNumber}.");

        BuyingStatus = BookingBuyingStatus.Requested;
        ExpenseRequestId = expenseRequestId;
        DisbursementReference = disbursementReference?.Trim();
    }

    internal void ReleaseExpenseRequest(Guid expenseRequestId)
    {
        if (BuyingStatus != BookingBuyingStatus.Requested || ExpenseRequestId != expenseRequestId)
            throw new InvalidOperationException($"Charge '{Id}' is not assigned to pending expense request '{expenseRequestId}'.");

        BuyingStatus = EstimatedBuyingAmount > 0m ? BookingBuyingStatus.Estimated : BookingBuyingStatus.NotRequired;
        ExpenseRequestId = null;
        DisbursementReference = null;
    }

    internal void ReconcileBuying(
        decimal actualBuyingAmount,
        string officialReceiptNumber,
        string reconciledBy,
        Guid? expenseRequestId = null)
    {
        if (Category == ChargeCategory.Discount)
            throw new InvalidOperationException("Discount lines cannot be reconciled against a vendor Official Receipt.");
        if (actualBuyingAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(actualBuyingAmount), "Actual buying amount cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(officialReceiptNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(reconciledBy);

        if (expenseRequestId.HasValue && ExpenseRequestId.HasValue && ExpenseRequestId != expenseRequestId)
            throw new InvalidOperationException($"Charge '{Description}' belongs to a different expense request ('{ExpenseRequestId}').");

        ActualBuyingAmount = actualBuyingAmount;
        BuyingAmount = actualBuyingAmount;
        OfficialReceiptNumber = officialReceiptNumber.Trim();
        ReconciledAt = DateTimeOffset.UtcNow;
        ReconciledBy = reconciledBy.Trim();
        if (expenseRequestId.HasValue)
            ExpenseRequestId = expenseRequestId;

        BuyingStatus = BookingBuyingStatus.Verified;
    }

    internal void Update(decimal quantity, decimal unitBuying, decimal unitSelling, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitBuying < 0m || unitSelling < 0m) throw new ArgumentOutOfRangeException(nameof(unitSelling));

        var sign = Category == ChargeCategory.Discount ? -1m : 1m;
        var estimatedBuying = sign * unitBuying * quantity;

        Quantity = quantity;
        EstimatedBuyingAmount = estimatedBuying;
        BuyingAmount = ActualBuyingAmount ?? estimatedBuying;
        SellingAmount = sign * unitSelling * quantity;
        Reason = reason.Trim();

        if (BuyingStatus is BookingBuyingStatus.Estimated or BookingBuyingStatus.NotRequired)
            BuyingStatus = estimatedBuying > 0m ? BookingBuyingStatus.Estimated : BookingBuyingStatus.NotRequired;
    }
}

public record ChargeBuyingAllocation(Guid ChargeId, decimal ActualBuyingAmount);

// ── Child entity: document / compliance requirement ──────────────────────────

public class BookingDocument : AuditableEntity, ITenantEntity
{
    public Guid BookingId { get; private set; }
    public BookingDocumentType Type { get; private set; }
    public BookingDocumentStatus Status { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }

    public bool IsSatisfied =>
        Status is BookingDocumentStatus.Verified or BookingDocumentStatus.Waived &&
        (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    protected BookingDocument() { }

    internal static BookingDocument Create(
        Guid bookingId, BookingDocumentType type, BookingDocumentStatus status,
        string? reference = null, DateTimeOffset? expiresAt = null) =>
        new()
        {
            BookingId = bookingId,
            Type = type,
            Status = status,
            Reference = reference,
            ExpiresAt = expiresAt
        };

    internal void Verify(string by)
    {
        if (Status == BookingDocumentStatus.Required)
            throw new InvalidOperationException($"Document '{Type}' has not been submitted yet.");

        Status = BookingDocumentStatus.Verified;
        ReviewedBy = by;
        ReviewedAt = DateTimeOffset.UtcNow;
    }

    internal void Reject(string by, string reason)
    {
        Status = BookingDocumentStatus.Rejected;
        ReviewedBy = by;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewNotes = reason;
    }

    internal void Waive(string by, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = BookingDocumentStatus.Waived;
        ReviewedBy = by;
        ReviewedAt = DateTimeOffset.UtcNow;
        ReviewNotes = reason;
    }
}

public record BookingTotals(
    string Currency,
    decimal Buying,
    decimal Selling,
    decimal EstimatedBuying = 0m)
{
    public decimal Margin => Selling - Buying;
    public decimal MarginPercent => Selling == 0m ? 0m : Math.Round(Margin / Selling * 100m, 2);
    public decimal CostVariance => Buying - EstimatedBuying;
}

// ── Domain events ────────────────────────────────────────────────────────────

public record BookingDraftCreated(Guid Id, Guid QuotationId, Guid CustomerId) : IDomainEvent;
public record BookingUpdated(Guid Id) : IDomainEvent;
public record BookingChargeAdded(Guid Id, Guid ChargeId) : IDomainEvent;
public record BookingChargeUpdated(Guid Id, Guid ChargeId) : IDomainEvent;
public record BookingChargeRemoved(Guid Id, Guid ChargeId) : IDomainEvent;
public record BookingDocumentVerified(Guid Id, Guid DocumentId) : IDomainEvent;
public record BookingConfirmed(Guid Id, Guid CustomerId, Guid? QuotationId, string BookingNumber) : IDomainEvent;
public record BookingStarted(Guid Id) : IDomainEvent;
public record BookingCompleted(Guid Id, Guid CustomerId) : IDomainEvent;
public record BookingCancelled(Guid Id, string Reason, bool WasConfirmed) : IDomainEvent;
public record BookingChargesReconciled(
    Guid Id,
    string OfficialReceiptNumber,
    IReadOnlyList<Guid> ChargeIds,
    decimal TotalActualBuying) : IDomainEvent;
public record BookingRescheduled(
    Guid Id,
    DateTimeOffset? OldPickupStart, DateTimeOffset? NewPickupStart,
    DateTimeOffset? OldDeliveryEnd, DateTimeOffset? NewDeliveryEnd,
    string Reason, string RequestedBy) : IDomainEvent;
