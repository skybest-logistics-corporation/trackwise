import type { TruckTonnageClass, TruckType } from "@trackwise/shared/api/generated";

export const truckTypeOptions: { label: string; value: TruckType }[] = [
  { label: "Flatbed", value: "flatbed" },
  { label: "Freight Truck", value: "freight_truck" },
  { label: "Reefer", value: "reefer" },
  { label: "Tanker", value: "tanker" },
  { label: "Box Truck", value: "box_truck" },
  { label: "Dump Truck", value: "dump_truck" },
  { label: "Tow Truck", value: "tow_truck" },
  { label: "Car Hauler", value: "car_hauler" },
  { label: "Container Truck", value: "container_truck" },
  { label: "Tautliner", value: "tautliner" },
  { label: "Low Loader", value: "low_loader" },
  { label: "Car Transporter", value: "car_transporter" },
  { label: "Swap Body", value: "swap_body" },
  { label: "Curtainsider", value: "curtainsider" },
  { label: "Wing Van", value: "wing_van" },
];

export const truckTonnageClassOptions: { label: string; value: TruckTonnageClass }[] = [
  { label: "2 Tonner", value: "two_tonner" },
  { label: "3 Tonner", value: "three_tonner" },
  { label: "4 Tonner", value: "four_tonner" },
  { label: "10 Tonner", value: "ten_tonner" },
];

export const truckTypeLabels = Object.fromEntries(
  truckTypeOptions.map((o) => [o.value, o.label]),
) as Record<TruckType, string>;

export const truckTonnageClassLabels = Object.fromEntries(
  truckTonnageClassOptions.map((o) => [o.value, o.label]),
) as Record<TruckTonnageClass, string>;
