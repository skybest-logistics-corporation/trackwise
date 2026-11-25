
using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Queries.GetPublicQuotation;

public sealed class GetPublicQuotationQuery : IQuery<Result<PublicQuotationDto>>
{
    public required Guid TenantId { get; set; }
    public required string Token { get; set; }
}
