using Trackwise.Application.Modules.Financial.Invoices.Commands.BookingInvoiceCommands;
using Trackwise.Application.Tests.TestKit;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.Enums.FreightForwarding.BookingEnums;
using Trackwise.Domain.Primitives.ValueObjects;
using Xunit;

namespace Trackwise.Application.Tests.Financial;

public sealed class BookingInvoiceLifecycleTests
{
    #region Invoice status transitions

    [Theory]
    [InlineData(InvoiceStatus.Draft, true)]
    [InlineData(InvoiceStatus.PendingApproval, true)]
    [InlineData(InvoiceStatus.Rejected, true)]
    [InlineData(InvoiceStatus.Approved, false)]
    [InlineData(InvoiceStatus.Issued, false)]
    [InlineData(InvoiceStatus.Sent, false)]
    [InlineData(InvoiceStatus.PartiallyPaid, false)]
    [InlineData(InvoiceStatus.Overdue, false)]
    [InlineData(InvoiceStatus.Paid, false)]
    [InlineData(InvoiceStatus.Cancelled, false)]
    public void CanMove_ToCancelled_OnlyBeforeApproval(InvoiceStatus current, bool expected)
    {
        Assert.Equal(expected, BookingInvoiceLifecycle.CanMove(current, InvoiceStatus.Cancelled));
    }

    #endregion

    #region Releasing charges from an invoice

    [Fact]
    public void ReleaseChargesFromInvoice_ReleasesOnlyThatInvoicesCharges_AndLeavesBookingStatusAlone()
    {
        var booking = NewDraftBooking();
        var released = AddExtraCharge(booking, unitBuying: 100m, unitSelling: 150m, reason: "Late pickup");
        var retained = AddExtraCharge(booking, unitBuying: 50m, unitSelling: 75m, reason: "Customer request");
        var cancelledInvoiceId = Guid.NewGuid();
        var activeInvoiceId = Guid.NewGuid();

        booking.MarkChargesInvoiced(cancelledInvoiceId, [released.Id]);
        booking.MarkChargesInvoiced(activeInvoiceId, [retained.Id]);

        booking.ReleaseChargesFromInvoice(cancelledInvoiceId);

        Assert.Equal(BillingStatus.Unbilled, released.BillingStatus);
        Assert.Null(released.InvoiceId);
        Assert.Equal(BillingStatus.Invoiced, retained.BillingStatus);
        Assert.Equal(activeInvoiceId, retained.InvoiceId);
        Assert.Equal(BookingStatus.Draft, booking.Status);
    }

    [Fact]
    public void ReleaseChargesFromInvoice_ReleasedChargesBecomeBillableAgain()
    {
        var booking = NewDraftBooking();
        var charge = AddExtraCharge(booking);
        var invoiceId = Guid.NewGuid();
        booking.MarkChargesInvoiced(invoiceId, [charge.Id]);

        booking.ReleaseChargesFromInvoice(invoiceId);

        Assert.Contains(charge, booking.GetUnbilledCharges());
    }

    [Fact]
    public void ReleaseChargesFromInvoice_UnknownInvoice_IsNoOp()
    {
        var booking = NewDraftBooking();
        var invoiceId = Guid.NewGuid();
        var charge = AddExtraCharge(booking);
        booking.MarkChargesInvoiced(invoiceId, [charge.Id]);

        booking.ReleaseChargesFromInvoice(Guid.NewGuid());

        Assert.Equal(BillingStatus.Invoiced, charge.BillingStatus);
        Assert.Equal(invoiceId, charge.InvoiceId);
    }

    #endregion

    #region Helpers

    /// <summary>Draft booking from an accepted quotation. These tests only care about billing state, not the route or cargo.</summary>
    private static Booking NewDraftBooking() =>
        Booking.CreateDraftFromQuotation(TestQuotations.Accepted(NewQuotation()), 1);

    private static Quotation NewQuotation()
    {
        var lane = LaneTransit.PointToPoint(
            originLocationId: Guid.NewGuid(),
            destinationLocationId: Guid.NewGuid(),
            pickupStart: DateTimeOffset.UtcNow,
            deliveryEnd: DateTimeOffset.UtcNow.AddDays(1),
            distanceKm: 100m);

        var cargo = CargoSpecification.Create(
            commodity: "General Cargo",
            totalWeightKg: 1000m,
            palletCount: 2,
            declaredCargoValue: new Money { Amount = 5000m, Currency = "USD" },
            requiresActiveRefrigeration: false);

        return Quotation.Create(
            customerId: Guid.NewGuid(),
            branchId: Guid.NewGuid(),
            referenceNumber: "QT-26-BILL-00001",
            currency: "USD",
            mode: TransportMode.Road,
            freightMode: FreightMode.Trucking,
            shipmentType: ShipmentType.FTL,
            incoterm: Incoterm.DAP,
            lane: lane,
            cargoSpec: cargo);
    }

    private static ChargeCode NewChargeCode(
        ChargeCategory category = ChargeCategory.Accessorial,
        string code = "ACC") =>
        ChargeCode.Create(
            code: code,
            name: $"{code} charge",
            category: category,
            modes: [FreightMode.Trucking],
            defaultUnit: "per shipment");

    private static BookingChargeLine AddExtraCharge(
        Booking booking,
        decimal quantity = 1m,
        decimal unitBuying = 10m,
        decimal unitSelling = 15m,
        string reason = "Reason") =>
        booking.AddAdditionalCharge(NewChargeCode(), quantity, unitBuying, unitSelling, reason);

    #endregion
}
