import { DecimalPipe, TitleCasePipe } from "@angular/common";
import { Component, computed, input, model, output, signal } from "@angular/core";
import type { QuotationLineDto } from "@trackwise/shared/api";
import {
  Card,
  EmptyState,
  UiButton,
  UiDataTable,
  UiDialog,
  UiTableCheckbox,
} from "@trackwise/shared/ui";
import {
  toUpdateRequest,
  UpdateQuotationLineForm,
  type LineChanges,
  type LineUpdate,
} from "../update-quotation-line-form/update-quotation-line-form";

interface Money {
  currency: string;
  amount: number;
}

type TableRow =
  | {
      kind: "group";
      key: string;
      name: string;
      count: number;
      lineKeys: string[];
      buying: Money[];
      selling: Money[];
    }
  | { kind: "line"; key: string; line: QuotationLineDto }
  | { kind: "total"; key: string; buying: Money[]; selling: Money[] };

const NO_TARIFF_KEY = "__none__";

type LineRow = Extract<TableRow, { kind: "line" }>;
type SelectionState = "none" | "some" | "all";

@Component({
  selector: "app-quotation-lines-table",
  templateUrl: "./quotation-lines-table.html",
  imports: [
    Card,
    DecimalPipe,
    EmptyState,
    TitleCasePipe,
    UiButton,
    UiDataTable,
    UiTableCheckbox,
    UpdateQuotationLineForm,
    UiDialog,
  ],
})
export class QuotationLinesTable {
  public readonly lines = input.required<QuotationLineDto[]>();
  public readonly disabled = input(false);
  public readonly remove = output<string>();
  public readonly removeMany = output<string[]>();

  public readonly saving = input(false);
  public readonly editVisible = model(false);
  public readonly updateMany = output<LineUpdate[]>();

  protected readonly editingLines = signal<QuotationLineDto[]>([]);

  protected readonly editHeader = computed(() => {
    const lines = this.editingLines();
    return lines.length === 1 ? `Edit line ${lines[0].chargeCode}` : `Edit ${lines.length} lines`;
  });

  protected editSelected(): void {
    const keys = this.selectedKeys();
    const lines = this.lineRows()
      .filter((r) => keys.has(r.key))
      .map((r) => r.line);
    if (lines.length === 0) return;

    this.editingLines.set(lines);
    this.editVisible.set(true);
  }

  protected saveEdit(changes: LineChanges): void {
    const updates = this.editingLines()
      .filter((line) => !!line.id)
      .map((line) => ({ lineId: line.id!, body: toUpdateRequest(line, changes) }));
    if (updates.length > 0) this.updateMany.emit(updates);
  }
  /**
   * One flat list so every row shares the same columns: a heading row per tariff (with subtotals),
   * its lines, and a grand total when more than one tariff is present.
   *
   * Requires `tariffId` and `tariffName` on QuotationLineDto. Tariffs appear in the order their
   * first line appears; lines keep their relative order inside each tariff.
   */
  protected readonly rows = computed<TableRow[]>(() => {
    const groups = new Map<string, { name: string; lines: QuotationLineDto[] }>();

    for (const line of this.lines()) {
      const key = line.tariffId ?? NO_TARIFF_KEY;
      const group = groups.get(key) ?? {
        name: line.tariffName ?? "Other charges",
        lines: [],
      };
      group.lines.push(line);
      groups.set(key, group);
    }

    const rows: TableRow[] = [];
    const allBuying = new Map<string, number>();
    const allSelling = new Map<string, number>();

    for (const [key, group] of groups) {
      const buying = new Map<string, number>();
      const selling = new Map<string, number>();

      for (const line of group.lines) {
        this.add(buying, line.currency, line.buyingAmount);
        this.add(selling, line.currency, line.sellingAmount);
        this.add(allBuying, line.currency, line.buyingAmount);
        this.add(allSelling, line.currency, line.sellingAmount);
      }

      rows.push({
        kind: "group",
        key: `group:${key}`,
        name: group.name,
        count: group.lines.length,
        lineKeys: group.lines.map((l) => `line:${l.id}`),
        buying: this.toMoney(buying),
        selling: this.toMoney(selling),
      });

      for (const line of group.lines) {
        rows.push({ kind: "line", key: `line:${line.id}`, line });
      }
    }

    if (groups.size > 1) {
      rows.push({
        kind: "total",
        key: "total",
        buying: this.toMoney(allBuying),
        selling: this.toMoney(allSelling),
      });
    }

    return rows;
  });

