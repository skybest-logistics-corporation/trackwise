import { DecimalPipe, TitleCasePipe } from "@angular/common";
import { Component, computed, input, output } from "@angular/core";
import type { QuotationLineDto } from "@trackwise/shared/api";
import { Card, EmptyState, UiButton, UiDataTable } from "@trackwise/shared/ui";

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
      buying: Money[];
      selling: Money[];
    }
  | { kind: "line"; key: string; line: QuotationLineDto }
  | { kind: "total"; key: string; buying: Money[]; selling: Money[] };

const NO_TARIFF_KEY = "__none__";

@Component({
  selector: "app-quotation-lines-table",
  templateUrl: "./quotation-lines-table.html",
  imports: [Card, DecimalPipe, EmptyState, TitleCasePipe, UiButton, UiDataTable],
})
export class QuotationLinesTable {
  public readonly lines = input.required<QuotationLineDto[]>();
  public readonly remove = output<string>();

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
