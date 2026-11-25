import { DatePipe, TitleCasePipe, UpperCasePipe } from "@angular/common";
import { Component, computed, effect, inject, input, signal } from "@angular/core";
import { ErrorState, LoadingSkeleton, PdfViewerComponent, type PdfLoader } from "@trackwise/shared";
import {
  acceptPublicQuotation,
  Api,
  getPublicQuotation,
  getPublicQuotationPdf$Pdf,
  type PublicQuotationDto,
} from "@trackwise/shared/api";
import {
  Badge,
  Icon,
  Surface,
  Typography,
  UiButton,
  type UiBadgeIntent,
} from "@trackwise/shared/ui";
import { PublicLayout } from "@/shared/layout";

@Component({
  selector: "cp-public-quotation",
  templateUrl: "./public-quotation.html",
  imports: [
    Badge,
    DatePipe,
    ErrorState,
    LoadingSkeleton,
    PublicLayout,
    Surface,
    Typography,
    PdfViewerComponent,
    UiButton,
    TitleCasePipe,
    UpperCasePipe,
    Icon,
  ],
})
export class PublicQuotation {
  private readonly api = inject(Api);

  protected readonly tenantId = input.required<string>();
  protected readonly token = input.required<string>();

  protected readonly quotation = signal<PublicQuotationDto | null>(null);
  protected readonly isLoading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly pdfLoader: PdfLoader = async (token) => {
    const blob = await this.api.invoke(getPublicQuotationPdf$Pdf, {
      tenantId: this.tenantId(),
      token,
    });
    console.log("pdf blob", blob instanceof Blob, blob.type, blob.size);
    return blob;
  };

  protected readonly message = signal("");
  protected readonly isAccepting = signal(false);
  protected readonly acceptError = signal<string | null>(null);

  protected readonly canAccept = computed(() => this.quotation()?.status === "issued");
  protected readonly messageValid = computed(() => this.message().trim().length > 0);

  protected async accept(): Promise<void> {
    if (!this.messageValid() || this.isAccepting()) return;

    this.isAccepting.set(true);
    this.acceptError.set(null);
    try {
      await this.api.invoke(acceptPublicQuotation, {
        tenantId: this.tenantId(),
        token: this.token(),
        body: { message: this.message().trim() },
      });
      await this.loadData(this.tenantId(), this.token()); // status flips to Accepted
    } catch (err: unknown) {
      this.acceptError.set(err instanceof Error ? err.message : "Unable to accept the quotation.");
    } finally {
      this.isAccepting.set(false);
    }
  }

  constructor() {
    effect(() => {
      const tenantId = this.tenantId();
      const token = this.token();
      if (tenantId && token) {
        this.loadData(tenantId, token);
      }
    });
  }

  private async loadData(tenantId: string, token: string): Promise<void> {
    this.isLoading.set(true);
    this.error.set(null);
    try {
      const data = await this.api.invoke(getPublicQuotation, { tenantId, token });
      this.quotation.set(data);
    } catch (err: unknown) {
      console.error("Failed to load quotation:", err);
      this.error.set(
        err instanceof Error && err.message.includes("expired")
          ? "This quotation link has expired or been revoked."
          : "Unable to load quotation. The link may be invalid or expired.",
      );
    } finally {
      this.isLoading.set(false);
    }
  }

  protected formatStatus(status: string | undefined): string {
    return status ? status.replace(/_/g, " ").replace(/\b\w/g, (c) => c.toUpperCase()) : "";
  }

  protected getSeverity(status: string | undefined): UiBadgeIntent {
    switch (status) {
      case "issued":
        return "info";
      case "accepted":
        return "success";
      case "expired":
        return "warn";
      case "rejected":
      case "auto_declined":
        return "danger";
      default:
        return "info";
    }
  }

  protected formatMoney(amount: number | null | undefined): string {
    if (amount == null) return "—";
    return new Intl.NumberFormat("en-PH", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  }

  protected formatStopType(type: string | undefined): string {
    return type ? type.replace(/_/g, " ") : "";
  }
}