  private readonly lineRows = computed(() =>
    this.rows().filter((r): r is LineRow => r.kind === "line"),
  );

  /** Two-way bound to the table's `selection`. May hold stale rows; read via `selectedKeys`. */
  protected readonly selectedRows = signal<LineRow[]>([]);

  /** Keys of selected lines that still exist, so deleted lines drop out. */
  private readonly selectedKeys = computed(() => {
    const available = new Set(this.lineRows().map((r) => r.key));
    return new Set(
      this.selectedRows()
        .map((r) => r.key)
        .filter((key) => available.has(key)),
    );
  });

  protected readonly selectedCount = computed(() => this.selectedKeys().size);
  protected readonly allState = computed(() => this.stateOf(this.lineRows().map((r) => r.key)));

  protected isKeySelected(key: string): boolean {
    return this.selectedKeys().has(key);
  }

  protected stateOf(keys: readonly string[]): SelectionState {
    if (keys.length === 0) return "none";
    const selected = this.selectedKeys();
    const count = keys.filter((k) => selected.has(k)).length;
    return count === 0 ? "none" : count === keys.length ? "all" : "some";
  }

  protected toggleKeys(keys: readonly string[], checked: boolean): void {
    const target = new Set(keys);
    const kept = this.selectedRows().filter((r) => !target.has(r.key));
    this.selectedRows.set(
      checked ? [...kept, ...this.lineRows().filter((r) => target.has(r.key))] : kept,
    );
  }

  protected toggleAll(checked: boolean): void {
    this.toggleKeys(
      this.lineRows().map((r) => r.key),
      checked,
    );
  }

  protected clearSelection(): void {
    this.selectedRows.set([]);
  }

  protected removeSelected(): void {
    const keys = this.selectedKeys();
    const ids = this.lineRows()
      .filter((r) => keys.has(r.key))
      .map((r) => r.line.id)
      .filter((id): id is string => !!id);

    if (ids.length > 0) this.removeMany.emit(ids);
  }

  protected formatText(val?: string | null): string {
    if (!val) return "-";
    return val.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
  }

  protected transform(
    quantity: number | string | null | undefined,
    unit: string | null | undefined,
  ): string {
    if (quantity == null && !unit) return "";
    if (quantity == null) return unit ?? "";
    if (!unit) return `${quantity}`;

    const num = Number(quantity);
    let cleanedUnit = unit.trim();

    // 1. Remove leading "per " or "per" (e.g., "per trip" -> "trip")
    cleanedUnit = cleanedUnit.replace(/^per\s+/i, "");

    // 2. Pluralize basic units if quantity is not 1 (e.g., 2 trip -> 2 trips)
    if (!isNaN(num) && num !== 1) {
      cleanedUnit = this.pluralize(cleanedUnit);
    }

    // 3. Prevent space before % (e.g., "1 % of linehaul" -> "1% of linehaul")
    if (cleanedUnit.startsWith("%")) {
      return `${quantity}${cleanedUnit}`;
    }

    return `${quantity} (${cleanedUnit})`;
  }

  private pluralize(unit: string): string {
    // Only pluralize simple words (avoid touching expressions like "% of linehaul")
    const words = unit.split(" ");
    const firstWord = words[0].toLowerCase();

    const rules: Record<string, string> = {
      trip: "trips",
      hour: "hours",
      day: "days",
      mile: "miles",
      stop: "stops",
      load: "loads",
    };

    if (rules[firstWord]) {
      words[0] = rules[firstWord];
      return words.join(" ");
    }

    return unit;
  }

  /** Subtotals are kept per currency: buying and selling can be in different currencies. */
  private add(
    target: Map<string, number>,
    currency: string | null | undefined,
    amount: number | string | null | undefined,
  ): void {
    const key = currency ?? "";
    target.set(key, (target.get(key) ?? 0) + Number(amount ?? 0));
  }

  private toMoney(source: Map<string, number>): Money[] {
    return [...source].map(([currency, amount]) => ({ currency, amount }));
  }
}
