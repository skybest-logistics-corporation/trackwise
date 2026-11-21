import { Component, effect, inject, input, model, output, signal } from "@angular/core";
import { NonNullableFormBuilder, ReactiveFormsModule } from "@angular/forms";
import { form, FormField, FormRoot, maxLength, required } from "@angular/forms/signals";
import { Api, copyTariff } from "@trackwise/shared/api";
import { Stack, UiButton, UiDialog, UiFormField, UiTextField } from "@trackwise/shared/ui";
import { ToastService } from "@/core/services";

interface TariffCopyModel {
  name: string;
}

const EMPTY: TariffCopyModel = {
  name: "",
};

@Component({
  selector: "app-tariff-copy",
  templateUrl: "./tariff-copy.component.html",
  imports: [
    UiDialog,
    UiButton,
    UiFormField,
    UiTextField,
    Stack,
    ReactiveFormsModule,
    FormRoot,
    FormField,
  ],
})
export class TariffCopyComponent {
  private readonly api = inject(Api);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly toastService = inject(ToastService);

  public readonly tariffId = input.required<string>();
  public readonly originName = input<string>("");
  public readonly isOpen = model<boolean>(false);
  public readonly copied = output<string>();

  protected readonly isSubmitting = signal(false);

  protected readonly model = signal<TariffCopyModel>({
    ...EMPTY,
  });

  constructor() {
    effect(
      () => {
        if (this.isOpen()) {
          this.model.set({ name: `${this.originName()} - Copy` });
        }
      },
      { allowSignalWrites: true },
    );
  }

  protected readonly form = form(
    this.model,
    (p) => {
      required(p.name, {
        message: "Tariff name is required.",
      });

      maxLength(p.name, 200, {
        message: "Tariff name cannot exceed 200 characters.",
      });
    },
    {
      submission: {
        action: async () => {
          this.isSubmitting.set(true);
          try {
            const response = await this.api.invoke(copyTariff, {
              body: {
                id: this.tariffId(),
                name: this.model().name,
              },
            });

            this.toastService.showSuccess("Tariff copied successfully");
            console.log(response);
            this.copied.emit(response as unknown as string);
            this.close();
          } catch {
            this.toastService.showError("Failed to copy tariff");
          } finally {
            this.isSubmitting.set(false);
          }
          return undefined;
        },
      },
    },
  );

  protected close(): void {
    this.model.set({ ...EMPTY });
    this.model.set({ name: "" });
    this.isOpen.set(false);
  }
}
