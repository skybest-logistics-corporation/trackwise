import { Component, computed, inject, input, model, output, signal } from "@angular/core";
import { getApiErrorMessage } from "@trackwise/shared";
import {
  Api,
  createTripFromBooking,
  type CrewPlanRowDto,
  type TruckDto,
  type TruckTonnageClass,
  type TruckType,
} from "@trackwise/shared/api";
import { truckTonnageClassOptions } from "@trackwise/shared/api/enums";
import {
  Alert,
  Stack,
  Typography,
  UiButton,
  UiDialog,
  UiFormField,
  UiSelectField,
} from "@trackwise/shared/ui";
import { ToastService } from "@/core/services";
import { SearchTruck } from "@/shared/components/search";
import { SearchCrewPlan } from "../../search/search-crew-plan/search-crew-plan";

/**
 * Makes the trip for a confirmed Cold Chain booking: one load per drop from the pickup warehouse,
 * crewed from the day's crew plan, then priced from the client's agreement. Emits the new trip's
 * id; the page decides whether to open it or stay put.
 */
@Component({
  selector: "app-create-trip-dialog",
  templateUrl: "./create-trip-dialog.html",
  imports: [Alert, Stack, Typography, UiButton, UiDialog, UiFormField, SearchCrewPlan],
})
export class CreateTripDialog {
  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  public readonly bookingId = input.required<string>();
  // public readonly branchId = input.required<string>();
  public readonly tripDate = input<string | null | undefined>(null); // yyyy-MM-dd
  public readonly bookedSize = input<TruckTonnageClass | null | undefined>(null);
  public readonly truckType = input<TruckType | null | undefined>(null);

  protected readonly crewRow = signal<CrewPlanRowDto | null>(null);

  public readonly dropCount = input(0);
  // public readonly containerType = input<TruckTonnageClass | null | undefined>(null);
  public readonly open = model(false);
  public readonly created = output<string>();

  protected readonly truck = signal<TruckDto | null>(null);
  protected readonly isBusy = signal(false);

  protected readonly showAllSizes = signal(false);
  protected readonly sizeFilter = computed(() => (this.showAllSizes() ? null : this.bookedSize()));

  protected readonly bookedSizeLabel = computed(() => sizeLabel(this.bookedSize()));

  /** A different size is allowed - a 4T can cover a 2T job - and is billed at the booked size. */
  protected readonly sizeMismatch = computed(() => {
    const truckSize = this.crewRow()?.tonnageClass;
    const booked = this.bookedSize();
    return !!truckSize && !!booked && truckSize !== booked;
  });

  protected readonly truckSizeLabel = computed(() => sizeLabel(this.truck()?.tonnageClass));

  protected onOpened(): void {
    this.crewRow.set(null);
  }

  protected async create(): Promise<void> {
    if (this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      const result = await this.api.invoke(createTripFromBooking, {
        id: this.bookingId(),
        body: { truckId: this.crewRow()?.truckId ?? null },
      });

      if (result.pricingError) {
        this.toast.showWarning(`Trip made, but not priced yet: ${result.pricingError}`);
      } else {
        this.toast.showSuccess("Trip made and priced from the agreement.");
      }

      this.open.set(false);
      this.created.emit(result.tripId!);
    } catch (error) {
      this.toast.showError(getApiErrorMessage(error, "Could not make the trip."));
    } finally {
      this.isBusy.set(false);
    }
  }

  protected readonly tripDateLabel = computed(() => {
    const date = this.tripDate();
    if (!date) {
      return "";
    }
    const [year, month, day] = date.split("-").map(Number);
    return new Date(year, month - 1, day).toLocaleDateString("en-PH", {
      weekday: "short",
      day: "numeric",
      month: "short",
      year: "numeric",
    });
  });
}

function sizeLabel(size: string | null | undefined): string {
  return truckTonnageClassOptions.find((o) => o.value === size)?.label ?? "";
}
