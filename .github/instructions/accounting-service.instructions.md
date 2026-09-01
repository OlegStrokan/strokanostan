---
applyTo: "Accounting/**"
description: "Use when working on the Accounting Service — append-only double-entry ledger fed by Payment money-events over Kafka, with aggregate reconciliation, FX reporting projection, and an internal admin surface for OpsConsole."
---

# Accounting Service

## Overview

The single source of monetary truth. Payment writes a money-event in the same transaction as the
money mutation; Accounting consumes that stream and posts balanced double-entry transactions. Also
runs the reconciliation worker that detects drift the per-entity workers in Payment and Order
structurally cannot see.

## Architecture (Clean Architecture + CQRS)

- **Api/** — gRPC services (`AccountingGrpcService` for Order, `AdminAccountingGrpcService` for OpsConsole), interceptors, Program.cs
- **Application/** — MediatR command/query handlers, `MoneyEventPayload` contract, reader interfaces, `IIncidentReporter`
- **Domain/** — `LedgerTransaction` aggregate + factories, `LedgerEntry`, `ProcessedEvent`, `FxRate`, `LedgerReportingEntry`, `ReportingProjector`
- **Infrastructure/** — EF Core persistence, Kafka consumer, background workers, Telegram reporter, read queries
- **Protos/** — `accounting.proto` (Order-facing) + `admin_ops.proto` (OpsConsole-facing) + `common.proto`

## Tech Stack

.NET 8, gRPC, PostgreSQL + EF Core 8 (migrations, never `EnsureCreated`), Confluent.Kafka consumer,
MediatR. Tests: xUnit + NSubstitute; `Infrastructure.Tests` uses EF Core **InMemory**.

## Chart of accounts

`CustomerAuthorized`, `CustomerCaptured`, `MerchantRevenue`, `TaxPayable`, `RefundsPayable`,
`GatewayFees`, `FxGainLoss`, `Chargebacks`, `AuthorizationHold`.

## Posting map

| Source | `transaction_ref` | Debit | Credit |
|---|---|---|---|
| `PaymentAuthorizedEvent` | `authorize:{paymentId}` | `customer_authorized` | `authorization_hold` |
| `PaymentVoidedEvent` | `void:{paymentId}` | `authorization_hold` | `customer_authorized` |
| `PaymentCapturedEvent` | `capture:{paymentId}` | `customer_captured` (gross−fee) + `gateway_fees` | `merchant_revenue` (gross−tax) + `tax_payable` |
| `RefundIssuedEvent` | `refund:{refundId}` | `refunds_payable` | `customer_captured` |
| Order `ReverseRevenue` | `reversal:{returnRequestId}` | `merchant_revenue` | `refunds_payable` |
| Order `CancelReversal` | `cancel-reversal:{reversalId}` | swaps every leg of the original | |
| OpsConsole adjustment | `adjustment:{adjustmentId}` | operator-supplied balanced legs | |

## Key Rules

- **Append-only. Always.** There is no update or delete path on `ledger_transactions` / `ledger_entries`, and none may be added. Corrections are new reversing transactions. This is the property the whole service exists to provide.
- **Only the aggregate factories build a transaction.** `LedgerTransaction` has a private constructor and every factory calls `EnsureBalanced()`, so no code path can persist an unbalanced posting.
- **`transaction_ref` never derives from an amount.** It comes from a business identifier (BUG-006: keying a reversal on `(orderId, amount, currency)` silently swallowed a second return of the same value). An order can have multiple returns.
- **Idempotency is two-layered and atomic.** `processed_events.event_id` is the PRIMARY KEY (Kafka is at-least-once); `ledger_transactions.transaction_ref` is UNIQUE. Both write in one `SaveChangesAsync`, so an event can never be marked processed without its posting.
- **Zero-value legs are skipped, not posted.** `LedgerEntry` rejects non-positive amounts; `fee` and `tax` are usually zero.
- **The consumer commits past poison messages** after `MoneyEventConsumer:MaxOffsetRetries`, logs `Critical`, and records it via `IMoneyEventConsumerMonitor`. Holding the partition would stall every later payment. The resulting drift is what reconciliation exists to surface — never silence that path.
- **`GetLedgerHealth` passes `ReportIncidents: false`.** Never flip it: an operator refreshing a page would page the finance channel every time.
- **Two callers, two keys.** `ApiKeyAuthInterceptor` picks the expected key by service name — `AdminAccountingService` takes `InternalServices:OpsConsoleApiKey`, everything else takes `InternalServices:AccountingApiKey`. Do not collapse them; OpsConsole would gain `ReverseRevenue`.
- **Accounting owns its own alerting.** It never calls Order's `IIncidentReporter`. The contract here is `LedgerIncident` (aggregate-shaped), not Order's `IncidentAlert` (which requires a `Guid OrderId`).
- **The reporting layer is derived and rebuildable.** It never feeds back into `ledger_entries`. Every leg of a transaction converts at one rate; rounding residue becomes an explicit `fx_gain_loss` leg with a null `entry_id` — do not absorb it into a real account.
- **No FX rate means no projection.** The transaction stays queued rather than being booked at a guess.

## Refund ownership

The refund cash leg has exactly one owner: **Payment**. Order's `IAccountingGateway` deliberately has
no `RecordRefund` method — a client with zero correct callers is a double-book waiting to happen. The
`RecordRefund` rpc stays on the **server** as a backfill and manual-correction surface only.

## Background workers

| Worker | Does |
|---|---|
| `MoneyEventsConsumerService` | Kafka → `IngestMoneyEventCommand`; manual offset commit, seek-back retry |
| `ReconcileLedgerWorker` | three drift checks on a schedule, pages Telegram on findings |
| `ReportingProjectionWorker` | converts unprojected transactions into the reporting currency; seeds FX rates from config |

## Configuration

- **Kafka**: BootstrapServers, MoneyEventsTopic (`payment.money-events`)
- **MoneyEventConsumer**: Enabled, ConsumerGroupId, MaxOffsetRetries, RetryDelaySeconds
- **LedgerReconciliation**: Enabled, IntervalMinutes, StartupDelaySeconds, MaxFindings, MaxAcceptableLag
- **Reporting**: Enabled, ReportingCurrency, IntervalMinutes, StartupDelaySeconds, BatchSize, SeedRates
- **IncidentReporter:Telegram**: Enabled, BotToken, ChatId, TimeoutSeconds
- **InternalServices**: AccountingApiKey (Order), OpsConsoleApiKey (OpsConsole)

## Gotchas

- **`accounting.proto` exists in two copies** (`Accounting/Accounting/Protos/protos/` and `Order/Order/Protos/protos/`), and `admin_ops.proto` in two more (`Accounting/…` and `OpsConsole/Protos/accounting_admin_ops.proto`). Nothing enforces sync — update by hand.
- **`MoneyEventPayload` is a hand-copied contract.** Payment's version is a *private* record inside its serializer. `MoneyEventPayloadContractTests` is the only thing tying them together.
- **EF Core 8 InMemory throws on untranslatable LINQ** — useful as a translation smoke test. `let` inside a `group … into` does not translate; use `.GroupBy(k).Select(g => new { aggregates }).Where(…)` so it becomes `HAVING`.
- **Postgres runs on host port 5441** in dev (5437 belongs to Order's write DB).
- Local Postgres is required for integration-style work; there is no Testcontainers project here.

## Known gaps

- **Tax is always zero.** `ForCapture` splits `merchant_revenue` / `tax_payable` whenever `tax > 0` and the money event carries the field, but nothing upstream computes tax. Do not invent tax rules here.
- **Capture does not release the authorization hold.** Payment emits no event when an auth converts, so `customer_authorized` / `authorization_hold` keep a stale equal-and-opposite pair. The trial balance still ties out; per-account hold balances overstate reality.
- **No cross-transaction FX revaluation.** `fx_gain_loss` only closes rounding.

## Deliberate deviations from the original design sketch

- **No `ILedgerPoster` abstraction.** The balance invariant lives in the aggregate (`EnsureBalanced`), and the private constructor means the factories are the only way to build a transaction — a separate poster would be a second place for the rule to drift.
- **The incident reporter was not lifted into `shared/`.** Each service's Dockerfile sets its build context to its own folder, so a cross-solution `ProjectReference` breaks the image. The repo already duplicates at service boundaries (`accounting.proto`, `common.proto`) for the same reason.
- **`ledger_reporting_entries` has a surrogate `id` PK**, not `entry_id`, because the synthetic `fx_gain_loss` residual row has no primitive entry behind it. A unique filtered index on `entry_id WHERE entry_id IS NOT NULL` keeps rebuilds from double-counting.
