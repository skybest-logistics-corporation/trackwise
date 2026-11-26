using System;
using System.Collections.Generic;
using System.Text;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Shared.Models;

namespace Trackwise.Application.Modules.Operations.FreightForwarding.Commands.QuotationCommands.UpdateQuotationLine;

public record UpdateQuotationLineCommand(
    Guid QuotationId,
    Guid LineId,
    decimal Quantity,
    decimal? BaseAmount,
    MarkupType MarkupType,
    decimal MarkupValue,
    decimal? FxRate,
    string? Notes) : ICommand<Result>;
