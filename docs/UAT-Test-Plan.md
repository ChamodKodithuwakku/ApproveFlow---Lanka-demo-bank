# ApproveFlow – User Acceptance Test Plan

## 1. Purpose and scope

This plan lets business users (branch makers, credit checkers, administrators and internal audit) confirm that
ApproveFlow meets the requirements in [`Requirements.md`](Requirements.md) before go-live. It covers the maker-checker
workflow, segregation of duties, approval limits, concurrency, audit trail, reporting, data migration, the AI risk
summary, the REST API and key security behaviour.

## 2. Environment and data

| Item | Value |
|---|---|
| Environment | UAT (`ASPNETCORE_ENVIRONMENT=Development` for seeded data), SQL Server 2019+ |
| URL | `https://<uat-host>/` (locally <http://localhost:5001> or <http://localhost:5080> in SQLite demo mode) |
| Data | Deterministic demo seed: 3 branches, 4 products, 30 customers, 60 applications in every status |
| Accounts | `admin@`, `maker1@`, `maker2@`, `checker1@` (limit LKR 2,000,000), `checker2@` (limit LKR 10,000,000), `auditor@demo.lk`. Password = `Seed:DemoPassword` |
| Files | `docs/sample-legacy-loans.csv` (50 rows, 11 deliberately invalid) |
| Reset | Drop/recreate the UAT database (or delete `approveflow-demo.db`) to restore the seed |

## 3. Entry and exit criteria

- **Entry**: build deployed from a green CI run; database migrated; seed loaded; testers have accounts.
- **Exit**: all *High* priority cases pass; no open Severity-1/2 defects; any Medium failures have an agreed workaround
  and fix date; business owner and internal audit sign off (section 6).

## 4. Test cases

Priority: H = high, M = medium. Record **Pass / Fail / Blocked**, the defect id and the tester's initials in the last
columns when executing.

| ID | Scenario | Role | Steps | Expected result | Pri | Result |
|---|---|---|---|---|---|---|
| UAT-01 | Successful sign-in | Maker | Open the URL, sign in as `maker1@demo.lk` | Dashboard shows "My drafts", "Returned to me for correction" and "awaiting decision" cards; name and role in the header | H | |
| UAT-02 | Lockout after 5 failed attempts | Any | Enter a wrong password for `maker2@demo.lk` 5 times, then the correct one | Message that the account is temporarily locked (15 min); correct password still refused while locked; Auditor sees `LoginFailed` and `AccountLockedOut` events | H | |
| UAT-03 | Admin unlocks an account | Admin | After UAT-02, *Users & limits* → Unlock on the locked user → confirm | Status returns to Active; user can sign in; `AccountUnlocked` security event recorded | M | |
| UAT-04 | Create a draft with live calculation | Maker | *New application* → customer, Vehicle Loan, amount 3,500,000, tenure 60, keep the default rate, income 250,000, obligations 30,000 → Save draft | Product limits and rate shown on selection; EMI ≈ LKR 79,635.76 and DBR ≈ 43.85% (moderate) shown live; reference `LDB-YYYY-NNNNNN` created; status Draft | H | |
| UAT-05 | Product limits enforced | Maker | Create a Personal Loan for LKR 9,000,000 | Save refused with the message next to *Amount*: amounts must be within LKR 50,000 – 5,000,000 | H | |
| UAT-06 | Input validation | Maker | Leave purpose empty; enter tenure 500; enter a negative income → Save | Field-level errors; nothing saved | M | |
| UAT-07 | Edit own draft | Maker | Open the draft from UAT-04 → Edit → change amount → Save | Changes saved; EMI/DBR recalculated; audit shows an *Update* with old and new amount | H | |
| UAT-08 | Maker cannot edit another maker's draft | Maker | As `maker2`, open a Draft created by `maker1` | No Edit/Submit buttons; direct URL `/LoanApplications/Edit/{id}` shows "cannot be edited" | H | |
| UAT-09 | Submit for approval | Maker | Draft from UAT-04 → *Submit for approval* → confirm in the dialog | Status Submitted; timeline shows "Submitted for approval"; Edit no longer available | H | |
| UAT-10 | Approval queue | Checker | Sign in as `checker1` → *Approval queue* | Pending items oldest first with age badges (green 0-2, amber 3-5, red 6+ days); items above LKR 2,000,000 marked as dual approval | M | |
| UAT-11 | Approve within limit | Checker | `checker2` approves a Submitted item ≤ LKR 10,000,000 → confirm | Status Approved; decision by and time recorded; timeline entry "Approved" | H | |
| UAT-12 | Dual approval above limit | Checker | `checker1` approves the LKR 3,500,000 item from UAT-09 | Dialog warns it is above the limit; after confirming, status is *Pending 2nd approval* and the first approver is shown | H | |
| UAT-13 | Same checker cannot give second approval | Checker | `checker1` reopens the item from UAT-12 | No decision buttons; notice "You gave the first approval; a different checker must complete…" | H | |
| UAT-14 | Second approval completes | Checker | `checker2` approves the item from UAT-12 | Status Approved; timeline shows "First approval (dual control)" then "Approved" by different users | H | |
| UAT-15 | Four-eyes: maker never decides | Admin + Maker | (Control test) Admin attempts to give `maker1` the Checker role as well | Save refused: "a user cannot be both Maker and Checker". Service-level four-eyes is also covered by automated tests | H | |
| UAT-16 | Reject requires remarks | Checker | On a Submitted item → Reject → leave remarks empty or type "too short" | Dialog requires at least 10 characters; status unchanged | H | |
| UAT-17 | Reject with remarks | Checker | Reject with "Debt-burden ratio exceeds the policy threshold." | Status Rejected; remarks visible in the timeline | H | |
| UAT-18 | Return for correction and resubmit | Checker → Maker | Checker returns an item with remarks → maker opens it, edits, resubmits | Status ReturnedForCorrection, then Submitted; the remarks are visible to the maker; history shows *Returned* then *Resubmitted* | H | |
| UAT-19 | Approved / rejected records are immutable | Maker | Open an Approved application; try `/LoanApplications/Edit/{id}` | No edit actions; direct URL refused | H | |
| UAT-20 | Concurrent decision | 2 Checkers | `checker1` and `checker2` open the same Submitted item in two browsers; checker2 returns it; checker1 then clicks Approve | checker1 gets "This record was modified by another user…"; the item stays Returned | H | |
| UAT-21 | Withdraw a draft | Maker | Draft → Withdraw → confirm | Draft disappears from lists; audit shows a *SoftDelete* | M | |
| UAT-22 | Audit trail search and detail | Auditor | *Audit trail* → filter Entity = LoanApplications, Action = Update, today's date → Details | Rows show user, IP, changed columns and old/new JSON; no passwords or security stamps anywhere | H | |
| UAT-23 | Audit export | Auditor | Apply a filter → *Export CSV* → open in Excel | CSV with the filtered rows and Colombo and UTC times; an `AuditLogExported` event recorded | M | |
| UAT-24 | Auditor is read-only | Auditor | Browse applications, reports and customers; try `/LoanApplications/Create` and `/Admin/Users` | No create/approve/edit actions; restricted pages show *Access denied*; `AccessDenied` event recorded | H | |
| UAT-25 | Reports | Checker / Auditor | *Reports* → last 90 days → All branches, then Galle only | KPIs (applications, approval rate, turnaround), charts and tables by status, branch, product; checker performance. Values change with the branch filter | H | |
| UAT-26 | Dashboard ageing | Checker | Dashboard | Ageing chart buckets 0-2 / 3-5 / 6+ days match the queue ages; approved/rejected today counts update after UAT-11/17 | M | |
| UAT-27 | Legacy data migration | Admin | *Data migration* → upload `sample-legacy-loans.csv` | Reconciliation: 50 rows, 39 imported, 11 rejected; amounts balance; rejected lines 6, 11, 15, 18, 21, 26, 30, 33, 37, 41, 42 with reasons; imported items marked *Legacy* | H | |
| UAT-28 | Duplicate import blocked | Admin | Upload the same file again | Refused: "already been imported (batch #n)" | H | |
| UAT-29 | AI risk summary | Checker / Auditor | Open an application → *Generate AI Risk Summary* | Summary, risk level and factors shown under the label "AI-generated – advisory only, final decision rests with the Checker"; the audit trail has an `AiRiskSummaryGenerated` event whose payload contains no name, NIC, phone or address | M | |
| UAT-30 | REST API end-to-end | API client | In Swagger: `POST /api/v1/auth/token` (checker2) → Authorize → `GET /loan-applications?status=Submitted` → `POST /{id}/approve` with the `rowVersion` | 200 responses; repeating the approve with the old `rowVersion` returns 409; calling without a token returns 401; a maker token on approve returns 403 | H | |
| UAT-31 | Session security | Any | Sign in, then have Admin deactivate the user (or change their roles) | Within 5 minutes the UI session ends; an existing API token is rejected immediately (401) | M | |
| UAT-32 | Time zone and currency | Any | Compare timeline times with the local clock | All times shown in Asia/Colombo; amounts as `LKR 1,234,567.89` | M | |

## 5. Defect severity

| Severity | Definition |
|---|---|
| 1 – Critical | Control failure (e.g. a maker can approve, audit not written, limit bypassed) or data loss |
| 2 – Major | Feature unusable without workaround |
| 3 – Minor | Feature usable with workaround; incorrect message or format |
| 4 – Cosmetic | Layout, spelling |

## 6. Sign-off

| Role | Name | Decision (Accept / Reject) | Signature | Date |
|---|---|---|---|---|
| Business owner – Retail Credit | | | | |
| Head of Operations (makers) | | | | |
| Internal Audit | | | | |
| IT Security | | | | |
| Project manager | | | | |
