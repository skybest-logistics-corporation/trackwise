import { DecimalPipe, UpperCasePipe } from "@angular/common";
import { Component, computed, effect, inject, input, output, signal } from "@angular/core";
import { FormsModule } from "@angular/forms";
import {
  Api,
  getChargeCodes,
  getTariffById,
  getTariffs,
  type AddQuotationLineItem,
  type ChargeCategory,
  type ChargeCodeDto,
  type MarkupType,
  type OperationalServiceType,
  type QuotationDto,
  type TariffDto,
  type TariffRateDto,
} from "@trackwise/shared/api";
import { markupTypeOptions } from "@trackwise/shared/api/enums";
import {
  Alert,
  Grid,
  Icon,
  Stack,
  Surface,
  Typography,
  UiButton,
  UiFormField,
  UiNumberField,
  UiSelectField,
  UiTextField,
} from "@trackwise/shared/ui";

export type QuotationLineFormValue = AddQuotationLineItem;

export interface EditableQuotationLine {
  id: string;
  tariffId: string;
  tariffName: string;
  tariffRateId: string;
  chargeCodeId: string;
  chargeCode: string;
  chargeName: string;
  category: ChargeCategory;
  pricingModel: string;
  percentage: number | null;
  percentageBase: string | null;
  unit: string | null;
  tariffBaseAmount: number | null;
  baseAmount: number | null; // Explicit valuation or override
  serviceType: OperationalServiceType | null;
  quantity: number | null;
  markupType: MarkupType | null;
  markupValue: number | null;
  fxRate: number | null;
  notes: string;
}

interface LineEstimate {
  cost: number | null;
  sell: number | null;
}

interface LineGroup {
  id: string;
  name: string;
  loading: boolean;
  hasRates: boolean;
  items: { line: EditableQuotationLine; number: number }[];
  invalidCount: number;
}

@Component({
  selector: "app-quotation-line-form",
  templateUrl: "./quotation-line-form.html",
  imports: [
    Alert,
    DecimalPipe,
    FormsModule,
    Grid,
    Icon,
    Stack,
    Surface,
    Typography,
    UiButton,
    UiFormField,
    UiNumberField,
    UiSelectField,
    UiTextField,
    UpperCasePipe,
  ],
})
export class QuotationLineForm {
  private readonly api = inject(Api);

  public readonly save = output<QuotationLineFormValue[]>();
  public readonly dismiss = output<void>();
  public readonly quotation = input<QuotationDto | null>(null);

  protected readonly tariffs = signal<TariffDto[]>([]);
  protected readonly chargeCodesById = signal<Map<string, ChargeCodeDto>>(new Map());

  /** Tariffs the user has picked, in the order they were added. */
  protected readonly selectedTariffIds = signal<string[]>([]);
  protected readonly ratesByTariff = signal<ReadonlyMap<string, TariffRateDto[]>>(new Map());
  protected readonly loadingTariffIds = signal<ReadonlySet<string>>(new Set());

  /** Draft mode: tariff waiting to be added with the "Add tariff" button. */
  protected readonly pendingTariffId = signal<string | null>(null);
  protected readonly selectedAdditionalRateId = signal<string | null>(null);

  protected readonly lines = signal<EditableQuotationLine[]>([]);
  private readonly expandedIds = signal<ReadonlySet<string>>(new Set());

  protected readonly bulkMarkupType = signal<MarkupType | null>("percentage");
  protected readonly bulkMarkupValue = signal<number | null>(0);

  protected readonly serviceTypeOptions: { label: string; value: OperationalServiceType }[] = [
    { label: "Standard", value: "standard" },
    { label: "Line Haul", value: "line_haul" },
    { label: "Pre-Cooling & PTI", value: "pre_cooling_and_pti" },
    { label: "Genset Fuel", value: "genset_fuel" },
    { label: "Continuous Telematics", value: "continuous_telematics" },
    { label: "Cold Storage Plugin", value: "cold_storage_plugin" },
    { label: "Terminal Handling (Origin)", value: "terminal_handling_origin" },
    { label: "Terminal Handling (Destination)", value: "terminal_handling_destination" },
    { label: "Customs Clearance (Export)", value: "customs_clearance_export" },
    { label: "Customs Clearance (Import)", value: "customs_clearance_import" },
    { label: "Bunker Adjustment Factor (BAF)", value: "bunker_adjustment_factor" },
    { label: "Security Filing (ISF)", value: "security_filing_isf" },
    { label: "Demurrage & Detention", value: "demurrage_detention" },
    { label: "Liftgate Pickup/Delivery", value: "liftgate_pickup_delivery" },
    { label: "HAZMAT Surcharge", value: "hazmat_surcharge" },
  ];

