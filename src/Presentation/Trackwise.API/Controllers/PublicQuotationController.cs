using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AcceptPublicQuotation;
using Trackwise.Application.Modules.Operations.FreightForwarding.Queries;
using Trackwise.Application.Modules.Operations.FreightForwarding.Queries.GetPublicQuotation;
using Trackwise.Shared.Models;

namespace Trackwise.API.Controllers;

/// <summary>
///     API endpoints for public quotation access via tokenized links (no authentication required).
///     Allows customers to view the issued quotation (price offer), download its PDF and accept it
///     through a share-by-email link. The link token is the only authentication.
/// </summary>
[ApiController]
[Route("public/quotations")]
[Produces("application/json")]
[AllowAnonymous]
public class PublicQuotationController(IMediator mediator) : ControllerBase
{
    /// <summary>
    ///     Get public quotation information (status, validity, totals) via a quotation link token.
    /// </summary>
    [HttpGet("{tenantId:guid}/{token}", Name = "GetPublicQuotation")]
    [ProducesResponseType(typeof(PublicQuotationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, string token, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPublicQuotationQuery { TenantId = tenantId, Token = token }, ct);
        return result.IsSuccess ? Ok(result.Value) : NotFound(ErrorResponse.FromResult(result));
    }

    /// <summary>
    ///     Get the quotation as a PDF document via a quotation link token.
    /// </summary>
    [HttpGet("{tenantId:guid}/{token}/pdf", Name = "GetPublicQuotationPdf")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPdf(Guid tenantId, string token, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPublicQuotationPdfQuery { TenantId = tenantId, Token = token }, ct);
        if (!result.IsSuccess)
            return NotFound(ErrorResponse.FromResult(result));

        var file = result.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    ///     Accept the quotation on behalf of the customer. A message is required.
    /// </summary>
    [HttpPost("{tenantId:guid}/{token}/accept", Name = "AcceptPublicQuotation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Accept(
        Guid tenantId,
        string token,
        [FromBody] AcceptPublicQuotationRequest body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new AcceptPublicQuotationCommand
        {
            TenantId = tenantId,
            Token = token,
            Message = body.Message
        }, ct);

        return result.IsSuccess ? NoContent() : BadRequest(ErrorResponse.FromResult(result));
    }
}

public sealed record AcceptPublicQuotationRequest(string Message);
