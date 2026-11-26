using Microsoft.EntityFrameworkCore;
using Trackwise.DbMigrator.Abstractions;
using Trackwise.DbMigrator.Models;
using Trackwise.Domain.Entities;
using Trackwise.Domain.Primitives.Enums;
using Trackwise.Domain.Primitives.Enums.FreightForwarding;

namespace Trackwise.DbMigrator.Seeders.FakeData;

/// <summary>
/// Seeds the sample charge codes plus one versioned tariff per scenario (domestic trucking,
/// domestic reefer, import sea FCL, export air, triangular sea) with all rates attached, so a
/// Quotation has live TariffRates to price against. Container types are resolved by code from the
/// migration-seeded reference list (<c>AddContainerTypesAndCurrencies</c>) rather than re-declared here.
/// </summary>
/// <remarks>
/// Rates are persisted in the order declared in <see cref="TariffCatalog.Tariffs"/>, and
/// <see cref="TariffCatalog.Slice"/> uses that same declaration to split <c>CreatedTariffRates</c>
/// back into per-tariff lists, so QuotationSeeder never relies on magic offsets.
/// </remarks>
internal class TariffSeeder(ILogger<TariffSeeder> logger) : SeederBase(logger)
{
    public override string Name => nameof(TariffSeeder);
    public override SeederType Type => SeederType.FakeData;
    public override int Order => 215;
    public override IReadOnlyList<string> DependsOn => [nameof(ChargeCodeSeeder)];

    protected override async Task<bool> HasExistingDataAsync(SeederContext context, CancellationToken cancellationToken)
    {
        return await context.TenantUnitOfWork.Repository<Tariff>().CountAsync(ct: cancellationToken) > 0;
    }

    public override async Task SeedAsync(SeederContext context, CancellationToken cancellationToken = default)
    {
        var existingChargeCodes = context.CreatedChargeCodes;
        if (existingChargeCodes is null)
        {
            LogUpstreamSkipped();
            return;
        }

        LogStarting();

        // 1. Charge codes: reuse whatever ChargeCodeSeeder created, add any sample codes that are missing.
        var byCode = existingChargeCodes
            .GroupBy(c => c.Code)
            .ToDictionary(g => g.Key, g => g.First());

        var chargeCodeRepo = context.TenantUnitOfWork.Repository<ChargeCode>();
        foreach (var def in TariffCatalog.ChargeCodes)
        {
            if (byCode.ContainsKey(def.Code)) continue;

            var chargeCode = ChargeCode.Create(
                def.Code, def.Name, def.Category, def.Modes, def.Unit, def.RequiresApproval, def.Description);
            chargeCode.Currency = def.Currency;

            await chargeCodeRepo.AddAsync(chargeCode, cancellationToken);
            byCode[def.Code] = chargeCode;
        }

        // 2. Prefer a 40' High Cube for sea tariffs, fall back to the 40' GP.
        var containerTypes = await context.TenantUnitOfWork.Repository<ContainerType>()
            .Query()
            .Where(t => t.Code == "HC40" || t.Code == "GP40")
            .ToListAsync(cancellationToken);
        var container40 = containerTypes.FirstOrDefault(t => t.Code == "HC40")
                          ?? containerTypes.FirstOrDefault(t => t.Code == "GP40");

        // 3. Tariffs + rates
        var tariffRepo = context.TenantUnitOfWork.Repository<Tariff>();
        var rates = new List<TariffRate>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var def in TariffCatalog.Tariffs)
        {
            var tariff = Tariff.Create(
                def.FreightMode, ServiceLevel.Standard, def.Name, def.Currency,
                def.OriginPort, def.DestinationPort,
                null, null,
                def.UsesContainerType ? container40?.Id : null,
                null, today.AddMonths(-1), null);

            foreach (var r in def.Rates)
            {
                var chargeCode = byCode[r.Code];

                var rate = tariff.AddRate(
                    chargeCode.Id, r.Model, r.BaseAmount, r.Unit,
                    r.Minimum, r.Maximum, r.FreeDays, r.RateAfterFreeDays,
                    r.Percentage, r.PercentageBase, r.BreakpointJson,
                    r.EstimatedBuyingAmount);

                // Quotation pricing/validation reads rate.ChargeCode (category, currency), so attach it.
                rate.ChargeCode = chargeCode;
                rate.BreakpointMode = r.BreakpointMode;
                rates.Add(rate);
            }

            await tariffRepo.AddAsync(tariff, cancellationToken);
        }

