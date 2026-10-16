# ApproveFlow – Requirements Specification

| | |
|---|---|
| Product | ApproveFlow – Maker-Checker Loan Approval System |
| Organisation | Lanka Demo Bank (fictional) |
| Version | 1.0 |
| Currency / time zone | LKR / Asia/Colombo (UTC+05:30) |

## 1. Business problem

Retail loan applications at Lanka Demo Bank are captured by branch officers and approved by credit officers using
spreadsheets and e-mail. This causes four recurring audit findings:

1. **No enforced segregation of duties.** The person who prepares an application can also approve it.
2. **Approval authority is not controlled.** Officers approve amounts above their delegated limits, with no second
   signature.
3. **No reliable audit trail.** It is not possible to show who changed which figure, when, from where and what the
   previous value was.
4. **Slow, invisible queues.** Management cannot see how long applications wait or how individual checkers perform.

The bank also has historical applications in a legacy system that must be migrated with a verifiable reconciliation.

**Objective:** a web application and API that enforce the maker-checker control in the system itself, keep a
tamper-evident audit trail, report on throughput and ageing, and support controlled data migration.

## 2. Stakeholders and user roles

| Role | Description | Key permissions |
|---|---|---|
| **Maker** | Branch officer who prepares applications | Create, edit and submit *own* Draft or Returned applications; withdraw own drafts; register customers |
| **Checker** | Credit officer with a delegated approval limit (LKR) | Approve within limit, give first/second approval above it, reject, return for correction; never on own items |
| **Admin** | System administrator | Manage users, roles, approval limits, branches, loan products; run data migration; no workflow decisions |
| **Auditor** | Internal audit / compliance | Read-only access to everything, including the audit trail, reports and AI summaries; CSV export |

Permission matrix (✓ = allowed, – = denied by policy *and* by the service layer):

| Capability | Maker | Checker | Admin | Auditor |
|---|---|---|---|---|
| View applications, history, customers | ✓ | ✓ | ✓ | ✓ |
| Create / edit / submit / withdraw application | ✓ (own) | – | – | – |
| Approve / reject / return | – | ✓ (not own) | – | – |
| Generate AI risk summary | – | ✓ | – | ✓ |
| Reports | – | ✓ | ✓ | ✓ |
| Audit trail and export | – | – | – | ✓ |
| Users, limits, branches, products | – | – | ✓ | – |
| Legacy data import | – | – | ✓ | – |

Role assignment itself follows segregation of duties: one user cannot be both Maker and Checker, and an Auditor holds
no other role.

## 3. Business rules

| ID | Rule |
|---|---|
| BR-01 | Workflow: `Draft → Submitted → (Approved \| Rejected \| ReturnedForCorrection)`; `ReturnedForCorrection → Submitted`. Above-limit approvals pass through `PendingSecondApproval`. |
| BR-02 | **Four-eyes**: the user who created, last edited or submitted an application can never approve, reject or return it. |
| BR-03 | A Checker approves alone only up to their approval limit. Above the limit the approval is a *first approval*; a different Checker must give the second approval before the status becomes Approved. |
| BR-04 | Reject and Return require remarks of at least 10 characters (maximum 1,000). |
| BR-05 | Approved and Rejected applications are immutable. |
| BR-06 | Only the owning Maker may edit or submit, and only in Draft or ReturnedForCorrection. Only Drafts may be withdrawn (soft delete). |
| BR-07 | Amount and tenure must be within the selected product's limits; tenure 1–360 months; interest rate 0–100%. |
| BR-08 | EMI = P·r·(1+r)ⁿ / ((1+r)ⁿ − 1). Debt-burden ratio = (existing obligations + EMI) ÷ monthly income × 100. Above 40% is moderate risk and above 60% is high risk (flagged, not blocked). |
| BR-09 | Reference numbers are unique, in the format `LDB-YYYY-NNNNNN` (YYYY = Colombo year). Legacy references keep their own format; the `LDB-` prefix is reserved. |
| BR-10 | Concurrent changes: a change based on a stale version is refused with "record was modified by another user". |
| BR-11 | Every state change is recorded (who, action, from/to status, remarks, timestamp). |
| BR-12 | Records are never physically deleted. |

## 4. Functional requirements

### 4.1 Authentication and administration
| ID | Requirement |
|---|---|
| FR-01 | Users sign in with e-mail and password; accounts lock for 15 minutes after 5 consecutive failures. |
| FR-02 | Users can change their password; the policy is at least 12 characters with upper, lower, digit and symbol. |
| FR-03 | Admins create users, assign roles (segregation of duties validated), set branch and approval limit, deactivate and unlock users. |
| FR-04 | Deactivation or a role change takes effect for existing sessions and API tokens without waiting for expiry. |
| FR-05 | Admins maintain branches and loan products (code, name, amount and tenure ranges, default rate, active flag). |

