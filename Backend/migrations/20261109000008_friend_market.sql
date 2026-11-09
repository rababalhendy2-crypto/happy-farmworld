-- Unity version (0.7.0): market between friends (design document: "offers to sell and buy within limits,
-- executed by the server"). Child-safe: only friends see and buy each other's offers; no free text.
--  * A player keeps up to cfg.market.max_offers offers. Listing takes the goods out of storage at once.
--  * Unit price must lie between the stall price (items.sell_price) and cfg.market.max_price_factor times it.
--  * Buying moves coins and goods in one transaction; the buyer needs coins and storage space.
--  * Cancelling an unsold offer returns the goods (needs storage space).

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
