using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AcceptPublicQuotation;


/// <summary>
/// Customer acceptance of an issued quotation through its public link. The link token is the
/// only authentication, so the handler must validate it before touching the quotation.
/// </summary>
public class AcceptPublicQuotationCommand : ICommand<Result<Guid>>
{
    /// <summary>The tenant that owns the quotation.</summary>
    public required Guid TenantId { get; set; }

    /// <summary>The quotation link token.</summary>
    public required string Token { get; set; }

    /// <summary>The customer's message. Required, up to 1000 characters.</summary>
    public required string Message { get; set; }
}
