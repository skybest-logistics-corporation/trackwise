using Trackwise.Application.Abstractions;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Financial.Expenses.Commands;

public class CreateBodyShopExpenseCommand : ICommand<Result<Guid>>, IExpenseFields
{
    public decimal Amount { get; set; }
    /// <summary>ISO code of <see cref="Amount"/>. Null means the tenant's currency.</summary>
    public string? Currency { get; set; }
    public string? VendorName { get; set; }
    public DateTime ExpenseDate { get; set; }
    public required string ReceiptBlobPath { get; set; }
    public string? Notes { get; set; }
    public required Guid TruckId { get; set; }
    public string? VendorAddress { get; set; }
    public string? VendorPhone { get; set; }
    public string? RepairDescription { get; set; }
    public DateTime? EstimatedCompletionDate { get; set; }
    public DateTime? ActualCompletionDate { get; set; }
}