  protected readonly markupTypes = markupTypeOptions;

  constructor() {
    effect(() => {
      const quotation = this.quotation();

      if (!quotation) {
        return;
      }

      void this.loadData(quotation);
    });
  }

  // --- Quotation status --------------------------------------------------

  /**
   * Multiple tariffs are only allowed on a draft quotation (the API rejects lines on anything else).
   * Compared case-insensitively because generated enums are usually lower-case strings.
   */
  protected readonly isDraft = computed(
    () => String(this.quotation()?.status ?? "").toLowerCase() === "draft",
  );

  protected readonly showNotDraftWarning = computed(
    () => this.quotation() != null && !this.isDraft(),
  );

  // --- Tariff selection --------------------------------------------------

  private readonly tariffNamesById = computed(
    () => new Map(this.tariffs().map((t) => [t.id ?? "", t.name ?? "Tariff"])),
  );

  /** Draft: tariffs not picked yet. Otherwise: every tariff (the pick replaces the current one). */
  protected readonly tariffOptions = computed(() => {
    const picked = new Set(this.selectedTariffIds());
    const all = this.tariffs();
    const available = this.isDraft() ? all.filter((t) => !picked.has(t.id ?? "")) : all;

    return available.map((tariff) => ({ value: tariff.id, label: tariff.name }));
  });

  /** Value shown by the select when only one tariff is allowed. */
  protected readonly singleTariffValue = computed(() => this.selectedTariffIds()[0] ?? null);

  protected readonly isLoadingTariff = computed(() => this.loadingTariffIds().size > 0);

  protected readonly groups = computed<LineGroup[]>(() => {
    const issues = this.lineIssues();
    const lines = this.lines();
    const names = this.tariffNamesById();
    const loading = this.loadingTariffIds();
    const rates = this.ratesByTariff();
    let counter = 0;

    return this.selectedTariffIds().map((id) => {
      const items = lines
        .filter((l) => l.tariffId === id)
        .map((line) => ({ line, number: ++counter }));

      return {
        id,
        name: names.get(id) ?? "Tariff",
        loading: loading.has(id),
        hasRates: (rates.get(id)?.length ?? 0) > 0,
        items,
        invalidCount: items.filter((i) => (issues.get(i.line.id)?.length ?? 0) > 0).length,
      };
    });
  });

  /** Charges that were removed from any selected tariff and can be put back. */
  protected readonly availableRateOptions = computed(() => {
    const codes = this.chargeCodesById();
    const names = this.tariffNamesById();
    const used = new Set(this.lines().map((l) => l.tariffRateId));
    const multi = this.selectedTariffIds().length > 1;
    const options: { label: string; value: string }[] = [];

    for (const tariffId of this.selectedTariffIds()) {
      for (const r of this.ratesByTariff().get(tariffId) ?? []) {
        if (used.has(r.id ?? "")) continue;

        const code = codes.get(r.chargeCodeId ?? "");
        const labelPrefix = code?.code ? `${code.code} — ` : "";
        const rateSummary =
          r.pricingModel === "percentage"
            ? `${r.percentage}% ${r.percentageBase ?? "BAS"}`
            : `${r.baseAmount} / ${r.unit ?? "unit"}`;
        const tariffPrefix = multi ? `${names.get(tariffId) ?? "Tariff"}: ` : "";

        options.push({
          label: `${tariffPrefix}${labelPrefix}${code?.name ?? "Unknown Charge"} (${rateSummary})`,
          value: r.id ?? "",
        });
      }
    }

    return options;
  });

  // --- Validation & estimates -------------------------------------------

  /** Per-line list of human-readable problems. Empty array = line is valid. */
  protected readonly lineIssues = computed(() => {
    const result = new Map<string, string[]>();

    for (const l of this.lines()) {
      const issues: string[] = [];
      if (!l.tariffRateId) issues.push("Missing tariff rate");
      if (l.serviceType == null) issues.push("Select a service type");
      if (l.quantity == null || l.quantity <= 0) issues.push("Quantity must be greater than 0");
      if (l.markupType == null) issues.push("Choose a markup type");
      if (l.markupValue == null || l.markupValue < 0) issues.push("Enter a markup of 0 or more");
      if (this.isCustomPercentage(l) && !(l.baseAmount != null && l.baseAmount > 0)) {
        issues.push(`Enter a valuation base (${l.percentageBase || "custom"})`);
      }
      result.set(l.id, issues);
    }

    return result;
  });

