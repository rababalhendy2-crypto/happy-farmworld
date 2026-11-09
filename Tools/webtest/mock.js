// Fake Supabase for headless tests: answers the auth + hf_* RPCs the Unity client calls.
const now = () => Date.now() / 1000;
const layout = ['cow','cow','sheep','sheep','chicken','chicken','chicken','chicken'];
function animals(producing) { return layout.map((t, i) => ({ type: t, status: producing && i % 2 == 0 ? 'producing' : 'hungry', started_at: now() - (i == 0 ? 99999 : 60), hungry_since: now() - 100, happiness: 80 })); }
const me = { coins: 250, premium: 0, xp: 40, level: 3, display_name: 'Farmer', friend_code: 'MEF4RM', likes: 2,
  inventory: { wheat: 12, carrot: 4, egg: 2 }, plots: Array.from({ length: 6 }, () => ({ crop: '', planted_at: 0, watered: false })),
  animals: animals(false), workshops: {}, storage_level: 0, missions: {}, daily_reward: { last_day: Math.floor(Date.now()/86400000) - 1, step: 6 },
  orders: [{ items: { wheat: 4 }, coins: 23, xp: 2, available_at: 0 }, { items: { egg: 2 }, coins: 20, xp: 2, available_at: 0 }, { items: { carrot: 5 }, coins: 43, xp: 5, available_at: 0 }] };
const friendFarm = { code: 'SAM742', display_name: 'Farmer', level: 5, likes: 7, storage_level: 1, workshops: { mill: { jobs: [] } },
  plots: [{ crop: 'wheat', planted_at: now() - 5000 }, { crop: 'corn', planted_at: now() - 200 }, { crop: 'carrot', planted_at: now() - 60 }, { crop: 'tomato', planted_at: now() - 9000 }, { crop: 'pumpkin', planted_at: now() - 900 }, { crop: '', planted_at: 0 }],
  animals: animals(true), liked: false, gifted: false };
let friends = [{ code: 'SAM742', name: 'Farmer', level: 5, state: 'friend', liked: false, gifted: false }, { code: 'NAD477', name: 'Farmer', level: 2, state: 'incoming', liked: false, gifted: false }];
let giftsShown = false;
let pendingEmail = '';
let myOffers = [{ id: 1, item: 'egg', qty: 2, unit_price: 16 }];
let friendOffers = [{ id: 11, item: 'milk', qty: 2, unit_price: 20 }, { id: 12, item: 'pumpkin', qty: 1, unit_price: 90 }, { id: 13, item: 'bread', qty: 3, unit_price: 100 }];
let nextId = 20;
const log = [];
function state() { return Object.assign({}, me, { server_time: now(), version: 1 }); }
function handle(path, body) {
  log.push(path + ' ' + JSON.stringify(body || {}).slice(0, 120));
  if (path.endsWith('/auth/v1/user')) {
    if (body && body.email) { pendingEmail = body.email; return { id: 'u1', email: '', new_email: body.email }; }
    return { id: 'u1', email: '', new_email: pendingEmail };
  }
  if (path.includes('/auth/v1/')) return { access_token: 'tok', refresh_token: 'ref', expires_in: 3600, user: { id: 'u1' } };
  const fn = path.split('/rpc/')[1];
  switch (fn) {
    case 'hf_init_player': case 'hf_get_state': return state();
    case 'hf_action': {
      const a = body.p_action, g = body.p_args || {};
      if (a == 'plant') me.plots[g.idx] = { crop: g.crop, planted_at: now(), watered: false };
      if (a == 'claim_daily') {
        const day = Math.floor(now() / 86400);
        if (me.daily_reward.last_day >= day) return { ok: false, error: 'already_done', state: state() };
        const step = me.daily_reward.step, rw = step % 7 == 6 ? { coins: 100, premium: 2 } : { coins: 30 };
        me.coins += rw.coins; me.premium += rw.premium || 0; me.daily_reward = { last_day: day, step: step + 1 };
        return { ok: true, error: '', reward: rw, step, state: state() };
      }
      return { ok: true, error: '', state: state() };
    }
    case 'hf_friends': {
      const r = { ok: true, code: me.friend_code, likes: me.likes, friends, new_gifts: giftsShown ? 0 : 1, new_likes: 0 };
      giftsShown = true; return r;
    }
    case 'hf_friend_request': {
      const c = (body.p_code || '').toUpperCase();
      if (c == 'ZZZZZZ') return { ok: false, error: 'not_found' };
      if (friends.find(f => f.code == c)) return { ok: false, error: 'already' };
      friends.push({ code: c, name: 'Farmer', level: 1, state: 'outgoing', liked: false, gifted: false });
      return { ok: true, error: '', accepted: false };
    }
    case 'hf_friend_respond': {
      const f = friends.find(x => x.code == body.p_code);
      if (!f) return { ok: false, error: 'not_found' };
      if (body.p_accept) f.state = 'friend'; else friends = friends.filter(x => x != f);
      return { ok: true, error: '' };
    }
    case 'hf_delete_my_data': return { ok: true, error: '' };
    case 'hf_market_offers': return { ok: true, offers: body.p_code ? friendOffers : myOffers };
    case 'hf_market_list': {
      if (myOffers.length >= 4) return { ok: false, error: 'full' };
      if ((me.inventory[body.p_item] || 0) < body.p_qty) return { ok: false, error: 'missing_items' };
      me.inventory[body.p_item] -= body.p_qty;
      myOffers.push({ id: nextId++, item: body.p_item, qty: body.p_qty, unit_price: body.p_unit_price });
      return { ok: true, error: '', offers: myOffers, state: state() };
    }
    case 'hf_market_cancel': {
      const o = myOffers.find(x => x.id == body.p_id); if (!o) return { ok: false, error: 'not_found' };
      myOffers = myOffers.filter(x => x != o); me.inventory[o.item] = (me.inventory[o.item] || 0) + o.qty;
      return { ok: true, error: '', offers: myOffers, state: state() };
    }
    case 'hf_market_buy': {
      const o = friendOffers.find(x => x.id == body.p_id); if (!o) return { ok: false, error: 'gone' };
      const total = o.qty * o.unit_price; if (me.coins < total) return { ok: false, error: 'no_coins' };
      friendOffers = friendOffers.filter(x => x != o); me.coins -= total; me.inventory[o.item] = (me.inventory[o.item] || 0) + o.qty;
      return { ok: true, error: '', item: o.item, qty: o.qty, coins: total, state: state() };
    }
    case 'hf_visit': return Object.assign({ ok: true, server_time: now() }, friendFarm);
    case 'hf_social': {
      if (body.p_kind == 'like') { if (friendFarm.liked) return { ok: false, error: 'already' }; friendFarm.liked = true; friendFarm.likes++; return { ok: true, error: '' }; }
      if (friendFarm.gifted) return { ok: false, error: 'already' }; friendFarm.gifted = true; return { ok: true, error: '', coins: 10 };
    }
  }
  return { ok: false, error: 'unknown' };
}
module.exports = { handle, log };
