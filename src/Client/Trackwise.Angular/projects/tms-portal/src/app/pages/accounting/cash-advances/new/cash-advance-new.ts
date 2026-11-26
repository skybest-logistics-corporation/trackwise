import { DecimalPipe } from "@angular/common";
import { Component, computed, inject, signal, type OnInit } from "@angular/core";
import { form, FormField, FormRoot, required } from "@angular/forms/signals";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import { Api, getBookingById, type BookingDto } from "@trackwise/shared/api";
import {
  Card,
  Container,
  Grid,
  Icon,
  Spinner,
  Stack,
  Typography,
  UiButton,
  UiDateField,
  UiFormField,
  UiSelectField,
  UiTextareaField,
  UiTextField,
  ValidatedForm,
} from "@trackwise/shared/ui";
import { ToastService } from "@/core/services";
import { PageHeader } from "@/shared/components";
import {
  extractSelectedAllocations,
  toIsoDateString,
  type DisbursementLineAllocation,
} from "@/shared/utils/disbursement-loader";

interface CashAdvanceFormModel {
  custodianName: string;
  employeeCode: string;
  department: string;
  dateNeeded: Date | null;
  liquidationDueDate: Date | null;
  fieldLocation: string;
  releaseMethod: "cash_counter" | "gcash_maya" | "payroll_bank";
  disbursementAccount: string;
  currency: string;
  purpose: string;
  acceptedLiquidationPolicy: boolean;
}

const addDays = (days: number): Date => {
  const d = new Date();
  d.setDate(d.getDate() + days);
  return d;
};

@Component({
  selector: "app-cash-advance-new",
  templateUrl: "./cash-advance-new.html",
  imports: [
    Card,
    Container,
    DecimalPipe,
    FormField,
    FormRoot,
    Grid,
    Icon,
    PageHeader,
    RouterLink,
    Spinner,
    Stack,
    Typography,
    UiButton,
    UiFormField,
    UiSelectField,
    UiTextareaField,
    UiTextField,
    UiDateField,
    ValidatedForm,
  ],
})
export class CashAdvanceNewComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  protected readonly isLoading = signal(true);
  protected readonly booking = signal<BookingDto | null>(null);
  protected readonly allocations = signal<DisbursementLineAllocation[]>([]);

  protected readonly departments = [
    { label: "Customs Brokerage & Port Liaison", value: "customs_liaison" },
    { label: "Forwarding Operations", value: "forwarding_ops" },
    { label: "Fleet / Trucking Dispatch", value: "fleet_dispatch" },
    { label: "Cold Chain / Reefer Operations", value: "reefer_ops" },
  ];

  protected readonly releaseMethods = [
    { label: "Cashier Counter Release", value: "cash_counter" },
    { label: "GCash / Maya Instant Transfer", value: "gcash_maya" },
    { label: "Employee Bank / ATM Account", value: "payroll_bank" },
  ];

  protected readonly model = signal<CashAdvanceFormModel>({
    custodianName: "",
    employeeCode: "",
    department: "customs_liaison",
    dateNeeded: addDays(0),
    liquidationDueDate: addDays(5),
    fieldLocation: "",
    releaseMethod: "cash_counter",
    disbursementAccount: "",
    currency: "PHP",
    purpose: "",
    acceptedLiquidationPolicy: false,
  });

  protected readonly backLink = computed(() => {
    const id = this.booking()?.id;
    return id ? `/bookings-executions/${id}` : "/accounting/cash-advances";
  });

  protected readonly bookingSubtitle = computed(() => {
    const b = this.booking();
    return b ? `Booking ${b.bookingNumber} · ${b.customerName}` : "Create field cash advance";
  });

  protected readonly totalAdvance = computed(() =>
    this.allocations().reduce((sum, l) => sum + Number(l.requestedAmount || 0), 0),
  );

  protected readonly form = form(
    this.model,
    (p) => {
      required(p.custodianName, { message: "Custodian / employee name is required." });
      required(p.department, { message: "Department is required." });
      required(p.dateNeeded, { message: "Date needed is required." });
      required(p.liquidationDueDate, { message: "Liquidation due date is required." });
      required(p.fieldLocation, { message: "Port or field location is required." });
      required(p.purpose, { message: "Purpose is required." });

      if (this.model().releaseMethod !== "cash_counter") {
        required(p.disbursementAccount, { message: "Account or e-wallet number is required." });
      }
    },
    {
      submission: {
        action: async () => {
          if (!this.model().acceptedLiquidationPolicy) {
            this.toastService.showError("Please confirm the liquidation undertaking.");
            return undefined;
          }

          const current = this.model();
          const payload = {
            kind: "cash-advance" as const,
            bookingId: this.booking()?.id ?? null,
            ...current,
            dateNeeded: toIsoDateString(current.dateNeeded),
            liquidationDueDate: toIsoDateString(current.liquidationDueDate),
            totalAmount: this.totalAdvance(),
            lines: this.allocations(),
          };

          try {
            // Replace with generated API call, e.g. await this.api.invoke(createCashAdvance, { body: payload });
            console.log("Submitting Cash Advance:", payload);
            this.toastService.showSuccess("Cash Advance request submitted.");
            await this.router.navigateByUrl(this.backLink());
          } catch {
            this.toastService.showError("Failed to submit Cash Advance.");
          }
          return undefined;
        },
      },
    },
  );

  async ngOnInit(): Promise<void> {
    const bookingId = this.route.snapshot.queryParamMap.get("bookingId");
    const chargeIds = this.route.snapshot.queryParamMap.get("chargeIds");

    if (!bookingId) {
      this.isLoading.set(false);
      return;
    }

    try {
      const b = await this.api.invoke(getBookingById, { id: bookingId });
      this.booking.set(b);
      const lines = extractSelectedAllocations(b, chargeIds);
      this.allocations.set(lines);

      const defaultPort = b.pod?.code || b.pol?.code || "";
      this.model.update((curr) => ({
        ...curr,
        currency: b.currency ?? "PHP",
        fieldLocation: defaultPort,
        purpose: `Operational cash advance for ${lines.map((l) => l.codeAndDescription).join(", ")} (${b.bookingNumber})`,
      }));
    } catch {
      this.toastService.showError("Failed to load booking charges.");
    } finally {
      this.isLoading.set(false);
    }
  }

  protected togglePolicy(checked: boolean): void {
    this.model.update((curr) => ({ ...curr, acceptedLiquidationPolicy: checked }));
  }

  protected updateLineAmount(index: number, rawValue: string): void {
    const amount = Math.max(0, Number(rawValue || 0));
    this.allocations.update((rows) =>
      rows.map((r, i) => (i === index ? { ...r, requestedAmount: amount } : r)),
    );
  }
}
