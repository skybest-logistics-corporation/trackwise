using Trackwise.Application.Abstractions;
using Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.GenerateQuotationPdf;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Queries;

/// <summary>
/// Query to get public quotation information for a customer via quotation link token.
/// </summary>
public sealed class GetPublicQuotationPdfQuery : IQuery<Result<FileResponse>>
{
    public required Guid TenantId { get; set; }
    public required string Token { get; set; }
}
