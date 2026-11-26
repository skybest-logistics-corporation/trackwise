import { type BookingChargeDto, type BookingDto } from "@trackwise/shared/api";

export interface DisbursementLineAllocation {
  chargeId: string;
  codeAndDescription: string;
  category: string;
  estimatedBuying: number;
  requestedAmount: number;
  currency: string;
}

export function toIsoDateString(date: Date | null): string | null {
  if (!date) return null;
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function extractSelectedAllocations(
  booking: BookingDto,
  chargeIdsParam: string | null,
): DisbursementLineAllocation[] {
  const requestedIds = new Set(
    (chargeIdsParam ?? "")
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean),
  );

  console.log(booking);

  const charges = booking.charges ?? [];
  const filtered =
    requestedIds.size > 0
      ? charges.filter((c) => c.id && requestedIds.has(c.id))
      : charges.filter((c) => Number(c.buyingAmount ?? 0) > 0);

  return filtered.map((c: BookingChargeDto) => {
    const est = Number(c.estimatedBuyingAmount ?? c.buyingAmount ?? 0);
    return {
      chargeId: c.id!,
      codeAndDescription: c.description ?? "Charge",
      category: String(c.category ?? "Accessorial"),
      estimatedBuying: est,
      requestedAmount: est,
      currency: c.currency ?? booking.currency ?? "PHP",
    };
  });
}