        await context.TenantUnitOfWork.SaveChangesAsync(cancellationToken);
        context.CreatedTariffRates = rates;
        LogCompleted(rates.Count);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Seed catalog (single source of truth for TariffSeeder and QuotationSeeder)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed record ChargeCodeDef(
    string Code, string Name, ChargeCategory Category, FreightMode[] Modes,
    string Unit, string? Currency, bool RequiresApproval = false, string? Description = null);

internal sealed record TariffDef(
    string Name, FreightMode FreightMode, string Currency,
    string? OriginPort, string DestinationPort, bool UsesContainerType,
    IReadOnlyList<RateDef> Rates);

internal sealed record RateDef(
    string Code, PricingModel Model, decimal BaseAmount, string Unit,
    decimal? EstimatedBuyingAmount = null,
    decimal? Minimum = null, decimal? Maximum = null,
    int? FreeDays = null, decimal? RateAfterFreeDays = null,
    decimal? Percentage = null, string? PercentageBase = null,
    string? BreakpointJson = null, BreakpointMode BreakpointMode = BreakpointMode.AllUnits);

/// <summary>Per-tariff rate lists, split back out of <c>CreatedTariffRates</c>.</summary>
internal sealed record SeededTariffRates(
    IReadOnlyList<TariffRate> DomesticTrucking,
    IReadOnlyList<TariffRate> DomesticReefer,
    IReadOnlyList<TariffRate> ImportSeaFcl,
    IReadOnlyList<TariffRate> ExportAir,
    IReadOnlyList<TariffRate> TriangularSea,
    IReadOnlyList<TariffRate> DomesticTrucking40Ft,
    IReadOnlyList<TariffRate> DomManilaCebuFcl,
    IReadOnlyList<TariffRate> ImpShanghaiManilaOcean,
    IReadOnlyList<TariffRate> ImpManilaLocal,
    IReadOnlyList<TariffRate> ExpManilaLaxAir,
    IReadOnlyList<TariffRate> TruckingManilaBatangas,
    IReadOnlyList<TariffRate> ReeferManilaDavao);

internal static class SeedCodes
{
    // Domestic trucking / reefer (PHP)
    public const string TrkLinehaul = "TRK-LH";
    public const string TrkFuel = "TRK-FSC";
    public const string TrkDrop = "TRK-DROP";
    public const string TrkLiftgate = "TRK-LIFT";
    public const string TrkToll = "TRK-TOLL";
    public const string TrkDetention = "TRK-DET";
    public const string TrkDiscount = "TRK-DISC";
    public const string ReefLinehaul = "REEF-LH";
    public const string ReefPti = "REEF-PTI";
    public const string ReefGenset = "REEF-GEN";
    public const string ReefTelematics = "REEF-TEL";
    public const string ReefPlugin = "REEF-PLG";
    public const string TrkLabor = "TRK-LAB";
    public const string TrkWarehouse = "TRK-WHF";
    public const string TrkAfterHours = "TRK-OT";

    // Sea (USD)
    public const string OceanFreight40 = "OFR-40HC";
    public const string Baf = "BAF-SEA";
    public const string ThcDestination = "THC-D";
    public const string DocImport = "DOC-IMP";
    public const string DeliveryOrder = "DO-FEE";
    public const string CustomsImport = "CUS-IMP";
    public const string Demurrage = "DEM-IMP";
    public const string Insurance = "INS-CIF";

    // Air (USD)
    public const string AirFreight = "AIR-FRT";
    public const string AirFuel = "AIR-FSC";
    public const string AirSecurity = "AIR-SEC";
    public const string ThcOrigin = "THC-O";
    public const string CustomsExport = "CUS-EXP";
    public const string AwbFee = "AWB-FEE";
    public const string HazmatAir = "HAZ-AIR";

    // Triangular (USD)
    public const string TriFreight = "TRI-OFR";
    public const string TriHandling = "TRI-HDL";
    public const string BlSwitch = "BL-SWITCH";
    public const string IsfFiling = "ISF-FEE";
    public const string TriThcOrigin = "TRI-THCO";
    public const string TriDiscount = "TRI-DISC";

