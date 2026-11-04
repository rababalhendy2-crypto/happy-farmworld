-- Happy Farm World: server-authoritative game actions.
-- The client sends an ACTION (e.g. "harvest plot 3") plus an idempotency key, never a balance.
-- Each action: lock the player's wallet row (serialises all actions of one player),
-- validate with server time + server config, apply, log transactions, store the result
-- under the idempotency key. A retried request with the same key returns the stored result.
-- These rules mirror scripts/core/farm_rules.gd exactly (same config file).

-- ------------------------------------------------------------------ helpers (internal)
create or replace function public.hf_now() returns double precision
language sql stable as $$ select extract(epoch from clock_timestamp())::double precision $$;

create or replace function public.hf_cfg() returns jsonb
language sql stable security definer set search_path = public as $$ select data from public.game_config where id = 1 $$;

create or replace function public.hf_err(code text) returns jsonb
language sql immutable as $$ select jsonb_build_object('ok', false, 'error', code) $$;

create or replace function public.hf_ok(extra jsonb default '{}'::jsonb) returns jsonb
language sql immutable as $$ select jsonb_build_object('ok', true, 'error', '') || extra $$;

create or replace function public.hf_inv_count(p uuid, item text) returns int
language sql stable security definer set search_path = public as $$
  select coalesce((select quantity from public.inventory where player_id = p and item_id = item), 0) $$;

create or replace function public.hf_inv_total(p uuid) returns int
language sql stable security definer set search_path = public as $$
  select coalesce(sum(quantity), 0)::int from public.inventory where player_id = p $$;

create or replace function public.hf_capacity(cfg jsonb, storage_level int) returns int
language sql immutable as $$
  select (cfg->'economy'->'storage_levels'->
          least(greatest(storage_level, 0), jsonb_array_length(cfg->'economy'->'storage_levels') - 1)
         ->>'capacity')::int $$;