  protected readonly invalidCount = computed(
    () => [...this.lineIssues().values()].filter((i) => i.length > 0).length,
  );

  protected readonly isValid = computed(() => this.lines().length > 0 && this.invalidCount() === 0);

  protected readonly canSubmit = computed(
    () => this.isValid() && this.isDraft() && !this.isLoadingTariff(),
  );

  /**
   * Same charge code coming from more than one selected tariff is legal, but it's the most likely
   * way to double-charge a customer, so flag it (non-blocking).
   */
  protected readonly crossTariffNotes = computed(() => {
    const lines = this.lines();
    const tariffsByCode = new Map<string, Map<string, string>>();

    for (const l of lines) {
      if (!l.chargeCodeId) continue;
      const m = tariffsByCode.get(l.chargeCodeId) ?? new Map<string, string>();
      m.set(l.tariffId, l.tariffName);
      tariffsByCode.set(l.chargeCodeId, m);
    }

    const result = new Map<string, string>();
    for (const l of lines) {
      const m = tariffsByCode.get(l.chargeCodeId);
      if (!m || m.size < 2) continue;
      const others = [...m].filter(([id]) => id !== l.tariffId).map(([, name]) => name);
      result.set(l.id, `Same charge is also in ${others.join(", ")}`);
    }

    return result;
  });

  /**
   * Indicative figures only. Lines whose base comes from the quotation (BAS/CIF)
   * can't be priced here and are reported as "unpriced". FX is not applied.
   */
  protected readonly lineEstimates = computed(() => {
    const result = new Map<string, LineEstimate>();

    for (const l of this.lines()) {
      let cost: number | null = null;

      if (this.isPercentageLine(l)) {
        if (l.baseAmount != null && l.percentage != null) {
          cost = (l.baseAmount * l.percentage) / 100;
        }
      } else {
        const base = l.baseAmount ?? l.tariffBaseAmount;
        if (base != null && l.quantity != null) cost = base * l.quantity;
      }

      let sell: number | null = null;
      if (cost != null && l.markupType != null && l.markupValue != null) {
        sell =
          l.markupType === "percentage" ? cost * (1 + l.markupValue / 100) : cost + l.markupValue;
      }

      result.set(l.id, { cost, sell });
    }

    return result;
  });

  protected readonly totals = computed(() => {
    let sell = 0;
    let priced = 0;
    let unpriced = 0;

    for (const e of this.lineEstimates().values()) {
      if (e.sell == null) {
        unpriced++;
      } else {
        sell += e.sell;
        priced++;
      }
    }

    return { sell, priced, unpriced };
  });

  // --- Data loading ------------------------------------------------------

  private async loadData(quotation: QuotationDto): Promise<void> {
    const [tariffsResult, chargeCodes] = await Promise.all([
      this.api.invoke(getTariffs, {
        Mode: quotation.freightMode,
        OnlyActive: true,
        PageSize: 100,
      }),
      this.api.invoke(getChargeCodes, { OnlyActive: true }),
    ]);

    this.tariffs.set(tariffsResult.items ?? []);
    this.chargeCodesById.set(new Map(chargeCodes.map((c) => [c.id ?? "", c])));
  }

  // --- Tariff actions ----------------------------------------------------

  /** Draft mode: user clicked "Add tariff". */
  async addPendingTariff(): Promise<void> {
    const id = this.pendingTariffId();
    if (!id || !this.isDraft() || this.selectedTariffIds().includes(id)) return;

    this.pendingTariffId.set(null);
    await this.addTariff(id);
  }

  /** Non-draft mode: the select replaces the current tariff. */
  async replaceTariff(id: string | null): Promise<void> {
    if (id === this.selectedTariffIds()[0]) return;

    // Don't silently discard work. Swap `confirm` for your app's confirmation dialog service.
    if (
      this.lines().length > 0 &&
      !confirm("Changing the tariff will replace the charges below. Continue?")
    ) {
      return;
    }

    this.clearAll();
    if (id) await this.addTariff(id);
  }

