import { Component, inject } from "@angular/core";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { Router, RouterLink } from "@angular/router";
import { Permission } from "@trackwise/shared";
import { Api, deactivateTariff, type TariffDto } from "@trackwise/shared/api";
import { PermissionService } from "@trackwise/shared/services";
import { Stack, UiButton, UiTooltip } from "@trackwise/shared/ui";
import { downloadBlobFile } from "@trackwise/shared/utils";
import { debounceTime, distinctUntilChanged, map, Subject } from "rxjs";
import { ToastService } from "@/core/services";
import { PageHeader } from "@/shared/components";
import {
  TariffsFilterPanel,
  type TariffsFilterState,
} from "../components/tariffs-filter-panel/tariffs-filter-panel";
import { TariffsTable } from "../components/tariffs-table/tariffs-table";
import { TariffsListStore } from "../store/tariffs-list.store";

@Component({
  selector: "app-tariffs-list",
  templateUrl: "./tariffs-list.html",
  providers: [TariffsListStore],
  imports: [PageHeader, RouterLink, Stack, TariffsFilterPanel, TariffsTable, UiButton, UiTooltip],
})
export class TariffsListComponent {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  private readonly search$ = new Subject<string>();

  protected readonly store = inject(TariffsListStore);
  protected readonly canManage = inject(PermissionService).hasPermission(Permission.Tariff.Manage);

  constructor() {
    this.store.setFilters(
      {
        EffectiveOn: this.todayAsIsoDate(),
        OnlyActive: true,
        IsActive: true,
      },
      { reload: false },
    );

    this.search$
      .pipe(
        map((value) => value.trim()),
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((search) => this.store.setSearch(search));
  }

  protected addTariff(): void {
    this.router.navigate(["/tariffs/add"]);
  }

  protected onFiltersChanged(filters: TariffsFilterState): void {
    const apiFilters: Record<string, unknown> = {
      EffectiveOn: this.todayAsIsoDate(),
      OnlyActive: false,
      Mode: filters.mode ?? undefined,
      TruckType: filters.truckType ?? undefined,
      ServiceLevel: filters.serviceLevel ?? undefined,
      IsActive: filters.isActive ?? undefined,
    };

    this.store.setFilters(apiFilters);
  }

  private todayAsIsoDate(): string {
    const today = new Date();
    const month = String(today.getMonth() + 1).padStart(2, "0");
    const day = String(today.getDate()).padStart(2, "0");
    return `${today.getFullYear()}-${month}-${day}`;
  }

  protected onSearchChanged(search: string): void {
    this.search$.next(search);
  }

  protected exportToCsv(): void {
    const tariffs = this.store.data();

    if (tariffs.length === 0) {
      this.toastService.showWarning("No tariffs to export");
      return;
    }

    const headers = [
      "Name",
      "Freight Mode",
      "Origin Lane",
      "Destination Lane",
      "Equipment Type",
      "Service Level",
      "Currency",
      "Effective From",
      "Status",
      "Contract ID",
    ];

    const rows = tariffs.map((tariff) => [
      tariff.name ?? "",
      tariff.mode ?? "",
      tariff.originLane ?? "",
      tariff.destinationLane ?? "",
      tariff.containerTypeCode ?? "",
      tariff.serviceLevel ?? "",
      tariff.currency ?? "",
      tariff.effectiveFrom ?? "",
      tariff.isActive ? "Active" : "Inactive",
      tariff.contractRef ?? "",
    ]);

    const csvContent = [headers, ...rows]
      .map((row) => row.map((cell) => `"${String(cell).replace(/"/g, '""')}"`).join(","))
      .join("\n");

    const blob = new Blob([csvContent], {
      type: "text/csv;charset=utf-8;",
    });

    downloadBlobFile(blob, `tariffs-export-${new Date().toISOString().split("T")[0]}.csv`);

    this.toastService.showSuccess(`Exported ${tariffs.length} tariff(s) to CSV`);
  }

  protected askDeactivate(tariff: TariffDto): void {
    if (!tariff.id) {
      return;
    }

    this.toastService.confirm({
      header: "Deactivate Tariff",
      message: `Deactivate tariff "${tariff.name}"? It will remain available in inactive history.`,
      accept: async () => {
        try {
          await this.api.invoke(deactivateTariff, { id: tariff.id!, body: {} });

          this.toastService.showSuccess(`Tariff "${tariff.name}" has been deactivated`);

          this.store.retry();
        } catch {
          this.toastService.showError("Failed to deactivate tariff");
        }
      },
    });
  }
}