    // Sample set (currency-less codes: each tariff's currency decides)
    public const string FfOfr = "FF-OFR";
    public const string FfAfr = "FF-AFR";
    public const string FfLhl = "FF-LHL";
    public const string FfBaf = "FF-BAF";
    public const string FfPss = "FF-PSS";
    public const string FfFsc = "FF-FSC";
    public const string FfSsc = "FF-SSC";
    public const string FfThcOrg = "FF-THCO";
    public const string FfThcDst = "FF-THCD";
    public const string FfArr = "FF-ARR";
    public const string FfDoc = "FF-DOC";
    public const string FfAwb = "FF-AWB";
    public const string FfDof = "FF-DOF";
    public const string FfHdl = "FF-HDL";
    public const string FfCci = "FF-CCI";
    public const string FfCec = "FF-CEC";
    public const string FfTrk = "FF-TRK";
    public const string FfDrp = "FF-DRP";
    public const string FfWait = "FF-WAIT";
    public const string FfRgs = "FF-RGS";
    public const string FfTlm = "FF-TLM";
    public const string FfDet = "FF-DET";
    public const string FfIns = "FF-INS";
    public const string FfDisc = "FF-DISC";
}

internal static class TariffCatalog
{
    private static readonly FreightMode[] Dom = [FreightMode.Domestic];
    private static readonly FreightMode[] Imp = [FreightMode.Import];
    private static readonly FreightMode[] Exp = [FreightMode.Export];
    private static readonly FreightMode[] Tri = [FreightMode.Triangular];
    private static readonly FreightMode[] Intl = [FreightMode.Import, FreightMode.Export, FreightMode.Triangular];
    private static readonly FreightMode[] Truck = [FreightMode.Trucking];
    private static readonly FreightMode[] Reefer = [FreightMode.Reefer];
    private static readonly FreightMode[] Lane = [FreightMode.Trucking, FreightMode.Reefer];
    private static readonly FreightMode[] DomImpExp = [FreightMode.Domestic, FreightMode.Import, FreightMode.Export];
    private static readonly FreightMode[] DomImpExpTri = [FreightMode.Domestic, FreightMode.Import, FreightMode.Export, FreightMode.Triangular];
    private static readonly FreightMode[] ImpExp = [FreightMode.Import, FreightMode.Export];
    private static readonly FreightMode[] ImpExpLane = [FreightMode.Import, FreightMode.Export, FreightMode.Trucking, FreightMode.Reefer];
    private static readonly FreightMode[] AllModes = Enum.GetValues<FreightMode>();

