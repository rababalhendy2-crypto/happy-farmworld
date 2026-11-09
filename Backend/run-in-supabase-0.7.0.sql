-- Farm World 0.7.0: friend market + server self-test. Paste everything into the Supabase SQL Editor and press Run.

update public.game_config set data = data || jsonb_build_object('market', jsonb_build_object(
  'max_offers', 4, 'max_qty', 10, 'max_price_factor', 3)), updated_at = now() where id = 1;

create table if not exists public.market_offers (
  id bigint generated always as identity primary key,
  seller uuid not null references public.profiles(id) on delete cascade,
  item text not null,
  qty int not null check (qty > 0),
  unit_price int not null check (unit_price > 0),
  created_at timestamptz not null default now());
create index if not exists market_offers_seller on public.market_offers (seller);
alter table public.market_offers enable row level security;   -- only the functions below touch it

create or replace function public.hf_offers_of(p uuid) returns jsonb
language sql stable security definer set search_path = public as $$
  select coalesce(jsonb_agg(jsonb_build_object('id', id, 'item', item, 'qty', qty, 'unit_price', unit_price) order by id), '[]'::jsonb)
  from public.market_offers where seller = p $$;

-- p_code null = the player's own offers
create or replace function public.hf_market_offers(p_code text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_code is null or p_code = '' then return jsonb_build_object('ok', true, 'offers', public.hf_offers_of(uid)); end if;
  o := public.hf_by_code(p_code);
  if o is null or not public.hf_are_friends(uid, o) then return public.hf_err('not_found'); end if;
  return jsonb_build_object('ok', true, 'offers', public.hf_offers_of(o));
end $$;

create or replace function public.hf_market_list(p_item text, p_qty int, p_unit_price int) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); base int;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  perform 1 from public.wallets where player_id = uid for update;
  if not found then return public.hf_err('no_player'); end if;
  base := (cfg->'items'->p_item->>'sell_price')::int;
  if base is null or p_qty is null or p_qty < 1 or p_qty > (cfg->'market'->>'max_qty')::int
     or p_unit_price is null or p_unit_price < base or p_unit_price > base * (cfg->'market'->>'max_price_factor')::int
    then return public.hf_err('invalid'); end if;
  if (select count(*) from public.market_offers where seller = uid) >= (cfg->'market'->>'max_offers')::int
    then return public.hf_err('full'); end if;
  if not public.hf_inv_remove(uid, p_item, p_qty, 'market_list') then return public.hf_err('missing_items'); end if;
  insert into public.market_offers (seller, item, qty, unit_price) values (uid, p_item, p_qty, p_unit_price);
  return public.hf_ok(jsonb_build_object('offers', public.hf_offers_of(uid), 'state', public.hf_state(uid)));
end $$;

create or replace function public.hf_market_cancel(p_id bigint) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); m public.market_offers%rowtype;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  perform 1 from public.wallets where player_id = uid for update;
  select * into m from public.market_offers where id = p_id and seller = uid for update;
  if not found then return public.hf_err('not_found'); end if;
  if public.hf_inv_total(uid) + m.qty > public.hf_capacity(cfg, (select storage_level from public.farms where player_id = uid))
    then return public.hf_err('storage_full'); end if;
  delete from public.market_offers where id = m.id;
  perform public.hf_inv_add(uid, m.item, m.qty, 'market_cancel');
  return public.hf_ok(jsonb_build_object('offers', public.hf_offers_of(uid), 'state', public.hf_state(uid)));
end $$;

create or replace function public.hf_market_buy(p_id bigint) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); m public.market_offers%rowtype; total int; coins int; s uuid;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  select seller into s from public.market_offers where id = p_id;
  if not found then return public.hf_err('gone'); end if;
  -- lock both wallets in a fixed order so two friends buying from each other cannot deadlock
  perform 1 from public.wallets where player_id in (uid, s) order by player_id for update;
  select * into m from public.market_offers where id = p_id for update;
  if not found then return public.hf_err('gone'); end if;
  if m.seller = uid or not public.hf_are_friends(uid, m.seller) then return public.hf_err('invalid'); end if;
  total := m.qty * m.unit_price;
  select w.coins into coins from public.wallets w where player_id = uid;
  if coins < total then return public.hf_err('no_coins'); end if;
  if public.hf_inv_total(uid) + m.qty > public.hf_capacity(cfg, (select storage_level from public.farms where player_id = uid))
    then return public.hf_err('storage_full'); end if;
  delete from public.market_offers where id = m.id;
  perform public.hf_money(uid, 'coins', -total, 'market_buy', m.id::text);
  perform public.hf_money(m.seller, 'coins', total, 'market_sale', m.id::text);
  perform public.hf_inv_add(uid, m.item, m.qty, 'market_buy');
  return public.hf_ok(jsonb_build_object('item', m.item, 'qty', m.qty, 'coins', total, 'state', public.hf_state(uid)));
