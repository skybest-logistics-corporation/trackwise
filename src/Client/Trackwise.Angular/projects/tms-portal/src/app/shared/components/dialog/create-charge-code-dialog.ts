import { Component, effect, inject, input, model, output, signal, untracked } from "@angular/core";
import { form, FormField, maxLength, minLength, required } from "@angular/forms/signals";
import {
  Api,
  createChargeCode,
  type ChargeCategory,
  type ChargeCodeDto,
  type CreateChargeCodeCommand,
  type FreightMode,
} from "@trackwise/shared/api";
import { freightModeOptions } from "@trackwise/shared/api/enums";
import {
  UiButton,
  UiDialog,
  UiFormField,
  UiMultiSelectField,
  UiSelectField,
  UiTextField,
} from "@trackwise/shared/ui";
import { ToastService } from "@/core/services";

interface ChargeCodeModel {
  code: string;
  name: string;
  category: ChargeCategory | null;
  applicableModes: FreightMode[];
  defaultUnit: string;
}

/** Quick-create for a charge code from inside another dialog. Full editing stays on the tariff page. */
@Component({
  selector: "app-create-charge-code-dialog",
  templateUrl: "./create-charge-code-dialog.html",
  imports: [
    FormField,
    UiButton,
    UiDialog,
    UiFormField,
    UiMultiSelectField,
    UiSelectField,
    UiTextField,
  ],
})
export class CreateChargeCodeDialog {
  private readonly api = inject(Api);
  private readonly toastService = inject(ToastService);

  readonly open = model(false);
  /** What the user typed in the search box; used to prefill the form. */
  readonly seed = input("");
  /** Emits the saved code (e.g. "DEM") so the caller can reload and select it. */
  readonly created = output<ChargeCodeDto>();

  protected readonly isBusy = signal(false);
  protected readonly modeOptions = freightModeOptions;
  protected readonly categoryOptions = [
    { label: "Base", value: "base" as ChargeCategory },
    { label: "Accessorial", value: "accessorial" as ChargeCategory },
    { label: "Surcharge", value: "surcharge" as ChargeCategory },
    { label: "Tax", value: "tax" as ChargeCategory },
    { label: "Discount", value: "discount" as ChargeCategory },
  ];

  protected readonly model = signal<ChargeCodeModel>(this.emptyModel(""));

  protected readonly form = form(this.model, (p) => {
    required(p.code, { message: "Charge code is required." });
    minLength(p.code, 2, { message: "Charge code must be at least 2 characters." });
    maxLength(p.code, 20, { message: "Charge code cannot exceed 20 characters." });
    required(p.name, { message: "Name is required." });
    required(p.category, { message: "Category is required." });
    required(p.applicableModes, { message: "Select at least one applicable mode." });
    required(p.defaultUnit, { message: "Default unit is required." });
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        const seed = this.seed();
        untracked(() => this.model.set(this.emptyModel(seed)));
      }
    });
  }

  protected async submit(): Promise<void> {
    if (!this.form().valid() || this.isBusy()) {
      return;
    }

    const value = this.model();
    const command: CreateChargeCodeCommand = {
      code: value.code.trim().toUpperCase(),
      name: value.name.trim(),
      description: null,
      category: value.category!,
      applicableModes: value.applicableModes,
      defaultUnit: value.defaultUnit.trim(),
      requiresApproval: false,
      taxCode: null,
    };

    this.isBusy.set(true);
    try {
      const created = await this.api.invoke(createChargeCode, { body: command });
      this.toastService.showSuccess(`${command.code} charge code created`);
      this.open.set(false);
      this.created.emit(created);
    } catch {
      this.toastService.showError("Failed to create charge code");
    } finally {
      this.isBusy.set(false);
    }
  }

  /** A short single word reads as a code ("DEM"); anything longer reads as a name. */
  private emptyModel(seed: string): ChargeCodeModel {
    const text = seed.trim();
    const looksLikeCode = text.length > 0 && text.length <= 6 && !/\s/.test(text);

    return {
      code: looksLikeCode ? text.toUpperCase() : "",
      name: looksLikeCode ? "" : text,
      category: "accessorial",
      applicableModes: freightModeOptions.map((o) => o.value),
      defaultUnit: "per shipment",
    };
  }
}
