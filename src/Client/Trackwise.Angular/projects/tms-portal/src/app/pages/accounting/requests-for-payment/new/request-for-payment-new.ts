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

interface RfpFormModel {
  vendorName: string;
  vendorTin: string;
  priority: "normal" | "urgent" | "critical_release";
  vendorInvoiceNumber: string;
  invoiceDate: Date | null;
  dueDate: Date | null;
  paymentMethod: "check" | "managers_check" | "bank_transfer" | "online_portal";
  bankName: string;
  bankAccountNumber: string;
  currency: string;
  vatType: "vat_12" | "zero_rated" | "vat_exempt";
  ewtRate: number;
  remarks: string;
}

@Component({
  selector: "app-request-for-payment-new",
  templateUrl: "./request-for-payment-new.html",
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
export class RequestForPaymentNewComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  protected readonly isLoading = signal(true);
  protected readonly booking = signal<BookingDto | null>(null);
  protected readonly allocations = signal<DisbursementLineAllocation[]>([]);

  protected readonly priorities = [
    { label: "Normal (Standard AP Run)", value: "normal" },
    { label: "Urgent (Same/Next Day)", value: "urgent" },
    { label: "Critical Cargo Release (Avoid Demurrage/Detention)", value: "critical_release" },
  ];

  protected readonly paymentMethods = [
    { label: "Corporate Check", value: "check" },
    { label: "Manager's Check (Shipping Line / Port)", value: "managers_check" },
    { label: "Bank Transfer / Wire", value: "bank_transfer" },
    { label: "Carrier / Terminal Online Portal", value: "online_portal" },
  ];

  protected readonly vatTypes = [
    { label: "12% VAT Inclusive (Local Registered Vendor)", value: "vat_12" },
    { label: "0% Zero-Rated (International Carrier / PEZA)", value: "zero_rated" },
    { label: "VAT-Exempt / Non-VAT", value: "vat_exempt" },
  ];

  protected readonly ewtOptions = [
    { label: "0% – None / International Carrier", value: 0 },
    { label: "1% – Purchase of Goods (WC158)", value: 1 },
    { label: "2% – Freight, Trucking, Arrastre, Customs Broker (WC160)", value: 2 },
    { label: "5% – Rentals / Equipment Lease (WC100)", value: 5 },
    { label: "10% – Professional / Brokerage Fee Individual", value: 10 },
  ];

  protected readonly model = signal<RfpFormModel>({
    vendorName: "",
    vendorTin: "",
    priority: "normal",
    vendorInvoiceNumber: "",
    invoiceDate: new Date(),
    dueDate: new Date(),
    paymentMethod: "bank_transfer",
    bankName: "",
    bankAccountNumber: "",
    currency: "PHP",
    vatType: "vat_12",
    ewtRate: 2,
    remarks: "",
  });

  protected readonly backLink = computed(() => {
    const id = this.booking()?.id;
    return id ? `/bookings-executions/${id}` : "/accounting/requests-for-payment";
  });

  protected readonly bookingSubtitle = computed(() => {
    const b = this.booking();
    return b ? `Booking ${b.bookingNumber} · ${b.customerName}` : "Create vendor payment request";
  });

  protected readonly taxBreakdown = computed(() => {
    const gross = this.allocations().reduce((sum, l) => sum + Number(l.requestedAmount || 0), 0);
    const { vatType, ewtRate } = this.model();

    const netOfVat = vatType === "vat_12" ? gross / 1.12 : gross;
    const vatAmount = vatType === "vat_12" ? gross - netOfVat : 0;
    const ewtAmount = netOfVat * (Number(ewtRate || 0) / 100);
    const netPayable = gross - ewtAmount;

    return { gross, netOfVat, vatAmount, ewtAmount, netPayable };
  });

  protected readonly form = form(
    this.model,
    (p) => {
      required(p.vendorName, { message: "Payee / Vendor name is required." });
      required(p.vendorInvoiceNumber, { message: "Vendor billing / SOA reference is required." });
      required(p.invoiceDate, { message: "Invoice date is required." });
      required(p.dueDate, { message: "Due date is required." });
      required(p.currency, { message: "Currency is required." });
      required(p.remarks, { message: "Payment remarks are required." });

      const pm = this.model().paymentMethod;
      if (pm === "bank_transfer" || pm === "online_portal") {
        required(p.bankName, { message: "Bank or portal name is required." });
        required(p.bankAccountNumber, { message: "Account or reference number is required." });
      }
    },
    {
      submission: {
        action: async () => {
          if (this.allocations().length === 0) {
            this.toastService.showError("At least one booking charge line must be included.");
            return undefined;
          }

          const current = this.model();
          const payload = {
            kind: "rfp" as const,
            bookingId: this.booking()?.id ?? null,
            ...current,
            invoiceDate: toIsoDateString(current.invoiceDate),
            dueDate: toIsoDateString(current.dueDate),
            ...this.taxBreakdown(),
            lines: this.allocations(),
          };

          try {
            // Replace with generated API call, e.g. await this.api.invoke(createRequestForPayment, { body: payload });
            console.log("Submitting RFP:", payload);
            this.toastService.showSuccess("Request for Payment submitted.");
            await this.router.navigateByUrl(this.backLink());
          } catch {
            this.toastService.showError("Failed to submit Request for Payment.");
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
        remarks: `Payment for ${lines.map((l) => l.codeAndDescription).join(", ")} under Booking ${b.bookingNumber}`,
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
