using Trackwise.Shared.Identity.Policies;
using Trackwise.Shared.Models;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackwise.Application.Modules.Compliance.Incidents.Commands;
using Trackwise.Application.Modules.IdentityAccess.Employees.Commands;
using Trackwise.Application.Modules.IdentityAccess.Employees.Queries;
using Trackwise.Application.Modules.Operations.Loads.Commands;
using Trackwise.Application.Modules.Operations.Trips.Commands;

namespace Trackwise.API.Controllers;

[ApiController]
[Route("drivers")]
[Produces("application/json")]
public class DriverController(IMediator mediator) : ControllerBase
{
    private const int MaxPhotoUploadBytes = 60 * 1024 * 1024;

    [HttpGet("{userId:guid}", Name = "GetDriverByUserId")]
    [ProducesResponseType(typeof(EmployeeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [Authorize(Policy = Permission.Driver.View)]
    public async Task<IActionResult> GetById(Guid userId)
    {
        var result = await mediator.Send(new GetEmployeeByIdQuery { UserId = userId });
        return result.IsSuccess ? Ok(result.Value) : NotFound(ErrorResponse.FromResult(result));
    }

    [HttpPost("{userId:guid}/device-token", Name = "SetDriverDeviceToken")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> SetDeviceToken(Guid userId, [FromBody] SetDriverDeviceTokenCommand request)
    {
        request.UserId = userId;
        var result = await mediator.Send(request);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }

    [HttpPost("confirm-load-status", Name = "ConfirmLoadStatus")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> ConfirmLoadStatus([FromBody] ConfirmLoadStatusCommand request)
    {
        var result = await mediator.Send(request);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }

    /// <summary>
    /// Records one of the crew's trip events from the Driver App - arrived, loading or unloading
    /// started or finished, departed, or the garage times.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/events", Name = "RecordDriverTripEvent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> RecordTripEvent(Guid tripId, [FromBody] RecordDriverTripEventCommand request)
    {
        request.TripId = tripId;
        var result = await mediator.Send(request);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }

    /// <summary>Takes back the crew's own last trip event, within a few minutes.</summary>
    [HttpPost("trips/{tripId:guid}/events/undo", Name = "UndoDriverTripEvent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> UndoTripEvent(Guid tripId, [FromBody] UndoDriverTripEventCommand request)
    {
        request.TripId = tripId;
        var result = await mediator.Send(request);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }

    /// <summary>
    /// The crew's "Problem" button: opens a case on their trip - an accident, a breakdown - and alerts
    /// the office. The office reviews it and decides on a rescue.
    /// </summary>
    [HttpPost("trips/{tripId:guid}/incidents", Name = "ReportDriverIncident")]
    [ProducesResponseType(typeof(IncidentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> ReportIncident(Guid tripId, [FromBody] ReportDriverIncidentCommand request)
    {
        request.TripId = tripId;
        var result = await mediator.Send(request);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(ErrorResponse.FromResult(result));
    }

    /// <summary>Photos of a case the crew reported: the damage, the scene, the other vehicle.</summary>
    [HttpPost("incidents/{incidentId:guid}/photos", Name = "AddDriverIncidentPhotos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    [RequestSizeLimit(MaxPhotoUploadBytes)]
    public async Task<IActionResult> AddIncidentPhotos(Guid incidentId, [FromForm] AddDriverIncidentPhotosRequest request)
    {
        var command = new AddDriverIncidentPhotosCommand
        {
            IncidentId = incidentId,
            Files = request.Photos
                .Where(f => f.Length > 0)
                .Select(f => new FileUpload
                {
                    Content = f.OpenReadStream(),
                    FileName = f.FileName,
                    ContentType = f.ContentType,
                    FileSizeBytes = f.Length
                })
                .ToList()
        };
        var result = await mediator.Send(command);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }

    [HttpPost("update-load-proximity", Name = "UpdateLoadProximity")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [Authorize(Policy = Permission.Driver.Manage)]
    public async Task<IActionResult> UpdateLoadProximity([FromBody] UpdateLoadProximityCommand request)
    {
        var result = await mediator.Send(request);
        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }
}

public record AddDriverIncidentPhotosRequest(List<IFormFile> Photos);
