using Trackwise.Application.Abstractions;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.RecordQuotationLinkAccess;

/// <summary>
/// Records a public-quotation-link access (counter + timestamp). Enqueued fire-and-forget
/// from the <c>GetPublicQuotation</c> query handler so the read path stays write-free.
/// </summary>
public class RecordQuotationLinkAccessCommand : ICommand
{
    public Guid TenantId { get; set; }
    public Guid QuotationLinkId { get; set; }
}