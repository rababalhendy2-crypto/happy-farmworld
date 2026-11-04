-- Backend tests: run against a fresh DB with the stub + migrations applied.
-- Each test runs as the `authenticated` role with a fake JWT subject, like a real client.
\set ON_ERROR_STOP on
insert into auth.users values ('11111111-1111-1111-1111-111111111111'), ('22222222-2222-2222-2222-222222222222');

create or replace function pg_temp.as_player(p uuid) returns void language plpgsql as $$
begin perform set_config('request.jwt.claim.sub', p::text, false); end $$;

create temp table results (name text, ok boolean);
grant all on results to authenticated;

set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
-- 1. init creates farm with starter values
insert into results select 'init coins', (hf_init_player('Selda')->>'coins')::int = 120;
insert into results select 'init idempotent', (hf_init_player('Other')->>'display_name') = 'Selda';
insert into results select 'six plots', jsonb_array_length(hf_get_state()->'plots') = 6;
-- 2. plant costs coins
insert into results select 'plant ok', (hf_action('plant', '{"idx":0,"crop":"wheat"}', 'a0000000-0000-0000-0000-000000000001')->>'ok')::boolean;
insert into results select 'coins deducted', (hf_get_state()->>'coins')::int = 115;
-- 3. idempotency: same key again does not charge twice
insert into results select 'replay flagged', (hf_action('plant', '{"idx":1,"crop":"wheat"}', 'a0000000-0000-0000-0000-000000000001')->>'replayed')::boolean;
insert into results select 'no double charge', (hf_get_state()->>'coins')::int = 115;
insert into results select 'plot 1 untouched by replay', hf_get_state()->'plots'->1->>'crop' = '';
-- 4. cannot harvest early (server time)
insert into results select 'no early harvest', hf_action('harvest', '{"idx":0}', gen_random_uuid())->>'error' = 'not_ready';
-- 5. locked crop, invalid input, unknown action
insert into results select 'locked crop', hf_action('plant', '{"idx":1,"crop":"pumpkin"}', gen_random_uuid())->>'error' = 'locked';
insert into results select 'bad index', hf_action('plant', '{"idx":99,"crop":"wheat"}', gen_random_uuid())->>'error' = 'invalid';
insert into results select 'negative sell', hf_action('sell', '{"item":"wheat","qty":-5}', gen_random_uuid())->>'error' = 'invalid';
insert into results select 'unknown action', hf_action('give_me_coins', '{"amount":99999}', gen_random_uuid())->>'error' = 'invalid';
insert into results select 'coins still 115', (hf_get_state()->>'coins')::int = 115;
-- 6. feed + sell from starter inventory
insert into results select 'feed chicken', (hf_action('feed', jsonb_build_object('idx',
  (select i - 1 from jsonb_array_elements(hf_get_state()->'animals') with ordinality x(a, i) where a->>'type' = 'chicken')), gen_random_uuid())->>'ok')::boolean;
insert into results select 'sell wheat', (hf_action('sell', '{"item":"wheat","qty":2}', gen_random_uuid())->>'coins')::int = 6;
insert into results select 'cannot sell more than owned', hf_action('sell', '{"item":"wheat","qty":500}', gen_random_uuid())->>'error' = 'missing_items';
-- 7. overspending impossible
insert into results select 'workshop locked', hf_action('buy_workshop', '{"id":"bakery"}', gen_random_uuid())->>'error' = 'locked';
-- 8. direct writes are blocked (RLS + revoked privileges)
do $$ begin
  begin update public.wallets set coins = 999999; insert into results values ('direct update blocked', false);
  exception when insufficient_privilege then insert into results values ('direct update blocked', true); end;
  begin insert into public.inventory values ('11111111-1111-1111-1111-111111111111', 'pumpkin', 99); insert into results values ('direct insert blocked', false);
  exception when insufficient_privilege then insert into results values ('direct insert blocked', true); end;
  begin perform public.hf_money('11111111-1111-1111-1111-111111111111', 'coins', 99999, 'cheat', null); insert into results values ('internal helper blocked', false);
  exception when insufficient_privilege then insert into results values ('internal helper blocked', true); end;
