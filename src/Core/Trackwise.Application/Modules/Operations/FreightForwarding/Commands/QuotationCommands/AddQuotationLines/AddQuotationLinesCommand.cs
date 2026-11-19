using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.AddQuotationLines;

public class AddQuotationLinesCommand : ICommand<Result<IReadOnlyList<Guid>>>
{
    public Guid QuotationId { get; set; }
    public List<AddQuotationLineItem> Lines { get; set; } = [];
}

/// <summary>Same fields as AddQuotationLineCommand, minus QuotationId.</summary>
public class AddQuotationLineItem
{
    public Guid TariffRateId { get; set; }
    public OperationalServiceType ServiceType { get; set; } = OperationalServiceType.Standard;
    public decimal Quantity { get; set; }
    public MarkupType MarkupType { get; set; }
    public decimal MarkupValue { get; set; }
    public decimal? FxRate { get; set; }
    public decimal? BaseAmount { get; set; }
    public string? Notes { get; set; }
}
