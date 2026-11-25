using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Application.Abstractions;
using Trackwise.Application.Abstractions.CurrentUser;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Persistence;
using Trackwise.Mappings;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.BookingCommands.AddBookingCharge;

internal sealed class AddBookingChargeHandler(ITenantUnitOfWork tenantUow, ICurrentUserService currentUserService)
    : IAppRequestHandler<AddBookingChargeCommand, Result<BookingChargeDto>>
{
    public async Task<Result<BookingChargeDto>> Handle(AddBookingChargeCommand req, CancellationToken ct)
    {
        // Note: .Include(b => b.Charges) can be removed if AddAdditionalCharge does not need existing charges
        var booking = await tenantUow.Repository<Booking>().Query()
            .FirstOrDefaultAsync(b => b.Id == req.BookingId, ct);

        if (booking is null)
            return Result<BookingChargeDto>.Fail($"Booking with ID '{req.BookingId}' not found.", ErrorCodes.NotFound);

        var chargeCode = await tenantUow.Repository<ChargeCode>().Query()
            .FirstOrDefaultAsync(c => c.Id == req.ChargeCodeId, ct);

        if(chargeCode is null)
        {
            return Result<BookingChargeDto>.Fail($"ChargeCode with ID '{req.ChargeCodeId}' not found.", ErrorCodes.NotFound);
        }

        BookingChargeLine charge;
        try
        {
            charge = booking.AddAdditionalCharge(
                chargeCode, req.Quantity, req.UnitBuying, req.UnitSelling, req.Reason, currentUserService.GetUserName());
        }
        catch (InvalidOperationException ex)
        {
            return Result<BookingChargeDto>.Fail(ex.Message);
        }

        // Explicitly register with the repository so EF marks the entity as Added
        await tenantUow.Repository<BookingChargeLine>().AddAsync(charge, ct);

        await tenantUow.SaveChangesAsync(ct);

        return Result<BookingChargeDto>.Ok(charge.ToChargeDto());
    }
}
