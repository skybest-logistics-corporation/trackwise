import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from "@angular/core";
import { toObservable, toSignal } from "@angular/core/rxjs-interop";
import {
  form,
  FormField,
  FormRoot,
  maxLength,
  pattern,
  required,
  validate,
} from "@angular/forms/signals";
import { RouterLink } from "@angular/router";
import {
  Api,
  createListStore,
  createLocation,
  EmptyState,
  getApiErrorMessage,
  getLocations,
  ToastService,
} from "@trackwise/shared";
import type {
  ComplianceRegime,
  CreateQuotationCommand,
  CustomerDto,
  FreightMode,
  Incoterm,
  LocationDto,
  QuotationDto,
  RouteStopSummaryDto,
  ShipmentType,
  TemperatureZone,
  TerminalSummaryDto,
  TransportMode,
} from "@trackwise/shared/api";
import {
  Grid,
  Icon,
  Stack,
  Surface,
  Typography,
  UiButton,
  UiCheckboxField,
  UiDialog,
  UiFormField,
  UiNumberField,
  UiSelectField,
  UiTextareaField,
  UiTextField,
} from "@trackwise/shared/ui";
import { debounceTime, distinctUntilChanged, map } from "rxjs";
import {
  LocationForm,
  type LocationFormValue,
} from "@/pages/master-data/location/components/location-form/location-form";
import { SearchCustomer, SearchTerminal } from "@/shared/components/search";
import { BranchesStore } from "../../store/branches-list.store";
import { RouteStopDialog, type FormModel } from "../add-stop-dialog/add-stop-dialog";

const LANE_FREIGHT_MODES: readonly FreightMode[] = ["reefer", "trucking"];
const DOMESTIC_FREIGHT_MODES: readonly FreightMode[] = ["domestic", ...LANE_FREIGHT_MODES];

export interface QuotationFormModel {
  customer: CustomerDto | null;
  branchId: string;
  currency: string;
  incoterm: Incoterm;
  freightMode: FreightMode;
  mode: TransportMode;
  shipmentType: ShipmentType;
  requiresCustomsBrokerage: boolean;
  isHazardous: boolean;

  // Route & Lane (reefer / trucking)
  stops: RouteStopSummaryDto[] | null;
  originLocationId: string;
  destinationLocationId: string;
  requestedPickupWindowStart: string;
  requestedDeliveryWindowEnd: string;
  totalDistanceKm: string;

  // Ports (everything else)
  pol: TerminalSummaryDto | null;
  pod: TerminalSummaryDto | null;

  // Cargo Specs
  commodity: string;
  palletCount: number | null;
  totalWeightKg: number | null;
  declaredCargoValueAmount: number | null;
  declaredCargoValueCurrency: string;

  // Temperature Control
  temperatureZone: TemperatureZone;
  setpointCelsius: string;
  allowableVarianceCelsius: string;
  compliance: ComplianceRegime;
  requiresContinuousTelematics: boolean;

  notes: string;
}

const EMPTY_FORM: QuotationFormModel = {
  customer: null,
  branchId: "",
  currency: "PHP",
  incoterm: "fca",
  freightMode: "domestic",
  mode: "road",
  shipmentType: "ftl",
  requiresCustomsBrokerage: false,
  isHazardous: false,

  stops: null,
  originLocationId: "",
  destinationLocationId: "",
  requestedPickupWindowStart: "",
  requestedDeliveryWindowEnd: "",
  totalDistanceKm: "",

  pol: null,
  pod: null,

  commodity: "",
  palletCount: 0,
  totalWeightKg: 0,
  declaredCargoValueAmount: 0,
  declaredCargoValueCurrency: "PHP",

  temperatureZone: "ambient",
  setpointCelsius: "",
  allowableVarianceCelsius: "",
  compliance: "standard",
  requiresContinuousTelematics: false,

  notes: "",
};

