-- Acceptance gates from the design document (page 8), server side. Run on a test copy of the database:
-- early harvest, negative balance, full storage, repeated request (same key), double purchase on the market,
-- buying with too few coins, visiting someone who is not a friend. Results in zz_selftest.
create table if not exists public.zz_selftest(n serial primary key, step text, res jsonb, at timestamptz default now());
delete from public.zz_selftest;
do $$
declare u1 uuid := gen_random_uuid(); u2 uuid := gen_random_uuid(); r jsonb; k uuid := gen_random_uuid(); c1 text; c2 text; oid bigint;
begin
  insert into auth.users (id, instance_id, aud, role, is_anonymous, created_at, updated_at) values
    (u1, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now()),
    (u2, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now());
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null); c1 := r->>'friend_code';
  insert into public.zz_selftest(step,res) values ('start', jsonb_build_object('coins', r->'coins', 'inv', r->'inventory'));
  -- early harvest
  r := public.hf_action('plant', '{"idx":0,"crop":"wheat"}', gen_random_uuid());
  r := public.hf_action('harvest', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('early_harvest_refused', r - 'state');
  -- same request twice (offline queue resends): must only count once
  r := public.hf_action('plant', '{"idx":1,"crop":"wheat"}', k);
  r := public.hf_action('plant', '{"idx":1,"crop":"wheat"}', k);
  insert into public.zz_selftest(step,res) values ('replay_same_key', jsonb_build_object('replayed', r->'replayed', 'coins', r->'state'->'coins'));
  -- negative balance: plant with no coins
  update public.wallets set coins = 0 where player_id = u1;
  r := public.hf_action('plant', '{"idx":2,"crop":"wheat"}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('plant_without_coins', (r - 'state') || jsonb_build_object('coins', r->'state'->'coins'));
  begin
    update public.wallets set coins = -5 where player_id = u1;
    insert into public.zz_selftest(step,res) values ('negative_coins_possible', '{"bad": true}');
  exception when check_violation then insert into public.zz_selftest(step,res) values ('negative_coins_blocked', '{"ok": true}');
  end;
  -- full storage: fill to capacity, then harvest a ripe field
  update public.wallets set coins = 500 where player_id = u1;
  insert into public.inventory(player_id, item_id, quantity) values (u1, 'carrot', 60)
    on conflict (player_id, item_id) do update set quantity = 60;
  update public.farms set plots = jsonb_set(plots, '{0,planted_at}', to_jsonb(public.hf_now() - 1000)) where player_id = u1;
  r := public.hf_action('harvest', '{"idx":0}', gen_random_uuid()); insert into public.zz_selftest(step,res) values ('harvest_storage_full', r - 'state');
  -- market: list, then two buyers race / one buys twice, and a buyer without coins
  update public.inventory set quantity = 10 where player_id = u1 and item_id = 'carrot';
  perform set_config('request.jwt.claims', json_build_object('sub', u2, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null); c2 := r->>'friend_code';
  insert into public.zz_selftest(step,res) values ('visit_stranger', public.hf_visit(c1));
  perform public.hf_friend_request(c1);
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  perform public.hf_friend_respond(c2, true);
  r := public.hf_market_list('carrot', 3, 2 * (public.hf_cfg()->'items'->'carrot'->>'sell_price')::int);
  oid := (r->'offers'->0->>'id')::bigint; insert into public.zz_selftest(step,res) values ('list', r - 'state');
  perform set_config('request.jwt.claims', json_build_object('sub', u2, 'role', 'authenticated')::text, true);
  update public.wallets set coins = 10 where player_id = u2;
  insert into public.zz_selftest(step,res) values ('buy_too_poor', public.hf_market_buy(oid) - 'state');
  update public.wallets set coins = 500 where player_id = u2;
  insert into public.zz_selftest(step,res) values ('buy', (public.hf_market_buy(oid) - 'state'));
  insert into public.zz_selftest(step,res) values ('buy_twice', public.hf_market_buy(oid) - 'state');
  insert into public.zz_selftest(step,res) values ('gem_rush_bad_index', public.hf_action('rush_plot', '{"idx":99}', gen_random_uuid()) - 'state');
  delete from auth.users where id in (u1, u2);
  insert into public.zz_selftest(step,res) values ('cleanup', jsonb_build_object('left', (select count(*) from public.profiles where id in (u1, u2))));
end $$;
select step, res from public.zz_selftest order by n;