end $$;

revoke execute on function public.hf_offers_of(uuid) from public, anon, authenticated;
revoke execute on function public.hf_market_offers(text), public.hf_market_list(text, int, int), public.hf_market_cancel(bigint),
  public.hf_market_buy(bigint) from public, anon;
grant execute on function public.hf_market_offers(text), public.hf_market_list(text, int, int), public.hf_market_cancel(bigint),
  public.hf_market_buy(bigint) to authenticated;

-- Unity version (0.7.1): "Delete my data" removes the login too (auth.users row, including a linked
-- email), not only the game data. Deleting the auth user cascades to profiles and every player table.
create or replace function public.hf_delete_my_data() returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid();
begin
  if uid is null then raise exception 'not authenticated'; end if;
  delete from public.profiles where id = uid;   -- cascades to every player table
  delete from auth.users where id = uid;        -- the login itself, with any linked email
  return public.hf_ok();
end $$;
revoke execute on function public.hf_delete_my_data() from public, anon;
grant execute on function public.hf_delete_my_data() to authenticated;

-- ------------------------------------------------------------------ self-test (two temporary players, deleted at the end)
create table if not exists public.zz_selftest(n serial primary key, step text, res jsonb, at timestamptz default now());
alter table public.zz_selftest enable row level security;
delete from public.zz_selftest;
do $$
declare u1 uuid := gen_random_uuid(); u2 uuid := gen_random_uuid(); c1 text; c2 text; r jsonb; oid bigint;
begin
  insert into auth.users (id, instance_id, aud, role, is_anonymous, created_at, updated_at) values
    (u1, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now()),
    (u2, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now());
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null); c1 := r->>'friend_code'; insert into public.zz_selftest(step,res) values ('init1', jsonb_build_object('code', c1, 'coins', r->'coins', 'inv', r->'inventory'));
  perform set_config('request.jwt.claims', json_build_object('sub', u2, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null); c2 := r->>'friend_code'; insert into public.zz_selftest(step,res) values ('init2', jsonb_build_object('code', c2, 'coins', r->'coins'));
  insert into public.zz_selftest(step,res) values ('visit_not_friend', public.hf_visit(c1));
  insert into public.zz_selftest(step,res) values ('req_bad', public.hf_friend_request('ZZZZZZ'));
  insert into public.zz_selftest(step,res) values ('req', public.hf_friend_request(lower(c1)));
  insert into public.zz_selftest(step,res) values ('req_again', public.hf_friend_request(c1));
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  insert into public.zz_selftest(step,res) values ('friends1', public.hf_friends());
  insert into public.zz_selftest(step,res) values ('accept', public.hf_friend_respond(c2, true));
  insert into public.zz_selftest(step,res) values ('visit', public.hf_visit(c2) - 'plots' - 'animals');
  insert into public.zz_selftest(step,res) values ('like', public.hf_social(c2, 'like'));
  insert into public.zz_selftest(step,res) values ('like_again', public.hf_social(c2, 'like'));
  insert into public.zz_selftest(step,res) values ('gift', public.hf_social(c2, 'gift'));
  insert into public.zz_selftest(step,res) values ('gift_again', public.hf_social(c2, 'gift'));
  perform public.hf_inv_add(u1, 'wheat', 5, 'selftest');
  insert into public.zz_selftest(step,res) values ('list_too_cheap', public.hf_market_list('wheat', 2, 1));
  r := public.hf_market_list('wheat', 2, 6); insert into public.zz_selftest(step,res) values ('list', r - 'state');
  oid := (r->'offers'->0->>'id')::bigint;
  perform set_config('request.jwt.claims', json_build_object('sub', u2, 'role', 'authenticated')::text, true);
  insert into public.zz_selftest(step,res) values ('friends2', public.hf_friends());
  insert into public.zz_selftest(step,res) values ('shop', public.hf_market_offers(c1));
  r := public.hf_market_buy(oid); insert into public.zz_selftest(step,res) values ('buy', (r - 'state') || jsonb_build_object('coins_after', r->'state'->'coins', 'wheat_after', r->'state'->'inventory'->'wheat'));
  insert into public.zz_selftest(step,res) values ('buy_again', public.hf_market_buy(oid));
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  insert into public.zz_selftest(step,res) values ('seller_after', jsonb_build_object('coins', public.hf_get_state()->'coins', 'offers', public.hf_market_offers(null)));
  insert into public.zz_selftest(step,res) values ('remove', public.hf_friend_respond(c2, false));
  insert into public.zz_selftest(step,res) values ('visit_after_remove', public.hf_visit(c2));
  delete from auth.users where id in (u1, u2);
  insert into public.zz_selftest(step,res) values ('cleanup', jsonb_build_object('players_left', (select count(*) from public.profiles where id in (u1, u2))));
end $$;
select step, res from public.zz_selftest order by n;