    public static readonly IReadOnlyList<ChargeCodeDef> ChargeCodes =
    [
        // Domestic trucking (PHP)
        new(SeedCodes.TrkLinehaul,   "Trucking Linehaul",         ChargeCategory.Base,        Dom, "per trip",      "PHP"),
        new(SeedCodes.TrkFuel,       "Fuel Surcharge",            ChargeCategory.Surcharge,   Dom, "% of linehaul", "PHP"),
        new(SeedCodes.TrkDrop,       "Additional Drop Fee",       ChargeCategory.Accessorial, Dom, "per drop",      "PHP"),
        new(SeedCodes.TrkLiftgate,   "Liftgate Service",          ChargeCategory.Accessorial, Dom, "per delivery",  "PHP"),
        new(SeedCodes.TrkToll,       "Toll & Port Access",        ChargeCategory.Accessorial, Dom, "per trip",      "PHP"),
        new(SeedCodes.TrkDetention,  "Truck Detention",           ChargeCategory.Accessorial, Dom, "per hour",      "PHP"),
        new(SeedCodes.TrkDiscount,   "Contract Volume Discount",  ChargeCategory.Discount,    Dom, "per trip",      "PHP"),
        new(SeedCodes.TrkLabor,      "Loading/Unloading Labor",   ChargeCategory.Accessorial, Dom, "per hour",      "PHP"),
        new(SeedCodes.TrkWarehouse,  "Warehouse Handling Fee",    ChargeCategory.Accessorial, Dom, "per pallet",    "PHP"),
        new(SeedCodes.TrkAfterHours, "After-Hours Delivery",      ChargeCategory.Accessorial, Dom, "per trip",      "PHP"),

        // Domestic reefer (PHP)
        new(SeedCodes.ReefLinehaul,   "Reefer Linehaul",       ChargeCategory.Base,        Dom, "per trip", "PHP"),
        new(SeedCodes.ReefPti,        "Pre-Cooling & PTI",     ChargeCategory.Accessorial, Dom, "per trip", "PHP"),
        new(SeedCodes.ReefGenset,     "Genset Fuel",           ChargeCategory.Accessorial, Dom, "per hour", "PHP"),
        new(SeedCodes.ReefTelematics, "Continuous Telematics", ChargeCategory.Accessorial, Dom, "per trip", "PHP"),
        new(SeedCodes.ReefPlugin,     "Cold Storage Plug-in",  ChargeCategory.Accessorial, Dom, "per day",  "PHP"),

        // Import sea FCL (USD)
        new(SeedCodes.OceanFreight40, "Ocean Freight 40'HC",             ChargeCategory.Base,        Imp, "per container",     "USD"),
        new(SeedCodes.Baf,            "Bunker Adjustment Factor",        ChargeCategory.Surcharge,   Intl,"per container",     "USD"),
        new(SeedCodes.ThcDestination, "Terminal Handling - Destination", ChargeCategory.Accessorial, Imp, "per container",     "USD"),
        new(SeedCodes.DocImport,      "Import Documentation Fee",        ChargeCategory.Accessorial, Imp, "per shipment",      "USD"),
        new(SeedCodes.DeliveryOrder,  "Delivery Order Fee",              ChargeCategory.Accessorial, Imp, "per shipment",      "USD"),
        new(SeedCodes.CustomsImport,  "Customs Brokerage - Import",      ChargeCategory.Accessorial, Imp, "per entry",         "USD"),
        new(SeedCodes.Demurrage,      "Demurrage (Import)",              ChargeCategory.Accessorial, Imp, "per container/day", "USD", Description: "Charged after free days"),
        new(SeedCodes.Insurance,      "Cargo Insurance",                 ChargeCategory.Accessorial, Intl,"% of CIF",          "USD"),

        // Export air (USD)
        new(SeedCodes.AirFreight,    "Air Freight",                 ChargeCategory.Base,        Exp, "per kg",       "USD"),
        new(SeedCodes.AirFuel,       "Fuel Surcharge (Air)",        ChargeCategory.Surcharge,   Exp, "per kg",       "USD"),
        new(SeedCodes.AirSecurity,   "Security Surcharge (Air)",    ChargeCategory.Surcharge,   Exp, "per kg",       "USD"),
        new(SeedCodes.ThcOrigin,     "Terminal Handling - Origin",  ChargeCategory.Accessorial, Exp, "per kg",       "USD"),
        new(SeedCodes.CustomsExport, "Customs Clearance - Export",  ChargeCategory.Accessorial, Exp, "per entry",    "USD"),
        new(SeedCodes.AwbFee,        "Air Waybill Fee",             ChargeCategory.Accessorial, Exp, "per shipment", "USD"),
        new(SeedCodes.HazmatAir,     "Dangerous Goods Handling",    ChargeCategory.Surcharge,   Exp, "per shipment", "USD", RequiresApproval: true),

        // Triangular (USD)
        new(SeedCodes.TriFreight,   "Ocean Freight 40'HC (Third-Country)", ChargeCategory.Base,        Tri, "per container", "USD"),
        new(SeedCodes.TriHandling,  "Triangular Trade Handling Fee",       ChargeCategory.Accessorial, Tri, "per shipment",  "USD"),
        new(SeedCodes.BlSwitch,     "Switch B/L Re-issuance",              ChargeCategory.Accessorial, Tri, "per B/L",       "USD"),
        new(SeedCodes.IsfFiling,    "ISF 10+2 Filing",                     ChargeCategory.Accessorial, Tri, "per filing",    "USD"),
        new(SeedCodes.TriThcOrigin, "Terminal Handling - Origin",          ChargeCategory.Accessorial, Tri, "per container", "USD"),
        new(SeedCodes.TriDiscount,  "Volume Discount",                     ChargeCategory.Discount,    Tri, "per shipment",  "USD"),

        // ── Sample set: currency-less so each tariff's currency decides ──
        new(SeedCodes.FfOfr,    "Ocean Freight",                    ChargeCategory.Base,        DomImpExpTri, "per container",      null),
        new(SeedCodes.FfAfr,    "Air Freight",                      ChargeCategory.Base,        ImpExp,       "per kg",             null),
        new(SeedCodes.FfLhl,    "Linehaul (Trucking/Reefer)",       ChargeCategory.Base,        Lane,         "per trip",           null),
        new(SeedCodes.FfBaf,    "Bunker Adjustment Factor",         ChargeCategory.Surcharge,   DomImpExpTri, "per container",      null),
        new(SeedCodes.FfPss,    "Peak Season Surcharge",            ChargeCategory.Surcharge,   ImpExp,       "per container",      null),
        new(SeedCodes.FfFsc,    "Fuel Surcharge",                   ChargeCategory.Surcharge,   ImpExpLane,   "per kg / % of base", null),
        new(SeedCodes.FfSsc,    "Security Surcharge",               ChargeCategory.Surcharge,   ImpExp,       "per kg",             null),
        new(SeedCodes.FfThcOrg, "Terminal Handling - Origin",       ChargeCategory.Accessorial, [FreightMode.Domestic, FreightMode.Export], "per container", null),
        new(SeedCodes.FfThcDst, "Terminal Handling - Destination",  ChargeCategory.Accessorial, [FreightMode.Domestic, FreightMode.Import], "per container", null),
        new(SeedCodes.FfArr,    "Arrastre & Wharfage",              ChargeCategory.Accessorial, DomImpExp,    "per container",      null),
        new(SeedCodes.FfDoc,    "Documentation Fee",                ChargeCategory.Accessorial, DomImpExp,    "per shipment",       null),
        new(SeedCodes.FfAwb,    "Air Waybill Fee",                  ChargeCategory.Accessorial, Exp,          "per shipment",       null),
        new(SeedCodes.FfDof,    "Delivery Order Fee",               ChargeCategory.Accessorial, Imp,          "per shipment",       null),
        new(SeedCodes.FfHdl,    "Origin Cargo Handling",            ChargeCategory.Accessorial, ImpExp,       "per kg",             null),
        new(SeedCodes.FfCci,    "Customs Clearance - Import",       ChargeCategory.Accessorial, Imp,          "per shipment",       null),
        new(SeedCodes.FfCec,    "Customs Clearance - Export",       ChargeCategory.Accessorial, Exp,          "per shipment",       null),
        new(SeedCodes.FfTrk,    "Port-to-Door Trucking",            ChargeCategory.Accessorial, DomImpExp,    "per trip",           null),
        new(SeedCodes.FfDrp,    "Additional Drop",                  ChargeCategory.Accessorial, Lane,         "per drop",           null),
        new(SeedCodes.FfWait,   "Waiting Time",                     ChargeCategory.Accessorial, Lane,         "per hour",           null),
        new(SeedCodes.FfRgs,    "Reefer Genset Rental",             ChargeCategory.Accessorial, Reefer,       "per trip",           null),
        new(SeedCodes.FfTlm,    "Continuous Telematics",            ChargeCategory.Accessorial, Reefer,       "per trip",           null),
        new(SeedCodes.FfDet,    "Detention",                        ChargeCategory.Accessorial, DomImpExp,    "per container-day",  null),
        new(SeedCodes.FfIns,    "Cargo Insurance",                  ChargeCategory.Accessorial, AllModes,     "% of CIF",           null),
        new(SeedCodes.FfDisc,   "Volume Discount",                  ChargeCategory.Discount,    AllModes,     "per shipment",       null),
    ];