const CustomerLocationsStore = createListStore<LocationDto>(getLocations, {
  defaultSortField: "Name",
  defaultPageSize: 100,
});

const SHIPMENT_TYPES_BY_MODE: Record<TransportMode, { label: string; value: ShipmentType }[]> = {
  road: [
    { label: "FTL – Full Truckload", value: "ftl" },
    { label: "LTL – Less Than Truckload", value: "ltl" },
  ],
  ocean: [
    { label: "FCL – Full Container Load", value: "fcl" },
    { label: "LCL – Less Than Container Load", value: "lcl" },
  ],
  air: [
    { label: "Air Standard", value: "air_standard" },
    { label: "Air Express", value: "air_express" },
    { label: "Air Charter", value: "air_charter" },
  ],
  rail: [
    { label: "Full Wagon Load (FTL)", value: "ftl" },
    { label: "Less Than Wagon Load (LTL)", value: "ltl" },
    { label: "Intermodal Container (FCL)", value: "fcl" },
  ],
  multimodal: [
    { label: "FCL – Container Load", value: "fcl" },
    { label: "LCL – Groupage Load", value: "lcl" },
    { label: "FTL – Full Truckload", value: "ftl" },
    { label: "Air Standard", value: "air_standard" },
  ],
};

@Component({
  selector: "app-quotation-form",
  templateUrl: "./quotation-form.html",
  providers: [CustomerLocationsStore, BranchesStore],
  imports: [
    FormField,
    FormRoot,
    Grid,
    Icon,
    RouterLink,
    SearchCustomer,
    Stack,
    Surface,
    Typography,
    UiButton,
    UiCheckboxField,
    UiFormField,
    UiSelectField,
    UiTextareaField,
    UiTextField,
    UiNumberField,
    RouteStopDialog,
    EmptyState,
    UiDialog,
    LocationForm,
    SearchTerminal,
  ],
})
export class QuotationForm {
  protected readonly selectedCustomerLocationsStore = inject(CustomerLocationsStore);
  protected readonly branchesStore = inject(BranchesStore);

  protected readonly locationSearch = signal("");

  public readonly isLoading = input(false);

  /** When provided, the form is pre-filled for editing. */
  public readonly quotation = input<QuotationDto | null>(null);
  public readonly submitLabel = input("Create Quotation");

  public readonly save = output<CreateQuotationCommand>();
  public isAddingRouteStop = signal(false);

  protected readonly model = signal<QuotationFormModel>({ ...EMPTY_FORM });

  private previousFreightMode: FreightMode = "domestic";
  private previousTransportMode: TransportMode = "road";

  // Reactive state derived from form signals
  protected readonly currentFreightMode = computed(() => this.model().freightMode);
  protected readonly currentTransportMode = computed(() => this.model().mode);
  protected readonly isReefer = computed(() => this.model().freightMode === "reefer");

  private readonly api = inject(Api);
  private readonly toast = inject(ToastService);

  /** Which select asked for the new location, so the result lands in the right field. */
  private readonly createLocationTarget = signal<"origin" | "destination">("origin");

  /** Locations created here this session; merged with the store's list until it reloads. */
  private readonly createdLocations = signal<LocationDto[]>([]);

  /** Store data plus locally created locations, deduped by id. */
  protected readonly customerLocations = computed(() => {
    const loaded = this.selectedCustomerLocationsStore.data();
    const loadedIds = new Set(loaded.map((l) => l.id));
    const q = this.debouncedLocationSearch().toLowerCase();
    const created = this.createdLocations().filter(
      (l) =>
        !loadedIds.has(l.id) && (!q || `${l.code ?? ""} ${l.name ?? ""}`.toLowerCase().includes(q)),
    );
    return [...loaded, ...created];
  });

  protected readonly createLocationOpen = signal(false);
  protected readonly newLocationName = signal("");
  protected readonly isCreatingLocation = signal(false);

