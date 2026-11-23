import { inject, Injector } from "@angular/core";
import { toObservable } from "@angular/core/rxjs-interop";
import { Router, type CanActivateFn, type Routes } from "@angular/router";
import { featureGuardFromData, Permission } from "@trackwise/shared";
import { FeatureService } from "@trackwise/shared/services";
import { filter, map, take } from "rxjs";
import { AuthService, PermissionService, settingsGuard } from "@/core/auth";
import { passesAccessGate } from "@/shared/layout/nav-menu";
import { systemNav } from "@/shared/layout/sidebar/nav/system.nav";

const settingsLandingGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const permissions = inject(PermissionService);
  const features = inject(FeatureService);
  const router = inject(Router);

  return toObservable(auth.authInitialized, { injector: inject(Injector) }).pipe(
    filter(Boolean),
    take(1),
    map(() => {
      if (permissions.hasPermission(Permission.Tenant.View)) {
        return true;
      }

      const firstTab = (
        systemNav.items.find((item) => item.id === "settings")?.children ?? []
      ).find((child) => passesAccessGate(child, features, permissions));

      return router.parseUrl(firstTab?.route ?? "/unauthorized");
    }),
  );
};

export const settingsRoutes: Routes = [
  {
    path: "",
    loadComponent: () =>
      import("./settings-layout/settings-layout").then((m) => m.SettingsLayoutComponent),
    // Per child, with the child's snapshot - so `data.permission` is enforced and a new tab
    // can't be added ungated.
    canActivateChild: [settingsGuard],
    children: [
      {
        path: "",
        redirectTo: "company",
        pathMatch: "full",
      },
      {
        path: "company",
        loadComponent: () =>
          import("./company-settings/company-settings").then((m) => m.CompanySettingsComponent),
        canActivate: [settingsLandingGuard],
        data: { breadcrumb: "Company", permission: Permission.Tenant.View },
      },
      {
        path: "billing",
        loadComponent: () =>
          import("./billing-settings/billing-settings").then((m) => m.BillingSettings),
        data: { breadcrumb: "Billing", permission: Permission.Payment.View },
      },
      {
        path: "integrations",
        loadComponent: () =>
          import("./integrations-settings/integrations-settings").then(
            (m) => m.IntegrationsSettings,
          ),
        canActivate: [featureGuardFromData],
        data: {
          breadcrumb: "Integrations",
          permission: [Permission.Accounting.View, Permission.ApiKey.View],
          feature: ["accounting", "mcp_server"],
        },
      },
      {
        path: "roles",
        loadComponent: () => import("./roles-settings/roles-settings").then((m) => m.RolesSettings),
        data: { breadcrumb: "Roles & permissions", permission: Permission.TenantRole.Manage },
      },
      {
        path: "features",
        loadComponent: () =>
          import("./feature-settings/feature-settings").then((m) => m.FeatureSettingsComponent),
        data: { breadcrumb: "Features", permission: Permission.Tenant.View },
      },
    ],
  },
];