    // Chargeable-weight breakpoints (USD/kg), AllUnits mode: the matched tier prices the ENTIRE quantity.
    private const string AirBreakpoints = """
        [
          {"min":0,   "max":44,  "rate":5.20, "buyingRate":4.40},
          {"min":45,  "max":99,  "rate":4.80, "buyingRate":4.05},
          {"min":100, "max":299, "rate":4.30, "buyingRate":3.60},
          {"min":300, "max":499, "rate":3.90, "buyingRate":3.25},
          {"min":500,            "rate":3.50, "buyingRate":2.90}
        ]
        """;

    private const string ExportAirBreakpoints = """
        [
          {"min":0,   "rate":4.20, "buyingRate":3.55},
          {"min":45,  "rate":3.60, "buyingRate":3.05},
          {"min":100, "rate":3.20, "buyingRate":2.70},
          {"min":300, "rate":2.85, "buyingRate":2.40},
          {"min":500, "rate":2.60, "buyingRate":2.15}
        ]
        """;

    // Incremental: each band prices only the kg inside it.
    private const string HandlingBreakpoints = """
        [
          {"min":0,   "rate":0.25, "buyingRate":0.18},
          {"min":100, "rate":0.18, "buyingRate":0.13},
          {"min":500, "rate":0.12, "buyingRate":0.08}
        ]
        """;

