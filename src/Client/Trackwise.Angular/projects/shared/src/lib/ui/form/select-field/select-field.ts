import { NgTemplateOutlet } from "@angular/common";
import {
  booleanAttribute,
  Component,
  computed,
  contentChild,
  effect,
  ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  TemplateRef,
} from "@angular/core";
import type { FormValueControl, ValidationError } from "@angular/forms/signals";
import { Icon } from "../../icons/icon/icon";
import { HlmSelectImports } from "../../primitives/select";
import { DetachedControl } from "../detached-control";
import { focusFirstControl } from "../focus-control";

/** Context handed to the `#item` / `#selectedItem` templates: the raw option object. */
export interface UiSelectOptionContext<T = unknown> {
  $implicit: T;
}

/** One section of the panel. A null label renders as a separator-only section. */
export interface UiSelectGroup {
  label: string | null;
  options: readonly unknown[];
}

/**
 * Single-select dropdown.
 *
 * Implements `FormValueControl` only, never a legacy `ControlValueAccessor` - see `text-field.ts`
 * for the bridge contract. The inner `hlm-select` is driven with plain `[value]` / `(valueChange)`,
 * never `[formField]`; `uiDetachedControl` severs the ambient `NgControl` so brain's
 * `BrnFieldControl` does not track our Signal Forms control, and invalid styling is owned by
 * `[forceInvalid]="showInvalid()"` on the trigger. The panel portals via `*hlmSelectPortal` -
 * without it the overlay never opens.
 *
 * @example
 * <ui-form-field label="Color" for="color" [required]="true">
 *   <ui-select-field id="color" [formField]="form.color"
 *     [options]="colors" optionLabel="label" optionValue="value" />
 * </ui-form-field>
 */
@Component({
  selector: "ui-select-field",
  templateUrl: "./select-field.html",
  // `id` is a declared input, but a static `id="x"` attribute also lands on the host element.
  // Strip it so the id lives only on the inner control and `<label for>` targets something focusable.
  host: { "[attr.id]": "null" },
  imports: [HlmSelectImports, DetachedControl, Icon, NgTemplateOutlet],
})
export class UiSelectField<T = unknown> implements FormValueControl<T | null> {
  /** Label for a "create new" action at the bottom of the panel. Leave undefined to hide it. */
  public readonly createLabel = input<string | undefined>(undefined);

  /** Raised when the user clicks the "create new" action. Carries the current search text. */
  public readonly createNew = output<string>();

  public readonly filterChange = output<string>();

  private readonly optionCache = new Map<unknown, unknown>();

  constructor() {
    effect(() => this.filterChange.emit(this.filterText()));

    // Remember every option we've seen so the trigger can still render a selection
    // that the latest server response no longer contains.
    effect(() => {
      for (const opt of this.options()) this.optionCache.set(this.resolveValue(opt), opt);
    });
  }

  private findOption(value: unknown): unknown | undefined {
    return (
      this.options().find((opt) => this.resolveValue(opt) === value) ?? this.optionCache.get(value)
    );
  }

  protected onCreateNew(): void {
    this.createNew.emit(this.filterText().trim());
  }
  /**
   * Optional per-option renderer, projected as `<ng-template #item let-option>`; `$implicit` is the
   * raw option object, not the resolved `optionValue`. `descendants: false` is load-bearing: without
   * it, a nested `ui-select-field` in our projected content loses its `#item` to the outer select.
   */
  protected readonly itemTemplate = contentChild<TemplateRef<UiSelectOptionContext>>("item", {
    descendants: false,
  });

  /**
   * Optional renderer for the trigger (the chosen option), projected as
   * `<ng-template #selectedItem let-option>`. Distinct from `#item`, so the closed trigger can render
   * differently from the open panel's rows.
   */
  protected readonly selectedItemTemplate = contentChild<TemplateRef<UiSelectOptionContext>>(
    "selectedItem",
    { descendants: false },
  );
  /** The control's value. Required by `FormValueControl`. */
  public readonly value = model<T | null>(null);

  public readonly disabled = input(false, { transform: booleanAttribute });
  public readonly optionDisabled = input<string | undefined>(undefined);
  public readonly readonly = input(false, { transform: booleanAttribute });
  public readonly required = input(false, { transform: booleanAttribute });
  public readonly invalid = input(false, { transform: booleanAttribute });
  public readonly touched = input(false, { transform: booleanAttribute });
  public readonly dirty = input(false, { transform: booleanAttribute });
  public readonly errors = input<readonly ValidationError[]>([]);
  public readonly name = input<string>("");

