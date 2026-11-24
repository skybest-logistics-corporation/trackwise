import { DatePipe, DecimalPipe } from "@angular/common";
import { Component, computed, inject, signal, type OnInit } from "@angular/core";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import { PdfViewerComponent, Permission, type PdfLoader } from "@trackwise/shared";
import {
  Api,
  cancelBooking,
  completeBooking,
  confirmBooking,
  generateBookingPdf$Pdf,
  getBookingById,
  promoteBookingToWorkfile,
  regenerateBookingBillingAdvice,
  removeBookingCharge,
  startBookingExecution,
  type BillIndicator,
  type BookingChargeDto,
  type BookingDto,
  type BookingEventDto,
  type FreightMode,
  type ReportCategory,
  type TruckingVariant,
  type WorkfileType,
} from "@trackwise/shared/api";
import {
  billIndicatorOptions,
  reportCategoryOptions,
  truckingVariantOptions,
  workfileTypeOptions,
} from "@trackwise/shared/api/enums";
import {
  Alert,
  Grid,
  Spinner,
  Stack,
  Surface,
  Typography,
  UiButton,
  UiDialog,
  UiFormField,
  UiSelectField,
  UiTextareaField,
} from "@trackwise/shared/ui";
import { PermissionService } from "@/core/auth";
import { ToastService } from "@/core/services";
import { CreateTripDialog, PageHeader } from "@/shared/components";
import { truckTonnageClassLabels, truckTypeLabels } from "@/shared/constants/truck-options";
import { EnumLabelPipe } from "@/shared/pipes";
import { AddBookingChargeDialog } from "../components/add-booking-charge-dialog/add-booking-charge-dialog";
import {
  bookingStatusLabel,
  chargeSourceLabel,
  creditCheckLabel,
  documentStatusLabel,
  documentTypeLabel,
  humanize,
  statusKey,
} from "../utils/booking-labels";

