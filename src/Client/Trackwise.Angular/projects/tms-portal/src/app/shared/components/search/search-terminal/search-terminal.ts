import {
  Component,
  computed,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
} from "@angular/core";
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
  imports: [UiSelectField],
})
export class SearchTerminal implements FormValueControl<TerminalSummaryDto | null> {
  private readonly api = inject(Api);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The control's value. Required by `FormValueControl`. */
  public readonly value = model<TerminalSummaryDto | null>(null);
  public readonly disabled = input<boolean>(false);
  public readonly touch = output<void>();
  public readonly placeholder = model<string>("Select a terminal");

  protected readonly loading = signal(false);
  private readonly terminals = signal<TerminalSummaryDto[]>([]);

  private debounceHandle: ReturnType<typeof setTimeout> | undefined;
  private requestSeq = 0;

  /**
   * Search results, plus the current value if it isn't in them (e.g. edit mode, or after the
   * list changed), so the trigger can always render the selection.
   */
  protected readonly options = computed<TerminalOption[]>(() => {
    const list = this.terminals();
    const current = this.value();
    const items = current?.id && !list.some((t) => t.id === current.id) ? [current, ...list] : list;

    return items.map((terminal) => ({
      id: terminal.id,
      label: `${terminal.code} - ${terminal.name}`,
      terminal,
    }));
  });

  /** The select works on ids, so DTO identity no longer matters. */
  protected readonly selectedId = computed(() => this.value()?.id ?? null);

  constructor() {
    void this.search(""); // preload a default page so the panel isn't empty on open
  }

  protected onSelect(id: string | null): void {
    this.value.set(this.options().find((o) => o.id === id)?.terminal ?? null);
  }

  protected onFilterChange(query: string): void {
    clearTimeout(this.debounceHandle);
    this.debounceHandle = setTimeout(() => void this.search(query), DEBOUNCE_MS);
  }

  /** Signal Forms calls this via `FieldState.focusBoundControl()`. */
  public focus(options?: FocusOptions): void {
    focusFirstControl(this.host.nativeElement, options);
  }

  private async search(raw: string): Promise<void> {
    const q = raw.trim();
    const seq = ++this.requestSeq;

    // 1 character is too short to search; fall back to the default list.
    const search = q.length >= MIN_QUERY_LENGTH ? q : undefined;

    this.loading.set(true);
    try {
      // Active only: a retired port still resolves for the loads that already reference it, but
      // offering one here would put new work through a terminal nobody calls at any more.
      const result = await this.api.invoke(getTerminals, { Search: search, IsActive: true });
      if (seq === this.requestSeq) this.terminals.set(result.items ?? []);
    } catch {
      if (seq === this.requestSeq) this.terminals.set([]);
    } finally {
      if (seq === this.requestSeq) this.loading.set(false);
    }
  }
}
