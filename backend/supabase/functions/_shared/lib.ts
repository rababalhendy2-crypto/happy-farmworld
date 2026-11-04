// Shared, dependency-free helpers for the Edge Functions (testable with `deno test`).

export function b64ToBytes(b64: string): Uint8Array {
  const norm = b64.replace(/-/g, "+").replace(/_/g, "/");
  const pad = norm + "=".repeat((4 - (norm.length % 4)) % 4);
  return Uint8Array.from(atob(pad), (c) => c.charCodeAt(0));
}

export function bytesToB64Url(b: Uint8Array): string {
  let s = "";
  for (const x of b) s += String.fromCharCode(x);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

/** ECDSA signatures from AdMob are DER encoded; WebCrypto wants raw r||s (64 bytes for P-256). */
export function derToRaw(der: Uint8Array): Uint8Array {
  if (der[0] !== 0x30) throw new Error("bad DER");
  let i = 2;
  if (der[1] & 0x80) i = 2 + (der[1] & 0x7f);
  const readInt = () => {
    if (der[i++] !== 0x02) throw new Error("bad DER int");
    const len = der[i++];
    let v = der.slice(i, i + len);
    i += len;
    while (v.length > 32 && v[0] === 0) v = v.slice(1);
    const out = new Uint8Array(32);
    out.set(v, 32 - v.length);
    return out;
  };
  const r = readInt(), s = readInt();
  const raw = new Uint8Array(64);
  raw.set(r, 0);
  raw.set(s, 32);
  return raw;
}

export function rawToDer(raw: Uint8Array): Uint8Array {
  const enc = (v: Uint8Array) => {
    let x = v;
    while (x.length > 1 && x[0] === 0 && !(x[1] & 0x80)) x = x.slice(1);
    if (x[0] & 0x80) x = Uint8Array.from([0, ...x]);
    return Uint8Array.from([0x02, x.length, ...x]);
  };
  const r = enc(raw.slice(0, 32)), s = enc(raw.slice(32));
  return Uint8Array.from([0x30, r.length + s.length, ...r, ...s]);
}

export type VerifierKey = { keyId: number; base64: string };

/**
 * Verifies an AdMob server-side verification (SSV) callback.
 * `query` is the raw query string (without "?"). Signature covers everything before "&signature=".
 */
export async function verifyAdmobSsv(query: string, keys: VerifierKey[]): Promise<Record<string, string>> {
  const sigIdx = query.indexOf("&signature=");
  if (sigIdx < 0) throw new Error("missing signature");
  const message = query.slice(0, sigIdx);
  const params = new URLSearchParams(query);
  const signature = params.get("signature") ?? "";
  const keyId = Number(params.get("key_id"));
  const key = keys.find((k) => Number(k.keyId) === keyId);
  if (!key) throw new Error("unknown key id");
  const pub = await crypto.subtle.importKey("spki", b64ToBytes(key.base64), { name: "ECDSA", namedCurve: "P-256" }, false, ["verify"]);
  const ok = await crypto.subtle.verify({ name: "ECDSA", hash: "SHA-256" }, pub, derToRaw(b64ToBytes(signature)), new TextEncoder().encode(message));
  if (!ok) throw new Error("bad signature");
  return Object.fromEntries(params.entries());
}

/** Calls a Postgres function through PostgREST with the service role key (server-side only). */
export async function rpc(fetchFn: typeof fetch, url: string, serviceKey: string, fn: string, args: unknown) {
  const res = await fetchFn(`${url}/rest/v1/rpc/${fn}`, {
    method: "POST",
    headers: { apikey: serviceKey, Authorization: `Bearer ${serviceKey}`, "Content-Type": "application/json" },
    body: JSON.stringify(args),
  });
  if (!res.ok) throw new Error(`rpc ${fn} failed: ${res.status}`);
  return await res.json();
}

/** Google OAuth access token from a service-account key (JWT bearer flow, RS256). */
export async function googleAccessToken(fetchFn: typeof fetch, sa: { client_email: string; private_key: string }): Promise<string> {
  const now = Math.floor(Date.now() / 1000);
  const enc = (o: unknown) => bytesToB64Url(new TextEncoder().encode(JSON.stringify(o)));
  const unsigned = `${enc({ alg: "RS256", typ: "JWT" })}.${enc({
    iss: sa.client_email, scope: "https://www.googleapis.com/auth/androidpublisher",
    aud: "https://oauth2.googleapis.com/token", iat: now, exp: now + 3600,
  })}`;
  const pem = sa.private_key.replace(/-----[^-]+-----/g, "").replace(/\s+/g, "");
  const key = await crypto.subtle.importKey("pkcs8", b64ToBytes(pem), { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]);
  const sig = new Uint8Array(await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(unsigned)));
  const res = await fetchFn("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer", assertion: `${unsigned}.${bytesToB64Url(sig)}` }),
  });
  if (!res.ok) throw new Error("google auth failed");
  return (await res.json()).access_token;
}

export type PlayPurchase = { purchaseState?: number; obfuscatedExternalAccountId?: string; orderId?: string; acknowledgementState?: number };

/** Decide whether a Google Play product purchase may be granted to `userId`. */
export function checkPlayPurchase(p: PlayPurchase, userId: string): string | null {
  if (p.purchaseState !== 0) return "not_purchased";             // 0 = purchased, 1 = cancelled, 2 = pending
  if (p.obfuscatedExternalAccountId !== userId) return "wrong_account"; // purchase bound to this player
  return null;
}