@Component({
  selector: "app-booking-details",
  templateUrl: "./booking-details.html",
  imports: [
    Alert,
    DatePipe,
    DecimalPipe,
    Grid,
    PageHeader,
    RouterLink,
    Spinner,
    Stack,
    Typography,
    UiButton,
    UiDialog,
    UiFormField,
    UiSelectField,
    UiTextareaField,
    Surface,
    PdfViewerComponent,
    AddBookingChargeDialog,
    CreateTripDialog,
    EnumLabelPipe,
  ],
})
export class BookingDetailsComponent implements OnInit {
  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);
  private readonly permissionService = inject(PermissionService);

  protected readonly booking = signal<BookingDto | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly isBusy = signal(false);

  protected readonly confirmDialogVisible = signal(false);
  protected readonly cancelDialogVisible = signal(false);
  protected readonly cancellationReason = signal("");
  protected readonly promoteDialogVisible = signal(false);

  protected readonly openBookingPdfViewer = signal(false);

  protected readonly addChargeDialogVisible = signal(false);

  protected readonly truckTypeLabels = truckTypeLabels;
  protected readonly tonnageClassLabels = truckTonnageClassLabels;

  protected readonly canManage = computed(() =>
    this.permissionService.hasPermission(Permission.Booking.Manage),
  );

  protected readonly isCompleted = computed(() => this.status() === "completed");

  /** Charges can be added until the booking is completed or cancelled (same rule as the domain). */
  protected readonly canAddCharge = computed(
    () => this.canManage() && !this.isCancelled() && !this.isCompleted(),
  );

  // Template helpers
  protected readonly statusLabel = bookingStatusLabel;
  protected readonly creditLabel = creditCheckLabel;
  protected readonly documentStatus = documentStatusLabel;
  protected readonly documentType = documentTypeLabel;
  protected readonly chargeSource = chargeSourceLabel;
  protected readonly label = humanize;

  // ── Status (normalised so 'in_progress', 'inProgress' and 'InProgress' all match) ──────────────

  private readonly status = computed(() => statusKey(this.booking()?.status));
  protected readonly isDraft = computed(() => this.status() === "draft");
  protected readonly isConfirmed = computed(() => this.status() === "confirmed");
  protected readonly isInProgress = computed(() => this.status() === "inprogress");
  protected readonly isCancelled = computed(() => this.status() === "cancelled");

  protected readonly createTripDialogVisible = signal(false);
  protected readonly isColdChainOrTrucking = computed(
    () => this.booking()?.freightMode === "reefer" || this.booking()?.freightMode === "trucking",
  );

  protected readonly canCancel = computed(() => this.isDraft() || this.isConfirmed());
  protected readonly canOpenWorkfile = computed(() => this.isConfirmed() || this.isInProgress());
  protected readonly canRegenerateBillingAdvice = computed(() => {
    const booking = this.booking();
    if (!booking) return false;

    const invoices = booking.billingInvoices ?? [];
    return (
      invoices.some((invoice) => statusKey(invoice.status) === "cancelled") &&
      !invoices.some((invoice) => statusKey(invoice.status) === "draft") &&
      (booking.charges ?? []).some(
        (charge) => charge.isBillable && statusKey(charge.billingStatus) === "unbilled",
      )
    );
  });

  // ── Readiness ──────────────────────────────────────────────────────────────

  /** Straight from Booking.GetConfirmationBlockers() on the server. */
  protected readonly blockers = computed(() => this.booking()?.confirmationBlockers ?? []);
  protected readonly canConfirm = computed(() => this.isDraft() && this.blockers().length === 0);

  protected readonly canViewCosts = computed(() =>
    this.permissionService.hasPermission(Permission.Booking.ViewCosts),
  );

  /** Only an added, not-yet-invoiced charge can be removed (quoted charges are locked). */
  protected canRemoveCharge(charge: BookingChargeDto): boolean {
    return (
      this.canAddCharge() &&
      statusKey(charge.source) === "additional" &&
      statusKey(charge.billingStatus) !== "invoiced"
    );
  }

  protected readonly chargeToRemove = signal<BookingChargeDto | null>(null);

  protected async confirmRemoveCharge(): Promise<void> {
    const bookingId = this.booking()?.id;
    const chargeId = this.chargeToRemove()?.id;
    if (!bookingId || !chargeId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(removeBookingCharge, { id: bookingId, chargeId });
      this.chargeToRemove.set(null);
      this.toastService.showSuccess("Charge removed.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to remove the charge.");
    } finally {
      this.isBusy.set(false);
    }
  }
  // ── Display ────────────────────────────────────────────────────────────────

  protected readonly pageTitle = computed(() => this.booking()?.bookingNumber ?? "Booking");

  protected readonly subtitle = computed(() => {
    const b = this.booking();
    if (!b) {
      return "";
    }
    const parts = [this.statusLabel(b.status)];
    if (b.quotationReference) {
      parts.push(`from ${b.quotationReference}`);
    }
    return parts.join(" · ");
  });

  protected readonly handlingFlags = computed(() => {
    const b = this.booking();
    if (!b) {
      return [];
    }
    return [
      b.isHazardous ? "Dangerous goods" : null,
      b.temperature ? "Temperature controlled" : null,
      b.requiresCustomsBrokerage ? "Customs brokerage" : null,
    ].filter((flag): flag is string => !!flag);
  });

  // ── Activity timeline ──────────────────────────────────────────────────────

  /** Newest first, as returned by the server. */
  protected readonly events = computed(() => this.booking()?.events ?? []);

  /** Dot colour: green for progress, red for cancellations and rejections, neutral otherwise. */
  protected eventDotClass(event: BookingEventDto): string {
    const key = statusKey(event.type);
    if (key === "cancelled" || key.endsWith("rejected")) return "bg-red-500";
    if (
      ["confirmed", "completed", "documentverified", "documentwaived", "creditapproved"].includes(
        key,
      )
    ) {
      return "bg-emerald-500";
    }
    return "bg-muted-foreground/50";
  }

  /** Where `relatedId` leads, for the event types that point at a page. Null = plain text. */
  protected eventLink(event: BookingEventDto): string[] | null {
    if (!event.relatedId) return null;

    switch (statusKey(event.type)) {
      case "chargesinvoiced":
      case "chargesreleased":
        return ["/billing-invoicing", event.relatedId];
      case "created":
        return ["/quotations", event.relatedId];
      default:
        return null;
    }
  }

  /** "Draft → Confirmed" only when the status actually changed. */
  protected eventTransition(event: BookingEventDto): string | null {
    if (!event.fromStatus || event.fromStatus === event.toStatus) return null;
    return `${this.statusLabel(event.fromStatus)} → ${this.statusLabel(event.toStatus)}`;
  }

  // ── Open Workfile dialog ───────────────────────────────────────────────────

  protected readonly promoteType = signal<WorkfileType>("regular" as WorkfileType);
  protected readonly promoteDirection = signal<FreightMode>("import" as FreightMode);
  protected readonly promoteVariant = signal<TruckingVariant>("truck" as TruckingVariant);
  protected readonly promoteReportCategory = signal<ReportCategory>("psb" as ReportCategory);
  protected readonly promoteBillIndicator = signal<BillIndicator>("hbl" as BillIndicator);

  protected readonly workfileTypes = workfileTypeOptions;
  protected readonly truckingVariants = truckingVariantOptions;
  protected readonly reportCategories = reportCategoryOptions;
  protected readonly billIndicators = billIndicatorOptions;
  protected readonly freightModes = [
    { label: "Domestic", value: "domestic" as FreightMode },
    { label: "Import", value: "import" as FreightMode },
    { label: "Export", value: "export" as FreightMode },
    { label: "Triangular", value: "triangular" as FreightMode },
  ];

  /** Reefer files carry no bill of lading, so the indicator is not asked for. */
  protected readonly promoteNeedsBillIndicator = computed(() => this.promoteType() !== "reefer");

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get("id");
    if (!id) {
      return;
    }

    this.isLoading.set(true);
    try {
      const result = await this.api.invoke(getBookingById, { id });
      console.log("Booking loaded:", result);
      this.booking.set(result);
    } catch {
      this.toastService.showError("Failed to load booking.");
    } finally {
      this.isLoading.set(false);
    }
  }

  protected openTrip(tripId: string): void {
    void this.router.navigate(["/trips", tripId]);
  }

  // ── Workfile ───────────────────────────────────────────────────────────────

  protected openPromoteDialog(): void {
    this.promoteType.set("regular" as WorkfileType);
    // The booking already knows its direction, so start from that instead of asking again.
    this.promoteDirection.set((this.booking()?.freightMode ?? "import") as FreightMode);
    this.promoteVariant.set("truck" as TruckingVariant);
    this.promoteReportCategory.set("psb" as ReportCategory);
    this.promoteBillIndicator.set("hbl" as BillIndicator);
    this.promoteDialogVisible.set(true);
  }

  protected async confirmPromote(): Promise<void> {
    const bookingId = this.booking()?.id;
    if (!bookingId || this.isBusy()) {
      return;
    }

    const type = this.promoteType();

    this.isBusy.set(true);
    try {
      const workfileId = await this.api.invoke(promoteBookingToWorkfile, {
        id: bookingId,
        body: {
          type,
          // The generated request types enums as `?: T`, never `T | null`, so an inapplicable
          // field is omitted rather than sent as null.
          direction: type === "regular" ? this.promoteDirection() : undefined,
          variant: type === "trucking" ? this.promoteVariant() : undefined,
          reportCategory: this.promoteReportCategory(),
          billIndicator: this.promoteNeedsBillIndicator() ? this.promoteBillIndicator() : undefined,
        },
      });

      this.promoteDialogVisible.set(false);
      this.toastService.showSuccess("Workfile opened.");
      await this.router.navigateByUrl(`/forwarding/workfiles/${workfileId}`);
    } catch {
      this.toastService.showError("Failed to open a workfile for this booking.");
    } finally {
      this.isBusy.set(false);
    }
  }

  // ── Lifecycle ──────────────────────────────────────────────────────────────

  /** Confirm takes no body now: the carrier reference lives on the trip, not the booking. */
  protected async confirmBookingAction(): Promise<void> {
    const bookingId = this.booking()?.id;
    if (!bookingId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(confirmBooking, { id: bookingId });
      this.confirmDialogVisible.set(false);
      this.toastService.showSuccess("Booking confirmed.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to confirm booking. Check the open requirements.");
      await this.load(); // blockers may have changed since the page was loaded
    } finally {
      this.isBusy.set(false);
    }
  }

  protected async startExecution(): Promise<void> {
    const bookingId = this.booking()?.id;
    if (!bookingId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(startBookingExecution, { id: bookingId });
      this.toastService.showSuccess("Booking is now in progress.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to start execution.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected async complete(): Promise<void> {
    const bookingId = this.booking()?.id;
    if (!bookingId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(completeBooking, { id: bookingId });
      this.toastService.showSuccess("Booking completed.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to complete booking.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected async regenerateBillingAdvice(): Promise<void> {
    const bookingId = this.booking()?.id;
    if (!bookingId || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      const invoice = await this.api.invoke(regenerateBookingBillingAdvice, { bookingId });
      this.toastService.showSuccess("Replacement billing advice generated.");
      await this.router.navigateByUrl(`/billing-invoicing/${invoice.id}`);
    } catch {
      this.toastService.showError("Failed to regenerate billing advice.");
      await this.load();
    } finally {
      this.isBusy.set(false);
    }
  }

  protected openCancelDialog(): void {
    this.cancellationReason.set("");
    this.cancelDialogVisible.set(true);
  }

  protected async confirmCancel(): Promise<void> {
    const bookingId = this.booking()?.id;
    const reason = this.cancellationReason().trim();
    if (!bookingId || !reason || this.isBusy()) {
      return;
    }

    this.isBusy.set(true);
    try {
      await this.api.invoke(cancelBooking, { id: bookingId, body: { reason } });
      this.cancelDialogVisible.set(false);
      this.toastService.showSuccess("Booking cancelled.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to cancel booking.");
    } finally {
      this.isBusy.set(false);
    }
  }

  /** Why Confirm is disabled; shown as a tooltip on the button. */
  protected readonly confirmHint = computed(() => {
    if (!this.isDraft() || this.canConfirm()) {
      return null;
    }
    const count = this.blockers().length;
    return `${count} open requirement${count === 1 ? "" : "s"} before this booking can be confirmed.`;
  });

  protected readonly loadBookingPdf: PdfLoader = (id) =>
    this.api.invoke(generateBookingPdf$Pdf, { id });

  // Internal copy (cost permission): a second loader, not a flag on the viewer
  protected readonly loadBookingPdfInternal: PdfLoader = (id) =>
    this.api.invoke(generateBookingPdf$Pdf, { id, includeInternalCosts: true });
}
