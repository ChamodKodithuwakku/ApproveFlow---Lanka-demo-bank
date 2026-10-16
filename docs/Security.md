# ApproveFlow – Security Controls

This document lists each security control, where it is implemented, how it is verified, and which
[OWASP Top 10 (2021)](https://owasp.org/Top10/) risk it addresses.

## 1. Summary mapping

| OWASP risk | Main controls in ApproveFlow |
|---|---|
| **A01 Broken Access Control** | Named role policies on every controller/action plus a deny-by-default fallback; role checks repeated in services; four-eyes, ownership and dual control enforced in the domain; segregation of duties on role assignment; open-redirect protection; API accepts bearer tokens only |
| **A02 Cryptographic Failures** | HTTPS redirection + HSTS (1 year); `__Host-` Secure, HttpOnly, SameSite=Strict cookies; PBKDF2 password hashing (Identity); HMAC-SHA256 JWT with key ≥ 256 bits from secrets; only a hash of the security stamp in tokens |
| **A03 Injection** | EF Core LINQ (parameterised); reports via `CommandType.StoredProcedure` with typed parameters; whitelisted sort fields; Razor output encoding; strict CSP; AI output rendered with `textContent`; CSV export formula-injection guard |
| **A04 Insecure Design** | Maker-checker state machine in a rich domain model; approval limits and dual approval; immutable final states; optimistic concurrency; mandatory remarks; threat-driven tests |
| **A05 Security Misconfiguration** | Security headers middleware; no Server/X-Powered-By headers; Swagger off outside Development by default; developer exception page only in Development; demo seeding blocked in Production; `appsettings.Example.json` documents settings without secrets |
| **A06 Vulnerable and Outdated Components** | Central package management; NuGet audit; a vulnerable transitive dependency (`System.Security.Cryptography.Pkcs` 9.0.11) pinned to a patched version; current LTS runtime; CI on every push |
| **A07 Identification and Authentication Failures** | Strong password policy; lockout after 5 failures (15 min); rate-limited login and token endpoints; generic error messages (no account enumeration); 20-minute sliding sessions; security-stamp revalidation; JWT revocation on deactivation, lockout or stamp change |
| **A08 Software and Data Integrity Failures** | Anti-forgery tokens on all POSTs; append-only audit trail (interceptor guard + DB trigger + `DENY`); transactional audit; SHA-256 duplicate-file check on migration; libraries served locally (no third-party CDN scripts); reviewed CI pipeline |
| **A09 Security Logging and Monitoring Failures** | Field-level audit of every change (old/new values, user, IP, correlation id); security events for sign-in, failure, lockout, logout, token, access denied, password change, unlock, export, AI request; Serilog rolling files; Auditor UI and CSV export |
| **A10 Server-Side Request Forgery** | The only outbound call is to a fixed, configured LLM endpoint; no user-supplied URLs are fetched |

## 2. Controls in detail

### 2.1 Authentication (A07, A02)
- **ASP.NET Core Identity** with PBKDF2 hashing. Policy: at least 12 characters with upper-case, lower-case, digit,
  symbol and 4 unique characters (`AddIdentityPersistence`).
- **Lockout**: 5 failed attempts lock the account for 15 minutes, for new users too. It applies to UI sign-in
  (`PasswordSignInAsync(lockoutOnFailure: true)`) and to the API token endpoint (`CheckPasswordSignInAsync`).
  *Verified by* `SecurityTests.Five_failed_sign_ins_lock_the_account`.
- **No account enumeration**: the same "Invalid email or password" message whether or not the account exists.
- **Inactive accounts** cannot sign in or obtain tokens.
- **Session cookie**: `__Host-ApproveFlow`, `Secure`, `HttpOnly`, `SameSite=Strict`, 20-minute sliding expiry. The
  security stamp is revalidated every 5 minutes, so deactivation or a role change ends existing sessions.
- **JWT for the API**: HMAC-SHA256, 30-minute lifetime, issuer/audience/lifetime/signature/algorithm validated, 1-minute
  clock skew. On *every* request `OnTokenValidated` confirms the user still exists, is active, is not locked out and
  that the hashed security stamp in the token matches, so tokens are revoked immediately. *Verified by*
  `Token_is_revoked_when_the_user_is_deactivated`.
- The **signing key** must be supplied through secrets and be ≥ 32 bytes; startup fails outside Development if it is
  missing or too short.

### 2.2 Authorization (A01)
- **Policies** (`Policies.cs`): `AnyStaff`, `MakerOnly`, `CheckerOnly`, `AdminOnly`, `AuditorOnly`, `ReportViewer`,
  `RiskSummary`, `CustomerManager`. Every controller declares one; the **fallback policy** requires authentication for
  anything that forgets.
- **Defence in depth**: each application service calls `RequireRole(...)` again, and the domain enforces ownership
  (`not_owner`), four-eyes (`four_eyes_violation`) and dual control (`dual_approval_same_checker`). The UI only
  reflects permissions. *Verified by* `Maker_cannot_approve_own_application_even_when_also_holding_checker_role`,
  `Api_returns_403_when_a_maker_tries_to_approve`.
- **Segregation of duties in role assignment**: Maker + Checker and Auditor + anything are rejected. An admin cannot
  remove their own Admin role or deactivate themselves.
- **Open redirect**: the login `returnUrl` is honoured only if `Url.IsLocalUrl`.
- **API isolation**: API controllers authenticate *only* with the JWT bearer scheme, so a browser's cookie can never
  authorise an API call (no CSRF surface). The API returns 401/403 ProblemDetails, never a login redirect.

### 2.3 Input handling and injection (A03)
- **Data access**: EF Core LINQ only (parameterised). Stored procedures are called with `CommandType.StoredProcedure`
  and typed `DbParameter`s. The only raw SQL strings are constant (`NEXT VALUE FOR`, the ageing view query) and
  contain no user input. Sorting uses a whitelist (`LoanApplicationSortFields`); the API rejects other values with 400.
- **Validation**: FluentValidation on every request DTO (authoritative, 422), DataAnnotations for client hints, and
  domain invariants (`LoanTerms`, product limits, NIC format). The CSV import validates every field.
- **Output encoding**: Razor HTML-encodes by default. `Html.Raw` is used only for server-generated JSON in
  `<script type="application/json">` data blocks (System.Text.Json escapes `<`, `>`, `&`).
- **XSS hardening**: strict CSP (below). LLM output is inserted with `textContent`, never `innerHTML`.
- **CSV injection**: exported cells starting with `= + - @` (or tab/CR) are prefixed with `'`.
- **Uploads**: `.csv` only, 2 MB request limit (Kestrel and IIS), 5,000-row cap, parsed as data and never executed.

### 2.4 CSRF (A08)
- A global `AutoValidateAntiforgeryTokenAttribute` covers every unsafe MVC request. The antiforgery cookie is
  `__Host-` and SameSite=Strict. The AJAX AI-summary call sends the token in `X-CSRF-TOKEN`. *Verified by*
  `Login_post_without_antiforgery_token_is_rejected`.

### 2.5 Transport and headers (A02, A05)
`SecurityHeadersMiddleware` adds to every response:

| Header | Value |
|---|---|
| Content-Security-Policy | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'` (+ `upgrade-insecure-requests` outside Development) |
| X-Content-Type-Options | `nosniff` |
| X-Frame-Options | `DENY` |
| Referrer-Policy | `no-referrer` |
| Permissions-Policy | `camera=(), microphone=(), geolocation=(), payment=(), usb=()` |
| Cross-Origin-Opener-Policy / Resource-Policy | `same-origin` |
| Cache-Control | `no-store` on pages and API responses (customer data is never cached) |

There is no inline script or style in the application, which is why the CSP needs no `unsafe-inline`. Only Swagger
UI (Development tooling) gets a relaxed policy. HTTPS redirection is always on and HSTS (365 days,
includeSubDomains) is on outside Development. Kestrel's `Server` header is removed, and `web.config` removes IIS's
`Server` and `X-Powered-By`. *Verified by* `Responses_carry_security_headers`.

### 2.6 Rate limiting (A07, A04)
- `login` policy: 10 requests per 5 minutes per IP, on UI sign-in and `POST /api/v1/auth/token`.
- `api` policy: 120 requests per minute per user (or per IP when anonymous), on all API controllers and the AI summary.
- Rejections return **429** with `Retry-After` (ProblemDetails for API calls). Limits are configurable
  (`RateLimiting:*`).

### 2.7 Error handling (A05)
- The API always returns RFC 7807 ProblemDetails (`ApiExceptionFilter` for business errors,
  `ApiExceptionHandler` for unexpected ones) with a `traceId` and stable `code`. 500 responses never include the
  exception message or stack trace, in any environment.
- The UI shows a friendly error page with a reference id in Production; the developer exception page is
  Development-only. 404/403/429 have friendly pages.

### 2.8 Secrets management (A02, A05)
- Nothing sensitive is committed: no connection strings with passwords, keys or demo passwords in `appsettings*.json`.
- Development uses **user-secrets** (`scripts/setup-dev.ps1` generates a random JWT key and SA password). Hosting uses
  **environment variables** / App Service settings / Key Vault references (`ConnectionStrings__DefaultConnection`,
  `Jwt__SigningKey`, `AiRiskSummary__ApiKey`, `Bootstrap__AdminPassword`).
- `.env`, `appsettings.*.local.json`, `appsettings.Production.json`, SQLite files and logs are git-ignored.
  `appsettings.Example.json` documents every key with placeholders and is excluded from publish output.
- The AI API key is sent only in the `x-api-key` header; HttpClient logging is at Warning, and request/response
  bodies are never logged.
- The demo password is a development convenience only. Demo seeding refuses to run in Production. The first
  production admin is created through `Bootstrap:*` settings, which are removed after first sign-in.

### 2.9 Audit trail and monitoring (A09, A08)
- **Entity changes**: `AuditTrailInterceptor` records insert/update/delete/soft-delete with old/new JSON, changed
  columns, user id/name, IP, correlation id and UTC time, **in the same transaction** as the change. It covers
  domain tables, `AspNetUsers` (excluding hashes, stamps and counters) and role assignments.
- **Security events**: sign-in success/failure, lockout, inactive-account attempts, logout, token issued/denied, access
  denied, password change, unlock, audit export and AI requests.
- **Append-only**: no code path updates or deletes `AuditLog`; the interceptor throws if anything tries; the
  `trg_AuditLogs_AppendOnly` trigger blocks `UPDATE/DELETE` in SQL; `least-privilege-app-role.sql` denies
  `UPDATE/DELETE/ALTER` to the runtime login (which also prevents `TRUNCATE` and trigger tampering). *Verified by*
  `Audit_rows_are_append_only`, `Secrets_are_never_written_to_the_audit_trail`.
- **Correlation**: each audit row stores the request trace id, which also appears in Serilog request logs.

### 2.10 Data protection and privacy
- **Least privilege in the database**: a runtime role with data access only; migrations run under a separate
  deployment login.
- **Soft delete** keeps history; Approved/Rejected records are immutable.
- NICs are masked in list views.
- **AI privacy by construction**: `RiskSummaryInput` has no field for name, NIC, phone, address or free text. The
  exact payload sent is stored in the audit trail for verification. *Verified by*
  `Input_type_cannot_carry_personal_data`.

### 2.11 Supply chain (A06, A08)
- All package versions are in `Directory.Packages.props`. `CentralPackageTransitivePinningEnabled` is used to raise
  `System.Security.Cryptography.Pkcs` to a patched version (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7).
- Front-end libraries (Bootstrap 5.3.3, Bootstrap Icons 1.13.2, Chart.js 4.5.1, jQuery Validation) are vendored with
  their licences and served from the application origin.
- CI builds in Release with analyzers and warnings-as-errors and runs the full test suite on every push and PR.
  Deployment jobs are designed for protected GitHub environments with required reviewers (four-eyes on releases)
  and OIDC to Azure (no long-lived cloud credentials).

## 3. Residual risks and recommendations

| Risk | Recommendation for production |
|---|---|
| No multi-factor authentication | Enable Identity TOTP 2FA or federate to the bank's IdP (Entra ID / ADFS) with MFA and conditional access |
| Symmetric JWT key | Move to an asymmetric key in Azure Key Vault / HSM with rotation, or use the IdP's tokens |
| Rate limits are per instance | Use a distributed limiter or a WAF/API gateway when scaling out |
| Audit log in the same database | Stream audit rows to a SIEM / WORM storage; consider SQL Server Ledger tables for cryptographic tamper evidence |
| No automated DAST in CI | Add OWASP ZAP baseline scans and dependency review to the pipeline |
| Malware scanning of uploads | Scan uploaded files (e.g. Defender for Storage) before parsing if file types are extended beyond CSV |
