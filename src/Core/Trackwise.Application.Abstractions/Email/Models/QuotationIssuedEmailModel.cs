namespace Trackwise.Application.Abstractions.Email.Models;

/// <summary>
///     Model for quotation issued email templates.
/// </summary>
public class QuotationIssuedEmailModel
{
    public string CompanyName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public string QuotationUrl { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
    public string? PersonalMessage { get; set; }
}
