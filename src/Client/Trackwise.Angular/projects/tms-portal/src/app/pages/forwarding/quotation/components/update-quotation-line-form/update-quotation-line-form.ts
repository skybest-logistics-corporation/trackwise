import { Component, computed, input, linkedSignal, output } from "@angular/core";
import type {
  MarkupType,
  QuotationLineDto,
  UpdateQuotationLineRequest,
} from "@trackwise/shared/api";
import {
  Grid,
  UiButton,
  UiFormField,
  UiNumberField,
  UiSelectField,
  UiTextareaField,
} from "@trackwise/shared/ui";

/** `undefined` = keep the current value, `null` = clear it. */
export interface LineChanges {
  quantity?: number;
  baseAmount?: number | null;
  markupType?: MarkupType;
  markupValue?: number;
  fxRate?: number | null;
  notes?: string | null;
}

export interface LineUpdate {
  lineId: string;
  body: UpdateQuotationLineRequest;
}

export function needsValuationBase(line: QuotationLineDto): boolean {
  return !!line.unit?.includes("%");
}

/** The endpoint takes every field, so unchanged ones are filled in from the line itself. */
export function toUpdateRequest(
  line: QuotationLineDto,
  changes: LineChanges,
): UpdateQuotationLineRequest {
  return {
    quantity: changes.quantity ?? Number(line.quantity),
    baseAmount:
      changes.baseAmount !== undefined && needsValuationBase(line)
        ? changes.baseAmount
        : (line.buyingAmount ?? null),
    markupType: changes.markupType ?? line.markupType!,
    markupValue: changes.markupValue ?? Number(line.markupValue),
    fxRate: changes.fxRate !== undefined ? changes.fxRate : (line.fxRate ?? null),
    notes: changes.notes !== undefined ? changes.notes : (line.notes ?? null),
  };
}

interface LineSummary {
  id: string;
  index: number;
  title: string;
  category: string;
  rate: string;
  details: string;
}

const toNumber = (value: unknown): number | null =>
  value == null || value === "" ? null : Number(value);

const formatText = (text?: string | null): string =>
  text ? text.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase()) : "-";

@Component({
  selector: "app-update-quotation-line-form",
  templateUrl: "./update-quotation-line-form.html",
  imports: [Grid, UiButton, UiFormField, UiNumberField, UiSelectField, UiTextareaField],
})
export class UpdateQuotationLineForm {
  public readonly lines = input.required<QuotationLineDto[]>();
  public readonly busy = input(false);
  public readonly save = output<LineChanges>();
  public readonly dismiss = output<void>();

  protected readonly isBulk = computed(() => this.lines().length > 1);
  private readonly single = computed(() => (this.lines().length === 1 ? this.lines()[0] : null));

  protected readonly markupOptions: { label: string; value: MarkupType }[] = [
    { label: "Percentage", value: "percentage" },
    { label: "Flat", value: "flat" },
  ];

  // Prefilled for a single line, blank for several. Re-seeds if `lines` changes.
  protected readonly quantity = linkedSignal<number | null>(() =>
    toNumber(this.single()?.quantity),
  );
  protected readonly baseAmount = linkedSignal<number | null>(() =>
    toNumber(this.single()?.buyingAmount),
  );
  protected readonly markupType = linkedSignal<MarkupType | null>(
    () => this.single()?.markupType ?? null,
  );
  protected readonly markupValue = linkedSignal<number | null>(() =>
    toNumber(this.single()?.markupValue),
  );
  protected readonly fxRate = linkedSignal<number | null>(() => toNumber(this.single()?.fxRate));
  protected readonly notes = linkedSignal<string>(() => this.single()?.notes ?? "");

  protected readonly showValuationBase = computed(() => this.lines().some(needsValuationBase));

  protected readonly valuationBaseHint = computed(() => {
    const line = this.single();
    const base = line?.unit?.match(/%\s*of\s+(.+)$/i)?.[1]?.trim();
    return base
      ? `Taken from the quotation ${base}. Enter a value to override.`
      : "Taken from the quotation. Enter a value to override.";
  });

  protected readonly markupHint = computed(() =>
    this.markupType() === "percentage" ? "Margin in %" : "Flat amount added to the buying price",
  );

  /** Single line: every field is sent (null clears). Several lines: only the filled fields. */
  private readonly changes = computed<LineChanges>(() => {
    const notes = this.notes().trim();

    if (!this.isBulk()) {
      return {
        quantity: this.quantity() ?? undefined,
        baseAmount: this.baseAmount(),
        markupType: this.markupType() ?? undefined,
        markupValue: this.markupValue() ?? undefined,
        fxRate: this.fxRate(),
        notes: notes || null,
      };
    }

    const changes: LineChanges = {};
    if (this.quantity() != null) changes.quantity = this.quantity()!;
    if (this.baseAmount() != null) changes.baseAmount = this.baseAmount();
    if (this.markupType() != null) changes.markupType = this.markupType()!;
    if (this.markupValue() != null) changes.markupValue = this.markupValue()!;
    if (this.fxRate() != null) changes.fxRate = this.fxRate();
    if (notes) changes.notes = notes;
    return changes;
  });

  protected readonly summaries = computed<LineSummary[]>(() =>
    this.lines().map((line, i) => {
      const markup =
        line.markupType === "percentage" ? `${line.markupValue}%` : `${line.markupValue} flat`;

      return {
        id: line.id ?? String(i),
        index: i + 1,
        title: `${line.chargeCode} — ${line.chargeName}`,
        category: (line.category ?? "base").toUpperCase(),
        rate: this.rateLabel(line),
        details: `${formatText(line.serviceType)}, qty ${line.quantity}, markup ${markup}`,
      };
    }),
  );

  private rateLabel(line: QuotationLineDto): string {
    if (needsValuationBase(line)) return line.unit ?? "";

    const quantity = Number(line.quantity) || 1;
    const rate = Number(line.buyingAmount) / quantity;
    return `${Number.isFinite(rate) ? rate : ""} / ${line.unit ?? ""}`.trim();
  }

  protected readonly error = computed<string | null>(() => {
    const bulk = this.isBulk();
    const quantity = this.quantity();
    const markupValue = this.markupValue();
    const baseAmount = this.baseAmount();
    const fxRate = this.fxRate();

    if (!bulk && quantity == null) return "Quantity is required.";
    if (quantity != null && quantity <= 0) return "Quantity must be greater than zero.";
    if (!bulk && this.markupType() == null) return "Markup type is required.";
    if (!bulk && markupValue == null) return "Markup value is required.";
    if (markupValue != null && markupValue < 0) return "Markup value cannot be negative.";
    if (baseAmount != null && baseAmount < 0) return "Base amount cannot be negative.";
    if (fxRate != null && fxRate <= 0) return "FX rate must be greater than zero.";
    if (bulk && Object.keys(this.changes()).length === 0) {
      return "Fill in at least one field to change.";
    }
    return null;
  });

  protected submit(): void {
    if (this.error() || this.busy()) return;
    this.save.emit(this.changes());
  }
}