  protected isOptionDisabled(option: unknown): boolean {
    const path = this.optionDisabled();
    return !!path && !!(option as Record<string, unknown>)[path];
  }

  /** Raised when the field is blurred (the panel closes) so the form can mark it touched. */
  public readonly touch = output<void>();

  /** Raised after the user picks an option. */
  public readonly selectionChange = output<T | null>();

  /** Raised when the user clicks the clear (x) button. */
  public readonly cleared = output<void>();

  // Presentation
  public readonly options = input.required<readonly unknown[]>();
  /** Field path for an option's label. Undefined means the option itself is the label. */
  public readonly optionLabel = input<string | undefined>(undefined);
  /** Field path for an option's value. Undefined means the option itself is the value. */
  public readonly optionValue = input<string | undefined>(undefined);
  public readonly placeholder = input<string | undefined>(undefined);
  public readonly id = input<string>("");
  public readonly showClear = input(false, { transform: booleanAttribute });
  public readonly filter = input(false, { transform: booleanAttribute });
  public readonly fluid = input(true, { transform: booleanAttribute });
  public readonly styleClass = input<string | undefined>(undefined);
  public readonly filterPlaceholder = input<string | undefined>(undefined);
  public readonly filterBy = input<string | undefined>(undefined);
  public readonly loading = input(false, { transform: booleanAttribute });

  /** When true, `filterChange` drives the search and options are shown as given (no local filtering). */
  public readonly serverSide = input(false, { transform: booleanAttribute });

  /** Free-text filter over the visible options, active only when `filter` is set. */
  protected readonly filterText = signal("");

  protected readonly displayOptions = computed(() => {
    const opts = this.options();
    if (!this.filter() || this.serverSide()) return opts;
    const query = this.filterText().trim().toLowerCase();
    if (!query) return opts;
    return opts.filter((opt) => this.resolveLabel(opt).toLowerCase().includes(query));
  });

  /** Visible options split into groups, in order of first appearance. */
  protected readonly groups = computed<UiSelectGroup[]>(() => {
    const opts = this.displayOptions();
    const path = this.optionGroup();
    if (!path) return [{ label: null, options: opts }];

    const byLabel = new Map<string | null, unknown[]>();
    for (const opt of opts) {
      const raw = (opt as Record<string, unknown>)[path];
      const key = raw == null || raw === "" ? null : String(raw);
      const bucket = byLabel.get(key);
      if (bucket) bucket.push(opt);
      else byLabel.set(key, [opt]);
    }
    return [...byLabel].map(([label, options]) => ({ label, options }));
  });

  /** Field path for an option's group label. Undefined means a flat, ungrouped list. */
  public readonly optionGroup = input<string | undefined>(undefined);

  protected readonly showInvalid = computed(
    () => this.invalid() && (this.touched() || this.dirty()),
  );

  /**
   * True when something is selected. Guards `undefined` as well as `null`: the declared type is
   * `T | null`, but a consumer can still hand us `undefined`.
   */
  protected readonly hasValue = computed(() => {
    const current = this.value();
    return current !== null && current !== undefined;
  });

  /** The full option object behind the current value - the context for `#selectedItem`. */
  protected readonly selectedOption = computed(() =>
    this.hasValue() ? this.findOption(this.value()) : undefined,
  );

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected resolveValue(option: unknown): unknown {
    const path = this.optionValue();
    return path ? (option as Record<string, unknown>)[path] : option;
  }

  protected resolveLabel(option: unknown): string {
    const path = this.optionLabel();
    const label = path ? (option as Record<string, unknown>)[path] : option;
    return label == null ? "" : String(label);
  }

  /** Maps a stored value back to its label so `hlm-select-value` renders the selection. */
  protected readonly itemToString = (value: unknown): string => {
    const option = this.findOption(value);
    if (option !== undefined) return this.resolveLabel(option);
    return value == null ? "" : String(value);
  };

  protected onValueChange(next: T | null | undefined): void {
    const value = next ?? null;

    if (value !== null) {
      const option = this.findOption(value);
      if (option !== undefined && this.isOptionDisabled(option)) {
        return;
      }
    }

    this.value.set(value);
    this.selectionChange.emit(value);
  }

  protected onClosed(): void {
    this.filterText.set("");
    this.touch.emit();
  }

  /** Fired by both click and Enter/Space, so suppress the key's default (page scroll on Space). */
  protected clear(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.value.set(null);
    this.cleared.emit();
  }

  /** Signal Forms calls this via `FieldState.focusBoundControl()`. */
  public focus(options?: FocusOptions): void {
    focusFirstControl(this.host.nativeElement, options);
  }
}
