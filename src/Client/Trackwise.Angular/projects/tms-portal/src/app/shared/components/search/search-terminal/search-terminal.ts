import { Component, ElementRef, inject, input, model, output, signal } from "@angular/core";
import type { FormValueControl } from "@angular/forms/signals";
import { focusFirstControl } from "@trackwise/shared";
import {
  Api,
  getTerminals,
  type TerminalDto,
  type TerminalSummaryDto,
} from "@trackwise/shared/api";
import { UiSelectField } from "@trackwise/shared/ui";

const MIN_QUERY_LENGTH = 2;
const DEBOUNCE_MS = 300;

interface TerminalOption {
  id: string | undefined;
  label: string;
  terminal: TerminalSummaryDto;
}

/**
 * Terminal search autocomplete.
 *
 * Implements `FormValueControl` only - see `text-field.ts` for the FormValueControl bridge contract.
 */
@Component({
  selector: "app-search-terminal",
  templateUrl: "./search-terminal.html",
  imports: [UiAutocompleteField],
})
export class SearchTerminal implements FormValueControl<TerminalSummaryDto | null> {
  private readonly api = inject(Api);

  protected readonly suggestedTerminals = signal<TerminalDto[]>([]);
  protected readonly lastQuery = signal<string>("");

  /** The control's value. Required by `FormValueControl`. */
  public readonly value = model<TerminalSummaryDto | null>(null);
  public readonly disabled = input<boolean>(false);
  public readonly touch = output<void>();
  public readonly placeholder = model<string>("Select a terminal");

  protected readonly loading = signal(false);
  private readonly terminals = signal<TerminalSummaryDto[]>([]);

  private debounceHandle: ReturnType<typeof setTimeout> | undefined;
  private requestSeq = 0;

  /** Driven by the Reactive Forms / Signal Forms bridge. */
  public readonly disabled = input<boolean>(false);

  /** Raised on blur so the form can mark the field touched. */
  public readonly touch = output<void>();

  public readonly placeholder = model<string>("Type a terminal code or name");

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** Signal Forms calls this via `FieldState.focusBoundControl()`. */
  public focus(options?: FocusOptions): void {
    focusFirstControl(this.host.nativeElement, options);
  }

  protected async searchTerminal(event: { query: string }): Promise<void> {
    const q = event.query?.trim() ?? "";
    this.lastQuery.set(q);

    if (q.length < 2) {
      this.suggestedTerminals.set([]);
      return;
    }

    try {
      // Active only: a retired port still resolves for the loads that already reference it, but
      // offering one here would put new work through a terminal nobody calls at any more.
      const result = await this.api.invoke(getTerminals, { Search: q, IsActive: true });
      this.suggestedTerminals.set(result.items ?? []);
    } catch {
      this.suggestedTerminals.set([]);
    }
  }
}
