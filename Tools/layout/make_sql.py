"""Builds Backend/migrations/20261109000011_farm_layout.sql from game_config.json (build_items, default_layout, build_area)."""
import json, os
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
cfg = json.load(open(os.path.join(root, 'Assets/Resources/Data/game_config.json'), encoding='utf-8'))
# the server only needs what it checks: category, price, level, limit, for sale
items = {k: {kk: v[kk] for kk in ('cat', 'cost', 'step', 'level', 'max', 'sale') if kk in v} for k, v in cfg['build_items'].items()}
block = json.dumps({'build_items': items, 'default_layout': cfg['default_layout'], 'build_area': cfg['build_area']}, separators=(',', ':'))
sql = r"""-- 0.8.0: the farm can be rearranged and built on.
--  * farms.layout holds every piece: {u (id), t (catalog id), x, z (metres), r (quarter turns), p (plot, fields only)}.
--    null = the farm was never changed: the default farm from cfg.default_layout.
--  * move_item  {u, x, z, r}       move or turn a piece (must stay inside cfg.build_area)
--  * buy_item   {id, u, x, z, r}   buy from cfg.build_items: level, limit and price are checked here;
--                                  a field also adds a new empty plot
--  * sell_item  {u}                remove something bought, half its base price back (fields and the farm's own buildings stay)
-- Safe to run twice.

alter table public.farms add column if not exists layout jsonb;

update public.game_config set data = data || '__BLOCK__'::jsonb, updated_at = now() where id = 1;

-- default farm, plus a field for every plot that has none (older farms with more plots)
create or replace function public.hf_default_layout(cfg jsonb, plots jsonb) returns jsonb
language plpgsql immutable set search_path = public as $$
declare lay jsonb := coalesce(cfg->'default_layout', '[]'::jsonb); i int; a jsonb := cfg->'build_area';
begin
  for i in 0 .. jsonb_array_length(coalesce(plots, '[]'::jsonb)) - 1 loop
    if not exists (select 1 from jsonb_array_elements(lay) e where e->>'t' = 'field' and (e->>'p')::int = i) then
      lay := lay || jsonb_build_array(jsonb_build_object('u', 'auto_p' || i, 't', 'field',
        'x', (a->>'x')::numeric + 3 + (i % 8) * 4.6, 'z', (a->>'z')::numeric + 3 + (i / 8) * 4.4, 'r', 0, 'p', i));
    end if;
  end loop;
  return lay;
end $$;

create or replace function public.hf_in_area(cfg jsonb, x numeric, z numeric) returns boolean
language sql immutable set search_path = public as $$
  select x is not null and z is not null
     and x >= (cfg->'build_area'->>'x')::numeric and x <= (cfg->'build_area'->>'x')::numeric + (cfg->'build_area'->>'w')::numeric
     and z >= (cfg->'build_area'->>'z')::numeric and z <= (cfg->'build_area'->>'z')::numeric + (cfg->'build_area'->>'d')::numeric $$;

do $$ begin
  if to_regprocedure('public.hf_action_v3(text,jsonb,uuid)') is null then
    alter function public.hf_action(text, jsonb, uuid) rename to hf_action_v3;
  end if;
end $$;

create or replace function public.hf_action(p_action text, p_args jsonb, p_key uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg();
        prev jsonb; res jsonb; f public.farms%rowtype; pr public.profiles%rowtype; w public.wallets%rowtype;
        lay jsonb; a jsonb := coalesce(p_args, '{}'::jsonb); def jsonb; v_u text; v_id text; i int; pos int := -1;
        x numeric; z numeric; r int; cnt int; dcnt int; price int; item jsonb;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_action not in ('move_item', 'buy_item', 'sell_item') then return public.hf_action_v3(p_action, p_args, p_key); end if;
  if p_key is null then return public.hf_err('invalid'); end if;
  select * into w from public.wallets where player_id = uid for update;
  if not found then return public.hf_err('no_player'); end if;
  select result into prev from public.processed_actions where player_id = uid and idempotency_key = p_key;
  if found then return prev || jsonb_build_object('replayed', true, 'state', public.hf_state(uid)); end if;

  select * into f from public.farms where player_id = uid for update;
  select * into pr from public.profiles where id = uid;
  lay := coalesce(f.layout, public.hf_default_layout(cfg, f.plots));
  v_u := a->>'u';
  if v_u is not null then
    for i in 0 .. jsonb_array_length(lay) - 1 loop
      if lay->i->>'u' = v_u then pos := i; end if;
    end loop;
  end if;
  begin
    x := round((a->>'x')::numeric, 2); z := round((a->>'z')::numeric, 2); r := coalesce((a->>'r')::int, 0);
  exception when others then x := null; z := null; r := -1;
  end;

  if p_action = 'move_item' then
    if pos < 0 and v_u ~ '^auto_p[0-9]+$' then
      -- a field the client added for a plot without one: adopt it
      i := substring(v_u from 7)::int;
      if i < jsonb_array_length(f.plots) and not exists (select 1 from jsonb_array_elements(lay) e where e->>'t' = 'field' and (e->>'p')::int = i) then
        lay := lay || jsonb_build_array(jsonb_build_object('u', v_u, 't', 'field', 'x', 0, 'z', 0, 'r', 0, 'p', i));
        pos := jsonb_array_length(lay) - 1;
      end if;
    end if;
    if pos < 0 or r not between 0 and 3 or not public.hf_in_area(cfg, x, z) then res := public.hf_err('invalid');
    else
      lay := jsonb_set(lay, array[pos::text], (lay->pos) || jsonb_build_object('x', x, 'z', z, 'r', r));
      update public.farms set layout = lay, updated_at = now() where player_id = uid;
      res := public.hf_ok();
    end if;

  elsif p_action = 'buy_item' then
    v_id := a->>'id'; def := cfg->'build_items'->v_id;
    select count(*) into cnt from jsonb_array_elements(lay) e where e->>'t' = v_id;
    select count(*) into dcnt from jsonb_array_elements(coalesce(cfg->'default_layout', '[]'::jsonb)) e where e->>'t' = v_id;
    if def is null or not coalesce((def->>'sale')::boolean, false) then res := public.hf_err('invalid');
    elsif v_u is null or v_u !~ '^[a-z0-9_]{1,40}$' or pos >= 0 then res := public.hf_err('invalid');
    elsif r not between 0 and 3 or not public.hf_in_area(cfg, x, z) then res := public.hf_err('invalid');
    elsif jsonb_array_length(lay) >= 400 then res := public.hf_err('full');
    elsif pr.level < coalesce((def->>'level')::int, 1) then res := public.hf_err('locked');
    elsif cnt >= coalesce((def->>'max')::int, 99) then res := public.hf_err('full');
    else
      price := (def->>'cost')::int + coalesce((def->>'step')::int, 0) * greatest(0, cnt - dcnt);
      if w.coins < price then res := public.hf_err('not_enough_coins');
      else
        perform public.hf_money(uid, 'coins', -price, 'buy_item', v_id);
        item := jsonb_build_object('u', v_u, 't', v_id, 'x', x, 'z', z, 'r', r);
        if def->>'cat' = 'field' then
          item := item || jsonb_build_object('p', jsonb_array_length(f.plots));
          update public.farms set plots = plots || '[{"crop": "", "planted_at": 0, "watered": false}]'::jsonb where player_id = uid;
        end if;
        update public.farms set layout = lay || jsonb_build_array(item), updated_at = now() where player_id = uid;
        res := public.hf_ok(jsonb_build_object('cost', price));
      end if;
    end if;

  else -- sell_item
    if pos < 0 then res := public.hf_err('invalid');
    else
      def := cfg->'build_items'->(lay->pos->>'t');
      if def is null or not coalesce((def->>'sale')::boolean, false) or def->>'cat' = 'field' then res := public.hf_err('invalid');
      else
        price := (def->>'cost')::int / 2;
        perform public.hf_money(uid, 'coins', price, 'sell_item', lay->pos->>'t');
        update public.farms set layout = lay - pos, updated_at = now() where player_id = uid;
        res := public.hf_ok(jsonb_build_object('coins', price));
      end if;
    end if;
  end if;

  insert into public.processed_actions (player_id, idempotency_key, action, result) values (uid, p_key, p_action, res);
  return res || jsonb_build_object('state', public.hf_state(uid));
end $$;

-- the player's own state and a friend's visit carry the layout (null = default farm)
create or replace function public.hf_state(p uuid) returns jsonb
language sql stable security definer set search_path = public as $$
  select public.hf_state_core(p) || jsonb_build_object(
    'orders', (select orders from public.farms where player_id = p),
    'friend_code', (select friend_code from public.profiles where id = p),
    'likes', (select likes from public.profiles where id = p),
    'layout', (select layout from public.farms where player_id = p)) $$;

create or replace function public.hf_visit(p_code text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid := public.hf_by_code(p_code); d int := public.hf_day(public.hf_now());
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if o is null or not public.hf_are_friends(uid, o) then return public.hf_err('not_found'); end if;
  return (select jsonb_build_object('ok', true,
      'code', pr.friend_code, 'display_name', pr.display_name, 'level', pr.level, 'likes', pr.likes,
      'plots', f.plots, 'animals', f.animals, 'workshops', f.workshops, 'storage_level', f.storage_level, 'layout', f.layout,
      'liked', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o and s.kind = 'like' and s.day = d),
      'gifted', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o and s.kind = 'gift' and s.day = d),
      'server_time', public.hf_now())
    from public.profiles pr join public.farms f on f.player_id = pr.id where pr.id = o);
end $$;

revoke execute on function public.hf_action_v3(text, jsonb, uuid) from public, anon, authenticated;
revoke execute on function public.hf_default_layout(jsonb, jsonb), public.hf_in_area(jsonb, numeric, numeric) from public, anon, authenticated;
revoke execute on function public.hf_action(text, jsonb, uuid) from public, anon;
grant execute on function public.hf_action(text, jsonb, uuid) to authenticated;
revoke execute on function public.hf_state(uuid) from public, anon, authenticated;
revoke execute on function public.hf_visit(text) from public, anon;
grant execute on function public.hf_visit(text) to authenticated;
""".replace('__BLOCK__', block.replace("'", "''"))
open(os.path.join(root, 'Backend/migrations/20261109000011_farm_layout.sql'), 'w', encoding='utf-8').write(sql)
print('ok', len(sql))
