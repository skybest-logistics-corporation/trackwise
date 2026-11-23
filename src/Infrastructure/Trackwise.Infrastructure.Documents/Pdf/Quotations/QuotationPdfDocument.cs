using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Trackwise.Application.Abstractions.Documents;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;
using Trackwise.Domain.Primitives.ValueObjects;
using Trackwise.Infrastructure.Documents.Pdf.Shared;

namespace Trackwise.Infrastructure.Documents.Pdf.Quotations;

/// <summary>
/// The customer-facing formal rate offer. Everything it prints comes from the quotation
/// aggregate plus the names the application layer resolved for it.
/// <para>
/// Routing depends on the freight mode. Reefer and Trucking quote a stop-by-stop lane;
/// every other mode (Domestic / Import / Export / Triangular) quotes Port of Loading to
/// Port of Discharge.
/// </para>
/// <para>
/// Two charge layouts (all modes): a quotation priced for a single truck type/size/container type
/// prints the itemised charge table; a quotation whose tariffs span several truck types, sizes or
/// container types prints a rate matrix because those lines are alternatives, not charges that
/// add up. Lane modes get one row per delivery lane; port-routed modes get a single route row.
/// Columns are the truck / container options.
/// </para>
/// </summary>
public sealed class QuotationPdfDocument : IDocument
{
    private readonly Quotation _quote;
    private readonly Tenant _tenant;
    private readonly Image? _logo;
    private readonly string _customerName;
    private readonly string? _customerAddress;
    private readonly IReadOnlyDictionary<Guid, string> _locationNames;
    private readonly IReadOnlyList<PricedLine> _lines;

    /// <summary>Reefer / Trucking: routed by lane. Everything else: routed by POL / POD.</summary>
    private readonly bool _isLaneMode;

    // Multi-tariff layout state
    private readonly IReadOnlyList<PricedLine> _tariffLines;
    private readonly IReadOnlyList<PricedLine> _commonLines;
    private readonly IReadOnlyList<VehicleSpec> _columns;
    private readonly IReadOnlyList<string> _lanes;
    private readonly bool _isMatrix;

    private readonly record struct VehicleSpec(TruckType? Type, TruckTonnageClass? Tonnage, ContainerType? ContainerType);

    private sealed record PricedLine(
        string Code,
        string Description,
        string Category,
        string PricingModel,
        decimal Quantity,
        string? Unit,
        decimal? UnitRate,
        string Markup,
        decimal Buying,
        decimal Selling,
        bool Priced,
        VehicleSpec? Vehicle,   // null = not tied to a truck type/size/container type (common charge)
        string? Lane);          // row label, only set for vehicle-based tariffs

    public QuotationPdfDocument(QuotationPdfRequest request)
    {
        _quote = request.Quotation;
        _tenant = request.Tenant;
        _customerName = request.CustomerName;
        _customerAddress = request.CustomerAddress;
        _locationNames = request.LocationNames;
        _logo = PdfLogo.TryDecode(request.LogoBytes);

        // Must be set before BuildLines: LaneLabel depends on it.
        _isLaneMode = _quote.FreightMode is FreightMode.Reefer or FreightMode.Trucking;

        _lines = BuildLines(request);

        _tariffLines = _lines.Where(l => l.Vehicle.HasValue).ToList();
        _commonLines = _lines.Where(l => !l.Vehicle.HasValue).ToList();

        _columns = _tariffLines
            .Select(l => l.Vehicle!.Value)
            .Distinct()
            .OrderBy(v => v.Type)
            .ThenBy(v => v.Tonnage)
            .ThenBy(v => v.ContainerType?.Code)
            .ToList();

        _lanes = _tariffLines.Select(l => l.Lane ?? "—").Distinct().ToList(); // keeps first-seen order

        // More than one truck type/size/container type = alternatives, in any freight mode,
        // so a single itemised table would be misleading.
        _isMatrix = _columns.Count > 1;
    }

