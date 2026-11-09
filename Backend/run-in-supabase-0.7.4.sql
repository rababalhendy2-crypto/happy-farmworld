-- Farm World 0.7.4: gems finish a growing crop (rush_plot) + self-test. Paste everything into the Supabase SQL Editor and press Run.
begin;

update public.game_config set data = jsonb_set(data, '{gems}', '{"rush_seconds_per_gem": 300}'::jsonb), updated_at = now() where id = 1;

do $$ begin   -- safe to run twice: rename only once
  if to_regprocedure('public.hf_action_v2(text,jsonb,uuid)') is null then
    alter function public.hf_action(text, jsonb, uuid) rename to hf_action_v2;
  end if;
end $$;

create or replace function public.hf_action(p_action text, p_args jsonb, p_key uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); cfg jsonb := public.hf_cfg(); t double precision := public.hf_now();
        prev jsonb; res jsonb; f public.farms%rowtype; idx int; plot jsonb; grow double precision; left_s double precision;
        cost int; gems int;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_action <> 'rush_plot' then return public.hf_action_v2(p_action, p_args, p_key); end if;
  if p_key is null then return public.hf_err('invalid'); end if;
  perform 1 from public.wallets where player_id = uid for update;
  if not found then return public.hf_err('no_player'); end if;
  select result into prev from public.processed_actions where player_id = uid and idempotency_key = p_key;
  if found then return prev || jsonb_build_object('replayed', true, 'state', public.hf_state(uid)); end if;

  select * into f from public.farms where player_id = uid for update;
  idx := (coalesce(p_args, '{}'::jsonb)->>'idx')::int;
  if idx is null or idx < 0 or idx >= jsonb_array_length(f.plots) then res := public.hf_err('invalid');
  else
    plot := f.plots->idx;
    if coalesce(plot->>'crop', '') = '' then res := public.hf_err('invalid');
    else
      grow := (cfg->'crops'->(plot->>'crop')->>'grow_seconds')::double precision;
      left_s := (plot->>'planted_at')::double precision + grow - t;
      if left_s <= 0 then res := public.hf_ok(jsonb_build_object('cost', 0));
      else
        cost := greatest(1, ceil(left_s / coalesce((cfg->'gems'->>'rush_seconds_per_gem')::double precision, 300))::int);
        select premium_currency into gems from public.wallets where player_id = uid;
        if gems < cost then res := public.hf_err('not_enough_premium');
        else
          perform public.hf_money(uid, 'premium', -cost, 'rush_plot', plot->>'crop');
          update public.farms set plots = jsonb_set(plots, array[idx::text, 'planted_at'], to_jsonb(t - grow)), updated_at = now()
           where player_id = uid;
          res := public.hf_ok(jsonb_build_object('cost', cost));
        end if;
      end if;
    end if;
  end if;
  insert into public.processed_actions (player_id, idempotency_key, action, result) values (uid, p_key, p_action, res);
  return res || jsonb_build_object('state', public.hf_state(uid));
end $$;

revoke execute on function public.hf_action_v2(text, jsonb, uuid) from public, anon, authenticated;
revoke execute on function public.hf_action(text, jsonb, uuid) from public, anon;
grant execute on function public.hf_action(text, jsonb, uuid) to authenticated;
commit;

-- ------------------------------------------------------------------ self-test (one temporary player, deleted at the end)
create table if not exists public.zz_selftest(n serial primary key, step text, res jsonb, at timestamptz default now());
alter table public.zz_selftest enable row level security;
delete from public.zz_selftest;
do $$
declare u1 uuid := gen_random_uuid(); r jsonb;
begin
  insert into auth.users (id, instance_id, aud, role, is_anonymous, created_at, updated_at) values
    (u1, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now());
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null); insert into public.zz_selftest(step,res) values ('init', jsonb_build_object('coins', r->'coins', 'gems', r->'premium'));
  r := public.hf_action('claim_daily', '{}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('daily', (r - 'state') || jsonb_build_object('gems', r->'state'->'premium', 'daily', r->'state'->'daily_reward'));
  r := public.hf_action('claim_daily', '{}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('daily_again', r - 'state');
  r := public.hf_action('plant', '{"idx":0,"crop":"wheat"}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('plant', r - 'state');
  r := public.hf_action('harvest', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('harvest_too_early', r - 'state');
  r := public.hf_action('rush_plot', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('rush', (r - 'state') || jsonb_build_object('gems_after', r->'state'->'premium'));
  r := public.hf_action('harvest', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('harvest_after_rush', (r - 'state') || jsonb_build_object('wheat', r->'state'->'inventory'->'wheat'));
  r := public.hf_action('rush_plot', '{"idx":1}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('rush_empty', r - 'state');
  update public.wallets set premium_currency = 0 where player_id = u1;
  r := public.hf_action('plant', '{"idx":0,"crop":"wheat"}', gen_random_uuid());
  r := public.hf_action('rush_plot', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('rush_no_gems', r - 'state');
  delete from auth.users where id = u1;
  insert into public.zz_selftest(step,res) values ('cleanup', jsonb_build_object('players_left', (select count(*) from public.profiles where id = u1)));
end $$;
select step, res from public.zz_selftest order by n;
