import { DatePipe, DecimalPipe } from "@angular/common";
import { Component, computed, DestroyRef, inject, signal, type OnInit } from "@angular/core";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { ActivatedRoute, Router } from "@angular/router";
import { PdfViewerComponent, PermissionService, type PdfLoader } from "@trackwise/shared";
import {
  acceptQuotation,
  addQuotationLines,
  Api,
  approveQuotation,
  convertQuotationToBooking,
  createQuotationRevision,
  expireQuotation,
  generateQuotationPdf,
  getQuotationById,
  issueQuotation,
  rejectQuotation,
  removeQuotationLine,
  submitQuotation,
  updateQuotationLine,
  type ConvertQuotationToBookingRequest,
  type FreightMode,
  type QuotationDto,
  type QuotationEventType,
  type QuotationLineDto,
  type QuotationStatus,
} from "@trackwise/shared/api";
import {
  Alert,
  ConfirmDeleteDialog,
  Grid,
  Icon,
  Spinner,
  Stack,
  Surface,
  Typography,
  UiButton,
  UiDateField,
  UiDialog,
  UiFormField,
  UiNumberField,
  UiTextareaField,
  UiTextField,
  type IconColor,
  type IconName,
} from "@trackwise/shared/ui";
import { distinctUntilChanged, map } from "rxjs";
import { ContainerTypeOptionsService, ToastService } from "@/core/services";
import { PageHeader } from "@/shared/components";
import {
  QuotationLineForm,
  type QuotationLineFormValue,
} from "../components/quotation-line-form/quotation-line-form";
import { QuotationLinesTable } from "../components/quotation-lines-table/quotation-lines-table";
import type { LineUpdate } from "../components/update-quotation-line-form/update-quotation-line-form";

interface TimelineMeta {
  label: string;
  icon: IconName;
  color: IconColor;
}

const LANE_FREIGHT_MODES: readonly FreightMode[] = ["reefer", "trucking"];

