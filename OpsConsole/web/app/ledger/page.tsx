import Link from "next/link";
import {
  getLedgerHealth,
  getOrderMoneyTrail,
  getTrialBalance,
  type MoneyTrailTransaction,
} from "@/lib/opsConsole";
import { LedgerAdjustmentForm } from "./LedgerAdjustmentForm";

export const dynamic = "force-dynamic";

type SearchParams = {
  currency?: string;
  orderId?: string;
};

function money(value: number, currency: string) {
  return `${value.toFixed(4)} ${currency}`;
}

export default async function LedgerPage({
  searchParams,
}: {
  searchParams: Promise<SearchParams>;
}) {
  const { currency, orderId } = await searchParams;

  const [trialBalance, health] = await Promise.all([
    getTrialBalance(currency),
    getLedgerHealth(),
  ]);

  let moneyTrail: MoneyTrailTransaction[] = [];
  if (orderId) {
    moneyTrail = await getOrderMoneyTrail(orderId);
  }

  return (
    <main>
      <p>
        <Link href="/">&larr; Sagas</Link> · <Link href="/deadletters">Dead letters</Link>
      </p>
      <h1>Ledger</h1>

      <section>
        <h2>Reconciliation status</h2>
        <p className="count">
          {health.isHealthy
            ? `No drift. ${health.currenciesChecked} currency/currencies checked.`
            : `${health.findings.length} problem(s) found.`}
        </p>
        {!health.isHealthy && (
          <ul>
            {health.findings.map((finding) => (
              <li key={finding}>{finding}</li>
            ))}
          </ul>
        )}
      </section>

      <section>
        <h2>Trial balance ({trialBalance.reportingCurrency})</h2>
        <p className="count">
          Debits {trialBalance.reportingDebits.toFixed(4)} · Credits{" "}
          {trialBalance.reportingCredits.toFixed(4)} ·{" "}
          {trialBalance.isBalanced ? "balanced" : "UNBALANCED"}
        </p>

        <form method="get">
          <label>
            Reporting currency{" "}
            <input name="currency" defaultValue={currency ?? ""} placeholder="USD" />
          </label>
          <button type="submit">Apply</button>
        </form>

        <table>
          <thead>
            <tr>
              <th>Account</th>
              <th>Debits</th>
              <th>Credits</th>
              <th>Balance</th>
            </tr>
          </thead>
          <tbody>
            {trialBalance.reportingCurrencyBalances.map((row) => (
              <tr key={`reporting-${row.account}`}>
                <td>{row.account}</td>
                <td>{row.debits.toFixed(4)}</td>
                <td>{row.credits.toFixed(4)}</td>
                <td>{row.balance.toFixed(4)}</td>
              </tr>
            ))}
            {trialBalance.reportingCurrencyBalances.length === 0 && (
              <tr>
                <td colSpan={4}>
                  Nothing projected yet. The reporting worker converts postings on its own
                  schedule, and skips any currency with no FX rate.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </section>

      <section>
        <h2>Balances in transaction currency</h2>
        <table>
          <thead>
            <tr>
              <th>Account</th>
              <th>Currency</th>
              <th>Debits</th>
              <th>Credits</th>
              <th>Balance</th>
            </tr>
          </thead>
          <tbody>
            {trialBalance.transactionCurrencyBalances.map((row) => (
              <tr key={`native-${row.currency}-${row.account}`}>
                <td>{row.account}</td>
                <td>{row.currency}</td>
                <td>{row.debits.toFixed(4)}</td>
                <td>{row.credits.toFixed(4)}</td>
                <td>{row.balance.toFixed(4)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2>Money trail</h2>
        <form method="get">
          {currency && <input type="hidden" name="currency" value={currency} />}
          <label>
            Order id <input name="orderId" defaultValue={orderId ?? ""} size={40} />
          </label>
          <button type="submit">Search</button>
        </form>

        {orderId && moneyTrail.length === 0 && (
          <p className="count">No ledger postings for that order.</p>
        )}

        {moneyTrail.map((transaction) => (
          <div key={transaction.transactionId}>
            <h3>
              {transaction.refType} · {transaction.transactionRef}
            </h3>
            <p className="step-meta">{transaction.occurredAt}</p>
            <table>
              <thead>
                <tr>
                  <th>Account</th>
                  <th>Direction</th>
                  <th>Amount</th>
                </tr>
              </thead>
              <tbody>
                {transaction.entries.map((entry, index) => (
                  <tr key={`${transaction.transactionId}-${index}`}>
                    <td>{entry.account}</td>
                    <td>{entry.direction}</td>
                    <td>{money(entry.amount, entry.currency)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}
      </section>

      <section>
        <h2>Post adjusting entry</h2>
        <p className="step-meta">
          Appends a balanced correcting transaction. Existing entries are never edited or
          deleted — that is what makes the ledger worth trusting.
        </p>
        <LedgerAdjustmentForm />
      </section>
    </main>
  );
}
