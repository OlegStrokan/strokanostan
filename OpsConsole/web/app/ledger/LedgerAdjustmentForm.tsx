"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";

const ACCOUNTS = [
  "CustomerAuthorized",
  "CustomerCaptured",
  "MerchantRevenue",
  "TaxPayable",
  "RefundsPayable",
  "GatewayFees",
  "FxGainLoss",
  "Chargebacks",
  "AuthorizationHold",
];

type Leg = { account: string; direction: string; amount: string };

const EMPTY_LEGS: Leg[] = [
  { account: ACCOUNTS[0], direction: "Debit", amount: "" },
  { account: ACCOUNTS[1], direction: "Credit", amount: "" },
];

export function LedgerAdjustmentForm() {
  const router = useRouter();
  const [legs, setLegs] = useState<Leg[]>(EMPTY_LEGS);
  const [currency, setCurrency] = useState("USD");
  const [orderId, setOrderId] = useState("");
  const [reason, setReason] = useState("");
  const [pending, setPending] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const debits = sum(legs, "Debit");
  const credits = sum(legs, "Credit");
  const balanced = debits > 0 && debits === credits;

  function updateLeg(index: number, patch: Partial<Leg>) {
    setLegs((current) =>
      current.map((leg, i) => (i === index ? { ...leg, ...patch } : leg))
    );
  }

  async function post() {
    if (!reason.trim()) {
      setMessage("A reason is required.");
      return;
    }

    if (!balanced) {
      setMessage("Debits and credits must match before posting.");
      return;
    }

    if (!confirm(`Post a balanced adjustment of ${debits} ${currency}? This cannot be undone — only reversed.`)) {
      return;
    }

    setPending(true);
    setMessage(null);

    const response = await fetch("/api/ledger/adjustments", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        // Generated client-side so a double submit collapses to one posting.
        adjustmentId: crypto.randomUUID(),
        currency,
        reason,
        orderId: orderId || null,
        legs: legs.map((leg) => ({
          account: leg.account,
          direction: leg.direction,
          amount: Number(leg.amount),
        })),
      }),
    });

    const body = await response.json().catch(() => ({}));

    setPending(false);
    setMessage(
      response.status === 401
        ? "Not signed in."
        : response.status === 403
          ? "Missing Admin/SuperAdmin role."
          : response.ok
            ? `Posted transaction ${body.transactionId}.`
            : (body.error ?? "Failed.")
    );

    if (response.ok) {
      setLegs(EMPTY_LEGS);
      setReason("");
      setOrderId("");
      router.refresh();
    }
  }

  return (
    <div>
      <label>
        Currency{" "}
        <input value={currency} onChange={(e) => setCurrency(e.target.value)} size={5} />
      </label>{" "}
      <label>
        Order id (optional){" "}
        <input value={orderId} onChange={(e) => setOrderId(e.target.value)} size={38} />
      </label>

      <table>
        <thead>
          <tr>
            <th>Account</th>
            <th>Direction</th>
            <th>Amount</th>
          </tr>
        </thead>
        <tbody>
          {legs.map((leg, index) => (
            <tr key={index}>
              <td>
                <select
                  value={leg.account}
                  onChange={(e) => updateLeg(index, { account: e.target.value })}
                >
                  {ACCOUNTS.map((account) => (
                    <option key={account} value={account}>
                      {account}
                    </option>
                  ))}
                </select>
              </td>
              <td>
                <select
                  value={leg.direction}
                  onChange={(e) => updateLeg(index, { direction: e.target.value })}
                >
                  <option value="Debit">Debit</option>
                  <option value="Credit">Credit</option>
                </select>
              </td>
              <td>
                <input
                  value={leg.amount}
                  onChange={(e) => updateLeg(index, { amount: e.target.value })}
                  inputMode="decimal"
                  size={12}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <button
        type="button"
        onClick={() => setLegs((current) => [...current, { account: ACCOUNTS[0], direction: "Debit", amount: "" }])}
      >
        Add leg
      </button>

      <p className="step-meta">
        Debits {debits} · Credits {credits} · {balanced ? "balanced" : "not balanced"}
      </p>

      <label>
        Reason{" "}
        <input
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          size={60}
          placeholder="Why this correction is being posted"
        />
      </label>

      <p>
        <button disabled={pending || !balanced || !reason.trim()} onClick={post}>
          Post adjustment
        </button>
      </p>

      {message && <div className="step-meta">{message}</div>}
    </div>
  );
}

function sum(legs: Leg[], direction: string) {
  return legs
    .filter((leg) => leg.direction === direction)
    .reduce((total, leg) => total + (Number(leg.amount) || 0), 0);
}
