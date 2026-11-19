import { Pipe, type PipeTransform } from "@angular/core";

@Pipe({ name: "enumLabel" })
export class EnumLabelPipe implements PipeTransform {
  transform(value: string | null | undefined, labels?: Record<string, string>): string {
    if (!value) {
      return "-";
    }

    // Prefer the explicit label, fall back to "some_value" -> "Some Value"
    return (
      labels?.[value] ??
      value
        .split("_")
        .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
        .join(" ")
    );
  }
}
