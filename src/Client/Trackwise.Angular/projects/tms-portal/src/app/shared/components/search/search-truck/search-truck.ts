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
  getTruckById,
  getTrucks,
  type TruckDto,
  type TruckTonnageClass,
  type TruckType,
} from "@trackwise/shared/api";
import { UiSelectField } from "@trackwise/shared/ui";
import { truckTonnageClassLabels, truckTypeLabels } from "@/shared/constants/truck-options";
import { EnumLabelPipe } from "@/shared/pipes";

/** Option shape handed to `ui-select-field`: `label` is the searchable text, `truck` is the value. */
interface TruckOption {
  truck: TruckDto;
  label: string;
}

/** Large enough to hold the whole fleet, since filtering happens client-side. */
const TRUCK_PAGE_SIZE = 500;

/**
 * Component for selecting a truck.
 * Trucks are loaded once (optionally restricted by `[truckType]` / `[tonnageClass]`) and searched
 * client-side by truck number, driver names, type or tonnage class.
 * Its value is always a TruckDto; pass `[truckId]` to seed it from a bare ID.
 *
 * Implements `FormValueControl` only - see `text-field.ts` for the FormValueControl bridge contract.
 */
@Component({
  selector: "app-search-truck",
  templateUrl: "./search-truck.html",
  imports: [UiSelectField, EnumLabelPipe],
})
export class SearchTruck implements FormValueControl<TruckDto | null> {
  private readonly api = inject(Api);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Guards against an older, slower request overwriting a newer one. */
  private loadToken = 0;

  private readonly trucks = signal<TruckDto[]>([]);
  protected readonly loading = signal(false);

  /** The control's value. Required by `FormValueControl`. */
  public readonly value = model<TruckDto | null>(null);

  protected readonly truckTypeLabels = truckTypeLabels;
  protected readonly tonnageClassLabels = truckTonnageClassLabels;

  /**
   * Seeds the control from a bare truck ID, which is all an edit form usually has. Resolved to the
   * full DTO and written into `value`.
   *
   * This is a separate input rather than a `string` member of `value` because `[formField]` value
   * types are invariant: widening `value` to `TruckDto | string | null` would force every consumer's
   * model field to that same type.
   */
  public readonly truckId = input<string | null>(null);

  /** Only offer trucks of this type (applied server-side). */
  public readonly truckType = input<TruckType | null | undefined>(undefined);

  /** Only offer trucks of this tonnage class (applied client-side; the API has no such filter). */
  public readonly tonnageClass = input<TruckTonnageClass | null | undefined>(undefined);

  /** Driven by the Reactive Forms bridge / consumers to disable the input. */
  public readonly disabled = input<boolean>(false);

  /** Raised on blur so the form can mark the field touched. */
  public readonly touch = output<void>();

  protected readonly options = computed<TruckOption[]>(() => {
    const tonnage = this.tonnageClass();
    const selected = this.value();

    let list = this.trucks().filter((t) => !tonnage || t.tonnageClass === tonnage);

    // The select compares values by reference. Make sure the current value is in the list, and
    // that it is the very same object, so the trigger can render it. This also keeps a seeded
    // truck visible when it falls outside the active type/tonnage filters.
    if (selected) {
      const index = list.findIndex((t) => t.id === selected.id);
      list = index >= 0 ? list.map((t, i) => (i === index ? selected : t)) : [selected, ...list];
    }

    return list.map((truck) => ({ truck, label: this.buildLabel(truck) }));
  });

  /** Signal Forms calls this via `FieldState.focusBoundControl()`. */
  public focus(options?: FocusOptions): void {
    focusFirstControl(this.host.nativeElement, options);
  }

  constructor() {
    // (Re)load the list whenever the type filter changes.
    effect(() => {
      const type = this.truckType();
      untracked(() => void this.loadTrucks(type));
    });

    // Resolve the seeded ID to the full DTO. Reading `value` untracked keeps this from re-running
    // when the resolved truck is written back, and the id-equality check keeps a later user
    // selection (or a clear) from being undone.
    effect(() => {
      const id = this.truckId();
      if (!id || isEmptyGuid(id)) {
        return;
      }
      if (untracked(this.value)?.id === id) {
        return;
      }
      void this.resolveTruckById(id);
    });
  }

  /** Marks the control as touched so validation errors surface (on blur). */
  protected markTouched(): void {
    this.touch.emit();
  }

  private buildLabel(truck: TruckDto): string {
    return [
      truck.number,
      truck.mainDriver?.fullName,
      truck.secondaryDriver?.fullName,
      truck.type,
      truck.tonnageClass,
    ]
      .filter(Boolean)
      .join(" ");
  }

  private async loadTrucks(type: TruckType | null | undefined): Promise<void> {
    const token = ++this.loadToken;
    this.loading.set(true);
    try {
      const result = await this.api.invoke(getTrucks, {
        Types: type ? [type] : undefined,
        PageSize: TRUCK_PAGE_SIZE,
      });
      if (token === this.loadToken) {
        this.trucks.set(result.items ?? []);
      }
    } finally {
      if (token === this.loadToken) {
        this.loading.set(false);
      }
    }
  }

  private async resolveTruckById(id: string): Promise<void> {
    const result = await this.api.invoke(getTruckById, { truckOrDriverId: id });
    if (result) {
      this.value.set(result);
    }
  }
}
