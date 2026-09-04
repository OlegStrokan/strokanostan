import { getSessionToken } from "@/lib/session";

const BASE_URL = process.env.OPS_CONSOLE_API_URL ?? "http://localhost:5300";
const API_KEY = process.env.OPS_CONSOLE_ADMIN_API_KEY ?? "";

export async function POST(request: Request) {
  const token = await getSessionToken();

  if (!token) {
    return Response.json({ error: "Not logged in." }, { status: 401 });
  }

  const body = await request.text();

  const response = await fetch(`${BASE_URL}/api/ledger/adjustments`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Admin-Api-Key": API_KEY,
      Authorization: `Bearer ${token}`,
    },
    body,
    cache: "no-store",
  });

  const payload = await response.json().catch(() => ({}));
  return Response.json(payload, { status: response.status });
}