  private readonly debouncedLocationSearch = toSignal(
    toObservable(this.locationSearch).pipe(
      map((s) => s.trim()),
      debounceTime(300),
      distinctUntilChanged(),
    ),
    { initialValue: "" },
  );

  protected readonly branchOptions = computed(() =>
    this.branchesStore.data().map((b) => ({
      value: b.id ?? "",
      label: b.code ? `${b.code} — ${b.name}` : (b.name ?? ""),
    })),
  );

  // Dynamic Shipment Types filtered strictly by the chosen Transport Mode
  protected readonly shipmentTypeOptions = computed(() => {
    const mode = this.currentTransportMode() ?? "road";
    return SHIPMENT_TYPES_BY_MODE[mode] ?? SHIPMENT_TYPES_BY_MODE.road;
  });

  readonly routingModeOptions = [
    { label: "Point-to-Point", value: "point-to-point" },
    { label: "Multi-Route Stops", value: "multi-stop" },
  ];

  readonly routingMode = signal<"point-to-point" | "multi-stop">("point-to-point");

  protected readonly isLaneBased = computed(() =>
    LANE_FREIGHT_MODES.includes(this.model().freightMode),
  );
  protected readonly isDomestic = computed(() =>
    DOMESTIC_FREIGHT_MODES.includes(this.model().freightMode),
  );
  protected readonly isMultiStop = computed(
    () => this.isLaneBased() && this.routingMode() === "multi-stop",
  );
  protected readonly isPointToPoint = computed(
    () => this.isLaneBased() && this.routingMode() === "point-to-point",
  );

  protected readonly polLabel = computed(() =>
    this.currentTransportMode() === "air" ? "Airport of Departure (POL)" : "Port of Loading (POL)",
  );
  protected readonly podLabel = computed(() =>
    this.currentTransportMode() === "air"
      ? "Airport of Destination (POD)"
      : "Port of Discharge (POD)",
  );

  protected readonly incotermOptions: { label: string; value: Incoterm }[] = [
    { label: "EXW – Ex Works", value: "exw" },
    { label: "FCA – Free Carrier", value: "fca" },
    { label: "FOB – Free on Board (Maritime)", value: "fob" },
    { label: "CIF – Cost, Insurance & Freight", value: "cif" },
    { label: "CPT – Carriage Paid To", value: "cpt" },
    { label: "DAP – Delivered at Place", value: "dap" },
    { label: "DDP – Delivered Duty Paid", value: "ddp" },
  ];

  protected readonly freightModeOptions: { label: string; value: FreightMode; group: string }[] = [
    { label: "Domestic", value: "domestic", group: "Forwarding" },
    { label: "Import", value: "import", group: "Forwarding" },
    { label: "Export", value: "export", group: "Forwarding" },
    { label: "Triangular", value: "triangular", group: "Forwarding" },
    { label: "Reefer", value: "reefer", group: "Trucking & Reefer" },
    { label: "Trucking", value: "trucking", group: "Trucking & Reefer" },
  ];

  protected readonly transportModeOptions: { label: string; value: TransportMode }[] = [
    { label: "Road Transport", value: "road" },
    { label: "Ocean Freight", value: "ocean" },
    { label: "Air Freight", value: "air" },
    { label: "Rail Freight", value: "rail" },
    { label: "Multimodal Transport", value: "multimodal" },
  ];

  protected readonly temperatureZoneOptions: { label: string; value: TemperatureZone }[] = [
    { label: "Ambient (No Active Cooling)", value: "ambient" },
    { label: "Controlled Room Temperature (15°C to 25°C)", value: "controlled_room_temperature" },
    { label: "Chilled (2°C to 8°C)", value: "chilled" },
    { label: "Frozen (-20°C or colder)", value: "frozen" },
    { label: "Cryogenic (Deep Freeze)", value: "cryogenic" },
  ];

