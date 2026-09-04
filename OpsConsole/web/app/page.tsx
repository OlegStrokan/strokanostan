import Link from "next/link";
import { getSagas } from "@/lib/opsConsole";

export const dynamic = "force-dynamic";

const TAKE = 25;

const STATUSES = [
  "Pending",
  "Running",
  "WaitingForEvent",
  "Completed",
  "Failed",
  "FailedToCompensate",
  "Compensating",
  "Compensated",
  "TimedOut",
];

type SearchParams = {
  status?: string;
  sagaType?: string;
  search?: string;
  skip?: string;
};

export default async function SagasPage({
  searchParams,
}: {
  searchParams: Promise<SearchParams>;
}) {
  const resolvedSearchParams = await searchParams;
  const skip = Number(resolvedSearchParams.skip ?? 0) || 0;
  const { sagas, totalCount } = await getSagas({
    status: resolvedSearchParams.status,
    sagaType: resolvedSearchParams.sagaType,
    search: resolvedSearchParams.search,
    skip,
    take: TAKE,
  });

  const hasPrev = skip > 0;
  const hasNext = skip + TAKE < totalCount;

  return (
    <main>
      <p>
        <Link href="/deadletters">Dead letters &rarr;</Link>
        {" · "}
        <Link href="/ledger">Ledger &rarr;</Link>
      </p>
      <h1>Sagas</h1>

      <form className="filters" method="get">
        <select name="status" defaultValue={resolvedSearchParams.status ?? ""}>
          <option value="">All statuses</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
        <input
          name="sagaType"
          placeholder="Saga type"
          defaultValue={resolvedSearchParams.sagaType ?? ""}
        />
        <input
          name="search"
          placeholder="Saga id or correlation id"
          defaultValue={resolvedSearchParams.search ?? ""}
        />
        <button type="submit">Filter</button>
      </form>

      <p className="count">
        {totalCount} saga{totalCount === 1 ? "" : "s"} found
      </p>

      <table>
        <thead>
          <tr>
            <th>Id</th>
            <th>Correlation Id</th>
            <th>Type</th>
            <th>Status</th>
            <th>Current step</th>
            <th>Updated</th>
          </tr>
        </thead>
        <tbody>
          {sagas.map((s) => (
            <tr key={s.id}>
              <td>
                <Link href={`/sagas/${s.id}`}>{s.id}</Link>
              </td>
              <td>{s.correlationId}</td>
              <td>{s.sagaType}</td>
              <td>
                <span className={`status status-${s.status}`}>{s.status}</span>
              </td>
              <td>{s.currentStep}</td>
              <td>{new Date(s.updatedAt).toLocaleString()}</td>
            </tr>
          ))}
          {sagas.length === 0 && (
            <tr>
              <td colSpan={6}>No sagas match these filters.</td>
            </tr>
          )}
        </tbody>
      </table>

      <nav className="pagination">
        {hasPrev && (
          <Link href={buildPageLink(resolvedSearchParams, Math.max(skip - TAKE, 0))}>
            Previous
          </Link>
        )}
        {hasNext && (
          <Link href={buildPageLink(resolvedSearchParams, skip + TAKE)}>Next</Link>
        )}
      </nav>
    </main>
  );
}

function buildPageLink(searchParams: SearchParams, skip: number) {
  const params = new URLSearchParams();
  if (searchParams.status) params.set("status", searchParams.status);
  if (searchParams.sagaType) params.set("sagaType", searchParams.sagaType);
  if (searchParams.search) params.set("search", searchParams.search);
  params.set("skip", String(skip));
  return `/?${params.toString()}`;
}