    /// <summary>ORDER MATTERS: it must match <see cref="Slice"/> and <see cref="SeededTariffRates"/>.</summary>
    public static readonly IReadOnlyList<TariffDef> Tariffs =
    [
        // 0) Domestic dry trucking: Metro Manila → Batangas (10-wheeler)
        new("Manila - Batangas Domestic Trucking 2026", FreightMode.Domestic, "PHP", "PHMNL", "PHBTG", false,
        [
            new(SeedCodes.TrkLinehaul,  PricingModel.Flat,       18_500m, "per trip",     EstimatedBuyingAmount: 15_000m),
            new(SeedCodes.TrkFuel,      PricingModel.Percentage, 0m,      "% of linehaul", EstimatedBuyingAmount: null, Percentage: 8m, PercentageBase: "BAS"),
            new(SeedCodes.TrkDrop,      PricingModel.PerUnit,    1_200m,  "per drop",     EstimatedBuyingAmount: 900m),
            new(SeedCodes.TrkLiftgate,  PricingModel.Flat,       1_500m,  "per delivery", EstimatedBuyingAmount: 1_100m),
            new(SeedCodes.TrkToll,      PricingModel.Flat,       2_100m,  "per trip",     EstimatedBuyingAmount: 2_100m),
            new(SeedCodes.TrkDetention, PricingModel.PerUnit,    350m,    "per hour",     EstimatedBuyingAmount: null, FreeDays: 4, RateAfterFreeDays: 350m),
            new(SeedCodes.TrkDiscount,  PricingModel.Flat,       500m,    "per trip",     EstimatedBuyingAmount: null),
        ]),

        // 1) Domestic reefer: Metro Manila → Baguio
        new("Manila - Baguio Domestic Reefer 2026", FreightMode.Domestic, "PHP", "PHMNL", "PHBAG", false,
        [
            new(SeedCodes.ReefLinehaul,   PricingModel.Flat,       24_500m, "per trip",     EstimatedBuyingAmount: 20_000m),
            new(SeedCodes.ReefPti,        PricingModel.Flat,       3_200m,  "per trip",     EstimatedBuyingAmount: 2_500m),
            new(SeedCodes.ReefGenset,     PricingModel.PerUnit,    480m,    "per hour",     EstimatedBuyingAmount: 380m, Minimum: 2_400m),
            new(SeedCodes.ReefTelematics, PricingModel.Flat,       1_800m,  "per trip",     EstimatedBuyingAmount: 1_200m),
            new(SeedCodes.ReefPlugin,     PricingModel.PerUnit,    900m,    "per day",      EstimatedBuyingAmount: 700m, FreeDays: 1, RateAfterFreeDays: 900m),
            new(SeedCodes.TrkToll,        PricingModel.Flat,       2_600m,  "per trip",     EstimatedBuyingAmount: 2_600m),
            new(SeedCodes.TrkFuel,        PricingModel.Percentage, 0m,      "% of linehaul", EstimatedBuyingAmount: null, Percentage: 10m, PercentageBase: "BAS"),
        ]),

        // 2) Import sea FCL: Shanghai → Manila
        new("China - Manila Import FCL 2026", FreightMode.Import, "USD", "CNSHA", "PHMNL", true,
        [
            new(SeedCodes.OceanFreight40, PricingModel.PerUnit,    1_850m, "per 40'HC",         EstimatedBuyingAmount: 1_600m),
            new(SeedCodes.Baf,            PricingModel.PerUnit,    180m,   "per container",     EstimatedBuyingAmount: 165m),
            new(SeedCodes.ThcDestination, PricingModel.PerUnit,    150m,   "per container",     EstimatedBuyingAmount: 130m),
            new(SeedCodes.DocImport,      PricingModel.Flat,       45m,    "per shipment",      EstimatedBuyingAmount: 25m),
            new(SeedCodes.DeliveryOrder,  PricingModel.Flat,       60m,    "per shipment",      EstimatedBuyingAmount: 40m),
            new(SeedCodes.CustomsImport,  PricingModel.Flat,       150m,   "per entry",         EstimatedBuyingAmount: 95m, Minimum: 150m),
            new(SeedCodes.Demurrage,      PricingModel.PerUnit,    75m,    "per container/day", EstimatedBuyingAmount: null, FreeDays: 7, RateAfterFreeDays: 75m),
            new(SeedCodes.Insurance,      PricingModel.Percentage, 0m,     "% of CIF",          EstimatedBuyingAmount: null, Minimum: 25m, Percentage: 0.15m, PercentageBase: "CIF"),
        ]),

        // 3) Export air: Manila (NAIA) → Los Angeles (LAX)
        new("Manila - Los Angeles Export Air 2026", FreightMode.Export, "USD", "PHMNL", "USLAX", false,
        [
            new(SeedCodes.AirFreight,    PricingModel.Breakpoint, 0m,    "per kg",       EstimatedBuyingAmount: 4.40m, Minimum: 85m, BreakpointJson: AirBreakpoints, BreakpointMode: BreakpointMode.AllUnits),
            new(SeedCodes.AirFuel,       PricingModel.PerUnit,    0.45m, "per kg",       EstimatedBuyingAmount: 0.40m),
            new(SeedCodes.AirSecurity,   PricingModel.PerUnit,    0.12m, "per kg",       EstimatedBuyingAmount: 0.10m, Minimum: 15m),
            new(SeedCodes.ThcOrigin,     PricingModel.PerUnit,    0.08m, "per kg",       EstimatedBuyingAmount: 0.06m, Minimum: 25m),
            new(SeedCodes.CustomsExport, PricingModel.Flat,       95m,   "per entry",    EstimatedBuyingAmount: 60m),
            new(SeedCodes.AwbFee,        PricingModel.Flat,       35m,   "per shipment", EstimatedBuyingAmount: 15m),
            new(SeedCodes.HazmatAir,     PricingModel.Flat,       120m,  "per shipment", EstimatedBuyingAmount: 85m),
            new(SeedCodes.Insurance,     PricingModel.Percentage, 0m,    "% of CIF",     EstimatedBuyingAmount: null, Minimum: 25m, Percentage: 0.20m, PercentageBase: "CIF"),
        ]),

        // 4) Triangular sea: Shenzhen (shipper) → Long Beach (consignee), handled via Manila
        new("Shenzhen - Long Beach Triangular FCL (via Manila) 2026", FreightMode.Triangular, "USD", "CNSZX", "USLGB", true,
        [
            new(SeedCodes.TriFreight,   PricingModel.PerUnit, 3_200m, "per 40'HC",     EstimatedBuyingAmount: 2_850m),
            new(SeedCodes.Baf,          PricingModel.PerUnit, 220m,   "per container", EstimatedBuyingAmount: 200m),
            new(SeedCodes.TriThcOrigin, PricingModel.PerUnit, 140m,   "per container", EstimatedBuyingAmount: 120m),
            new(SeedCodes.TriHandling,  PricingModel.Flat,    250m,   "per shipment",  EstimatedBuyingAmount: 150m),
            new(SeedCodes.BlSwitch,     PricingModel.Flat,    75m,    "per B/L",       EstimatedBuyingAmount: 45m),
            new(SeedCodes.IsfFiling,    PricingModel.Flat,    60m,    "per filing",    EstimatedBuyingAmount: 35m),
            new(SeedCodes.TriDiscount,  PricingModel.Flat,    100m,   "per shipment",  EstimatedBuyingAmount: null),
        ]),

        // 5) Metro Manila - Batangas Port 40'FT Domestic Trucking
        new("Metro Manila - Batangas Port 40'FT Domestic Trucking 2026", FreightMode.Domestic, "PHP", "PHMNL", "PHBTG", false,
        [
            new(SeedCodes.TrkLinehaul,   PricingModel.Flat,       8_500m, "per trip",   EstimatedBuyingAmount: 6_800m),
            new(SeedCodes.TrkFuel,       PricingModel.Percentage, 0m,     "% of TRK",   EstimatedBuyingAmount: null, Percentage: 8m, PercentageBase: "BAS"),
            new(SeedCodes.TrkToll,       PricingModel.Flat,       650m,   "per trip",   EstimatedBuyingAmount: 650m),
            new(SeedCodes.TrkLabor,      PricingModel.PerUnit,    150m,   "per hour",   EstimatedBuyingAmount: 110m),
            new(SeedCodes.TrkWarehouse,  PricingModel.PerUnit,    45m,    "per pallet", EstimatedBuyingAmount: 30m),
            new(SeedCodes.TrkAfterHours, PricingModel.Flat,       2_000m, "per trip",   EstimatedBuyingAmount: 1_500m),
        ]),

        // 6) Domestic FCL: Manila → Cebu (PHP)
        new("Manila - Cebu Domestic FCL Q4 2026", FreightMode.Domestic, "PHP", "PHMNL", "PHCEB", true,
        [
            new(SeedCodes.FfOfr,    PricingModel.PerUnit,    42_000m, "per container",     EstimatedBuyingAmount: 36_000m),
            new(SeedCodes.FfBaf,    PricingModel.Percentage, 0m,      "% of base freight", EstimatedBuyingAmount: null, Minimum: 1_500m, Percentage: 12m, PercentageBase: "BAS"),
            new(SeedCodes.FfThcOrg, PricingModel.PerUnit,    5_200m,  "per container",     EstimatedBuyingAmount: 4_400m),
            new(SeedCodes.FfThcDst, PricingModel.PerUnit,    4_800m,  "per container",     EstimatedBuyingAmount: 4_000m),
            new(SeedCodes.FfDoc,    PricingModel.Flat,       1_200m,  "per BL",            EstimatedBuyingAmount: 700m),
            new(SeedCodes.FfDet,    PricingModel.PerUnit,    1_500m,  "per container-day", EstimatedBuyingAmount: null, FreeDays: 5, RateAfterFreeDays: 1_500m),
            new(SeedCodes.FfIns,    PricingModel.Percentage, 0m,      "% of CIF",          EstimatedBuyingAmount: null, Minimum: 500m, Percentage: 0.35m, PercentageBase: "CIF"),
            new(SeedCodes.FfDisc,   PricingModel.Flat,       2_000m,  "per shipment",      EstimatedBuyingAmount: null),
        ]),

        // 7) Import ocean: Shanghai → Manila (USD)
        new("Shanghai - Manila Import FCL Ocean Q4 2026", FreightMode.Import, "USD", "CNSHA", "PHMNL", true,
        [
            new(SeedCodes.FfOfr, PricingModel.PerUnit, 1_650m, "per container",     EstimatedBuyingAmount: 1_450m),
            new(SeedCodes.FfBaf, PricingModel.PerUnit, 220m,   "per container",     EstimatedBuyingAmount: 200m),
            new(SeedCodes.FfPss, PricingModel.PerUnit, 300m,   "per container",     EstimatedBuyingAmount: 260m),
            new(SeedCodes.FfDet, PricingModel.PerUnit, 45m,    "per container-day", EstimatedBuyingAmount: null, FreeDays: 7, RateAfterFreeDays: 45m),
        ]),

        // 8) Import local charges: Manila port (PHP). Mixed with tariff 7 on one quote via fxRate.
        new("Manila Port Import Local Charges Q4 2026", FreightMode.Import, "PHP", null, "PHMNL", false,
        [
            new(SeedCodes.FfThcDst, PricingModel.PerUnit, 9_200m, "per container", EstimatedBuyingAmount: 7_800m),
            new(SeedCodes.FfArr,    PricingModel.PerUnit, 2_900m, "per container", EstimatedBuyingAmount: 2_500m),
            new(SeedCodes.FfDof,    PricingModel.Flat,    1_500m, "per shipment",  EstimatedBuyingAmount: 1_000m),
            new(SeedCodes.FfCci,    PricingModel.Flat,    4_500m, "per shipment",  EstimatedBuyingAmount: 3_000m),
            new(SeedCodes.FfTrk,    PricingModel.PerUnit, 9_500m, "per trip",      EstimatedBuyingAmount: 7_800m),
        ]),

        // 9) Export air: Manila → Los Angeles (USD), AllUnits + Incremental breakpoints
        new("Manila - Los Angeles Air Export Q4 2026", FreightMode.Export, "USD", "PHMNL", "USLAX", false,
        [
            new(SeedCodes.FfAfr, PricingModel.Breakpoint, 4.20m, "per chargeable kg", EstimatedBuyingAmount: 3.55m, Minimum: 90m,
                BreakpointJson: ExportAirBreakpoints, BreakpointMode: BreakpointMode.AllUnits),
            new(SeedCodes.FfFsc, PricingModel.PerUnit,    0.55m, "per kg",            EstimatedBuyingAmount: 0.48m),
            new(SeedCodes.FfSsc, PricingModel.PerUnit,    0.15m, "per kg",            EstimatedBuyingAmount: 0.12m, Minimum: 15m),
            new(SeedCodes.FfAwb, PricingModel.Flat,       35m,   "per shipment",      EstimatedBuyingAmount: 15m),
            new(SeedCodes.FfCec, PricingModel.Flat,       120m,  "per shipment",      EstimatedBuyingAmount: 75m),
            new(SeedCodes.FfHdl, PricingModel.Breakpoint, 0.25m, "per kg",            EstimatedBuyingAmount: 0.18m, Minimum: 20m,
                BreakpointJson: HandlingBreakpoints, BreakpointMode: BreakpointMode.Incremental),
            new(SeedCodes.FfIns, PricingModel.Percentage, 0m,    "% of CIF",          EstimatedBuyingAmount: null, Minimum: 10m, Percentage: 0.30m, PercentageBase: "CIF"),
        ]),

        // 10) Trucking (lane mode): Metro Manila → Batangas (PHP)
        new("Metro Manila - Batangas Trucking (10W) Q4 2026", FreightMode.Trucking, "PHP", "PHMNL", "PHBTG", false,
        [
            new(SeedCodes.FfLhl,  PricingModel.PerUnit,    18_500m, "per trip", EstimatedBuyingAmount: 15_000m),
            new(SeedCodes.FfFsc,  PricingModel.Percentage, 0m,      "% of base", EstimatedBuyingAmount: null, Minimum: 500m, Percentage: 8m, PercentageBase: "BAS"),
            new(SeedCodes.FfDrp,  PricingModel.PerUnit,    1_200m,  "per drop", EstimatedBuyingAmount: 900m),
            new(SeedCodes.FfWait, PricingModel.PerUnit,    450m,    "per hour", EstimatedBuyingAmount: null, FreeDays: 4, RateAfterFreeDays: 450m),
        ]),

        // 11) Reefer (lane mode): Metro Manila → Davao (PHP)
        new("Metro Manila - Davao Reefer Q4 2026", FreightMode.Reefer, "PHP", "PHMNL", "PHDVO", false,
        [
            new(SeedCodes.FfLhl, PricingModel.PerUnit,    62_000m, "per trip", EstimatedBuyingAmount: 52_000m),
            new(SeedCodes.FfFsc, PricingModel.Percentage, 0m,      "% of base", EstimatedBuyingAmount: null, Percentage: 10m, PercentageBase: "BAS"),
            new(SeedCodes.FfRgs, PricingModel.PerUnit,    7_500m,  "per trip", EstimatedBuyingAmount: 6_000m),
            new(SeedCodes.FfTlm, PricingModel.PerUnit,    1_800m,  "per trip", EstimatedBuyingAmount: 1_200m),
            new(SeedCodes.FfDrp, PricingModel.PerUnit,    1_500m,  "per drop", EstimatedBuyingAmount: 1_100m),
        ]),
    ];

