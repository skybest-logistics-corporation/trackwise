import { UserData } from "./user-data";
import { isTenantStaffRole, UserRole } from "./user-role";

describe("UserData role normalization", () => {
  const baseIdentity = {
    sub: "user-1",
    name: "Jane Doe",
    given_name: "Jane",
    family_name: "Doe",
  };

  it("handles a single role string", () => {
    const data = new UserData({ ...baseIdentity, role: UserRole.Owner });
    expect(data.role).toBe(UserRole.Owner);
    expect(data.roles).toEqual([UserRole.Owner]);
  });

  it("handles an array of roles from OIDC multi-role claims", () => {
    const data = new UserData({
      ...baseIdentity,
      role: ["LegacyAdmin", UserRole.Owner, UserRole.Dispatcher],
    });
    expect(data.roles).toEqual(["LegacyAdmin", UserRole.Owner, UserRole.Dispatcher]);
    expect(data.role).toBe(UserRole.Owner);
  });

  it("handles missing role", () => {
    const data = new UserData(baseIdentity);
    expect(data.role).toBeNull();
    expect(data.roles).toEqual([]);
  });

  it("isTenantStaffRole safely rejects non-strings and customer role", () => {
    expect(isTenantStaffRole(UserRole.Owner)).toBe(true);
    expect(isTenantStaffRole("tenant.custom_ops")).toBe(true);
    expect(isTenantStaffRole(UserRole.Customer)).toBe(false);
    expect(isTenantStaffRole(UserRole.AppSuperAdmin)).toBe(false);
    expect(isTenantStaffRole(null)).toBe(false);
    expect(isTenantStaffRole(["tenant.owner"] as unknown as string)).toBe(false);
  });
});