end $$;
-- 9. player 2 cannot see player 1
select pg_temp.as_player('22222222-2222-2222-2222-222222222222');
select hf_init_player('Kid2');
insert into results select 'rls wallets isolated', (select count(*) from public.wallets) = 1;
insert into results select 'rls inventory isolated', not exists (select 1 from public.inventory where player_id = '11111111-1111-1111-1111-111111111111');
insert into results select 'rls transactions isolated', not exists (select 1 from public.transactions where player_id = '11111111-1111-1111-1111-111111111111');
-- player 2 reusing player 1's idempotency key is independent
insert into results select 'keys are per player', (hf_action('plant', '{"idx":0,"crop":"wheat"}', 'a0000000-0000-0000-0000-000000000001')->>'replayed') is null;
-- 10. bad display names rejected
insert into results select 'name with link rejected', hf_set_display_name('http://x.y/z')->>'error' = 'invalid';
insert into results select 'good name ok', (hf_set_display_name('Farm Kid')->>'ok')::boolean;
-- 11. not signed in
select set_config('request.jwt.claim.sub', '', false);
do $$ begin
  begin perform hf_get_state(); insert into results values ('anonymous request rejected', false);
  exception when others then insert into results values ('anonymous request rejected', true); end;
end $$;
reset role;

-- 12. time travel (as test superuser): pretend the crop was planted long ago, then harvest
update public.farms set plots = jsonb_set(plots, '{0,planted_at}', to_jsonb(extract(epoch from now()) - 1000))
 where player_id = '11111111-1111-1111-1111-111111111111';
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'harvest after time', (hf_action('harvest', '{"idx":0}', gen_random_uuid())->>'qty')::int = 3;
insert into results select 'xp after harvest', (hf_get_state()->>'xp')::int >= 1;
reset role;
-- 13. audit trail exists
insert into results select 'transactions logged', (select count(*) from public.transactions where player_id = '11111111-1111-1111-1111-111111111111') >= 5;
-- 14. delete my data
set role authenticated;
select pg_temp.as_player('22222222-2222-2222-2222-222222222222');
select hf_delete_my_data();
reset role;
insert into results select 'delete my data', not exists (select 1 from public.wallets where player_id = '22222222-2222-2222-2222-222222222222');

-- 15. phase 3 actions on the server
update public.profiles set level = 6 where id = '11111111-1111-1111-1111-111111111111';
update public.wallets set coins = 2000, premium_currency = 3 where player_id = '11111111-1111-1111-1111-111111111111';
insert into public.inventory values ('11111111-1111-1111-1111-111111111111', 'milk', 4)
  on conflict (player_id, item_id) do update set quantity = 4;
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'buy dairy', (hf_action('buy_workshop', '{"id":"dairy"}', gen_random_uuid())->>'ok')::boolean;
insert into results select 'dairy twice', hf_action('buy_workshop', '{"id":"dairy"}', gen_random_uuid())->>'error' = 'already_done';
insert into results select 'start cheese', (hf_action('start_job', '{"ws":"dairy","recipe":"cheese"}', gen_random_uuid())->>'ok')::boolean;
insert into results select 'milk consumed', (hf_get_state()->'inventory'->>'milk')::int = 2;
insert into results select 'wrong recipe', hf_action('start_job', '{"ws":"dairy","recipe":"bread"}', gen_random_uuid())->>'error' = 'invalid';
insert into results select 'cheese not ready', hf_action('collect_job', '{"ws":"dairy","job":0}', gen_random_uuid())->>'error' = 'not_ready';
insert into results select 'buy expansion', jsonb_array_length(hf_action('buy_expansion', '{"id":"field_north"}', gen_random_uuid())->'state'->'plots') = 9;
insert into results select 'upgrade storage', (hf_action('upgrade_storage', '{}', gen_random_uuid())->>'capacity')::int = 90;
insert into results select 'fountain needs gems', hf_action('buy_decoration', '{"id":"fountain"}', gen_random_uuid())->>'error' = 'not_enough_premium';
insert into results select 'gems unchanged', (hf_get_state()->>'premium')::int = 3;
insert into results select 'buy windmill', (hf_action('buy_decoration', '{"id":"windmill"}', gen_random_uuid())->>'ok')::boolean;
reset role;
update public.farms set workshops = jsonb_set(workshops, '{dairy,jobs,0,started_at}', to_jsonb(extract(epoch from now()) - 1000))
 where player_id = '11111111-1111-1111-1111-111111111111';
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'collect cheese', (hf_action('collect_job', '{"ws":"dairy","job":0}', gen_random_uuid())->>'item') = 'cheese';
reset role;

-- 16. daily gift + missions on the server
create temp table wk as select public.hf_active_missions(public.hf_cfg(), 'weekly', public.hf_now())->0->>'id' as id;
grant select on wk to authenticated;
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'daily gift', (hf_action('claim_daily', '{}', gen_random_uuid())->>'ok')::boolean;
insert into results select 'daily gift once', hf_action('claim_daily', '{}', gen_random_uuid())->>'error' = 'already_done';
insert into results select 'mission not ready', hf_action('claim_mission',
  jsonb_build_object('kind', 'weekly', 'id', (select id from wk)), gen_random_uuid())->>'error' = 'not_ready';