create or replace function public.hf_inv_add(p uuid, item text, qty int, reason text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if qty <= 0 then return; end if;
  insert into public.inventory (player_id, item_id, quantity) values (p, item, qty)
  on conflict (player_id, item_id) do update set quantity = public.inventory.quantity + excluded.quantity;
  insert into public.transactions (player_id, transaction_type, item_id, item_qty) values (p, reason, item, qty);
end $$;

create or replace function public.hf_inv_remove(p uuid, item text, qty int, reason text) returns boolean
language plpgsql security definer set search_path = public as $$
declare n int;
begin
  if qty <= 0 then return false; end if;
  update public.inventory set quantity = quantity - qty
   where player_id = p and item_id = item and quantity >= qty;
  get diagnostics n = row_count;
  if n = 0 then return false; end if;
  delete from public.inventory where player_id = p and item_id = item and quantity = 0;
  insert into public.transactions (player_id, transaction_type, item_id, item_qty) values (p, reason, item, -qty);
  return true;
end $$;

-- Changes coins or premium atomically; the CHECK (>= 0) constraint makes overspending impossible.
create or replace function public.hf_money(p uuid, currency text, delta int, reason text, ref text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if delta = 0 then return; end if;
  if currency = 'coins' then
    update public.wallets set coins = coins + delta, updated_at = now() where player_id = p;
  else
    update public.wallets set premium_currency = premium_currency + delta, updated_at = now() where player_id = p;
  end if;
  insert into public.transactions (player_id, transaction_type, currency, amount, reference_id)
  values (p, reason, currency, delta, ref);
end $$;

create or replace function public.hf_xp_for_next(cfg jsonb, lvl int) returns int
language plpgsql immutable as $$
declare t jsonb := cfg->'economy'->'level_xp'; n int := jsonb_array_length(t); last int; step int;
begin
  if lvl < n then return (t->>lvl)::int; end if;
  last := (t->>(n - 1))::int;
  step := case when n > 1 then last - (t->>(n - 2))::int else 100 end;
  return last + step * (lvl - n + 1);
end $$;

create or replace function public.hf_add_xp(p uuid, cfg jsonb, amount int) returns int
language plpgsql security definer set search_path = public as $$
declare lvl int; xp int; gained int := 0;
begin
  if amount <= 0 then return 0; end if;
  update public.profiles set experience = experience + amount, updated_at = now()
   where id = p returning level, experience into lvl, xp;
  while xp >= public.hf_xp_for_next(cfg, lvl) loop
    lvl := lvl + 1;
    gained := gained + 1;
    perform public.hf_money(p, 'coins', coalesce((cfg->'economy'->>'level_reward_coins')::int, 0), 'level_reward', lvl::text);
    perform public.hf_money(p, 'premium', coalesce((cfg->'economy'->>'level_reward_premium')::int, 0), 'level_reward', lvl::text);
  end loop;
  if gained > 0 then
    update public.profiles set level = lvl where id = p;
  end if;
  return gained;
end $$;

create or replace function public.hf_stat(p uuid, key text, delta int) returns void
language sql security definer set search_path = public as $$
  update public.profiles set stats = jsonb_set(stats, array[key], to_jsonb(coalesce((stats->>key)::int, 0) + delta))
   where id = p $$;

-- ------------------------------------------------------------------ missions (mirror of FarmRules)
create or replace function public.hf_day(t double precision) returns int
language sql immutable as $$ select floor(t / 86400.0)::int $$;

create or replace function public.hf_week(t double precision) returns int
language sql immutable as $$ select floor((public.hf_day(t) + 3) / 7.0)::int $$;

create or replace function public.hf_active_events(cfg jsonb, t double precision) returns setof jsonb
language sql stable as $$
  select e from jsonb_array_elements(coalesce(cfg->'missions'->'events', '[]'::jsonb)) e
   where e->>'from' <= to_char(to_timestamp(t) at time zone 'UTC', 'YYYY-MM-DD')
     and to_char(to_timestamp(t) at time zone 'UTC', 'YYYY-MM-DD') <= e->>'to' $$;

create or replace function public.hf_mission_kinds(cfg jsonb, t double precision) returns text[]
language sql stable as $$
  select array['daily', 'weekly'] || coalesce(array_agg('event:' || (e->>'id')), '{}')
    from public.hf_active_events(cfg, t) e $$;

create or replace function public.hf_period(kind text, t double precision) returns int
language sql immutable as $$
  select case kind when 'daily' then public.hf_day(t) when 'weekly' then public.hf_week(t) else 0 end $$;

create or replace function public.hf_active_missions(cfg jsonb, kind text, t double precision) returns jsonb
language plpgsql stable as $$
declare pool jsonb; n int; cnt int; period int; out jsonb := '[]'::jsonb; k int; e jsonb;
begin
  if kind like 'event:%' then
    for e in select * from public.hf_active_events(cfg, t) loop
      if 'event:' || (e->>'id') = kind then return coalesce(e->'missions', '[]'::jsonb); end if;
    end loop;
    return '[]'::jsonb;
  end if;
  pool := cfg->'missions'->kind;
  if pool is null or jsonb_array_length(pool) = 0 then return '[]'::jsonb; end if;
  n := jsonb_array_length(pool);
  cnt := least(coalesce((cfg->'missions'->>(kind || '_count'))::int, 1), n);
  period := public.hf_period(kind, t);
  for k in 0 .. cnt - 1 loop
    out := out || jsonb_build_array(pool->((period * cnt + k) % n));
  end loop;
  return out;
end $$;

create or replace function public.hf_bucket(m jsonb, kind text, t double precision) returns jsonb
language sql immutable as $$
  select case when m->kind is null or (m->kind->>'period')::int <> public.hf_period(kind, t)
    then jsonb_build_object('period', public.hf_period(kind, t), 'progress', '{}'::jsonb, 'claimed', '[]'::jsonb)
    else m->kind end $$;

create or replace function public.hf_track(p uuid, cfg jsonb, ev text, it text, qty int, t double precision) returns void
language plpgsql security definer set search_path = public as $$
declare m jsonb; b jsonb; kind text; mis jsonb; cur int;
begin
  select missions into m from public.profiles where id = p;
  foreach kind in array public.hf_mission_kinds(cfg, t) loop
    b := public.hf_bucket(m, kind, t);
    for mis in select * from jsonb_array_elements(public.hf_active_missions(cfg, kind, t)) loop
      if mis->>'event' = ev and (mis->>'item' = '' or mis->>'item' = it) then
        cur := coalesce((b->'progress'->>(mis->>'id'))::int, 0);
        b := jsonb_set(b, array['progress', mis->>'id'], to_jsonb(least((mis->>'count')::int, cur + qty)));
      end if;
    end loop;
    m := m || jsonb_build_object(kind, b);
  end loop;
  update public.profiles set missions = m where id = p;
end $$;

-- Rewards from missions / gifts. Items are added even above storage capacity: a reward is never lost.
create or replace function public.hf_grant_reward(p uuid, cfg jsonb, reward jsonb, reason text) returns int
language plpgsql security definer set search_path = public as $$
declare k text;
begin
  perform public.hf_money(p, 'coins', coalesce((reward->>'coins')::int, 0), reason, null);
  perform public.hf_money(p, 'premium', coalesce((reward->>'premium')::int, 0), reason, null);
  for k in select jsonb_object_keys(coalesce(reward->'items', '{}'::jsonb)) loop
    perform public.hf_inv_add(p, k, (reward->'items'->>k)::int, reason);
  end loop;
  return public.hf_add_xp(p, cfg, coalesce((reward->>'xp')::int, 0));
end $$;

-- Full player state in the same shape the Godot client uses (FarmRules state).
create or replace function public.hf_state(p uuid) returns jsonb
language sql stable security definer set search_path = public as $$
  select jsonb_build_object(
    'version', 1,
    'coins', w.coins, 'premium', w.premium_currency,
    'xp', pr.experience, 'level', pr.level,
    'display_name', pr.display_name,
    'inventory', coalesce((select jsonb_object_agg(item_id, quantity) from public.inventory where player_id = p), '{}'::jsonb),
    'plots', f.plots, 'animals', f.animals, 'workshops', f.workshops,
    'expansions', f.expansions, 'decorations', f.decorations, 'storage_level', f.storage_level,
    'stats', pr.stats,
    'missions', pr.missions, 'daily_reward', pr.daily_reward, 'achievements', pr.achievements,
    'server_time', public.hf_now())
  from public.profiles pr join public.wallets w on w.player_id = pr.id join public.farms f on f.player_id = pr.id
  where pr.id = p $$;

-- ------------------------------------------------------------------ public RPCs

-- Creates the player's farm on first login. Safe to call every launch.
create or replace function public.hf_init_player(p_display_name text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); t double precision := public.hf_now();
        plots jsonb := '[]'::jsonb; animals jsonb := '[]'::jsonb; k text; i int;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if exists (select 1 from public.profiles where id = uid) then
    return public.hf_state(uid);
  end if;
  for i in 1 .. coalesce((cfg->'economy'->>'starting_plots')::int, 6) loop
    plots := plots || jsonb_build_array(jsonb_build_object('crop', '', 'planted_at', 0, 'watered', false));
  end loop;
  for k in select key from jsonb_each(cfg->'animals') where coalesce((value->>'special')::boolean, false) = false loop
    animals := animals || jsonb_build_array(jsonb_build_object('type', k, 'status', 'hungry', 'started_at', 0,
      'hungry_since', t, 'happiness', coalesce((cfg->'animal_care'->>'start_happiness')::int, 70)));
  end loop;
  insert into public.profiles (id, display_name) values (uid, coalesce(nullif(p_display_name, ''), 'Farmer'));
  insert into public.wallets (player_id, coins, premium_currency)
  values (uid, (cfg->'economy'->>'start_coins')::int, coalesce((cfg->'economy'->>'start_premium')::int, 0));
  insert into public.farms (player_id, plots, animals) values (uid, plots, animals);
  for k in select jsonb_object_keys(coalesce(cfg->'economy'->'start_inventory', '{}'::jsonb)) loop
    perform public.hf_inv_add(uid, k, (cfg->'economy'->'start_inventory'->>k)::int, 'starter_pack');
  end loop;
  insert into public.transactions (player_id, transaction_type, currency, amount)
  values (uid, 'account_created', 'coins', (cfg->'economy'->>'start_coins')::int);
  return public.hf_state(uid);
end $$;

create or replace function public.hf_get_state() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not authenticated'; end if;
  return public.hf_state(auth.uid());
end $$;

create or replace function public.hf_set_display_name(p_name text) returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not authenticated'; end if;
  if p_name is null or char_length(p_name) not between 2 and 16 or p_name !~ '^[[:alnum:] _.-]+$' then
    return public.hf_err('invalid');
  end if;
  update public.profiles set display_name = p_name, updated_at = now() where id = auth.uid();
  return public.hf_ok();
end $$;

-- Privacy: a player can delete all their game data.
create or replace function public.hf_delete_my_data() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not authenticated'; end if;
  delete from public.profiles where id = auth.uid();  -- cascades to every player table
  return public.hf_ok();
end $$;

-- The single entry point for every game action.
create or replace function public.hf_action(p_action text, p_args jsonb, p_key uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); t double precision := public.hf_now();
  w public.wallets%rowtype; f public.farms%rowtype; pr public.profiles%rowtype;
  res jsonb; prev jsonb;
  idx int; v_id text; item text; qty int; def jsonb; plot jsonb; a jsonb; cost int; prem int;
  grow double precision; levels int; xp int; h double precision; care jsonb; jobs jsonb; job jsonb; k text; kind text;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_key is null or p_action is null then return public.hf_err('invalid'); end if;
  p_args := coalesce(p_args, '{}'::jsonb);

  -- Lock first, then check idempotency, so two concurrent retries cannot both apply.
  select * into w from public.wallets where player_id = uid for update;
  if not found then return public.hf_err('no_player'); end if;
  select result into prev from public.processed_actions where player_id = uid and idempotency_key = p_key;
  if found then
    return prev || jsonb_build_object('replayed', true, 'state', public.hf_state(uid));
  end if;
  select * into f from public.farms where player_id = uid for update;
  select * into pr from public.profiles where profiles.id = uid;

  case p_action
  -- ---------------------------------------------------------------- crops
  when 'plant' then
    idx := (p_args->>'idx')::int; v_id := p_args->>'crop'; def := cfg->'crops'->v_id;
    if idx is null or idx < 0 or idx >= jsonb_array_length(f.plots) or def is null then res := public.hf_err('invalid');
    elsif f.plots->idx->>'crop' <> '' then res := public.hf_err('busy');
    elsif pr.level < (def->>'unlock_level')::int then res := public.hf_err('locked');
    elsif w.coins < (def->>'seed_price')::int then res := public.hf_err('not_enough_coins');
    else
      cost := (def->>'seed_price')::int;
      perform public.hf_money(uid, 'coins', -cost, 'seed_purchase', v_id);
      update public.farms set plots = jsonb_set(plots, array[idx::text],
        jsonb_build_object('crop', v_id, 'planted_at', t, 'watered', false)), updated_at = now() where player_id = uid;
      perform public.hf_track(uid, cfg, 'plant', v_id, 1, t);
      res := public.hf_ok(jsonb_build_object('cost', cost));
    end if;

  when 'water' then
    idx := (p_args->>'idx')::int;
    if idx is null or idx < 0 or idx >= jsonb_array_length(f.plots) then res := public.hf_err('invalid');
    else
      plot := f.plots->idx;
      if plot->>'crop' = '' then res := public.hf_err('not_ready');
      else
        grow := (cfg->'crops'->(plot->>'crop')->>'grow_seconds')::double precision;
        if (plot->>'watered')::boolean then grow := grow * (1 - (cfg->'economy'->>'water_speedup')::double precision); end if;
        if t - (plot->>'planted_at')::double precision >= grow then res := public.hf_err('not_ready');
        elsif (plot->>'watered')::boolean then res := public.hf_err('already_done');
        else
          update public.farms set plots = jsonb_set(plots, array[idx::text, 'watered'], 'true'::jsonb), updated_at = now()
           where player_id = uid;
          res := public.hf_ok();
        end if;
      end if;
    end if;

  when 'harvest' then
    idx := (p_args->>'idx')::int;
    if idx is null or idx < 0 or idx >= jsonb_array_length(f.plots) then res := public.hf_err('invalid');
    else
      plot := f.plots->idx; v_id := plot->>'crop';
      if v_id = '' then res := public.hf_err('not_ready');
      else
        def := cfg->'crops'->v_id;
        grow := (def->>'grow_seconds')::double precision;
        if (plot->>'watered')::boolean then grow := grow * (1 - (cfg->'economy'->>'water_speedup')::double precision); end if;
        qty := (def->>'yield')::int;
        if t - (plot->>'planted_at')::double precision < grow then res := public.hf_err('not_ready');
        elsif public.hf_inv_total(uid) + qty > public.hf_capacity(cfg, f.storage_level) then res := public.hf_err('storage_full');
        else
          perform public.hf_inv_add(uid, v_id, qty, 'harvest');
          update public.farms set plots = jsonb_set(plots, array[idx::text],
            '{"crop": "", "planted_at": 0, "watered": false}'::jsonb), updated_at = now() where player_id = uid;
          perform public.hf_stat(uid, 'harvests', 1);
          perform public.hf_track(uid, cfg, 'harvest', v_id, qty, t);
          xp := (def->>'xp')::int;
          levels := public.hf_add_xp(uid, cfg, xp);
          res := public.hf_ok(jsonb_build_object('item', v_id, 'qty', qty, 'xp', xp, 'levels', levels));
        end if;
      end if;
    end if;

  -- ---------------------------------------------------------------- animals
  when 'feed' then
    idx := (p_args->>'idx')::int;
    if idx is null or idx < 0 or idx >= jsonb_array_length(f.animals) then res := public.hf_err('invalid');
    else
      a := f.animals->idx; def := cfg->'animals'->(a->>'type'); care := cfg->'animal_care';
      if a->>'status' <> 'hungry' then res := public.hf_err('busy');
      elsif pr.level < (def->>'unlock_level')::int then res := public.hf_err('locked');
      elsif not public.hf_inv_remove(uid, def->>'feed_item', (def->>'feed_qty')::int, 'animal_feed') then
        res := public.hf_err('missing_items');
      else
        h := (a->>'happiness')::double precision
             - greatest(0, t - (a->>'hungry_since')::double precision) / 3600.0 * (care->>'decay_per_hour_hungry')::double precision;
        h := greatest(h, (care->>'min_happiness')::double precision) + (care->>'feed_bonus')::double precision;
        h := least(greatest(h, 0), 100);
        a := a || jsonb_build_object('status', 'producing', 'started_at', t, 'happiness', h::int);
        update public.farms set animals = jsonb_set(animals, array[idx::text], a), updated_at = now() where player_id = uid;
        perform public.hf_track(uid, cfg, 'feed', a->>'type', 1, t);
        res := public.hf_ok(jsonb_build_object('item', def->>'feed_item', 'qty', (def->>'feed_qty')::int));
      end if;
    end if;

  when 'collect' then
    idx := (p_args->>'idx')::int;
    if idx is null or idx < 0 or idx >= jsonb_array_length(f.animals) then res := public.hf_err('invalid');
    else
      a := f.animals->idx; def := cfg->'animals'->(a->>'type'); care := cfg->'animal_care';
      if a->>'status' = 'hungry'
         or t - (a->>'started_at')::double precision < (def->>'produce_seconds')::double precision then
        res := public.hf_err('not_ready');
      else
        qty := (def->>'product_qty')::int;
        if (a->>'happiness')::int >= (care->>'bonus_threshold')::int then qty := qty + (care->>'bonus_qty')::int; end if;
        if public.hf_inv_total(uid) + qty > public.hf_capacity(cfg, f.storage_level) then res := public.hf_err('storage_full');
        else
          perform public.hf_inv_add(uid, def->>'product', qty, 'animal_product');
          a := a || jsonb_build_object('status', 'hungry', 'hungry_since', t);
          update public.farms set animals = jsonb_set(animals, array[idx::text], a), updated_at = now() where player_id = uid;
          perform public.hf_stat(uid, 'collected', 1);
          perform public.hf_track(uid, cfg, 'collect', def->>'product', qty, t);
          xp := (def->>'xp')::int;
          levels := public.hf_add_xp(uid, cfg, xp);
          res := public.hf_ok(jsonb_build_object('item', def->>'product', 'qty', qty, 'xp', xp, 'levels', levels));
        end if;
      end if;
    end if;

  -- ---------------------------------------------------------------- market
  when 'sell' then
    item := p_args->>'item'; qty := (p_args->>'qty')::int; def := cfg->'items'->item;
    if def is null or qty is null or qty <= 0 then res := public.hf_err('invalid');
    elsif not public.hf_inv_remove(uid, item, qty, 'sell') then res := public.hf_err('missing_items');
    else
      cost := (def->>'sell_price')::int * qty;
      perform public.hf_money(uid, 'coins', cost, 'sell', item);
      perform public.hf_stat(uid, 'sold', qty);
      perform public.hf_track(uid, cfg, 'sell', item, qty, t);
      xp := case when coalesce((cfg->'economy'->>'sell_xp_per_coins')::int, 0) > 0
                 then cost / (cfg->'economy'->>'sell_xp_per_coins')::int else 0 end;
      levels := public.hf_add_xp(uid, cfg, xp);
      res := public.hf_ok(jsonb_build_object('coins', cost, 'xp', xp, 'levels', levels));
    end if;

  -- ---------------------------------------------------------------- workshops
  when 'buy_workshop' then
    v_id := p_args->>'id'; def := cfg->'workshops'->v_id;
    if def is null then res := public.hf_err('invalid');
    elsif f.workshops ? v_id then res := public.hf_err('already_done');
    elsif pr.level < (def->>'unlock_level')::int then res := public.hf_err('locked');
    elsif w.coins < (def->>'cost')::int then res := public.hf_err('not_enough_coins');
    else
      cost := (def->>'cost')::int;
      perform public.hf_money(uid, 'coins', -cost, 'buy_workshop', v_id);
      update public.farms set workshops = workshops || jsonb_build_object(v_id, jsonb_build_object('jobs', '[]'::jsonb)),
        updated_at = now() where player_id = uid;
      res := public.hf_ok(jsonb_build_object('cost', cost));
    end if;

  when 'start_job' then
    v_id := p_args->>'ws'; item := p_args->>'recipe'; def := cfg->'workshops'->v_id;
    if def is null or not (f.workshops ? v_id) or not ((def->'recipes') ? item) then res := public.hf_err('invalid');
    elsif jsonb_array_length(f.workshops->v_id->'jobs') >= (def->>'slots')::int then res := public.hf_err('busy');
    else
      res := null;
      for k in select jsonb_object_keys(cfg->'recipes'->item->'inputs') loop
        if public.hf_inv_count(uid, k) < (cfg->'recipes'->item->'inputs'->>k)::int then res := public.hf_err('missing_items'); end if;
      end loop;
      if res is null then
        for k in select jsonb_object_keys(cfg->'recipes'->item->'inputs') loop
          perform public.hf_inv_remove(uid, k, (cfg->'recipes'->item->'inputs'->>k)::int, 'production_input');
        end loop;
        update public.farms set workshops = jsonb_set(workshops, array[v_id, 'jobs'],
          (workshops->v_id->'jobs') || jsonb_build_array(jsonb_build_object('recipe', item, 'started_at', t))),
          updated_at = now() where player_id = uid;
        res := public.hf_ok(jsonb_build_object('recipe', item));
      end if;
    end if;

  when 'collect_job' then
    v_id := p_args->>'ws'; idx := (p_args->>'job')::int;
    if not (f.workshops ? v_id) or idx is null or idx < 0 or idx >= jsonb_array_length(f.workshops->v_id->'jobs') then
      res := public.hf_err('invalid');
    else
      job := f.workshops->v_id->'jobs'->idx; def := cfg->'recipes'->(job->>'recipe');
      qty := (def->>'qty')::int;
      if t - (job->>'started_at')::double precision < (def->>'seconds')::double precision then res := public.hf_err('not_ready');
      elsif public.hf_inv_total(uid) + qty > public.hf_capacity(cfg, f.storage_level) then res := public.hf_err('storage_full');
      else
        perform public.hf_inv_add(uid, job->>'recipe', qty, 'production_output');
        update public.farms set workshops = jsonb_set(workshops, array[v_id, 'jobs'], (workshops->v_id->'jobs') - idx),
          updated_at = now() where player_id = uid;
        perform public.hf_stat(uid, 'produced', qty);
        perform public.hf_track(uid, cfg, 'produce', job->>'recipe', qty, t);
        xp := (def->>'xp')::int;
        levels := public.hf_add_xp(uid, cfg, xp);
        res := public.hf_ok(jsonb_build_object('item', job->>'recipe', 'qty', qty, 'xp', xp, 'levels', levels, 'ws', v_id));
      end if;
    end if;

  -- ---------------------------------------------------------------- land, storage, decorations
  when 'buy_expansion' then
    v_id := p_args->>'id';
    select e into def from jsonb_array_elements(cfg->'expansions') e where e->>'id' = v_id;
    if def is null then res := public.hf_err('invalid');
    elsif f.expansions ? v_id then res := public.hf_err('already_done');
    elsif pr.level < (def->>'unlock_level')::int then res := public.hf_err('locked');
    elsif w.coins < (def->>'cost')::int then res := public.hf_err('not_enough_coins');
    else
      cost := (def->>'cost')::int;
      perform public.hf_money(uid, 'coins', -cost, 'buy_expansion', v_id);
      plot := '[]'::jsonb;
      for idx in 1 .. (def->>'plots')::int loop
        plot := plot || '[{"crop": "", "planted_at": 0, "watered": false}]'::jsonb;
      end loop;
      update public.farms set plots = plots || plot, expansions = expansions || to_jsonb(v_id), updated_at = now()
       where player_id = uid;
      res := public.hf_ok(jsonb_build_object('cost', cost));
    end if;

  when 'upgrade_storage' then
    idx := f.storage_level + 1;
    if idx >= jsonb_array_length(cfg->'economy'->'storage_levels') then res := public.hf_err('already_done');
    else
      cost := (cfg->'economy'->'storage_levels'->idx->>'cost')::int;
      if w.coins < cost then res := public.hf_err('not_enough_coins');
      else
        perform public.hf_money(uid, 'coins', -cost, 'upgrade_storage', idx::text);
        update public.farms set storage_level = idx, updated_at = now() where player_id = uid;
        res := public.hf_ok(jsonb_build_object('cost', cost, 'capacity', public.hf_capacity(cfg, idx)));
      end if;
    end if;

  when 'buy_decoration' then
    v_id := p_args->>'id'; def := cfg->'decorations'->v_id;
    if def is null then res := public.hf_err('invalid');
    elsif f.decorations ? v_id then res := public.hf_err('already_done');
    elsif pr.level < (def->>'unlock_level')::int then res := public.hf_err('locked');
    else
      cost := coalesce((def->>'cost')::int, 0); prem := coalesce((def->>'premium')::int, 0);
      if w.coins < cost then res := public.hf_err('not_enough_coins');
      elsif w.premium_currency < prem then res := public.hf_err('not_enough_premium');
      else
        perform public.hf_money(uid, 'coins', -cost, 'buy_decoration', v_id);
        perform public.hf_money(uid, 'premium', -prem, 'buy_decoration', v_id);
        update public.farms set decorations = decorations || to_jsonb(v_id), updated_at = now() where player_id = uid;
        res := public.hf_ok(jsonb_build_object('cost', cost, 'premium', prem));
      end if;
    end if;

  when 'buy_special_animal' then
    v_id := p_args->>'id'; def := cfg->'special_animals'->v_id;
    if def is null then res := public.hf_err('invalid');
    elsif exists (select 1 from jsonb_array_elements(f.animals) x where x->>'type' = v_id) then res := public.hf_err('already_done');
    elsif pr.level < coalesce((def->>'unlock_level')::int, 1) then res := public.hf_err('locked');
    elsif w.premium_currency < (def->>'premium')::int then res := public.hf_err('not_enough_premium');
    else
      perform public.hf_money(uid, 'premium', -(def->>'premium')::int, 'buy_special_animal', v_id);
      update public.farms set animals = animals || jsonb_build_array(jsonb_build_object('type', v_id, 'status', 'hungry',
        'started_at', 0, 'hungry_since', t, 'happiness', coalesce((cfg->'animal_care'->>'start_happiness')::int, 70))),
        updated_at = now() where player_id = uid;
      res := public.hf_ok(jsonb_build_object('premium', (def->>'premium')::int));
    end if;

  -- ---------------------------------------------------------------- missions & gifts
  when 'claim_mission' then
    kind := p_args->>'kind'; v_id := p_args->>'id'; def := null;
    select m into def from jsonb_array_elements(public.hf_active_missions(cfg, coalesce(kind, ''), t)) m where m->>'id' = v_id;
    if def is null then res := public.hf_err('invalid');
    else
      jobs := public.hf_bucket(pr.missions, kind, t);
      if jobs->'claimed' ? v_id then res := public.hf_err('already_done');
      elsif coalesce((jobs->'progress'->>v_id)::int, 0) < (def->>'count')::int then res := public.hf_err('not_ready');
      else
        jobs := jsonb_set(jobs, '{claimed}', (jobs->'claimed') || to_jsonb(v_id));
        update public.profiles set missions = missions || jsonb_build_object(kind, jobs) where profiles.id = uid;
        levels := public.hf_grant_reward(uid, cfg, def->'reward', 'mission_reward');
        res := public.hf_ok(jsonb_build_object('reward', def->'reward', 'levels', levels));
      end if;
    end if;

  when 'claim_achievement' then
    v_id := p_args->>'id'; def := null;
    select m into def from jsonb_array_elements(cfg->'missions'->'achievements') m where m->>'id' = v_id;
    if def is null then res := public.hf_err('invalid');
    elsif pr.achievements ? v_id then res := public.hf_err('already_done');
    elsif coalesce((pr.stats->>(def->>'stat'))::int, 0) < (def->>'count')::int then res := public.hf_err('not_ready');
    else
      update public.profiles set achievements = achievements || to_jsonb(v_id) where profiles.id = uid;
      levels := public.hf_grant_reward(uid, cfg, def->'reward', 'achievement_reward');
      res := public.hf_ok(jsonb_build_object('reward', def->'reward', 'levels', levels));
    end if;

  when 'claim_daily' then
    if coalesce((pr.daily_reward->>'last_day')::int, -1) >= public.hf_day(t) then res := public.hf_err('already_done');
    else
      idx := coalesce((pr.daily_reward->>'step')::int, 0);
      def := cfg->'daily_rewards'->(idx % jsonb_array_length(cfg->'daily_rewards'));
      update public.profiles set daily_reward = jsonb_build_object('last_day', public.hf_day(t), 'step', idx + 1)
       where profiles.id = uid;
      levels := public.hf_grant_reward(uid, cfg, def, 'daily_gift');
      res := public.hf_ok(jsonb_build_object('reward', def, 'step', idx, 'levels', levels));
    end if;

  else
    res := public.hf_err('invalid');
  end case;

  insert into public.processed_actions (player_id, idempotency_key, action, result) values (uid, p_key, p_action, res);
  return res || jsonb_build_object('state', public.hf_state(uid));
end $$;

-- ------------------------------------------------------------------ permissions
-- Internal helpers: nobody but the definer-owned RPCs may call them.
revoke execute on function public.hf_cfg(), public.hf_inv_count(uuid, text), public.hf_inv_total(uuid),
  public.hf_inv_add(uuid, text, int, text), public.hf_inv_remove(uuid, text, int, text),
  public.hf_money(uuid, text, int, text, text), public.hf_add_xp(uuid, jsonb, int),
  public.hf_stat(uuid, text, int), public.hf_state(uuid),
  public.hf_track(uuid, jsonb, text, text, int, double precision),
  public.hf_grant_reward(uuid, jsonb, jsonb, text) from public, anon, authenticated;
-- Public RPCs: signed-in players only (anonymous sign-in counts as signed in).
revoke execute on function public.hf_init_player(text), public.hf_get_state(), public.hf_action(text, jsonb, uuid),
  public.hf_set_display_name(text), public.hf_delete_my_data() from public, anon;
grant execute on function public.hf_init_player(text), public.hf_get_state(), public.hf_action(text, jsonb, uuid),
  public.hf_set_display_name(text), public.hf_delete_my_data() to authenticated;
