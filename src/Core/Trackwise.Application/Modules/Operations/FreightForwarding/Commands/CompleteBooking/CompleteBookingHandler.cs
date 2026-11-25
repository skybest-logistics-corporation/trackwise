using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding.BookingEnums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands;

internal sealed class CompleteBookingHandler(ITenantUnitOfWork tenantUow, ICurrentUserService currentUserService)
    : IAppRequestHandler<CompleteBookingCommand, Result>
{
    public async Task<Result> Handle(CompleteBookingCommand req, CancellationToken ct)
    {
        var booking = await tenantUow.Repository<Booking>().GetByIdAsync(req.Id, ct);
        if (booking is null)
        {
            return Result.Fail($"Booking with ID '{req.Id}' not found.");
        }

        var expectedStatus = booking.FreightMode is FreightMode.Reefer or FreightMode.Trucking
            ? BookingStatus.InProgress
            : BookingStatus.Confirmed;

        if (booking.Status != expectedStatus)
        {
            return Result.Fail($"Only {expectedStatus} bookings can be completed.");
        }

        booking.Complete(currentUserService.GetUserName());

        tenantUow.Repository<Booking>().Update(booking);
        await tenantUow.SaveChangesAsync(ct);

        return Result.Ok();
    }
}
