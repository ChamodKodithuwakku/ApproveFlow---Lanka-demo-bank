# ApproveFlow – Deployment Guide

Two supported targets:

- **A. Windows ASP.NET + MS SQL shared host** (e.g. MonsterASP.NET, SmarterASP.NET)
- **B. Azure App Service + Azure SQL Database**

Both run the same build output: a framework-dependent ASP.NET Core app hosted in IIS / App Service, with SQL Server
as the database.

---

## 0. Before you start (both targets)

1. **Runtime**: the host must offer **.NET 10** (ASP.NET Core Hosting Bundle 10.x on IIS). If it only offers
   .NET 8, build with `-p:ApproveFlowTargetFramework=net8.0`. Directory.Build.props and Directory.Packages.props
   switch the framework and the Microsoft.* package versions together.
2. **Build the package** (or take the `approveflow-web` artifact from a green CI run):
   ```bash
   dotnet publish src/ApproveFlow.Web/ApproveFlow.Web.csproj -c Release -o ./publish
   ```
3. **Database script** (or the `approveflow-sql` CI artifact):
   ```bash
   dotnet tool restore
   dotnet tool run dotnet-ef migrations script --idempotent --project src/ApproveFlow.Infrastructure --startup-project src/ApproveFlow.Infrastructure -o ./publish-sql/ApproveFlow.sql
   ```
   The script is **idempotent**: it checks `__EFMigrationsHistory` and applies only missing migrations, so it is safe
   to run on every release. It creates tables, keys, indexes, check constraints, the reference-number sequence, the
   reporting procedures, the ageing view and the append-only audit trigger.
4. **Generate secrets** (keep them in a password manager, never in git):
   - `Jwt__SigningKey`: a random string of at least 32 characters, e.g.
     `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))` in PowerShell.
   - Database login and password for the application (runtime), ideally separate from the deployment login.
   - Bootstrap admin e-mail and a temporary password (first deployment only).

### Configuration reference (production)

| Setting (environment variable form) | Required | Example / note |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` |
| `ConnectionStrings__DefaultConnection` | yes | `Server=tcp:<server>,1433;Database=ApproveFlow;User Id=<app_login>;Password=<secret>;Encrypt=True;TrustServerCertificate=False` |
| `Jwt__SigningKey` | yes | ≥ 32 random characters. Startup fails if it is missing or short |
| `Jwt__Issuer`, `Jwt__Audience` | no | Defaults in appsettings.json |
| `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` | first run | Creates the first Admin if none exists. **Remove after first sign-in** |
| `AiRiskSummary__ApiKey` | no | Without it the rule-based assessment is used |
| `Swagger__Enabled` | no | `false` (default outside Development) |
| `Database__ApplyMigrationsOnStartup` | no | Keep `false`: migrations are applied by the release process |
| `Serilog__WriteTo__1__Args__path` | recommended | Writable log path, e.g. `D:\home\LogFiles\approveflow-.log` (App Service) |

Never set `Seed__Enabled=true` in Production. Demo data is refused there anyway.

---

## A. Windows shared host (MonsterASP.NET / SmarterASP.NET)

Control-panel names differ slightly between providers; the steps are the same.

### A1. Create the site and database
1. In the control panel create a **website** (for example `approveflow.<your-domain>` or the free subdomain) and
   select **ASP.NET Core / .NET 10** (or "No managed code" with the .NET 10 hosting bundle installed by the host).
2. Enable **HTTPS** (free Let's Encrypt certificate) and **force HTTPS** if the panel offers it.
3. Create an **MS SQL database**. Note the server name, database name, login and password shown by the panel.
   If the panel allows a second login, create one for deployment (DDL) and use a limited one for the app.

### A2. Create the schema
Choose one:
- **Panel SQL tool / SSMS**: connect to the database (most hosts allow remote SSMS on port 1433), open
  `ApproveFlow.sql` from step 0.3 and execute it.
- **From your PC with the EF tool**:
  ```bash
  dotnet tool run dotnet-ef database update --project src/ApproveFlow.Infrastructure --startup-project src/ApproveFlow.Infrastructure --connection "Server=<host-sql-server>;Database=<db>;User Id=<deploy_login>;Password=<secret>;TrustServerCertificate=True"
  ```
- **Migration bundle** (a single exe, no SDK needed on the machine that runs it):
  ```bash
  dotnet tool run dotnet-ef migrations bundle --project src/ApproveFlow.Infrastructure --startup-project src/ApproveFlow.Infrastructure -r win-x64 -o efbundle.exe
  ./efbundle.exe --connection "<deployment connection string>"
  ```
Then optionally run `database/seed/reference-data.sql` (branches and products) and, if the host lets you manage
roles, `database/security/least-privilege-app-role.sql`.

### A3. Configure secrets
Shared hosts usually do not expose environment variables in the panel. Set them in the **published `web.config`**
on the server (keep this edited copy out of git):

```xml
<aspNetCore processPath="dotnet" arguments=".\ApproveFlow.Web.dll" hostingModel="inprocess" stdoutLogEnabled="false">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="ConnectionStrings__DefaultConnection" value="Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True" />
    <environmentVariable name="Jwt__SigningKey" value="<random 48+ chars>" />
    <environmentVariable name="Bootstrap__AdminEmail" value="admin@yourbank.example" />
    <environmentVariable name="Bootstrap__AdminPassword" value="<temporary strong password>" />
  </environmentVariables>
