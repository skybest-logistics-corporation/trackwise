import { CurrencyPipe, DatePipe, TitleCasePipe, UpperCasePipe } from "@angular/common";
import { Component, DestroyRef, inject, input, signal, type OnInit } from "@angular/core";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import {
  Api,
  getTariffById,
  removeTariffRate,
  type TariffDto,
  type TariffRateDto,
} from "@trackwise/shared/api";
import {
  Card,
  ConfirmDeleteDialog,
  CountBadge,
  EmptyState,
  Grid,
  Icon,
  InfoItem,
  Spinner,
  Stack,
  Typography,
  UiButton,
  UiDataTable,
  UiTabsImports,
} from "@trackwise/shared/ui";
import { distinctUntilChanged, map } from "rxjs";
import { ToastService } from "@/core/services";
import { PageHeader } from "@/shared/components";
import { truckTonnageClassLabels, truckTypeLabels } from "@/shared/constants/truck-options";
import { EnumLabelPipe } from "@/shared/pipes";
import { TariffCopyComponent } from "../components/tariff-copy/tariff-copy.component";

@Component({
  selector: "app-tariff-details",
  templateUrl: "./tariff-details.html",
  imports: [
    Card,
    CountBadge,
    CurrencyPipe,
    DatePipe,
    EnumLabelPipe,
    EmptyState,
    Grid,
    Icon,
    InfoItem,
    PageHeader,
    RouterLink,
    Spinner,
    Stack,
    TitleCasePipe,
    Typography,
    UiButton,
    UiDataTable,
    UiTabsImports,
    UpperCasePipe,
    ConfirmDeleteDialog,
    TariffCopyComponent,
  ],
})
export class TariffDetails implements OnInit {
  private readonly api = inject(Api);
  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  public readonly id = input.required<string>();
  protected readonly isLoading = signal(true);
  protected readonly tariff = signal<TariffDto | null>(null);

  protected readonly toDeleteRate = signal<TariffRateDto | null>(null);
  protected readonly deleteRateConfirmVisible = signal(false);
  protected readonly isDeletingLine = signal(false);

  protected readonly truckTypeLabels = truckTypeLabels;
  protected readonly tonnageClassLabels = truckTonnageClassLabels;

  protected readonly isCopyOpen = signal(false);

  ngOnInit(): void {
    void this.loadTariff();

    this.route.paramMap
      .pipe(
        map((params) => params.get("id")),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((id) => {
        if (id) void this.loadTariff();
      });
  }

  protected async loadTariff(): Promise<void> {
    this.isLoading.set(true);
    try {
      const response = await this.api.invoke(getTariffById, { id: this.id() });
      console.log("Tariff Details Response:", response);
      this.tariff.set(response);
    } catch {
      this.toastService.showError("Failed to load tariff");
    } finally {
      this.isLoading.set(false);
    }
  }

  protected promptDeleteRate(rate: TariffRateDto): void {
    this.toDeleteRate.set(rate);

    this.deleteRateConfirmVisible.set(true);
  }

  protected async onConfirmDeleteRate(): Promise<void> {
    const rateId = this.toDeleteRate()?.id;
    if (!rateId) return;

    this.isDeletingLine.set(true);

    try {
      await this.api.invoke(removeTariffRate, {
        rateId,
      });

      this.deleteRateConfirmVisible.set(false);
      this.toDeleteRate.set(null);
      this.loadTariff();
    } finally {
      this.isDeletingLine.set(false);
    }
  }

  protected onTariffCopied(newTariffId: string): void {
    // Navigate to the newly copied tariff
    void this.router.navigate(["/tariffs", newTariffId]);
    console.log("Navigating to copied tariff with ID:", newTariffId);
  }
}
