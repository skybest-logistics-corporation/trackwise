import type { Routes } from "@angular/router";
import { Permission } from "@trackwise/shared";
import { authGuard } from "@/core/auth";
import { CashAdvanceNewComponent } from "./cash-advances/new/cash-advance-new";
import { PettyCashNewComponent } from "./petty-cash/new/petty-cash-new";
import { RequestForPaymentNewComponent } from "./requests-for-payment/new/request-for-payment-new";

export const accountingRoutes: Routes = [
  {
    path: "",
    pathMatch: "full",
    redirectTo: "requests-for-payment/new",
  },
  {
    path: "requests-for-payment/new",
    component: RequestForPaymentNewComponent,
    canActivate: [authGuard],
    data: {
      breadcrumb: "New Request for Payment",
      permission: Permission.Booking.Manage,
    },
  },
  {
    path: "cash-advances/new",
    component: CashAdvanceNewComponent,
    canActivate: [authGuard],
    data: {
      breadcrumb: "New Cash Advance",
      permission: Permission.Booking.Manage,
    },
  },
  {
    path: "petty-cash/new",
    component: PettyCashNewComponent,
    canActivate: [authGuard],
    data: {
      breadcrumb: "New Petty Cash Voucher",
      permission: Permission.Booking.Manage,
    },
  },
];