    /// <summary>Splits the flat <c>CreatedTariffRates</c> list back into per-tariff lists.</summary>
    public static SeededTariffRates Slice(IEnumerable<TariffRate> allRates)
    {
        var all = allRates.ToList();
        var expected = Tariffs.Sum(t => t.Rates.Count);
        if (all.Count != expected)
            throw new InvalidOperationException(
                $"Expected {expected} seeded tariff rates but found {all.Count}; TariffSeeder and TariffCatalog are out of sync.");

        var offset = 0;
        IReadOnlyList<TariffRate> Next(int index)
        {
            var slice = all.Skip(offset).Take(Tariffs[index].Rates.Count).ToList();
            offset += slice.Count;
            return slice;
        }

        return new SeededTariffRates(
            Next(0), Next(1), Next(2), Next(3), Next(4), Next(5),
            Next(6), Next(7), Next(8), Next(9), Next(10), Next(11));
    }

    /// <summary>Finds a rate by charge code inside one tariff's rate list (codes are unique per tariff).</summary>
    public static TariffRate Rate(this IReadOnlyList<TariffRate> tariffRates, string code) =>
        tariffRates.SingleOrDefault(r => r.ChargeCode?.Code == code)
        ?? throw new InvalidOperationException($"Seeded tariff has no rate for charge code '{code}'.");
}