    /// <summary>
    /// Prices every line through the aggregate so percentage lines resolve their valuation
    /// base. A line whose base cannot be derived yet (a draft quotation) still prints, with
    /// its amount left blank rather than failing the whole offer.
    /// </summary>
    private List<PricedLine> BuildLines(QuotationPdfRequest request)
    {
        var result = new List<PricedLine>();

        foreach (var line in _quote.Lines)
        {
            var rate = line.TariffRate;
            var charge = rate?.ChargeCode;
            var tariff = rate?.Tariff;

            decimal buying = 0m;
            decimal selling = 0m;
            var priced = true;

            try
            {
                var prices = _quote.PriceLine(line);
                buying = prices.Buying.Amount;
                selling = prices.Selling.Amount;
            }
            catch (InvalidOperationException)
            {
                priced = false;
            }

            // Any one of truck type / tonnage / container type is enough to make the line an "option".
            VehicleSpec? vehicle = tariff is not null
                && (tariff.TruckType is not null || tariff.TonnageClass is not null || tariff.ContainerType is not null)
                    ? new VehicleSpec(tariff.TruckType, tariff.TonnageClass, tariff.ContainerType)
                    : null;

            result.Add(new PricedLine(
                Code: charge?.Code ?? "—",
                Description: charge?.Name ?? "Unspecified Charge",
                Category: PdfFormatting.Display(line.Category),
                PricingModel: rate is null ? "—" : PdfFormatting.Display(rate.PricingModel),
                Quantity: line.Quantity,
                Unit: line.TariffRate?.PricingModel == PricingModel.Percentage
                    ? $"{line.TariffRate.Percentage}{line.TariffRate?.Unit}"
                    : line.TariffRate?.Unit,
                UnitRate: rate?.BaseAmount,
                Markup: DescribeMarkup(line),
                Buying: buying,
                Selling: selling,
                Priced: priced,
                Vehicle: vehicle,
                Lane: vehicle.HasValue ? LaneLabel(tariff!) : null));
        }

        return result;
    }

    /// <summary>
    /// The row label in the matrix. Lane modes use the tariff name (e.g. "Reefer 2 Tonner Taguig - Baguio").
    /// Port-routed modes use the quotation's POL - POD, because the tariff name carries the container
    /// size ("20 FTR Tagoloan - MCT PORT") and would split each container type onto its own row.
    /// </summary>
    private string LaneLabel(Tariff tariff) =>
        _isLaneMode ? tariff.Name : RouteLabel();

    private string RouteLabel() =>
        $"{_quote.Pol?.Name ?? "To be advised"} - {_quote.Pod?.Name ?? "To be advised"}";

