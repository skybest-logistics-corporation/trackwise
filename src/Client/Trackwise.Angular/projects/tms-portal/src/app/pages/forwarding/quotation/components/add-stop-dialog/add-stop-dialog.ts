import {
  booleanAttribute,
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
} from "@angular/core";
import { form, FormField, FormRoot, required } from "@angular/forms/signals";
import {
  Api,
  createLocation,
  getApiErrorMessage,
  Stack,
  ToastService,
  UiButton,
  UiDialog,
  UiFormField,
  UiSelectField,
  type CustomerDto,
  type LocationDto,
  type SelectOption,
  type StopType,
} from "@trackwise/shared";
import {
  LocationForm,
  type LocationFormValue,
} from "@/pages/master-data/location/components/location-form/location-form";

export interface FormModel {
  stopType: StopType;
  locationId: string;
  /** The full location behind `locationId`, so the parent doesn't have to look it up. */
  location?: LocationDto;
}

const STOP_TYPE_LABELS: Record<StopType, string> = {
  pickup: "Pickup Location",
  dropoff: "Delivery Destination",
  waypoint: "Waypoint Stop",
  cross_dock_staging: "Crossdocking Staging",
};

const STOP_TYPE_OPTIONS: SelectOption<StopType>[] = (
  Object.keys(STOP_TYPE_LABELS) as StopType[]
).map((type) => ({
  label: STOP_TYPE_LABELS[type],
  value: type,
}));

@Component({
  selector: "app-add-stop-dialog",
  templateUrl: "add-stop-dialog.html",
  imports: [
    FormField,
    UiDialog,
    UiFormField,
    FormRoot,
    UiSelectField,
    UiButton,
    Stack,
    LocationForm,
  ],
})
export class RouteStopDialog {
  isOpen = model<boolean>(false);

  /** Locations from the parent's server-side search for the current query. */
  locations = input<LocationDto[]>([]);

  /** True while the parent is fetching locations for the current search. */
  loading = input(false, { transform: booleanAttribute });

  /** The client these stops belong to; new locations are created under them. */
  public readonly initialCustomer = input<CustomerDto | null>(null);

  /** Raised as the user types in the location select; the parent runs the server search. */
  searchChange = output<string>();

  /** Raised after a location is created here, so the parent can add it to its own lists. */
  locationCreated = output<LocationDto>();

  closed = output<void>();
  submitted = output<FormModel>();

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  protected readonly isLoading = signal(false);

  protected readonly createLocationOpen = signal(false);
  protected readonly newLocationName = signal("");

  protected readonly createdLocations = signal<LocationDto[]>([]);

  /** Every location seen so far, so a pick survives the next server response replacing the list. */
  private readonly seenLocations = new Map<string, LocationDto>();

  protected readonly stopTypeOptions = STOP_TYPE_OPTIONS;

  protected readonly locationOptions = computed<SelectOption<string>[]>(() => {
    const merged = new Map<string, LocationDto>();
    for (const loc of [...this.locations(), ...this.createdLocations()]) {
      if (loc.id) merged.set(loc.id, loc);
    }
    return [...merged.values()].map((d) => ({
      label: d.code ? `${d.code} — ${d.name}` : (d.name ?? ""),
      value: d.id!,
    }));
  });

  protected readonly stopModel = signal<FormModel>({
    stopType: "pickup",
    locationId: "",
  });

  protected readonly form = form(this.stopModel, (s) => {
    required(s.stopType);
    required(s.locationId);
  });

  constructor() {
    // Remember every location the server has returned.
    effect(() => {
      for (const loc of this.locations()) {
        if (loc.id) this.seenLocations.set(loc.id, loc);
      }
    });
  }

  protected onCreateLocation(searchText: string): void {
    this.newLocationName.set(searchText); // prefill the new-location form
    this.createLocationOpen.set(true);
  }

  protected onLocationCreated(location: LocationDto): void {
    this.seenLocations.set(location.id!, location);
    this.createdLocations.update((list) => [...list, location]);
    this.stopModel.update((m) => ({ ...m, locationId: location.id! }));
    this.locationCreated.emit(location);
  }

  close() {
    this.isOpen.set(false);
    this.closed.emit();
  }

  onSubmit() {
    if (!this.form().valid()) return;

    const val = this.stopModel();

    this.submitted.emit({
      stopType: val.stopType!,
      locationId: val.locationId,
      location: this.seenLocations.get(val.locationId),
    });

    this.close();
  }

  protected async create(value: LocationFormValue): Promise<void> {
    if (this.isLoading()) return;

    this.isLoading.set(true);
    try {
      const created = await this.api.invoke(createLocation, {
        body: { ...value, address: value.address ?? undefined },
      });

      this.onLocationCreated(created);
      this.createLocationOpen.set(false);
      this.toast.showSuccess("Location created.");
    } catch (error) {
      this.toast.showError(getApiErrorMessage(error, "Could not create the location."));
    } finally {
      this.isLoading.set(false);
    }
  }
}