  removeTariff(tariffId: string): void {
    this.selectedTariffIds.update((ids) => ids.filter((id) => id !== tariffId));
    this.ratesByTariff.update((m) => {
      const next = new Map(m);
      next.delete(tariffId);
      return next;
    });
    this.lines.update((items) => items.filter((l) => l.tariffId !== tariffId));
  }

  private clearAll(): void {
    this.selectedTariffIds.set([]);
    this.ratesByTariff.set(new Map());
    this.lines.set([]);
    this.expandedIds.set(new Set());
    this.selectedAdditionalRateId.set(null);
  }

  private async addTariff(tariffId: string): Promise<void> {
    this.selectedTariffIds.update((ids) => [...ids, tariffId]);
    this.loadingTariffIds.update((s) => new Set(s).add(tariffId));

    try {
      const tariff = await this.api.invoke(getTariffById, { id: tariffId });

      // The user removed this tariff while it was loading.
      if (!this.selectedTariffIds().includes(tariffId)) return;

      const rates = tariff.rates ?? [];
      const tariffName = tariff.name ?? this.tariffNamesById().get(tariffId) ?? "Tariff";
      const codes = this.chargeCodesById();

      this.ratesByTariff.update((m) => new Map(m).set(tariffId, rates));

      const newLines = rates.map((rate) =>
        this.createEditableLine(tariffId, tariffName, rate, codes.get(rate.chargeCodeId ?? "")),
      );

      this.lines.update((items) => [...items, ...newLines]);
      this.expandedIds.update((s) => {
        const next = new Set(s);
        for (const l of newLines) if (this.isCustomPercentage(l)) next.add(l.id);
        return next;
      });
    } catch (error) {
      // Don't leave an empty chip behind if the tariff couldn't be loaded.
      this.selectedTariffIds.update((ids) => ids.filter((id) => id !== tariffId));
      throw error;
    } finally {
      this.loadingTariffIds.update((s) => {
        const next = new Set(s);
        next.delete(tariffId);
        return next;
      });
    }
  }

  // --- Line actions ------------------------------------------------------

  addRateLine(rateId: string | null): void {
    if (!rateId) return;

    for (const tariffId of this.selectedTariffIds()) {
      const rate = this.ratesByTariff()
        .get(tariffId)
        ?.find((r) => r.id === rateId);
      if (!rate) continue;

      const code = this.chargeCodesById().get(rate.chargeCodeId ?? "");
      const tariffName = this.tariffNamesById().get(tariffId) ?? "Tariff";
      const newLine = this.createEditableLine(tariffId, tariffName, rate, code);

      this.lines.update((items) => [...items, newLine]);
      this.expandedIds.update((s) => new Set(s).add(newLine.id));
      break;
    }

    this.selectedAdditionalRateId.set(null);
  }

  duplicateLine(id: string): void {
    const source = this.lines().find((l) => l.id === id);
    if (!source) return;

    const copy: EditableQuotationLine = { ...source, id: crypto.randomUUID() };
    this.lines.update((items) => {
      const index = items.findIndex((l) => l.id === id);
      return [...items.slice(0, index + 1), copy, ...items.slice(index + 1)];
    });
    this.expandedIds.update((s) => new Set(s).add(copy.id));
  }

  removeLine(id: string): void {
    this.lines.update((items) => items.filter((l) => l.id !== id));
    this.expandedIds.update((s) => {
      const next = new Set(s);
      next.delete(id);
      return next;
    });
  }

  updateLine<K extends keyof EditableQuotationLine>(
    id: string,
    key: K,
    value: EditableQuotationLine[K],
  ): void {
    this.lines.update((items) =>
      items.map((line) => (line.id === id ? { ...line, [key]: value } : line)),
    );
  }

  /** Applies the bulk markup to every line, or only to one tariff's lines. */
  applyBulkMarkup(tariffId?: string): void {
    const type = this.bulkMarkupType() ?? "percentage";
    const value = this.bulkMarkupValue() ?? 0;
    this.lines.update((items) =>
      items.map((line) =>
        tariffId && line.tariffId !== tariffId
          ? line
          : { ...line, markupType: type, markupValue: value },
      ),
    );
  }

  // --- Expand / collapse -------------------------------------------------

  protected isExpanded(id: string): boolean {
    return this.expandedIds().has(id);
  }

