import { DecimalPipe } from "@angular/common";
import {
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
} from "@angular/core";
import { addBookingCharge, Api, getChargeCodes, type ChargeCodeDto } from "@trackwise/shared/api";
import {
  UiButton,
  UiDialog,
  UiFormField,
  UiNumberField,
  UiSelectField,
  UiTextareaField,
} from "@trackwise/shared/ui";
import { ToastService } from "@/core/services";
import { CreateChargeCodeDialog } from "@/shared/components/dialog/create-charge-code-dialog";
import { statusKey } from "../../utils/booking-labels";

/**
 * Adds a charge that was not in the quotation. Quoted charges are locked, so this is the only way
 * to bill something extra (detention, extra handling, a goodwill discount...).
 */
@Component({
  selector: "app-add-booking-charge-dialog",
  templateUrl: "./add-booking-charge-dialog.html",
  imports: [
    DecimalPipe,
    UiButton,
    UiDialog,
    UiFormField,
    UiNumberField,
    UiSelectField,
    UiTextareaField,
    CreateChargeCodeDialog,
  ],
})
export class AddBookingChargeDialog {
  private readonly api = inject(Api);
  private readonly toastService = inject(ToastService);

  protected readonly searchText = signal("");
  protected readonly createOpen = signal(false);
  protected readonly createSeed = signal("");

  readonly bookingId = input.required<string>();
  readonly currency = input.required<string>();
  protected readonly chargeCodes = signal<ChargeCodeDto[]>([]);
  /** Show buying cost. Hide it for roles that may add a charge but not see costs. */
  readonly showCosts = input(true);

  readonly open = model(false);
  /** Emitted after the charge is saved, so the page can reload. */
  readonly added = output<void>();

  protected readonly chargeCodeOptions = computed(() =>
    this.chargeCodes().map((ch) => ({
      value: ch,
      label: ch.name ? `${ch.code} - ${ch.name}` : (ch.code ?? ""),
    })),
  );

  protected readonly chargeCode = signal<ChargeCodeDto | null>(null);
  protected readonly quantity = signal<number | null>(1);
  protected readonly unitSelling = signal<number | null>(null);
  protected readonly unitBuying = signal<number | null>(0);
  protected readonly reason = signal("");
  protected readonly isBusy = signal(false);

  /** A discount is entered as a positive amount and applied as a reduction, like quotation lines. */
  protected readonly isDiscount = computed(
    () => statusKey(this.chargeCode()?.category) === "discount",
  );

  private readonly sign = computed(() => (this.isDiscount() ? -1 : 1));
  /** Unit hint from the selected charge code (e.g. "per shipment"). */
  protected readonly unitLabel = computed(() => this.chargeCode()?.defaultUnit ?? null);
  protected readonly sellingTotal = computed(
    () => this.sign() * (this.quantity() ?? 0) * (this.unitSelling() ?? 0),
  );
  protected readonly buyingTotal = computed(() =>
    this.showCosts() ? this.sign() * (this.quantity() ?? 0) * (this.unitBuying() ?? 0) : 0,
  );

  /** Same formula as BookingTotals on the server. */
  protected readonly margin = computed(() => this.sellingTotal() - this.buyingTotal());
  protected readonly marginPercent = computed(() => {
    const selling = this.sellingTotal();
    return selling === 0 ? 0 : (this.margin() / selling) * 100;
  });

  /** Selling below cost on a normal charge. Not meaningful for discounts, which reduce both sides. */
  protected readonly isBelowCost = computed(
    () =>
      this.showCosts() &&
      !this.isDiscount() &&
      (this.unitSelling() ?? 0) < (this.unitBuying() ?? 0),
  );

  protected readonly isValid = computed(
    () =>
      this.chargeCode() !== null &&
      (this.quantity() ?? 0) > 0 &&
      (this.unitSelling() ?? -1) >= 0 &&
      (!this.showCosts() || (this.unitBuying() ?? -1) >= 0) &&
      this.reason().trim().length > 0,
  );
  constructor() {
    // Start every opening from a clean form.
    effect(() => {
      if (this.open()) {
        untracked(() => this.reset());
      }
    });

    void this.loadChargeCodes();
  }

  /** Offer "create" only when the search matches nothing, using the select's own label match. */
  protected readonly createLabel = computed(() => {
    const query = this.searchText().trim();
    if (!query) return undefined;

    const lower = query.toLowerCase();
    const hasMatch = this.chargeCodeOptions().some((o) => o.label.toLowerCase().includes(lower));
    return hasMatch ? undefined : `Create charge code "${query}"`;
  });

  protected openCreate(text: string): void {
    this.createSeed.set(text || this.searchText());
    this.createOpen.set(true);
  }

  protected onChargeCodeCreated(created: ChargeCodeDto): void {
    this.chargeCodes.update((codes) => [...codes, created]);
    this.chargeCode.set(created);
    this.searchText.set("");
  }

  protected async submit(): Promise<void> {
    const chargeCode = this.chargeCode();
    if (!chargeCode || !this.isValid() || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(addBookingCharge, {
        id: this.bookingId(),
        body: {
          chargeCodeId: chargeCode.id,
          quantity: this.quantity() ?? 0,
          unitSelling: this.unitSelling() ?? 0,
          // Hidden cost field: send 0 rather than a stale value.
          unitBuying: this.showCosts() ? (this.unitBuying() ?? 0) : 0,
          reason: this.reason().trim(),
        },
      });

      this.toastService.showSuccess("Charge added.");
      this.open.set(false);
      this.added.emit();
    } catch {
      this.toastService.showError("Failed to add the charge. The booking may be closed.");
    } finally {
      this.isBusy.set(false);
    }
  }

  private reset(): void {
    this.chargeCode.set(null);
    this.quantity.set(1);
    this.unitSelling.set(null);
    this.unitBuying.set(0);
    this.reason.set("");
  }

  private async loadChargeCodes(): Promise<void> {
    try {
      const codes = await this.api.invoke(getChargeCodes, { OnlyActive: true });
      this.chargeCodes.set(codes ?? []);
    } catch (error) {
      console.error("Failed to load charge codes:", error);
      this.chargeCodes.set([]);
    }
  }
}
