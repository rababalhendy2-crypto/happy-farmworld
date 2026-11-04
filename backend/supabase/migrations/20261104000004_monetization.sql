-- Happy Farm World: purchases and rewarded ads, verified server-side.
-- * Real-money purchases are granted ONLY by the `verify-purchase` Edge Function after Google Play
--   confirms the purchase token (service role). The game client can never call the grant function.
-- * Ad rewards are granted ONLY by the `admob-ssv` Edge Function after AdMob's signed server-side
--   verification callback. Clicking "watch ad" alone grants nothing.
-- * Both are idempotent (unique purchase token / ad transaction id) and fully logged.

create table if not exists public.purchases (
  id bigint generated always as identity primary key,
  player_id uuid not null references public.profiles (id) on delete cascade,
  product_id text not null,
  purchase_token text not null unique,
  order_id text,
  status text not null default 'granted',
  created_at timestamptz not null default now()
);

create table if not exists public.ad_sessions (
  nonce uuid primary key default gen_random_uuid(),
  player_id uuid not null references public.profiles (id) on delete cascade,
  kind text not null check (kind in ('coins', 'speedup')),
  args jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default now(),
  used_at timestamptz
);

create table if not exists public.ad_rewards (
  transaction_id text primary key,
  player_id uuid not null references public.profiles (id) on delete cascade,
  kind text not null,
  coins int not null default 0,
  created_at timestamptz not null default now()
);
create index if not exists ad_rewards_player_day on public.ad_rewards (player_id, created_at);

alter table public.purchases enable row level security;
alter table public.ad_sessions enable row level security;
alter table public.ad_rewards enable row level security;
drop policy if exists "own purchases" on public.purchases;
create policy "own purchases" on public.purchases for select to authenticated using (player_id = auth.uid());
drop policy if exists "own ad rewards" on public.ad_rewards;
create policy "own ad rewards" on public.ad_rewards for select to authenticated using (player_id = auth.uid());
revoke insert, update, delete on public.purchases, public.ad_rewards from anon, authenticated;
revoke all on public.ad_sessions from anon, authenticated;
grant select on public.purchases, public.ad_rewards to authenticated;

create or replace function public.hf_ads_today(p uuid) returns int
language sql stable security definer set search_path = public as $$
  select count(*)::int from public.ad_rewards
   where player_id = p and created_at >= date_trunc('day', now() at time zone 'UTC') at time zone 'UTC' $$;

-- Client: "I want to watch an ad for <kind>". Returns a one-time nonce the ad SDK passes to AdMob
-- as custom_data, or an error if today's limit is reached. Grants nothing by itself.
create or replace function public.hf_ad_begin(p_kind text, p_args jsonb default '{}'::jsonb) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); n int; v_nonce uuid;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_kind not in ('coins', 'speedup') then return public.hf_err('invalid'); end if;
  n := public.hf_ads_today(uid);
  if n >= (cfg->'ads'->>'daily_limit')::int then return public.hf_err('ad_limit'); end if;
  insert into public.ad_sessions (player_id, kind, args) values (uid, p_kind, coalesce(p_args, '{}'::jsonb))
  returning ad_sessions.nonce into v_nonce;
  return public.hf_ok(jsonb_build_object('nonce', v_nonce, 'remaining', (cfg->'ads'->>'daily_limit')::int - n));
end $$;

create or replace function public.hf_ad_status() returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); n int;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  n := public.hf_ads_today(uid);
  return public.hf_ok(jsonb_build_object('watched', n, 'limit', (cfg->'ads'->>'daily_limit')::int,
    'next_coins', coalesce((cfg->'ads'->'coin_rewards'->>n)::int, 0)));
end $$;

