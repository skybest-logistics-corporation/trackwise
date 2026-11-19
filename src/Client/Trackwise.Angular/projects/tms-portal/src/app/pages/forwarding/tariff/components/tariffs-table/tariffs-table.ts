import { TitleCasePipe, UpperCasePipe } from "@angular/common";
import { Component, computed, effect, inject, input, output, signal } from "@angular/core";
import { Router, RouterLink } from "@angular/router";
import type { AppError } from "@trackwise/shared";
import type { TariffDto } from "@trackwise/shared/api";
import { DateFormatPipe } from "@trackwise/shared/pipes";
import type { ListLazyLoadEvent } from "@trackwise/shared/stores";
import {
  Card,
  UiButton,
  UiDataTable,
  UiMenu,
  UiSortHeader,
  UiTableRowDirectives,
  UiTooltip,
  type UiMenuItem,
} from "@trackwise/shared/ui";
import { DataContainer } from "@/shared/components";
import { truckTonnageClassLabels, truckTypeLabels } from "@/shared/constants/truck-options";
import { EnumLabelPipe } from "@/shared/pipes";

@Component({
  selector: "app-tariffs-table",
  templateUrl: "./tariffs-table.html",
  imports: [
    Card,
    DataContainer,
    DateFormatPipe,
    EnumLabelPipe,
    TitleCasePipe,
    UpperCasePipe,
    RouterLink,
    UiButton,
    UiDataTable,
    UiMenu,
    UiSortHeader,
    UiTableRowDirectives,
    UiTooltip,
  ],
})
export class TariffsTable {
  private readonly router = inject(Router);

  public readonly data = input.required<TariffDto[]>();
  public readonly isLoading = input(false);
  public readonly error = input<AppError | null>(null);
  public readonly isEmpty = input(false);
  public readonly totalRecords = input(0);
  public readonly pageSize = input(10);
  public readonly first = input(0);

  public readonly lazyLoad = output<ListLazyLoadEvent>();
  public readonly retry = output<void>();
  public readonly addTariff = output<void>();
  public readonly deleteTariff = output<TariffDto>();

  protected readonly selectedRow = signal<TariffDto | null>(null);

  protected readonly truckTypeLabels = truckTypeLabels;
  protected readonly tonnageClassLabels = truckTonnageClassLabels;

  constructor() {
    effect(() => {
      const rows = this.data();
      console.log("[TariffsTable] data:", rows);
      console.table(rows); // quick scan of the columns, including truckType / tonnageClass
      console.log("[TariffsTable] state:", {
        isLoading: this.isLoading(),
        isEmpty: this.isEmpty(),
        totalRecords: this.totalRecords(),
        first: this.first(),
        pageSize: this.pageSize(),
        error: this.error(),
      });
    });
  }

  protected readonly actionMenuItems = computed<UiMenuItem[]>(() => {
    const tariff = this.selectedRow();

    return [
      {
        label: "View tariff",
        icon: "eye",
        command: () => {
          if (tariff?.id) {
            this.router.navigateByUrl(`/tariffs/${tariff.id}`);
          }
        },
      },
      {
        label: "Edit tariff",
        icon: "square-pen",
        command: () => {
          if (tariff?.id) {
            this.router.navigateByUrl(`/tariffs/edit/${tariff.id}`);
          }
        },
      },
      {
        separator: true,
      },
      {
        label: "Deactivate tariff",
        icon: "circle-minus",
        command: () => {
          if (tariff) {
            this.deleteTariff.emit(tariff);
          }
        },
      },
    ];
  });

  protected onLazyLoad(event: ListLazyLoadEvent): void {
    this.lazyLoad.emit(event);
  }

  protected onRetry(): void {
    this.retry.emit();
  }

  protected onAddTariff(): void {
    this.addTariff.emit();
  }

  protected navigateToTariff(tariff: TariffDto): void {
    if (tariff.id) {
      this.router.navigateByUrl(`/tariffs/${tariff.id}`);
    }
  }

  protected getStatusBorderClass(isActive: boolean | undefined): string {
    return isActive ? "border-l-4 border-l-emerald-500" : "border-l-4 border-l-muted";
  }
}
