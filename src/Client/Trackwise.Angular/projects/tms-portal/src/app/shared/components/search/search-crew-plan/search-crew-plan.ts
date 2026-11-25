import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from "@angular/core";
import type { FormValueControl } from "@angular/forms/signals";
import { focusFirstControl, isEmptyGuid } from "@trackwise/shared";
import {
  Api,
  getCrewPlan,
  type CrewPlanRowDto,
  type TruckTonnageClass,
  type TruckType,
} from "@trackwise/shared/api";
import { UiSelectField } from "@trackwise/shared/ui";
import { truckTonnageClassLabels, truckTypeLabels } from "@/shared/constants/truck-options";
import { EnumLabelPipe } from "@/shared/pipes";

/** Option shape handed to `ui-select-field`: `label` is the searchable text, `row` is the value. */
interface CrewPlanOption {
  row: CrewPlanRowDto;
  label: string;
  disabled: boolean;
}

/**
 * Component for selecting a truck from a day's crew plan.
 * The plan for `[date]` is loaded (optionally restricted by `[truckType]` / `[branchId]`) and
 * searched client-side by truck number, truck name, crew names, type or tonnage class.
 * Its value is a CrewPlanRowDto, so consumers get the truck and its planned crew together.
 *
 * Implements `FormValueControl` only - see `text-field.ts` for the FormValueControl bridge contract.
 */
@Component({
  selector: "app-search-crew-plan",
  templateUrl: "./search-crew-plan.html",
  imports: [UiSelectField, EnumLabelPipe],
})
export class SearchCrewPlan implements FormValueControl<CrewPlanRowDto | null> {
  private readonly api = inject(Api);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Guards against an older, slower request overwriting a newer one. */
  private loadToken = 0;

  private readonly rows = signal<CrewPlanRowDto[]>([]);
  protected readonly loading = signal(false);

  /** The control's value. Required by `FormValueControl`. */
  public readonly value = model<CrewPlanRowDto | null>(null);

  protected readonly truckTypeLabels = truckTypeLabels;
  protected readonly tonnageClassLabels = truckTonnageClassLabels;

  /** The day whose crew plan is shown (`yyyy-MM-dd`). Nothing is loaded until it is set. */
  public readonly date = input<string | null | undefined>(undefined);

  /** Only offer trucks of this type (applied server-side). */
  public readonly truckType = input<TruckType | null | undefined>(undefined);

  /** Only offer rows of this branch (applied server-side). */
  public readonly branchId = input<string | null | undefined>(undefined);

  /** Only offer trucks of this tonnage class (applied client-side; the API has no such filter). */
  public readonly tonnageClass = input<TruckTonnageClass | null | undefined>(undefined);

  /** Only offer rows that have a driver planned for the day. */
  public readonly requireDriver = input<boolean>(false);

  /** Driven by the Reactive Forms bridge / consumers to disable the input. */
  public readonly disabled = input<boolean>(false);

  /** Raised on blur so the form can mark the field touched. */
  public readonly touch = output<void>();

  private isUnselectable(row: CrewPlanRowDto): boolean {
    return this.requireDriver() && (!row.driverId || isEmptyGuid(row.driverId));
  }

  protected readonly options = computed<CrewPlanOption[]>(() => {
    const tonnage = this.tonnageClass();
    const selected = this.value();

    let list = this.rows().filter((r) => !tonnage || r.tonnageClass === tonnage);

    // Keep the current selection visible even if filters/reloads dropped it.
    if (selected) {
      const index = list.findIndex((r) => r.truckId === selected.truckId);
      list = index >= 0 ? list.map((r, i) => (i === index ? selected : r)) : [selected, ...list];
    }

    return list.map((row) => ({
      row,
      label: this.buildLabel(row),
      disabled: this.isUnselectable(row),
    }));
  });

  /** Signal Forms calls this via `FieldState.focusBoundControl()`. */
  public focus(options?: FocusOptions): void {
    focusFirstControl(this.host.nativeElement, options);
  }

  constructor() {
    // (Re)load the plan whenever the date, type or branch changes.
    effect(() => {
      const date = this.date();
      const type = this.truckType();
      const branchId = this.branchId();
      untracked(() => void this.loadPlan(date, type, branchId));
    });
  }

  /** Marks the control as touched so validation errors surface (on blur). */
  protected markTouched(): void {
    this.touch.emit();
  }

  private buildLabel(row: CrewPlanRowDto): string {
    return [
      row.truckNumber,
      row.truckName,
      row.driverName,
      row.helperName,
      row.secondHelperName,
      row.truckType,
      row.tonnageClass,
    ]
      .filter(Boolean)
      .join(" ");
  }

  private async loadPlan(
    date: string | null | undefined,
    type: TruckType | null | undefined,
    branchId: string | null | undefined,
  ): Promise<void> {
    const token = ++this.loadToken;

    if (!date) {
      this.rows.set([]);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    try {
      const result = await this.api.invoke(getCrewPlan, {
        Date: date,
        TruckType: type ?? undefined,
        BranchId: branchId ?? undefined,
      });

      if (token === this.loadToken) {
        // A trip needs a truck, so skip rows that don't have one.
        this.rows.set((result ?? []).filter((r) => !!r.truckId && !isEmptyGuid(r.truckId)));
      }
    } finally {
      if (token === this.loadToken) {
        this.loading.set(false);
      }
    }
  }

  protected onSelect(row: CrewPlanRowDto | null): void {
    if (row && this.isUnselectable(row)) {
      return; // keep the current value
    }
    this.value.set(row);
  }
}
