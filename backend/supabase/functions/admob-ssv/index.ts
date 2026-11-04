// AdMob server-side verification callback -> grants the rewarded-ad coins exactly once.
// Configure in AdMob: Rewarded ad unit > Server-side verification > callback URL:
//   https://<project>.supabase.co/functions/v1/admob-ssv
// The game passes user_id = Supabase user id and custom_data = nonce from hf_ad_begin.
import { rpc, verifyAdmobSsv, type VerifierKey } from "../_shared/lib.ts";

const KEYS_URL = "https://www.gstatic.com/admob/reward/verifier-keys.json";
let cachedKeys: { at: number; keys: VerifierKey[] } | null = null;

async function keys(): Promise<VerifierKey[]> {
  if (cachedKeys && Date.now() - cachedKeys.at < 3600_000) return cachedKeys.keys;
  const res = await fetch(KEYS_URL);
  const json = await res.json();
  cachedKeys = { at: Date.now(), keys: json.keys };
  return json.keys;
}

Deno.serve(async (req) => {
  const url = new URL(req.url);
  const query = url.search.slice(1);
  if (!query) return new Response("ok", { status: 200 }); // AdMob "verify URL" ping
  try {
    const p = await verifyAdmobSsv(query, await keys());
    const result = await rpc(fetch, Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
      "hf_grant_ad_reward", { p_user: p.user_id, p_nonce: p.custom_data, p_transaction_id: p.transaction_id });
    return Response.json(result, { status: 200 });
  } catch (e) {
    console.error("ssv rejected:", (e as Error).message);
    return new Response("rejected", { status: 400 });
  }
});
