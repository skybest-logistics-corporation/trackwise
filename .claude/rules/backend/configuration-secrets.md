---
paths:
  - "src/Presentation/**/appsettings*.json"
  - "src/Presentation/**/Setup.cs"
  - "src/Core/Trackwise.Domain/Options/**/*.cs"
  - "src/Infrastructure/**/*Options.cs"
  - "deploy/.env.example"
  - "deploy/docker-compose*.yml"
---

# Configuration and Secrets

Every setting resolves from these layers, last one wins per key: `appsettings.json` → user-secrets
(Development only) → environment variables. Code reads the merged result through `IOptions<T>` /
`IConfiguration` and never cares which layer a value came from.

| Layer                   | Holds                                                   | In git |
| ----------------------- | ------------------------------------------------------- | ------ |
| `appsettings.json`      | Every key, with safe defaults or `<placeholder>` values | Yes    |
| `dotnet user-secrets`   | All local dev overrides - real keys and dev tweaks      | No     |
| Env vars (`DOCKER_ENV`) | Prod values, `Section__Key` form                        | No     |

## Rules

- **Do not create `appsettings.Development.json`** (or `.Local.json`). Local overrides, secret or
  not, go in user-secrets. It is gitignored, so it was never shared anyway - it only gave keys a
  second place to hide inside the repo folder.
- **Never commit a real key** to `appsettings.json`. Use a `<What it is>` placeholder, or `""` when an
  empty value means "feature off".
- **Adding a setting** = placeholder in `appsettings.json` + matching `Section__Key=` line in
  `deploy/.env.example` + `dotnet user-secrets set "Section:Key" "..." --project <host>` locally.
  Prod gets it by adding the line to the `DOCKER_ENV` GitHub secret - ask before touching that.
- Hosts with a `UserSecretsId`: `Trackwise.API`, `Trackwise.IdentityServer`, `Trackwise.DbMigrator`.
  Each host has its own store - a key both hosts need (e.g. `Resend:ApiKey`) is set in both.
- Credential files (`firebase-adminsdk-key.json`, `google-services.json`, keystores) stay
  gitignored; never `git add -f` them.

## Traps

- **Nested keys use `:`** in user-secrets (`Llm:Providers:Gemini:ApiKey`), arrays use an index
  (`Nsfs:Tenants:0`), and env vars use `__`.
- **Bulk import pipes JSON on stdin**: `cat x.json | dotnet user-secrets set --project <host>`. Pipe
  from bash, not PowerShell - PowerShell prepends a BOM and the tool fails with `'0xEF' is an invalid
start of a value`.
- **A placeholder is a real value to the app.** `"<OpenAI API key>"` is a non-empty string, so a
  provider looks configured until its first call fails. Override with `""` to switch it off.
- **Placeholders that guard access must be overridden in prod**, e.g.
  `Impersonation:MasterPassword` - the committed value is public.
- Dev-only defaults committed in `appsettings.json` (`Test12345#` Postgres + SuperAdmin password)
  are fine locally; prod must override them via env vars.