@Component({
  selector: "app-quotation-details",
  templateUrl: "./quotation-details.html",
  imports: [
    Alert,
    DatePipe,
    DecimalPipe,
    Grid,
    Icon,
    PageHeader,
    QuotationLineForm,
    QuotationLinesTable,
    Spinner,
    Stack,
    Surface,
    Typography,
    UiButton,
    UiTextField,
    UiDateField,
    UiDialog,
    UiFormField,
    UiNumberField,
    UiTextareaField,
    ConfirmDeleteDialog,
    PdfViewerComponent,
  ],
})
export class QuotationDetailsComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);

  private readonly api = inject(Api);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);
  private readonly containerTypes = inject(ContainerTypeOptionsService);

  protected readonly containerTypeOptions = this.containerTypes.options;

  protected readonly quotation = signal<QuotationDto | null>(null);
  protected readonly isLoading = signal(false);
  protected readonly isBusy = signal(false);

  protected readonly addLineDialogVisible = signal(false);
  protected readonly rejectDialogVisible = signal(false);
  protected readonly rejectionReason = signal("");

  protected readonly issueDialogVisible = signal(false);
  protected readonly validityDays = signal<number>(30);
  protected readonly customerEmail = signal<string>("");

  protected readonly convertDialogVisible = signal(false);
  protected readonly repeatBooking = signal(false);
  protected readonly convertContainerTypeId = signal<string | null>(null);
  protected readonly convertPickupDate = signal<Date | null>(null);
  protected readonly convertDeliveryDate = signal<Date | null>(null);
  protected readonly convertNotes = signal("");

  protected readonly isNotesExpanded = signal(false);

  protected readonly openQuotationPdfViewer = signal(false);

  // Deletion dialog state
  protected readonly deleteLineDialogVisible = signal(false);
  protected readonly isDeletingLine = signal(false);
  protected readonly linesToDelete = signal<QuotationLineDto[]>([]);

  // Edit lines dialog state
  protected readonly editLinesVisible = signal(false);
  protected readonly isUpdatingLines = signal(false);

  protected readonly deleteEntityLabel = computed(() =>
    this.linesToDelete().length === 1 ? "Quotation Line" : "Quotation Lines",
  );

  protected readonly deleteEntityName = computed(() => {
    const lines = this.linesToDelete();
    return lines.length === 1 ? (lines[0].chargeName ?? "") : `${lines.length} lines`;
  });

  private readonly currentPermissions = inject(PermissionService);

  protected readonly acceptDialogVisible = signal(false);
  protected readonly acceptMessage = signal("");

  /** Lane-based (reefer / trucking) bookings can't be dispatched without a pickup date. */
  protected readonly isPickupDateRequired = computed(() => this.isLaneBased());

  protected readonly canApprove = computed(() =>
    this.currentPermissions.hasPermission("Permission.Quotation.Approve"),
  );

  private readonly timelineMeta: Record<QuotationEventType, TimelineMeta> = {
    created: { label: "Created", icon: "file-pen-line", color: "muted" },
    edited: { label: "Edited", icon: "pencil", color: "muted" },
    revision_created: { label: "Revision Created", icon: "file-pen", color: "muted" },
    submitted: { label: "Submitted", icon: "send", color: "primary" },
    approved: { label: "Approved", icon: "circle-check", color: "success" },
    rejected: { label: "Rejected", icon: "circle-x", color: "danger" },
    issued: { label: "Issued", icon: "file-output", color: "primary" },
    accepted: { label: "Accepted", icon: "shield-check", color: "success" },
    expired: { label: "Expired", icon: "calendar-x", color: "warning" },
    converted_to_booking: { label: "Converted to Booking", icon: "calendar", color: "success" },
  };

  protected timelineEntry(type?: string): TimelineMeta {
    return (
      this.timelineMeta[type as QuotationEventType] ?? {
        label: this.formatLabel(type),
        icon: "circle",
        color: "muted",
      }
    );
  }

  ngOnInit(): void {
    void this.containerTypes.load();

    this.route.paramMap
      .pipe(
        map((params) => params.get("id")),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        if (id) void this.load(id);
      });
  }

  protected readonly pickupDateMissing = computed(
    () => this.isPickupDateRequired() && !this.convertPickupDate(),
  );

  /** Distinct truck type + tonnage combinations across the lines' tariffs. */
  private readonly vehicleSpecs = computed(() => {
    const specs = new Map<string, string>();
    for (const line of this.quotation()?.lines ?? []) {
      if (!line.tariffTruckType && !line.tariffTonnageClass) continue; // not a vehicle-based tariff
      const key = `${line.tariffTruckType ?? ""}|${line.tariffTonnageClass ?? ""}`;
      const label = [
        this.formatLabel(line.tariffTruckType),
        this.formatLabel(line.tariffTonnageClass),
      ]
        .filter((part) => part !== "-")
        .join(" ");
      specs.set(key, label);
    }
    return [...specs.values()];
  });

  /** A booking is one truck type and size, so a mixed quotation can't be converted as is. */
  protected readonly hasMixedVehicleSpecs = computed(() => this.vehicleSpecs().length > 1);
  protected readonly mixedVehicleSummary = computed(() => this.vehicleSpecs().join(", "));

  /** Revisions can only be made from the latest, non-draft version. */
  protected readonly canCreateRevision = computed(() => {
    const q = this.quotation();
    return !!q && q.status !== "draft" && q.revisionNumber === q.revisions?.length;
  });

  /** Reefer / Trucking quote a stop-by-stop lane; everything else quotes POL / POD. */
  protected readonly isLaneBased = computed(() => {
    const mode = this.quotation()?.freightMode;
    return !!mode && LANE_FREIGHT_MODES.includes(mode);
  });

  protected readonly isReefer = computed(() => this.quotation()?.freightMode === "reefer");

  protected readonly sortedStops = computed(() =>
    [...(this.quotation()?.stops ?? [])].sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)),
  );

  protected readonly stopTypeLabels: Record<string, string> = {
    pickup: "Pickup",
    dropoff: "Drop-off",
    waypoint: "Waypoint",
    cross_dock_staging: "Cross-dock Staging",
  };

  protected readonly polLabel = computed(() =>
    this.quotation()?.mode === "air" ? "Airport of Departure (POL)" : "Port of Loading (POL)",
  );
  protected readonly podLabel = computed(() =>
    this.quotation()?.mode === "air" ? "Airport of Destination (POD)" : "Port of Discharge (POD)",
  );

  protected promptDeleteLines(lineIds: string[]): void {
    const ids = new Set(lineIds);
    const lines = (this.quotation()?.lines ?? []).filter((l) => !!l.id && ids.has(l.id));
    if (lines.length === 0) return;

    this.linesToDelete.set(lines);
    this.deleteLineDialogVisible.set(true);
  }

  protected async onConfirmDeleteLines(): Promise<void> {
    const quotationId = this.quotation()?.id;
    const lines = this.linesToDelete();
    if (!quotationId || lines.length === 0 || this.isDeletingLine()) return;

    this.isDeletingLine.set(true);
    let failed = 0;
    try {
      // Sequential: each delete changes the same quotation (totals), so parallel calls risk conflicts.
      for (const line of lines) {
        try {
          await this.api.invoke(removeQuotationLine, { id: quotationId, lineId: line.id! });
        } catch {
          failed++;
        }
      }

      const removed = lines.length - failed;
      if (failed === 0) {
        this.toastService.showSuccess(
          `${removed} line${removed === 1 ? "" : "s"} removed from quotation.`,
        );
      } else {
        this.toastService.showError(
          `${removed} removed, ${failed} failed. The failed lines are still selected.`,
        );
      }

      this.deleteLineDialogVisible.set(false);
      await this.load();
    } finally {
      this.isDeletingLine.set(false);
    }
  }

  protected async onUpdateLines(updates: LineUpdate[]): Promise<void> {
    const id = this.quotation()?.id;
    if (!id || updates.length === 0 || this.isUpdatingLines()) return;

    this.isUpdatingLines.set(true);
    let failed = 0;
    try {
      // Sequential, like the bulk delete: each update touches the same quotation.
      for (const { lineId, body } of updates) {
        try {
          await this.api.invoke(updateQuotationLine, { id, lineId, body });
        } catch {
          failed++;
        }
      }

      const updated = updates.length - failed;
      if (failed === 0) {
        this.toastService.showSuccess(`${updated} line${updated === 1 ? "" : "s"} updated.`);
      } else {
        this.toastService.showError(
          `${updated} updated, ${failed} failed. The failed lines are still selected.`,
        );
      }

      this.editLinesVisible.set(false);
      await this.load();
    } finally {
      this.isUpdatingLines.set(false);
    }
  }

  private async load(id?: string): Promise<void> {
    const quotationId = id ?? this.quotation()?.id ?? this.route.snapshot.paramMap.get("id");
    if (!quotationId) return;

    this.isLoading.set(true);
    try {
      const result = await this.api.invoke(getQuotationById, { id: quotationId });
      this.quotation.set(result);
    } catch {
      this.toastService.showError("Failed to load quotation.");
    } finally {
      this.isLoading.set(false);
    }
  }

  // protected async addLine(value: QuotationLineFormValue): Promise<void> {
  //   const quotationId = this.quotation()?.id;
  //   if (!quotationId || this.isBusy()) return;

  //   this.isBusy.set(true);
  //   try {
  //     await this.api.invoke(addQuotationLine, { id: quotationId, body: value });
  //     this.addLineDialogVisible.set(false);
  //     this.toastService.showSuccess("Line added to quotation.");
  //     await this.load();
  //   } catch {
  //     this.toastService.showError("Failed to add line. Check the tariff rate and currency.");
  //   } finally {
  //     this.isBusy.set(false);
  //   }
  // }

  protected submit(): void {
    const id = this.quotation()?.id;
    if (!id) return;

    this.toastService.confirm({
      header: "Submit Quotation",
      message: "Submit this quotation for internal approval?",
      accept: async () => {
        this.isBusy.set(true);
        try {
          await this.api.invoke(submitQuotation, { id });
          this.toastService.showSuccess("Quotation submitted for approval.");
          await this.load();
        } catch {
          this.toastService.showError("Failed to submit quotation.");
        } finally {
          this.isBusy.set(false);
        }
      },
    });
  }

  protected approve(): void {
    const quotation = this.quotation();
    if (!quotation?.id) return;

    this.toastService.confirm({
      header: "Approve Quotation",
      message: `Approve this quotation for ${quotation.customerName}?`,
      accept: async () => {
        this.isBusy.set(true);
        try {
          await this.api.invoke(approveQuotation, { id: quotation.id! });
          this.toastService.showSuccess("Quotation approved.");
          await this.load();
        } catch {
          this.toastService.showError("Failed to approve quotation.");
        } finally {
          this.isBusy.set(false);
        }
      },
    });
  }

  protected openIssueDialog(): void {
    this.validityDays.set(30);
    this.customerEmail.set(this.quotation()?.customerEmail ?? "");
    this.issueDialogVisible.set(true);
  }

  protected async confirmIssue(): Promise<void> {
    const id = this.quotation()?.id;
    if (!id || this.isBusy()) return;

    this.isBusy.set(true);
    try {
      await this.api.invoke(issueQuotation, {
        id,
        body: {
          validityDays: this.validityDays(),
          customerEmail: this.customerEmail(),
        },
      });
      this.issueDialogVisible.set(false);
      this.toastService.showSuccess("Quotation issued and sent successfully.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to issue quotation.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected openAcceptDialog(): void {
    this.acceptMessage.set("");
    this.acceptDialogVisible.set(true);
  }

  protected async confirmAccept(): Promise<void> {
    const id = this.quotation()?.id;
    if (!id || this.isBusy()) return;

    const message = this.acceptMessage().trim();

    this.isBusy.set(true);
    try {
      await this.api.invoke(acceptQuotation, {
        id,
        body: { message: message || null },
      });
      this.acceptDialogVisible.set(false);
      this.toastService.showSuccess("Quotation accepted.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to accept quotation.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected expire(): void {
    const id = this.quotation()?.id;
    if (!id) return;

    this.toastService.confirm({
      header: "Expire Quotation",
      message: "Are you sure you want to expire this quotation?",
      accept: async () => {
        this.isBusy.set(true);
        try {
          await this.api.invoke(expireQuotation, { id });
          this.toastService.showSuccess("Quotation marked as expired.");
          await this.load();
        } catch {
          this.toastService.showError("Failed to expire quotation.");
        } finally {
          this.isBusy.set(false);
        }
      },
    });
  }

  protected edit(): void {
    const id = this.quotation()?.id;
    if (!id) return;

    void this.router.navigate(["/quotations", id, "edit"]);
  }

  protected openRejectDialog(): void {
    this.rejectionReason.set("");
    this.rejectDialogVisible.set(true);
  }

  protected async confirmReject(): Promise<void> {
    const quotationId = this.quotation()?.id;
    const reason = this.rejectionReason().trim();
    if (!quotationId || !reason || this.isBusy()) return;

    this.isBusy.set(true);
    try {
      await this.api.invoke(rejectQuotation, { id: quotationId, body: { reason } });
      this.rejectDialogVisible.set(false);
      this.toastService.showSuccess("Quotation rejected.");
      await this.load();
    } catch {
      this.toastService.showError("Failed to reject quotation.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected createRevision(): void {
    const quotationId = this.quotation()?.id;
    if (!quotationId) return;

    this.toastService.confirm({
      header: "Create Quotation Revision",
      message: "Create a new draft with the same customer, freight mode, route, and charge setup?",
      accept: async () => {
        this.isBusy.set(true);
        try {
          const revisionId = await this.api.invoke(createQuotationRevision, { id: quotationId });
          this.toastService.showSuccess("Quotation revision created.");
          await this.router.navigateByUrl(`/quotations/${revisionId}`);
        } catch {
          this.toastService.showError("Failed to create quotation revision.");
        } finally {
          this.isBusy.set(false);
        }
      },
    });
  }

  protected openRevision(id?: string): void {
    if (!id) return;
    void this.router.navigateByUrl(`/quotations/${id}`);
  }

  protected openConvertDialog(): void {
    this.repeatBooking.set(false);
    this.openBookingDialog();
  }

  protected openRepeatBookingDialog(): void {
    this.repeatBooking.set(true);
    this.openBookingDialog();
  }

  private openBookingDialog(): void {
    this.convertContainerTypeId.set(null);
    this.convertPickupDate.set(null);
    this.convertDeliveryDate.set(null);
    this.convertNotes.set("");
    this.convertDialogVisible.set(true);
  }

  protected async confirmConvertToBooking(): Promise<void> {
    const quotationId = this.quotation()?.id;
    if (!quotationId || this.isBusy() || this.hasMixedVehicleSpecs()) return;

    if (this.pickupDateMissing()) {
      this.toastService.showWarning("Requested pickup date is required for this freight mode.");
      return;
    }

    const body: ConvertQuotationToBookingRequest = {
      containerTypeId: this.convertContainerTypeId() ?? undefined,
      requestedPickupDate: this.convertPickupDate()?.toISOString() ?? null,
      requestedDeliveryDate: this.convertDeliveryDate()?.toISOString() ?? null,
      notes: this.convertNotes().trim() || null,
      repeat: this.repeatBooking(),
    };

    this.isBusy.set(true);
    try {
      const bookingId = await this.api.invoke(convertQuotationToBooking, { id: quotationId, body });
      this.convertDialogVisible.set(false);
      this.toastService.showSuccess(
        this.repeatBooking()
          ? "Another booking was created from this quotation."
          : "Booking created from this quotation.",
      );
      await this.router.navigateByUrl(`/bookings-executions/${bookingId}`);
    } catch {
      this.toastService.showError("Failed to convert quotation to booking.");
    } finally {
      this.isBusy.set(false);
    }
  }

  protected statusBadgeClass(status?: QuotationStatus): string {
    switch (status) {
      case "accepted":
      case "approved":
        return "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400";
      case "issued":
        return "bg-blue-500/10 text-blue-600 dark:text-blue-400";
      case "pending_approval":
        return "bg-amber-500/10 text-amber-600 dark:text-amber-400";
      case "rejected":
      case "auto_declined":
      case "expired":
        return "bg-destructive/10 text-destructive";
      case "draft":
      default:
        return "bg-muted text-muted-foreground";
    }
  }

  protected async addLines(lines: QuotationLineFormValue[]): Promise<void> {
    const quotationId = this.quotation()?.id;
    if (!quotationId || lines.length === 0 || this.isBusy()) return;

    this.isBusy.set(true);
    try {
      await this.api.invoke(addQuotationLines, { id: quotationId, body: { lines } });
      this.addLineDialogVisible.set(false);
      this.toastService.showSuccess(
        lines.length === 1
          ? "Line added to quotation."
          : `${lines.length} lines added to quotation.`,
      );
      await this.load();
    } catch {
      this.toastService.showError(
        "Failed to add lines. Nothing was saved, so check the tariff rates and currency.",
      );
    } finally {
      this.isBusy.set(false);
    }
  }

  protected formatLabel(text?: string | null): string {
    if (!text) return "-";
    return text.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase());
  }

  protected readonly loadQuotationPdf: PdfLoader = (id) =>
    this.api.invoke(generateQuotationPdf, { id });
}