insert into results select 'fake mission rejected', hf_action('claim_mission', '{"kind":"daily","id":"free_money"}', gen_random_uuid())->>'error' = 'invalid';
insert into results select 'achievement not ready', hf_action('claim_achievement', '{"id":"a_harvest_500"}', gen_random_uuid())->>'error' = 'not_ready';
reset role;

-- 17. rewarded ads: nonce from client, reward only via (simulated) verified callback
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
create temp table ad1 as select hf_ad_begin('coins') as r;
insert into results select 'ad begin gives nonce', (select r->>'nonce' from ad1) is not null;
insert into results select 'speed up ad begin', (hf_ad_begin('speedup', '{"idx":1}')->>'ok')::boolean;
insert into results select 'unknown ad kind', hf_ad_begin('free_gems')->>'error' = 'invalid';
do $$ begin
  begin perform public.hf_grant_ad_reward('11111111-1111-1111-1111-111111111111', gen_random_uuid(), 'x'); insert into results values ('client cannot grant ad reward', false);
  exception when insufficient_privilege then insert into results values ('client cannot grant ad reward', true); end;
  begin perform public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'gems_60', 'tok', 'o'); insert into results values ('client cannot grant purchase', false);
  exception when insufficient_privilege then insert into results values ('client cannot grant purchase', true); end;
end $$;
reset role;
create temp table c0 as select coins from public.wallets where player_id = '11111111-1111-1111-1111-111111111111';
insert into results select 'ad reward granted', (public.hf_grant_ad_reward('11111111-1111-1111-1111-111111111111', (select (r->>'nonce')::uuid from ad1), 'tx-1')->>'coins')::int = 300;
insert into results select 'ad coins added', (select coins from public.wallets where player_id = '11111111-1111-1111-1111-111111111111') = (select coins from c0) + 300;
insert into results select 'same ad tx not paid twice', (public.hf_grant_ad_reward('11111111-1111-1111-1111-111111111111', (select (r->>'nonce')::uuid from ad1), 'tx-1')->>'duplicate')::boolean;
insert into results select 'nonce single use', public.hf_grant_ad_reward('11111111-1111-1111-1111-111111111111', (select (r->>'nonce')::uuid from ad1), 'tx-2')->>'error' = 'already_done';
insert into results select 'nonce bound to player', public.hf_grant_ad_reward('22222222-2222-2222-2222-222222222222', (select (r->>'nonce')::uuid from ad1), 'tx-3')->>'error' = 'invalid';
-- daily limit: fill up to 5
insert into public.ad_rewards (transaction_id, player_id, kind) select 'fill-' || g, '11111111-1111-1111-1111-111111111111', 'coins' from generate_series(1, 4) g;
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'daily ad limit', hf_ad_begin('coins')->>'error' = 'ad_limit';
reset role;
-- 18. purchases: granted once per token, non-consumables never twice
insert into results select 'purchase gems', (public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'gems_20', 'tok-a', 'GPA.1')->>'ok')::boolean;
insert into results select 'same token not granted twice', (public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'gems_20', 'tok-a', 'GPA.1')->>'duplicate')::boolean;
insert into results select 'golden hen purchase', (public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'golden_hen', 'tok-b', 'GPA.2')->>'ok')::boolean;
insert into results select 'hen restore no duplicate', (public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'golden_hen', 'tok-c', 'GPA.2')->>'restored')::boolean;
insert into results select 'one golden hen', (select count(*) from public.farms, jsonb_array_elements(animals) a where player_id = '11111111-1111-1111-1111-111111111111' and a->>'type' = 'golden_hen') = 1;
insert into results select 'unknown product', public.hf_grant_purchase('11111111-1111-1111-1111-111111111111', 'free_stuff', 'tok-d', 'x')->>'error' = 'invalid';
set role authenticated;
select pg_temp.as_player('11111111-1111-1111-1111-111111111111');
insert into results select 'my purchases listed', hf_my_purchases() ? 'golden_hen';
insert into results select 'special animal for gems: already owned', hf_action('buy_special_animal', '{"id":"golden_hen"}', gen_random_uuid())->>'error' = 'already_done';
reset role;

select name, case when ok then 'PASS' else 'FAIL' end from results;
select format('%s passed, %s failed', count(*) filter (where ok), count(*) filter (where not ok)) as summary from results;