  protected toggleLine(id: string): void {
    this.expandedIds.update((s) => {
      const next = new Set(s);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected expandAll(): void {
    this.expandedIds.set(new Set(this.lines().map((l) => l.id)));
  }

  protected collapseAll(): void {
    this.expandedIds.set(new Set());
  }

  // --- Template helpers --------------------------------------------------

  protected isPercentageLine(line: EditableQuotationLine): boolean {
    return line.pricingModel?.toLowerCase() === "percentage";
  }

  protected isCustomPercentage(line: EditableQuotationLine): boolean {
    return (
      this.isPercentageLine(line) && line.percentageBase !== "BAS" && line.percentageBase !== "CIF"
    );
  }

  protected serviceTypeLabel(value: OperationalServiceType | null): string {
    return this.serviceTypeOptions.find((o) => o.value === value)?.label ?? "No service type";
  }

  private createEditableLine(
    tariffId: string,
    tariffName: string,
    rate: TariffRateDto,
    code?: ChargeCodeDto,
  ): EditableQuotationLine {
    const isPercentage = rate.pricingModel?.toLowerCase() === "percentage";

    return {
      id: crypto.randomUUID(),
      tariffId,
      tariffName,
      tariffRateId: rate.id ?? "",
      chargeCodeId: rate.chargeCodeId ?? "",
      chargeCode: code?.code ?? "",
      chargeName: code?.name ?? "Unknown Charge",
      category: code?.category ?? "base",
      pricingModel: rate.pricingModel ?? "flat",
      percentage: rate.percentage ?? null,
      percentageBase: rate.percentageBase ?? null,
      unit: rate.unit ?? null,
      tariffBaseAmount: rate.baseAmount ?? null,
      baseAmount: isPercentage ? null : (rate.baseAmount ?? null),
      serviceType: this.suggestServiceType(code),
      quantity: 1,
      markupType: this.bulkMarkupType(),
      markupValue: this.bulkMarkupValue(),
      fxRate: null,
      notes: "",
    };
  }

  private suggestServiceType(code?: ChargeCodeDto): OperationalServiceType {
    if (!code) return "standard";
    const text = `${code.code ?? ""} ${code.name ?? ""} ${code.description ?? ""}`.toLowerCase();

    if (text.includes("baf") || text.includes("bunker")) return "bunker_adjustment_factor";
    if (text.includes("isf") || text.includes("security")) return "security_filing_isf";
    if (text.includes("customs") && (text.includes("exp") || text.includes("export")))
      return "customs_clearance_export";
    if (text.includes("customs")) return "customs_clearance_import";
    if (text.includes("thc") || text.includes("terminal")) {
      return text.includes("dest") || text.includes("pod")
        ? "terminal_handling_destination"
        : "terminal_handling_origin";
    }
    if (text.includes("demurrage") || text.includes("detention")) return "demurrage_detention";
    if (text.includes("liftgate") || text.includes("tailgate")) return "liftgate_pickup_delivery";
    if (text.includes("telematics") || text.includes("tracking") || text.includes("iot"))
      return "continuous_telematics";
    if (text.includes("genset")) return "genset_fuel";
    if (text.includes("pre-cooling") || text.includes("pti")) return "pre_cooling_and_pti";
    if (text.includes("cold storage") || text.includes("plug")) return "cold_storage_plugin";
    if (text.includes("line haul") || text.includes("freight")) return "line_haul";

    return "standard";
  }

  onSubmit(): void {
    if (!this.canSubmit()) return;

    // Emit in tariff order so each tariff's charges stay together once saved.
    const ordered = this.groups().flatMap((g) => g.items.map((i) => i.line));

    const values: QuotationLineFormValue[] = ordered.map((line) => {
      let finalBaseAmount: number | null = line.baseAmount;

      // If user left baseAmount empty for flat/per_unit, fall back to the tariff's original baseAmount
      if (finalBaseAmount == null && !this.isPercentageLine(line)) {
        finalBaseAmount = line.tariffBaseAmount;
      }

      return {
        tariffRateId: line.tariffRateId,
        serviceType: line.serviceType!,
        quantity: line.quantity!,
        baseAmount: finalBaseAmount,
        markupType: line.markupType!,
        markupValue: line.markupValue!,
        fxRate: line.fxRate,
        notes: line.notes.trim() || null,
      };
    });

    this.save.emit(values);
  }
}
