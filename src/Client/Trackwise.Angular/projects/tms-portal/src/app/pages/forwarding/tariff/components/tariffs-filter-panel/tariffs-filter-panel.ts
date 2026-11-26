import { Component, input, output, signal } from "@angular/core";
import { type FreightMode, type ServiceLevel, type TruckType } from "@trackwise/shared/api";
import { freightModeOptions, serviceLevelOptions } from "@trackwise/shared/api/enums";
import {
  Card,
  Grid,
  Stack,
  UiButton,
  UiFormField,
  UiSelectField,
  UiTextField,
} from "@trackwise/shared/ui";
import { truckTypeOptions } from "@/shared/constants/truck-options";

export interface TariffsFilterState {
  mode: FreightMode | null;
  serviceLevel: ServiceLevel | null;
  isActive: boolean | null;
  truckType: TruckType | null;
}

@Component({
  selector: "app-tariffs-filter-panel",
  templateUrl: "./tariffs-filter-panel.html",
  imports: [Grid, Stack, Card, UiButton, UiFormField, UiSelectField, UiTextField],
})
export class TariffsFilterPanel {
  public readonly isLoading = input(false);

  public readonly filtersChanged = output<TariffsFilterState>();
  public readonly searchChanged = output<string>();

  protected readonly selectedMode = signal<FreightMode | null>(null);
  protected readonly selectedTruckType = signal<TruckType | null>(null);
  protected readonly selectedServiceLevel = signal<ServiceLevel | null>(null);
  protected readonly selectedStatus = signal<boolean | null>(true);

  protected readonly freightModeFilterOptions = [
    { label: "All Modes", value: null },
    ...freightModeOptions,
  ];
  protected readonly truckTypeFilterOptions = [
    { label: "All Types", value: null },
    ...truckTypeOptions,
  ];

  protected readonly serviceLevelFilterOptions = [
    { label: "All Service Levels", value: null },
    ...serviceLevelOptions,
  ];

  protected readonly statusOptions = [
    { label: "All Statuses", value: null },
    { label: "Active", value: true },
    { label: "Inactive", value: false },
  ];

  protected hasFilters(): boolean {
    return (
      this.selectedMode() !== null ||
      this.selectedServiceLevel() !== null ||
      this.selectedStatus() !== null
    );
  }

  protected onSearch(value: string): void {
    this.searchChanged.emit(value);
  }

  protected onModeChanged(value: FreightMode | null): void {
    this.selectedMode.set(value);
    this.emitFilters();
  }
  protected onTruckTypeChanged(value: TruckType | null): void {
    this.selectedTruckType.set(value);
    this.emitFilters();
  }

  protected onServiceLevelChanged(value: ServiceLevel | null): void {
    this.selectedServiceLevel.set(value);
    this.emitFilters();
  }

  protected onStatusChanged(value: boolean | null): void {
    this.selectedStatus.set(value);
    this.emitFilters();
  }

  protected clearFilters(): void {
    this.selectedMode.set(null);
    this.selectedServiceLevel.set(null);
    this.selectedStatus.set(null);

    this.emitFilters();
  }

  private emitFilters(): void {
    this.filtersChanged.emit({
      mode: this.selectedMode(),
      serviceLevel: this.selectedServiceLevel(),
      truckType: this.selectedTruckType(),
      isActive: this.selectedStatus(),
    });
  }
}