  protected readonly complianceRegimeOptions: { label: string; value: ComplianceRegime }[] = [
    { label: "Standard Logistics", value: "standard" },
    { label: "GDP (Good Distribution Practice - Pharma)", value: "gdp" },
    { label: "FSMA (Food Safety Modernization)", value: "fsma" },
    { label: "HACCP (Hazard Analysis Critical Control Point)", value: "haccp" },
  ];

  protected readonly selectedCustomerLocationOptions = computed(() =>
    this.customerLocations().map((loc) => ({
      value: loc.id ?? "",
      label: loc.code ? `${loc.code} — ${loc.name}` : (loc.name ?? ""),
    })),
  );

  /** Origin/destination options, or a single placeholder row until a customer is chosen. */
  protected readonly locationOptions = computed(() =>
    !this.model().customer
      ? [{ label: "Select a customer first", value: "" }]
      : this.selectedCustomerLocationOptions(),
  );

  protected readonly reeferZoneOptions = this.temperatureZoneOptions.filter(
    (o) => o.value !== "ambient",
  );

  protected readonly form = form(
    this.model,
    (p) => {
      required(p.customer, { message: "Customer is required." });
      required(p.branchId, { message: "Branch is required." });
      required(p.currency, { message: "Currency is required." });
      maxLength(p.currency, 3, { message: "Currency must be a 3-letter ISO code." });
      required(p.freightMode, { message: "Freight mode is required." });
      required(p.mode, { message: "Transport mode is required." });
      required(p.shipmentType, { message: "Shipment type is required." });
      required(p.incoterm, { message: "Incoterm is required." });

      // Ports: required unless the lane is in use
      required(p.pol, { when: () => !this.isLaneBased(), message: "Port of Loading is required." });
      required(p.pod, {
        when: () => !this.isLaneBased(),
        message: "Port of Discharge is required.",
      });

      // Lane: required for reefer / trucking
      required(p.originLocationId, {
        when: () => this.isPointToPoint(),
        message: "Origin location is required.",
      });
      required(p.destinationLocationId, {
        when: () => this.isPointToPoint(),
        message: "Destination location is required.",
      });
      validate(p.stops, ({ value }) =>
        this.isMultiStop() && (value()?.length ?? 0) < 2
          ? { kind: "minStops", message: "Add at least a pickup and a drop-off stop." }
          : undefined,
      );

      required(p.setpointCelsius, {
        when: () => this.isReefer(),
        message: "Temperature setpoint is required for Reefer.",
      });
      pattern(p.setpointCelsius, /^-?(?:\d+\.?\d*|\.\d+)$/, {
        when: () => this.isReefer(),
        message: "Enter a valid temperature setpoint.",
      });
    },
    {
      submission: {
        action: async () => {
          this.save.emit(this.toCommand());
          return undefined;
        },
      },
    },
  );

  private previousCustomerId: string | undefined;

  constructor() {
    // 0. Edit mode -> hydrate the form from an existing quotation
    effect(() => {
      const q = this.quotation();
      if (q) {
        untracked(() => this.patchFromQuotation(q));
      }
    });

    // 1. Customer location query sync
    effect(() => {
      const customer = this.model().customer;
      const search = this.debouncedLocationSearch();

      untracked(() => {
        // New customer -> drop the old query and the old customer's created locations.
        if (customer?.id !== this.previousCustomerId) {
          this.previousCustomerId = customer?.id;
          this.createdLocations.set([]);
          if (this.locationSearch() !== "") {
            this.locationSearch.set("");
            return; // the debounced value will re-run this effect with search = ""
          }
        }

        if (!customer) return;
        this.selectedCustomerLocationsStore.setFilters(
          { CustomerId: customer.id },
          { reload: false },
        );
        this.selectedCustomerLocationsStore.setSearch(search);
      });
    });

    // 2. Freight Mode watcher -> Auto-populate presets
    effect(() => {
      const mode = this.currentFreightMode();
      if (mode && mode !== this.previousFreightMode) {
        this.previousFreightMode = mode;
        untracked(() => this.handleFreightModeChange(mode));
      }
    });

    // 3. Transport Mode watcher -> Auto-correct Shipment Type
    effect(() => {
      const mode = this.currentTransportMode();
      if (mode && mode !== this.previousTransportMode) {
        this.previousTransportMode = mode;
        untracked(() => this.handleTransportModeChange(mode));
      }
    });

    // 5. Load active branches once
    this.branchesStore.setFilters({ IsActive: true }, { reload: true });

    // 6. Create mode: preselect the branch when there is only one choice
    effect(() => {
      const branches = this.branchesStore.data();
      if (this.quotation() || this.model().branchId || branches.length !== 1) return;
      untracked(() => this.model.update((m) => ({ ...m, branchId: branches[0].id ?? "" })));
    });
  }

