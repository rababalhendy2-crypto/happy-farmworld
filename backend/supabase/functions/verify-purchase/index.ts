// Verifies a Google Play purchase token and grants the product exactly once.
// Called by the game after the Play Billing purchase flow, with the player's Supabase JWT.
// Secrets (set with `supabase secrets set`): GOOGLE_SERVICE_ACCOUNT_JSON, ANDROID_PACKAGE_NAME.
import { checkPlayPurchase, googleAccessToken, rpc } from "../_shared/lib.ts";

Deno.serve(async (req) => {
  if (req.method !== "POST") return new Response("method", { status: 405 });
  const supaUrl = Deno.env.get("SUPABASE_URL")!;
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
  const jwt = (req.headers.get("Authorization") ?? "").replace("Bearer ", "");
  // Who is calling? Ask Supabase Auth (never trust a user id sent in the body).
  const who = await fetch(`${supaUrl}/auth/v1/user`, { headers: { apikey: Deno.env.get("SUPABASE_ANON_KEY")!, Authorization: `Bearer ${jwt}` } });
  if (!who.ok) return Response.json({ ok: false, error: "auth" }, { status: 401 });
  const userId: string = (await who.json()).id;

  const { product_id, purchase_token } = await req.json();
  if (typeof product_id !== "string" || typeof purchase_token !== "string") {
    return Response.json({ ok: false, error: "invalid" }, { status: 400 });
  }
  const pkg = Deno.env.get("ANDROID_PACKAGE_NAME")!;
  const sa = JSON.parse(Deno.env.get("GOOGLE_SERVICE_ACCOUNT_JSON")!);
  const token = await googleAccessToken(fetch, sa);
  const base = `https://androidpublisher.googleapis.com/androidpublisher/v3/applications/${pkg}/purchases/products/${encodeURIComponent(product_id)}/tokens/${encodeURIComponent(purchase_token)}`;
  const g = await fetch(base, { headers: { Authorization: `Bearer ${token}` } });
  if (!g.ok) return Response.json({ ok: false, error: "not_found" }, { status: 400 });
  const purchase = await g.json();
  const problem = checkPlayPurchase(purchase, userId);
  if (problem) return Response.json({ ok: false, error: problem }, { status: 400 });

  const result = await rpc(fetch, supaUrl, serviceKey, "hf_grant_purchase",
    { p_user: userId, p_product: product_id, p_token: purchase_token, p_order: purchase.orderId ?? null });
  if (result.ok && purchase.acknowledgementState === 0) {
    // Google refunds unacknowledged purchases after 3 days, so acknowledge after granting.
    await fetch(`${base}:acknowledge`, { method: "POST", headers: { Authorization: `Bearer ${token}` } });
  }
  return Response.json(result);
});
