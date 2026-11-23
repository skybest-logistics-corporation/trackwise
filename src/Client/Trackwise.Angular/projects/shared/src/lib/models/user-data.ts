import type { UserIdentity } from "./user-identity";

export class UserData {
  public readonly id: string;
  public readonly name: string;
  public readonly firstName: string;
  public readonly lastName: string;
  public readonly role: string | null;
  public readonly roles: string[];
  public readonly tenant?: string;
  public readonly jobTitle: string | null;

  constructor(userIdentity: UserIdentity) {
    this.id = userIdentity.sub;
    this.name = userIdentity.name;
    this.firstName = userIdentity.given_name;
    this.lastName = userIdentity.family_name;
    this.tenant = userIdentity.tenant;
    this.roles = normalizeRoles(userIdentity.role);
    this.role =
      this.roles.find((r) => r.startsWith("tenant.") || r.startsWith("app.")) ??
      this.roles[0] ??
      null;
    this.jobTitle = userIdentity.job_title ?? null;
  }

  public getFullName(): string {
    return `${this.firstName} ${this.lastName}`;
  }
}

function normalizeRoles(role: string | string[] | undefined): string[] {
  if (Array.isArray(role)) {
    return role.filter((r): r is string => typeof r === "string" && r.length > 0);
  }
  return typeof role === "string" && role.length > 0 ? [role] : [];
}