  /**
   * Maps the flat QuotationDto back into the form model.
   * Fields the DTO doesn't carry are derived or defaulted (see inline notes).
   */
  private patchFromQuotation(q: QuotationDto): void {
    const freightMode = q.freightMode ?? EMPTY_FORM.freightMode;
    const laneBased = LANE_FREIGHT_MODES.includes(freightMode);
    const mode = q.mode ?? EMPTY_FORM.mode;
    const currency = q.currency ?? EMPTY_FORM.currency;

    // IMPORTANT: sync these first, otherwise the freight/transport mode
    // watchers fire and overwrite incoterm, currency, mode and shipmentType
    // with their presets.
    this.previousFreightMode = freightMode;
    this.previousTransportMode = mode;

    // Decide routing mode from the stops. A plain pickup -> dropoff route is
    // treated as point-to-point, anything else as multi-stop.
    const stops = [...(q.stops ?? [])].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0));
    const first = stops[0];
    const last = stops[stops.length - 1];
    const isPointToPoint =
      stops.length === 2 && first?.type === "pickup" && last?.type === "dropoff";

    this.routingMode.set(stops.length > 0 && !isPointToPoint ? "multi-stop" : "point-to-point");

    const isMultiStop = laneBased && stops.length > 0 && !isPointToPoint;
    this.routingMode.set(isMultiStop ? "multi-stop" : "point-to-point");

    const isReefer = freightMode === "reefer";
    const spec = isReefer ? q.temperatureSpec : undefined;
    const zone = spec?.zone ?? (isReefer ? "chilled" : EMPTY_FORM.temperatureZone);

    this.model.set({
      ...EMPTY_FORM,
      branchId: q.branchId ?? "",
      customer: q.customerId
        ? ({ id: q.customerId, name: q.customerName ?? "" } as CustomerDto)
        : null,
      currency,
      incoterm: q.incoterm ?? EMPTY_FORM.incoterm,
      freightMode,
      mode,
      shipmentType: q.shipmentType ?? EMPTY_FORM.shipmentType,
      requiresCustomsBrokerage: q.requiresCustomsBrokerage!,
      isHazardous: q.isHazardous!,

      stops: isMultiStop ? stops : null,
      originLocationId: laneBased && isPointToPoint ? (first?.locationId ?? "") : "",
      destinationLocationId: laneBased && isPointToPoint ? (last?.locationId ?? "") : "",
      requestedPickupWindowStart: laneBased ? (first?.windowStart ?? "") : "",
      requestedDeliveryWindowEnd: laneBased ? (last?.windowEnd ?? "") : "",
      totalDistanceKm: q.totalDistanceKm?.toString() ?? "",
      pol: q.pol ?? null,
      pod: q.pod ?? null,

      commodity: q.commodity ?? "",
      palletCount: q.palletCount ?? 0,
      totalWeightKg: q.totalWeightKg ?? 0,
      declaredCargoValueAmount: q.declaredCargoValueAmount ?? 0,
      declaredCargoValueCurrency: q.declaredCargoValueCurrency ?? "",

      temperatureZone: zone,
      setpointCelsius: spec?.setpointCelsius?.toString() ?? "",
      allowableVarianceCelsius: spec?.allowableVarianceCelsius?.toString() ?? "",
      compliance: spec?.compliance ?? EMPTY_FORM.compliance,
      requiresContinuousTelematics: spec?.requiresContinuousTelematics ?? false,

      notes: q.notes ?? "",
    });
  }

  protected onCreateLocation(target: "origin" | "destination", searchText: string): void {
    if (!this.model().customer) return;

    this.createLocationTarget.set(target);
    this.newLocationName.set(searchText);
    this.createLocationOpen.set(true);
  }

  protected async createNewLocation(value: LocationFormValue): Promise<void> {
    if (this.isCreatingLocation()) return;

    this.isCreatingLocation.set(true);
    try {
      const created = await this.api.invoke(createLocation, {
        body: { ...value, address: value.address ?? undefined },
      });

      this.addCreatedLocation(created);
      const field =
        this.createLocationTarget() === "origin" ? "originLocationId" : "destinationLocationId";
      this.model.update((m) => ({ ...m, [field]: created.id ?? "" }));

      this.createLocationOpen.set(false);
      this.toast.showSuccess("Location created.");
    } catch (error) {
      this.toast.showError(getApiErrorMessage(error, "Could not create the location."));
    } finally {
      this.isCreatingLocation.set(false);
    }
  }

  /** Also used for locations created from the route-stop dialog. */
  protected addCreatedLocation(location: LocationDto): void {
    this.createdLocations.update((list) => [...list, location]);
  }

  private handleFreightModeChange(freightMode: FreightMode): void {
    this.model.update((curr) => {
      const temperature =
        freightMode === "reefer"
          ? {
              temperatureZone:
                curr.temperatureZone === "ambient" ? ("chilled" as const) : curr.temperatureZone,
            }
          : {
              temperatureZone: EMPTY_FORM.temperatureZone,
              setpointCelsius: "",
              allowableVarianceCelsius: "",
              compliance: EMPTY_FORM.compliance,
              requiresContinuousTelematics: false,
            };

      if (DOMESTIC_FREIGHT_MODES.includes(freightMode)) {
        return {
          ...curr,
          ...temperature,
          currency: "PHP",
          declaredCargoValueCurrency: "PHP",
          requiresCustomsBrokerage: false,
          incoterm: "fca",
          mode: "road",
          shipmentType: "ftl",
        };
      }

      // Import / Export / Triangular
      return {
        ...curr,
        ...temperature,
        currency: curr.currency === "PHP" ? "USD" : curr.currency,
        declaredCargoValueCurrency:
          curr.declaredCargoValueCurrency === "PHP" ? "USD" : curr.declaredCargoValueCurrency,
        requiresCustomsBrokerage: true,
        incoterm: freightMode === "import" ? "cif" : "fob",
        mode: curr.mode === "road" ? "ocean" : curr.mode,
        shipmentType: curr.mode === "road" ? "fcl" : curr.shipmentType,
      };
    });
  }

  private handleTransportModeChange(transportMode: TransportMode): void {
    const validShipmentTypes = SHIPMENT_TYPES_BY_MODE[transportMode].map((opt) => opt.value);
    this.model.update((curr) => {
      if (validShipmentTypes.includes(curr.shipmentType)) {
        return curr;
      }
      return {
        ...curr,
        shipmentType: validShipmentTypes[0],
      };
    });
  }

  private parseOptionalNumber(val: string | null | undefined): number | undefined {
    if (!val) return undefined;
    const trimmed = val.trim();
    if (!trimmed) return undefined;
    const parsed = Number(trimmed);
    return isNaN(parsed) ? undefined : parsed;
  }

  private toCommand(): CreateQuotationCommand {
    const v = this.model();
    const distance = this.parseOptionalNumber(v.totalDistanceKm);
    const setpoint = this.parseOptionalNumber(v.setpointCelsius);
    const variance = this.parseOptionalNumber(v.allowableVarianceCelsius);

    const laneBased = this.isLaneBased();
    const p2p = this.routingMode() === "point-to-point";

    const hasCargoData = Boolean(
      v.commodity?.trim() || v.totalWeightKg || v.palletCount || v.declaredCargoValueAmount,
    );

    const isReefer = this.isReefer();

    const command: CreateQuotationCommand = {
      customerId: v.customer?.id ?? "",
      branchId: v.branchId,
      currency: v.currency.trim().toUpperCase(),
      freightMode: v.freightMode,
      mode: v.mode,
      shipmentType: v.shipmentType,
      incoterm: v.incoterm,
      isHazardous: v.isHazardous,
      requiresCustomsBrokerage: v.requiresCustomsBrokerage,
      notes: v.notes.trim() || null,
      polId: laneBased ? null : (v.pol?.id ?? null),
      podId: laneBased ? null : (v.pod?.id ?? null),
      lane: laneBased
        ? {
            stops: p2p ? null : v.stops,
            originLocationId: p2p ? v.originLocationId.trim() || null : null,
            destinationLocationId: p2p ? v.destinationLocationId.trim() || null : null,
            requestedPickupWindowStart: v.requestedPickupWindowStart.trim() || null,
            requestedDeliveryWindowEnd: v.requestedDeliveryWindowEnd.trim() || null,
            totalDistanceKm: distance ?? null,
          }
        : undefined,
      cargo: hasCargoData
        ? {
            commodity: v.commodity?.trim() || null,
            palletCount: v.palletCount ?? undefined,
            totalWeightKg: v.totalWeightKg ?? undefined,
            declaredCargoValueAmount: v.declaredCargoValueAmount ?? undefined,
            declaredCargoValueCurrency:
              v.declaredCargoValueCurrency?.trim().toUpperCase() || v.currency.trim().toUpperCase(),
            requiresActiveRefrigeration: isReefer,
          }
        : undefined,
    };

    console.log(command);

    // Key off the flag only, so leftover setpoint values never leak into the
    // command when the user has switched cold chain off.
    if (isReefer) {
      command.temperatureSpec = {
        zone: v.temperatureZone,
        setpointCelsius: setpoint,
        allowableVarianceCelsius: variance,
        compliance: v.compliance,
        requiresContinuousTelematics: v.requiresContinuousTelematics,
      };
    }

    return command;
  }

  protected readonly stopTypeLabels: Record<string, string> = {
    pickup: "Pickup Location",
    dropoff: "Delivery Destination",
    waypoint: "Waypoint Stop",
    cross_dock_staging: "Crossdocking Staging",
  };

  protected handleAddStop(newStop: FormModel): void {
    const selectedLoc = this.selectedCustomerLocationsStore
      .data()
      .find((loc) => loc.id === newStop.locationId);

    const currentStops = this.model().stops ?? [];
    const stop: RouteStopSummaryDto = {
      sequence: currentStops.length + 1,
      type: newStop.stopType as RouteStopSummaryDto["type"],
      locationId: newStop.locationId,
      locationName: selectedLoc?.name ?? "",
      locationCode: selectedLoc?.code ?? null,
      windowStart: null,
      windowEnd: null,
    };

    this.model.update((curr) => ({
      ...curr,
      stops: [...(curr.stops ?? []), stop],
    }));
  }

  handleRemoveStop(stop: RouteStopSummaryDto): void {
    this.model.update((current) => ({
      ...current,
      stops: (current.stops ?? [])
        .filter((s) => s.sequence !== stop.sequence)
        .map((s, index) => ({
          ...s,
          sequence: index + 1,
        })),
    }));
  }
}