</aspNetCore>
```

Alternatively, upload an `appsettings.Production.json` containing only those keys. It is git-ignored and must never
be committed. Protect the file with the host's file permissions.

### A4. Publish
- **Web Deploy (recommended)**: download the *publish profile* from the panel, then
  ```bash
  dotnet publish src/ApproveFlow.Web/ApproveFlow.Web.csproj -c Release /p:PublishProfile=<downloaded>.pubxml /p:Password=<webdeploy-password>
  ```
  or use Visual Studio → Publish → Import profile. Web Deploy takes the site offline during the copy (`app_offline.htm`).
- **FTP**: upload the contents of `./publish` to the site root (`wwwroot`). Upload `app_offline.htm` first and delete
  it afterwards so locked DLLs can be replaced.

The `.github/workflows/ci.yml` file contains ready-to-enable `deploy-shared-host` (Web Deploy + sqlcmd migration) and
`deploy-ftp` jobs. Add the listed secrets under *Settings → Secrets and variables → Actions*.

### A5. First run
1. Browse to `https://<site>/health` → `Healthy`.
2. Sign in with the bootstrap admin, **change the password**, create the real users, branches and products.
3. Remove the `Bootstrap__*` variables from `web.config` and recycle the app.
4. Confirm HTTPS-only and security headers (for example with <https://securityheaders.com>).

Logs: the default path is `logs\` under the site root; make sure the app pool identity can write there, or point
`Serilog__WriteTo__1__Args__path` at the host's log folder. For start-up failures, temporarily set
`stdoutLogEnabled="true"` in `web.config`.

---

## B. Azure App Service + Azure SQL

### B1. Create resources (Azure CLI)
```bash
az group create -n rg-approveflow -l southeastasia
az sql server create -g rg-approveflow -n sql-approveflow-<unique> -l southeastasia --admin-user sqladmin --admin-password "<strong>"
az sql db create -g rg-approveflow -s sql-approveflow-<unique> -n ApproveFlow --service-objective S0 --backup-storage-redundancy Local
az sql server firewall-rule create -g rg-approveflow -s sql-approveflow-<unique> -n AllowAzure --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
az appservice plan create -g rg-approveflow -n plan-approveflow --sku B1 --is-linux
az webapp create -g rg-approveflow -p plan-approveflow -n approveflow-<unique> --runtime "DOTNETCORE:10.0"
az webapp update -g rg-approveflow -n approveflow-<unique> --https-only true
az webapp config set -g rg-approveflow -n approveflow-<unique> --min-tls-version 1.2 --ftps-state Disabled --always-on true
```
(Windows plans work too: omit `--is-linux` and use `--runtime "dotnet:10"`.)

### B2. Configure settings
```bash
az webapp config connection-string set -g rg-approveflow -n approveflow-<unique> --connection-string-type SQLAzure \
  --settings DefaultConnection="Server=tcp:sql-approveflow-<unique>.database.windows.net,1433;Database=ApproveFlow;User Id=<app_login>;Password=<secret>;Encrypt=True"
az webapp config appsettings set -g rg-approveflow -n approveflow-<unique> --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Jwt__SigningKey="<random 48+ chars>" \
  Bootstrap__AdminEmail="admin@yourbank.example" Bootstrap__AdminPassword="<temporary>" \
  ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  Serilog__WriteTo__1__Args__path="/home/LogFiles/approveflow-.log"
```
- Connection strings of type *SQLAzure* are exposed to .NET as `ConnectionStrings:DefaultConnection` automatically.
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (Linux) makes ASP.NET Core trust App Service's front end for scheme and
  client IP, so audit rows record the real client IP and HTTPS is detected correctly.
- **Better**: store secrets in **Azure Key Vault** and reference them with
  `@Microsoft.KeyVault(SecretUri=...)`, granting the web app's managed identity *Key Vault Secrets User*.
- **Passwordless SQL** (recommended): enable a system-assigned managed identity, create a contained database user
  for it (`CREATE USER [approveflow-<unique>] FROM EXTERNAL PROVIDER; ALTER ROLE approveflow_app ADD MEMBER [...]`)
  and use `Authentication=Active Directory Default` in the connection string. No password exists to leak.

### B3. Apply migrations
Run the idempotent script with the deployment login (Azure Data Studio, SSMS, `sqlcmd`, or the commented
`azure/sql-action` step in CI):
```bash
sqlcmd -S tcp:sql-approveflow-<unique>.database.windows.net,1433 -d ApproveFlow -U sqladmin -P "<strong>" -C -b -i ./publish-sql/ApproveFlow.sql
```
or `efbundle --connection "<deployment connection string>"` / `dotnet ef database update --connection "..."`.
Then run `database/security/least-privilege-app-role.sql` and create the runtime user in that role.

### B4. Deploy the package
```bash
cd publish && zip -r ../approveflow.zip . && cd ..
az webapp deploy -g rg-approveflow -n approveflow-<unique> --src-path approveflow.zip --type zip
```
Or enable the `deploy-azure` job in `.github/workflows/ci.yml`. It uses OIDC federation (`azure/login`), applies the
SQL script, then deploys with `azure/webapps-deploy`. Protect the `production` environment with required reviewers.

### B5. First run and hardening
1. `https://approveflow-<unique>.azurewebsites.net/health` → `Healthy`.
2. Sign in as the bootstrap admin, change the password, create users, then delete the `Bootstrap__*` settings.
3. Recommended: Azure SQL *auditing* and *Microsoft Defender for SQL*, App Service *Access restrictions* (bank
   network / Front Door only), Application Insights, automated backups with point-in-time restore (default),
   deployment slots (`staging` → swap) for zero-downtime releases.

---

## Running migrations in production: summary

| Method | When to use | Command |
|---|---|---|
| Idempotent SQL script | DBA-controlled change windows; review before execution; CI artifact | `dotnet ef migrations script --idempotent` → run with `sqlcmd`/SSMS |
| Migration bundle | Automated pipelines; no SDK on the target | `dotnet ef migrations bundle` → `efbundle --connection "..."` |
| `dotnet ef database update --connection` | One-off, from an admin workstation | see A2 |
| On application start | **Development only** (`Database:ApplyMigrationsOnStartup=true`) | not used in Production |

Always back up the database before applying migrations, and use a deployment login with DDL rights that is
separate from the application's runtime login.

## Rollback

- **Application**: redeploy the previous artifact (or swap the App Service slot back).
- **Database**: migrations are additive. To revert the reporting objects run
  `dotnet ef database update InitialCreate --connection "..."` (their `Down` drops the procedures, view and trigger).
  Restore from backup for anything else.
