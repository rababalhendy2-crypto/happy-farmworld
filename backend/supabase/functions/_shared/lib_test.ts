import { bytesToB64Url, checkPlayPurchase, derToRaw, rawToDer, verifyAdmobSsv } from "./lib.ts";

function assert(c: boolean, msg: string) { if (!c) throw new Error(msg); }

async function makeKey() {
  const kp = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const spki = new Uint8Array(await crypto.subtle.exportKey("spki", kp.publicKey));
  let s = ""; for (const b of spki) s += String.fromCharCode(b);
  return { kp, base64: btoa(s) };
}

async function signedQuery(kp: CryptoKeyPair, msg: string) {
  const raw = new Uint8Array(await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, kp.privateKey, new TextEncoder().encode(msg)));
  return `${msg}&signature=${bytesToB64Url(rawToDer(raw))}&key_id=123`;
}

const MSG = "ad_network=5450213213286189855&ad_unit=1234&custom_data=7f2c0d3e-1111-4a4a-9b9b-123456789abc&reward_amount=1&reward_item=coins&timestamp=1730000000000&transaction_id=abc123&user_id=11111111-1111-1111-1111-111111111111";

Deno.test("valid AdMob signature is accepted", async () => {
  const { kp, base64 } = await makeKey();
  const p = await verifyAdmobSsv(await signedQuery(kp, MSG), [{ keyId: 123, base64 }]);
  assert(p.transaction_id === "abc123" && p.custom_data.startsWith("7f2c"), "params parsed");
});

Deno.test("tampered reward is rejected", async () => {
  const { kp, base64 } = await makeKey();
  const q = (await signedQuery(kp, MSG)).replace("reward_amount=1", "reward_amount=999");
  let failed = false;
  try { await verifyAdmobSsv(q, [{ keyId: 123, base64 }]); } catch { failed = true; }
  assert(failed, "tampering detected");
});

Deno.test("signature from another key is rejected", async () => {
  const a = await makeKey(), b = await makeKey();
  let failed = false;
  try { await verifyAdmobSsv(await signedQuery(a.kp, MSG), [{ keyId: 123, base64: b.base64 }]); } catch { failed = true; }
  assert(failed, "foreign key rejected");
});

Deno.test("missing signature is rejected", async () => {
  let failed = false;
  try { await verifyAdmobSsv(MSG, []); } catch { failed = true; }
  assert(failed, "unsigned rejected");
});

Deno.test("DER <-> raw round trip", () => {
  const raw = new Uint8Array(64).map((_, i) => (i * 37 + 200) & 255);
  const back = derToRaw(rawToDer(raw));
  assert(back.every((v, i) => v === raw[i]), "round trip");
});

Deno.test("Play purchase checks", () => {
  const uid = "u1";
  assert(checkPlayPurchase({ purchaseState: 0, obfuscatedExternalAccountId: uid }, uid) === null, "valid");
  assert(checkPlayPurchase({ purchaseState: 2, obfuscatedExternalAccountId: uid }, uid) === "not_purchased", "pending rejected");
  assert(checkPlayPurchase({ purchaseState: 1, obfuscatedExternalAccountId: uid }, uid) === "not_purchased", "cancelled rejected");
  assert(checkPlayPurchase({ purchaseState: 0, obfuscatedExternalAccountId: "someone-else" }, uid) === "wrong_account", "other account rejected");
});
