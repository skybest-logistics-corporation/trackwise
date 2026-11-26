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

interface PettyCashFormModel {
  fundCode: string;
  voucherMode: "reimbursement_with_or" | "unliquidated_release";
  voucherDate: Date | null;
  receivedBy: string;
  expenseCategory: string;
  merchantName: string;
  merchantTin: string;
  officialReceiptNumber: string;
  vatStatus: "vat_12" | "non_vat";
  currency: string;
  particulars: string;
}

@Component({
  selector: "app-petty-cash-new",
  templateUrl: "./petty-cash-new.html",
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
export class PettyCashNewComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  protected readonly fundLimit = 5_000; // Standard 5,000 PHP revolving PCV ceiling
  protected readonly isLoading = signal(true);
  protected readonly booking = signal<BookingDto | null>(null);
  protected readonly allocations = signal<DisbursementLineAllocation[]>([]);

  protected readonly pettyCashFunds = [
    { label: "Manila Head Office – Operations PCF", value: "PCF-MNL-OPS" },
    { label: "Port / Customs Liaison PCF", value: "PCF-PORT" },
    { label: "Fleet & Garage Dispatch PCF", value: "PCF-FLEET" },
  ];

  protected readonly voucherModes = [
    { label: "Reimbursement (Official Receipt / SI On-Hand)", value: "reimbursement_with_or" },
    { label: "Petty Cash Advance (To Liquidate w/ OR)", value: "unliquidated_release" },
  ];

  protected readonly expenseCategories = [
    { label: "Documentation & BL / AWB Release Fees", value: "documentation" },
    { label: "Port Entry / Terminal Gate Pass / Stickers", value: "port_pass" },
    { label: "Courier & Waybill Dispatch", value: "courier" },
    { label: "Toll, Parking & Emergency Trip Expense", value: "trip_misc" },
    { label: "Notarization & Customs Stamping", value: "notary_customs" },
  ];

  protected readonly vatStatuses = [
    { label: "12% VAT", value: "vat_12" },
    { label: "Non-VAT", value: "non_vat" },
  ];

  protected readonly model = signal<PettyCashFormModel>({
    fundCode: "PCF-MNL-OPS",
    voucherMode: "reimbursement_with_or",
    voucherDate: new Date(),
    receivedBy: "",
    expenseCategory: "documentation",
    merchantName: "",
    merchantTin: "",
    officialReceiptNumber: "",
    vatStatus: "vat_12",
    currency: "PHP",
    particulars: "",
  });

  protected readonly backLink = computed(() => {
    const id = this.booking()?.id;
    return id ? `/bookings-executions/${id}` : "/accounting/petty-cash";
  });

  protected readonly bookingSubtitle = computed(() => {
    const b = this.booking();
    return b ? `Booking ${b.bookingNumber} · ${b.customerName}` : "Create petty cash voucher";
  });

  protected readonly totalPcv = computed(() =>
    this.allocations().reduce((sum, l) => sum + Number(l.requestedAmount || 0), 0),
  );

  protected readonly exceedsLimit = computed(
    () => this.model().currency === "PHP" && this.totalPcv() > this.fundLimit,
  );

  protected readonly form = form(
    this.model,
    (p) => {
      required(p.fundCode, { message: "Petty cash fund is required." });
      required(p.voucherDate, { message: "Voucher date is required." });
      required(p.receivedBy, { message: "Recipient name is required." });
      required(p.particulars, { message: "Particulars are required." });

      if (this.model().voucherMode === "reimbursement_with_or") {
        required(p.merchantName, { message: "Supplier / merchant name is required." });
        required(p.officialReceiptNumber, { message: "OR / Sales Invoice number is required." });
      }
    },
    {
      submission: {
        action: async () => {
          const current = this.model();
          const payload = {
            kind: "petty-cash" as const,
            bookingId: this.booking()?.id ?? null,
            ...current,
            voucherDate: toIsoDateString(current.voucherDate),
            totalAmount: this.totalPcv(),
            lines: this.allocations(),
          };

          try {
            // Replace with generated API call, e.g. await this.api.invoke(createPettyCashVoucher, { body: payload });
            console.log("Submitting Petty Cash Voucher:", payload);
            this.toastService.showSuccess("Petty Cash Voucher saved.");
            await this.router.navigateByUrl(this.backLink());
          } catch {
            this.toastService.showError("Failed to save Petty Cash Voucher.");
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

      this.model.update((curr) => ({
        ...curr,
        currency: b.currency ?? "PHP",
        particulars: `Petty cash for ${lines.map((l) => l.codeAndDescription).join(", ")} (${b.bookingNumber})`,
      }));
    } catch {
      this.toastService.showError("Failed to load booking charges.");
    } finally {
      this.isLoading.set(false);
    }
  }

  protected updateLineAmount(index: number, rawValue: string): void {
    const amount = Math.max(0, Number(rawValue || 0));
    this.allocations.update((rows) =>
      rows.map((r, i) => (i === index ? { ...r, requestedAmount: amount } : r)),
    );
  }
}