### 4.2 Loan applications
| ID | Requirement |
|---|---|
| FR-10 | Makers create applications for existing customers, choosing a product. The branch is the customer's home branch. |
| FR-11 | The form shows product limits and live EMI, total repayable, total interest and DBR. The server recalculates on save. |
| FR-12 | The list supports search (reference, customer name, NIC), filters (status, branch, product, date range, "only mine"), sorting and paging. |
| FR-13 | The details page shows terms, applicant, calculated figures, the approval timeline and only the actions the user may take. |
| FR-14 | Checkers have a queue of Submitted and PendingSecondApproval items, oldest first, showing age and above-limit markers. |
| FR-15 | Approve, reject and return use confirmation dialogs; reject and return demand remarks. |
| FR-16 | Makers can register new customers (name, NIC, branch, DOB, contact). NIC must be unique and valid. |

### 4.3 Audit
| ID | Requirement |
|---|---|
| FR-20 | Every insert, update and delete of audited data writes an audit record: table, key, action, old/new values, changed columns, user id/name, IP, correlation id, UTC time. |
| FR-21 | Sign-ins, failures, lockouts, logouts, token issuance/denial, access denials, password changes, unlocks, exports and AI requests are recorded. |
| FR-22 | Audit records cannot be changed or deleted through the application or by ad-hoc SQL. |
| FR-23 | Auditors filter by date range, user, entity, action, event type and record, view old/new values, and export to CSV. |

### 4.4 Reporting and dashboard
| ID | Requirement |
|---|---|
| FR-30 | Role-aware dashboard: own drafts, items returned to me, items awaiting decision, pending approvals, awaiting second approval, approved/rejected today, ageing chart, recent activity. |
| FR-31 | Approval summary for a period and optional branch: counts and amounts by status, branch and product; approval rate; average, minimum and maximum turnaround. |
| FR-32 | Checker performance: decisions by type, approved amount, average response time. |
| FR-33 | Reports are served by SQL Server stored procedures and are available in the UI and through the API. |

### 4.5 Data migration
| ID | Requirement |
|---|---|
| FR-40 | Admins upload a CSV (max 2 MB, 5,000 rows) of legacy applications; a template is downloadable. |
| FR-41 | Each row is validated: required fields, numeric and date formats, ranges, product limits, NIC, known active branch/product, duplicate reference in the file or the database, allowed status. |
| FR-42 | Valid rows are imported as one batch in a single transaction; invalid rows are stored with line number, reason and raw data. |
| FR-43 | A reconciliation report shows total, imported and rejected rows and amounts, and whether they balance. |
| FR-44 | The same file cannot be imported twice. |

### 4.6 AI risk summary
| ID | Requirement |
|---|---|
| FR-50 | Checkers and Auditors can generate an advisory risk summary on the details page. |
| FR-51 | Only non-identifying figures are sent to the LLM (no name, NIC, phone, address or free text). |
| FR-52 | The result is clearly labelled "AI-generated – advisory only, final decision rests with the Checker". |
| FR-53 | Without an LLM configuration, or on failure, a deterministic rule-based summary is shown. |
| FR-54 | Every request is recorded in the audit trail. |

### 4.7 REST API
| ID | Requirement |
|---|---|
| FR-60 | Versioned JSON API under `/api/v1` with JWT bearer authentication and the same role rules as the UI. |
| FR-61 | Endpoints: token; list/get/create/update applications; submit/approve/reject/return; history; reports summary. |
| FR-62 | Errors are RFC 7807 problem details with appropriate status codes (400/401/403/404/409/422/429). |
| FR-63 | OpenAPI documentation with an Authorize button. |

## 5. Non-functional requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-01 | Security | OWASP Top 10 controls (see `Security.md`): least privilege, CSRF protection, output encoding, parameterised data access, security headers, HTTPS/HSTS, rate limiting, no secrets in source. |
| NFR-02 | Integrity | Data change and its audit record are committed atomically. Optimistic concurrency on applications. Database constraints mirror business rules. |
| NFR-03 | Auditability | Audit trail retained in the database; application logs retained 30 days (rolling files) with correlation ids linking logs to audit rows. |
| NFR-04 | Performance | Design target: list and queue pages respond in under 1 second for 100k applications, through indexed access paths, server-side paging and a page size of at most 100. Not yet load-tested in v1. |
| NFR-05 | Availability | Health endpoint `/health` (database check) for host monitoring. |
| NFR-06 | Usability | Responsive Bootstrap UI usable on tablet and desktop; consistent status badges; confirmation for irreversible actions; accessible labels and skip link. |
| NFR-07 | Portability | Runs on IIS (Windows) and Azure App Service; SQL Server 2019+ or Azure SQL; target framework switchable between .NET 10 and .NET 8 in one place. |
| NFR-08 | Maintainability | Clean Architecture layering, automated tests in CI, central package management, analyzers with warnings-as-errors in Release. |
| NFR-09 | Privacy | Customer data is visible only to authenticated staff; identifiers are masked in lists; no personal data is sent to third-party AI services. |
| NFR-10 | Localisation | LKR amounts formatted with thousands separators; dates shown in Asia/Colombo time and stored in UTC. |

## 6. Assumptions and constraints

- Customers are already on-boarded (KYC is outside scope). Makers can register basic customer records.
- E-mail notifications, document uploads and core-banking disbursement integration are out of scope for v1.
- Demo data and accounts are created only in the Development environment.
- The AI summary is advisory and is never an input to automated decisions.