    private static string DescribeMarkup(QuotationLine line) => line.MarkupType switch
    {
        MarkupType.Percentage => $"+{line.MarkupValue.ToString("0.##")}%",
        MarkupType.Flat => $"+{line.MarkupValue.ToString("N2")}",
        _ => "—"
    };

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9).FontColor(Colors.Grey.Darken4));

            page.Header().Element(ComposeHeader);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem(3).Element(c => CompanyInfoColumn.Render(c, _tenant, _logo, includeTaxIds: false));

                row.RelativeItem(2).AlignRight().Column(meta =>
                {
                    meta.Item().Text("FORMAL RATE OFFER")
                        .FontSize(16)
                        .Bold()
                        .FontColor(Colors.Blue.Darken3);

                    meta.Item().PaddingTop(4).Text($"Date: {DateTime.UtcNow:MMMM dd, yyyy}").SemiBold();
                    meta.Item().Text($"Ref: {_quote.ReferenceNumber}").Bold().FontColor(Colors.Blue.Darken3);

                    if (_quote.ValidUntil.HasValue)
                    {
                        meta.Item().Text($"Valid Until: {_quote.ValidUntil.Value:MMMM dd, yyyy}")
                            .FontColor(Colors.Grey.Darken1);
                    }
                });
            });

            col.Item().PaddingTop(12).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

            col.Item().PaddingTop(10).Column(recipient =>
            {
                recipient.Item().Text($"TO: {_customerName}").Bold();

                if (!string.IsNullOrWhiteSpace(_customerAddress))
                {
                    recipient.Item().Text(_customerAddress).FontColor(Colors.Grey.Darken2);
                }

                recipient.Item().PaddingTop(4).Text(
                    $"RE: {PdfFormatting.Display(_quote.FreightMode)} " +
                    $"{PdfFormatting.Display(_quote.Mode)} - {PdfFormatting.Display(_quote.ShipmentType)}").Bold();
            });

            col.Item().PaddingTop(10).Text(text =>
            {
                text.Line($"Dear {_customerName},");
                text.Line("Good day.");
                text.Line("Please find attached our formal rate offer for dedicated logistics services for your perusal.");
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(8).Column(summary =>
            {
                summary.Item().Row(row =>
                {
                    Spec(row, "Quotation Reference", _quote.ReferenceNumber, 1.8f);
                    Spec(row, "Freight Mode", PdfFormatting.Display(_quote.FreightMode));
                    Spec(row, "Transport Mode", PdfFormatting.Display(_quote.Mode));
                    Spec(row, "Shipment Type", PdfFormatting.Display(_quote.ShipmentType));
                    Spec(row, "Incoterm", PdfFormatting.Display(_quote.Incoterm));
                    Spec(row, "Currency", _quote.Currency);
                });

                var truckTypes = TruckTypeSummary();
                var tonnage = TonnageClassSummary();
                var containerTypes = ContainerTypeSummary();

                if (truckTypes is not null || tonnage is not null || containerTypes is not null)
                {
                    summary.Item().PaddingTop(6).Row(row =>
                    {
                        if (truckTypes is not null)
                        {
                            Spec(row, "Truck Type", truckTypes);
                        }

                        if (tonnage is not null)
                        {
                            Spec(row, "Tonnage Class", tonnage);
                        }

                        if (containerTypes is not null)
                        {
                            Spec(row, "Container Type", containerTypes);
                        }
                    });
                }
            });

            // Non-lane modes route by POL / POD. Lane modes: each matrix row has its own lane, so one
            // quotation-level route would contradict it and the matrix only states the common pickup point.
            if (!_isLaneMode)
            {
                ComposePorts(col);
            }
            else if (_isMatrix)
            {
                ComposePickup(col);
            }
            else
            {
                ComposeRouting(col);
            }

            ComposeCargoAndTemperature(col);

            if (_isMatrix)
            {
                col.Item().PaddingTop(14).Element(ComposeRateMatrix);

                if (_commonLines.Count > 0)
                {
                    col.Item().PaddingTop(14).Element(ComposeCommonCharges);
                }
            }
            else
            {
                col.Item().PaddingTop(14).Element(ComposeLineTable);

                col.Item().PaddingTop(8).Background(Colors.Grey.Lighten4).Padding(6).Row(row =>
                {
                    row.RelativeItem().Text("TOTAL ESTIMATED CHARGES:").Bold().FontColor(Colors.Blue.Darken3);
                    row.RelativeItem().AlignRight().Text(PdfFormatting.Money(TotalSelling(), _quote.Currency))
                        .Bold().FontSize(11).FontColor(Colors.Blue.Darken3);
                });
            }

            col.Item().ShowEntire().Column(ComposeNotesAndSignature);
        });
    }

    // ── Routing ────────────────────────────────────────────────────────────────

    private List<RouteStop> OrderedStops() =>
        (_quote.Lane?.Stops ?? []).OrderBy(s => s.Sequence).ToList();

    /// <summary>
    /// Non-lane modes (Domestic / Import / Export / Triangular): Port of Loading to Port of Discharge.
    /// Requires Quotation.Pol / Quotation.Pod to be loaded (Include) by whoever builds the request.
    /// </summary>
    private void ComposePorts(ColumnDescriptor col)
    {
        var isAir = _quote.Mode == TransportMode.Air;

        col.Item().PaddingTop(14).Text("PORT ROUTING")
            .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

        col.Item().PaddingTop(4).Column(c =>
        {
            c.Item().Text($"• {(isAir ? "Airport of Departure" : "Port of Loading")} (POL): {TerminalName(_quote.Pol)}");
            c.Item().Text($"• {(isAir ? "Airport of Destination" : "Port of Discharge")} (POD): {TerminalName(_quote.Pod)}");
        });
    }

    private static string TerminalName(Terminal? terminal)
    {
        if (terminal is null)
        {
            return "To be advised";
        }

        var label = $"{terminal.Code} - {terminal.Name}";
        return string.IsNullOrWhiteSpace(terminal.CountryCode) ? label : $"{label} ({terminal.CountryCode})";
    }

    /// <summary>Matrix mode (lane modes): only the pickup point, since each lane has its own row.</summary>
    private void ComposePickup(ColumnDescriptor col)
    {
        var first = OrderedStops().FirstOrDefault();
        if (first is null)
        {
            return;
        }

        col.Item().PaddingTop(14).Text(text =>
        {
            text.Span("Pick up: ").Bold().FontColor(Colors.Blue.Darken3);
            text.Span(LocationName(first.LocationId));
        });
    }

    private void ComposeRouting(ColumnDescriptor col)
    {
        col.Item().PaddingTop(14).Text("OPERATIONAL ROUTING")
            .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

        col.Item().PaddingTop(4).Column(c =>
        {
            var stops = OrderedStops();

            if (stops.Count > 0)
            {
                c.Item().Text($"• Route: {LocationName(stops.First().LocationId)} to {LocationName(stops.Last().LocationId)} ({stops.Count} stop(s))");
            }

            var distanceKm = _quote.Lane?.TotalDistanceKm ?? 0m;
            if (distanceKm > 0m)
            {
                c.Item().Text($"• Distance: {distanceKm:N2} km");
            }

            foreach (var stop in stops)
            {
                c.Item().Text(
                    $"   {stop.Sequence}. {LocationName(stop.LocationId)} " +
                    $"({PdfFormatting.Display(stop.Type)})" +
                    (stop.WindowEnd.HasValue ? $" - by {stop.WindowEnd.Value:dd MMM yyyy HH:mm}" : string.Empty));
            }
        });
    }

    // ── Cargo & cold chain ─────────────────────────────────────────────────────

    private void ComposeCargoAndTemperature(ColumnDescriptor col)
    {
        var cargo = _quote.CargoSpec;
        if (cargo is not null)
        {
            col.Item().PaddingTop(14).Text("CARGO PROFILE")
                .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).Column(c =>
            {
                c.Item().Text($"• Commodity: {cargo.Commodity}");
                c.Item().Text($"• Pallet Count: {cargo.PalletCount} pallet(s) | Gross Weight: {cargo.TotalWeightKg:N2} kg");
                c.Item().Text($"• Declared Value: {PdfFormatting.Money(cargo.DeclaredCargoValue.Amount, cargo.DeclaredCargoValue.Currency)}");

                if (cargo.RequiresActiveRefrigeration)
                {
                    c.Item().Text("• Active refrigeration required.");
                }
            });
        }

        // Temperature control only applies to Reefer.
        var temperature = _quote.FreightMode == FreightMode.Reefer ? _quote.TemperatureSpec : null;
        if (temperature is not null)
        {
            col.Item().PaddingTop(14).Text("COLD CHAIN PROFILE")
                .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).Column(c =>
            {
                c.Item().Text($"• Zone: {PdfFormatting.Display(temperature.Zone)} | Setpoint: {temperature.SetpointCelsius:N1}°C");
                c.Item().Text($"• Allowable Variance: ±{temperature.AllowableVarianceCelsius:N1}°C | Compliance: {PdfFormatting.Display(temperature.Compliance)}");
                c.Item().Text($"• Continuous Telematics: {(temperature.RequiresContinuousTelematics ? "Required" : "Not required")}");
            });
        }
    }

    // ── Single-option layout: itemised table ───────────────────────────────────

    private void ComposeLineTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30);
                columns.RelativeColumn(4);
                columns.RelativeColumn(1.4f);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1.6f);
            });

            table.Header(header =>
            {
                header.Cell().Element(PdfStyles.HeaderCell).Text("Item");
                header.Cell().Element(PdfStyles.HeaderCell).Text("Charge Description");
                header.Cell().Element(PdfStyles.HeaderCell).Text("Pricing");
                header.Cell().Element(PdfStyles.HeaderCell).AlignRight().Text("Quantity");
                header.Cell().Element(PdfStyles.HeaderCell).AlignRight().Text($"Total ({_quote.Currency})");
                //header.Cell().Element(PdfStyles.HeaderCell).AlignRight().Text("Buying");
            });

            var index = 1;
            foreach (var line in _lines)
            {
                table.Cell().Element(PdfStyles.Cell).Text(index.ToString("00"));
                table.Cell().Element(PdfStyles.Cell).Column(cell =>
                {
                    cell.Item().Text($"{line.Code} - {line.Description}").SemiBold();
                    cell.Item().Text(SubLine(line)).FontSize(8).FontColor(Colors.Grey.Darken1);
                });
                table.Cell().Element(PdfStyles.Cell).Text(line.PricingModel);
                table.Cell().Element(PdfStyles.Cell).AlignRight()
                    .Text(line.Quantity.ToString("N2") + (string.IsNullOrWhiteSpace(line.Unit) ? string.Empty : $" ({line.Unit})"));
                table.Cell().Element(PdfStyles.Cell).AlignRight()
                    .Text(line.Priced ? PdfFormatting.Money(line.Selling, _quote.Currency) : "—")
                    .Bold();
                //table.Cell().Element(PdfStyles.Cell).AlignRight()
                //    .Text(line.Priced ? PdfFormatting.Money(line.Buying, _quote.Currency) : "—");
                index++;
            }
        });
    }

    // ── Multi-option layout: rate matrix ───────────────────────────────────────

    private void ComposeRateMatrix(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text(_isLaneMode ? "RATES PER TRIP" : "RATES BY CONTAINER TYPE")
                .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    foreach (var _ in _columns)
                    {
                        columns.RelativeColumn(1.5f);
                    }
                });

                table.Header(header =>
                {
                    header.Cell().Element(PdfStyles.HeaderCell).Text(_isLaneMode ? "Delivery Location" : "Route");
                    foreach (var spec in _columns)
                    {
                        header.Cell().Element(PdfStyles.HeaderCell).AlignRight().Text(ColumnLabel(spec));
                    }
                });

                foreach (var lane in _lanes)
                {
                    table.Cell().Element(PdfStyles.Cell).Text(lane).SemiBold();

                    foreach (var spec in _columns)
                    {
                        table.Cell().Element(PdfStyles.Cell).AlignRight()
                            .Text(CellAmount(lane, spec)).Bold();
                    }
                }
            });

            col.Item().PaddingTop(4)
                .Text(_isLaneMode
                    ? "Rates are per trip and depend on the truck engaged. Only one rate per delivery location applies."
                    : "Rates depend on the container type engaged. Only one rate applies per shipment.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }

    private string ColumnLabel(VehicleSpec spec)
    {
        var parts = new List<string>();

        if (spec.Type is { } type)
        {
            parts.Add(PdfFormatting.Display(type));
        }

        if (spec.Tonnage is { } tonnage)
        {
            parts.Add(NumberWordsToDigits(PdfFormatting.Display(tonnage)));
        }

        var container = ContainerTypeLabel(spec.ContainerType);
        if (container is not null)
        {
            parts.Add(container);
        }

        return string.Join(" ", parts).ToUpperInvariant();
    }

    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["One"] = "1",
        ["Two"] = "2",
        ["Three"] = "3",
        ["Four"] = "4",
        ["Five"] = "5",
        ["Six"] = "6",
        ["Seven"] = "7",
        ["Eight"] = "8",
        ["Nine"] = "9",
        ["Ten"] = "10",
        ["Twelve"] = "12",
        ["Fourteen"] = "14",
        ["Sixteen"] = "16",
        ["Twenty"] = "20",
    };

    private static readonly System.Text.RegularExpressions.Regex NumberWordPattern = new(
        $@"\b({string.Join("|", NumberWords.Keys)})\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>"Two Tonner" becomes "2 Tonner". Text with no number word is returned unchanged.</summary>
    private static string NumberWordsToDigits(string text) =>
        NumberWordPattern.Replace(text, m => NumberWords[m.Value]);

    /// <summary>Sum of everything priced under that row + option. A blank price is "—", never a partial sum.</summary>
    private string CellAmount(string lane, VehicleSpec spec)
    {
        var cell = _tariffLines.Where(l => (l.Lane ?? "—") == lane && l.Vehicle == spec).ToList();
        if (cell.Count == 0 || cell.Any(l => !l.Priced))
        {
            return "—";
        }

        return PdfFormatting.Money(cell.Sum(l => l.Selling), _quote.Currency);
    }

    /// <summary>Charges not tied to a truck / container type (e.g. permits) apply to whichever option is used.</summary>
    private void ComposeCommonCharges(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().Text(_isLaneMode ? "ADDITIONAL CHARGES (ALL TRUCKS)" : "ADDITIONAL CHARGES (ALL OPTIONS)")
                .Bold().FontSize(9).FontColor(Colors.Blue.Darken3);

            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(1.6f);
                });

                table.Header(h =>
                {
                    h.Cell().Element(PdfStyles.HeaderCell).Text("Charge Description");
                    h.Cell().Element(PdfStyles.HeaderCell).AlignRight().Text($"Total ({_quote.Currency})");
                });

                foreach (var line in _commonLines)
                {
                    table.Cell().Element(PdfStyles.Cell).Text($"{line.Code} - {line.Description}").SemiBold();
                    table.Cell().Element(PdfStyles.Cell).AlignRight()
                        .Text(line.Priced ? PdfFormatting.Money(line.Selling, _quote.Currency) : "—").Bold();
                }
            });
        });
    }

    // ── Notes, validity, signatures ────────────────────────────────────────────

    private void ComposeNotesAndSignature(ColumnDescriptor c)
    {
        // 1. Terms & Special Conditions / Notes
        if (!string.IsNullOrWhiteSpace(_quote.Notes))
        {
            c.Item().PaddingTop(3).Column(terms =>
            {
                // Use StringSplitOptions.None to preserve the empty lines
                var noteLines = _quote.Notes.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                foreach (var noteLine in noteLines)
                {
                    if (string.IsNullOrWhiteSpace(noteLine))
                    {
                        // Explicitly add vertical space for blank lines.
                        // Adjust the Height value to match your desired paragraph gap.
                        terms.Item().Height(8);
                    }
                    else
                    {
                        terms.Item().PaddingBottom(2).Text(noteLine.Trim()).FontSize(8);
                    }
                }
            });
        }

        // 2. Separate Quotation Validity Block
        if (_quote.ValidUntil.HasValue)
        {
            c.Item().PaddingTop(10).Text(text =>
            {
                text.Span("Offer Validity: ").Bold().FontSize(8);
                text.Span($"This quotation is valid until {_quote.ValidUntil.Value:MMMM dd, yyyy}.").FontSize(8);
            });
        }

        c.Item().PaddingTop(28).Row(sig =>
        {
            sig.RelativeItem().Column(s =>
            {
                s.Item().Text("Issued by:").SemiBold();
                s.Item().PaddingTop(35).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                s.Item().PaddingTop(3).Text("Commercial Pricing & Operations").Bold();
                s.Item().Text(_tenant.CompanyName ?? _tenant.Name);
            });

            sig.ConstantItem(40);

            sig.RelativeItem().Column(s =>
            {
                s.Item().Text("Conforme & Accepted by:").SemiBold();
                s.Item().PaddingTop(35).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                s.Item().PaddingTop(3).Text("Authorized Representative Signature").Bold();
                s.Item().Text(_customerName);
            });
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Distinct truck types across the tariffs ("Reefer", or "Reefer, Dry Van"); null if none.</summary>
    private string? TruckTypeSummary()
    {
        var types = _columns
            .Where(c => c.Type.HasValue)
            .Select(c => c.Type!.Value)
            .Distinct()
            .Select(t => PdfFormatting.Display(t))
            .ToList();

        return types.Count == 0 ? null : string.Join(", ", types);
    }

    /// <summary>Distinct tonnage classes across the tariffs ("2 Tonner", or "2 Tonner, 4 Tonner"); null if none.</summary>
    private string? TonnageClassSummary()
    {
        var values = _columns
            .Where(c => c.Tonnage.HasValue)
            .Select(c => c.Tonnage!.Value)
            .Distinct()
            .Select(t => NumberWordsToDigits(PdfFormatting.Display(t)))
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    /// <summary>The text printed for a container type: its name, falling back to its code.</summary>
    private static string? ContainerTypeLabel(ContainerType? containerType)
    {
        if (containerType is null)
        {
            return null;
        }

        var label = string.IsNullOrWhiteSpace(containerType.Name) ? containerType.Code : containerType.Name;
        return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
    }

    /// <summary>Distinct container types across the tariffs; null if none.</summary>
    private string? ContainerTypeSummary()
    {
        var values = _columns
            .Select(c => ContainerTypeLabel(c.ContainerType))
            .Where(l => l is not null)
            .Distinct()
            .ToList();

        return values.Count == 0 ? null : string.Join(", ", values);
    }

    private static void Spec(RowDescriptor row, string label, string value, float weight = 1f) =>
        row.RelativeItem(weight).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
            c.Item().Text(value).Bold();
        });

    private static string SubLine(PricedLine line)
    {
        var parts = new List<string> { line.Category, line.PricingModel };

        if (line.UnitRate.HasValue)
        {
            parts.Add($"Rate {line.UnitRate.Value.ToString("N2")}{(string.IsNullOrWhiteSpace(line.Unit) ? string.Empty : $" / {line.Unit}")}");
        }

        if (line.Markup != "—")
        {
            parts.Add($"Markup {line.Markup}");
        }

        return string.Join(" | ", parts);
    }

    private string LocationName(Guid locationId) =>
        _locationNames.TryGetValue(locationId, out var name) ? name : "Unknown Location";

    private decimal TotalSelling() => _lines.Where(l => l.Priced).Sum(l => l.Selling);

    private void ComposeFooter(IContainer container)
    {
        container.Column(footerCol =>
        {
            footerCol.Item().Element(c => PdfFooter.Render(
                c,
                _tenant,
                "Please confirm your acceptance by signing and returning this offer."));

            footerCol.Item().PaddingTop(4).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Medium));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }
}