-- Service role only (called by the admob-ssv Edge Function after verifying Google's signature).
create or replace function public.hf_grant_ad_reward(p_user uuid, p_nonce uuid, p_transaction_id text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare cfg jsonb := public.hf_cfg(); s public.ad_sessions%rowtype; n int; coins int := 0; idx int; secs int;
begin
  perform 1 from public.wallets where player_id = p_user for update;   -- serialise with game actions
  if exists (select 1 from public.ad_rewards where transaction_id = p_transaction_id) then
    return public.hf_ok(jsonb_build_object('duplicate', true));
  end if;
  select * into s from public.ad_sessions where nonce = p_nonce for update;
  if not found or s.player_id <> p_user then return public.hf_err('invalid'); end if;
  if s.used_at is not null then return public.hf_err('already_done'); end if;
  if s.created_at < now() - make_interval(secs => (cfg->'ads'->>'session_ttl_seconds')::int) then
    return public.hf_err('expired');
  end if;
  n := public.hf_ads_today(p_user);
  if n >= (cfg->'ads'->>'daily_limit')::int then return public.hf_err('ad_limit'); end if;
  if s.kind = 'coins' then
    coins := coalesce((cfg->'ads'->'coin_rewards'->>n)::int, 0);
    perform public.hf_money(p_user, 'coins', coins, 'rewarded_ad', p_transaction_id);
  else
    -- Speed up one growing crop by N seconds (moves its planted_at back in time).
    idx := (s.args->>'idx')::int; secs := (cfg->'ads'->>'speedup_seconds')::int;
    update public.farms
       set plots = jsonb_set(plots, array[idx::text, 'planted_at'], to_jsonb((plots->idx->>'planted_at')::double precision - secs))
     where player_id = p_user and idx is not null and idx >= 0 and idx < jsonb_array_length(plots)
       and plots->idx->>'crop' <> '';
    insert into public.transactions (player_id, transaction_type, reference_id) values (p_user, 'rewarded_ad_speedup', p_transaction_id);
  end if;
  insert into public.ad_rewards (transaction_id, player_id, kind, coins) values (p_transaction_id, p_user, s.kind, coins);
  update public.ad_sessions set used_at = now() where nonce = p_nonce;
  return public.hf_ok(jsonb_build_object('coins', coins, 'kind', s.kind));
end $$;

-- Service role only (called by the verify-purchase Edge Function after Google Play confirmed the token).
create or replace function public.hf_grant_purchase(p_user uuid, p_product text, p_token text, p_order text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare cfg jsonb := public.hf_cfg(); prod jsonb; g jsonb; k text; t double precision := public.hf_now();
begin
  perform 1 from public.wallets where player_id = p_user for update;
  if not found then return public.hf_err('no_player'); end if;
  if exists (select 1 from public.purchases where purchase_token = p_token) then
    return public.hf_ok(jsonb_build_object('duplicate', true));
  end if;
  select p into prod from jsonb_array_elements(cfg->'store'->'products') p where p->>'id' = p_product;
  if prod is null then return public.hf_err('invalid'); end if;
  if prod->>'type' = 'non_consumable' and exists (select 1 from public.purchases where player_id = p_user and product_id = p_product) then
    -- Already owned (e.g. restore on a new device with a new token): record, grant nothing twice.
    insert into public.purchases (player_id, product_id, purchase_token, order_id, status) values (p_user, p_product, p_token, p_order, 'restored');
    return public.hf_ok(jsonb_build_object('restored', true));
  end if;
  g := prod->'grants';
  perform public.hf_money(p_user, 'premium', coalesce((g->>'premium')::int, 0), 'purchase', p_product);
  perform public.hf_money(p_user, 'coins', coalesce((g->>'coins')::int, 0), 'purchase', p_product);
  for k in select jsonb_array_elements_text(coalesce(g->'decorations', '[]'::jsonb)) loop
    update public.farms set decorations = decorations || to_jsonb(k) where player_id = p_user and not (decorations ? k);
  end loop;
  for k in select jsonb_array_elements_text(coalesce(g->'animals', '[]'::jsonb)) loop
    update public.farms set animals = animals || jsonb_build_array(jsonb_build_object('type', k, 'status', 'hungry',
      'started_at', 0, 'hungry_since', t, 'happiness', 70))
     where player_id = p_user and not exists (select 1 from jsonb_array_elements(animals) x where x->>'type' = k);
  end loop;
  insert into public.purchases (player_id, product_id, purchase_token, order_id) values (p_user, p_product, p_token, p_order);
  return public.hf_ok(jsonb_build_object('product', p_product));
end $$;

create or replace function public.hf_my_purchases() returns jsonb
language sql stable security definer set search_path = public as $$
  select coalesce(jsonb_agg(distinct product_id), '[]'::jsonb) from public.purchases where player_id = auth.uid() $$;

revoke execute on function public.hf_ads_today(uuid), public.hf_grant_ad_reward(uuid, uuid, text),
  public.hf_grant_purchase(uuid, text, text, text) from public, anon, authenticated;
revoke execute on function public.hf_ad_begin(text, jsonb), public.hf_ad_status(), public.hf_my_purchases() from public, anon;
grant execute on function public.hf_ad_begin(text, jsonb), public.hf_ad_status(), public.hf_my_purchases() to authenticated;
-- On Supabase, Edge Functions use the service_role, which may execute the grant functions.
do $$ begin
  if exists (select 1 from pg_roles where rolname = 'service_role') then
    execute 'grant execute on function public.hf_grant_ad_reward(uuid, uuid, text), public.hf_grant_purchase(uuid, text, text, text) to service_role';
  end if;
end $$;
