-- 0.8.0 layout actions: move, turn, buy (price, level, limit, coins, area), field adds a plot, sell, replay, visit.
create table if not exists public.zz_selftest(n serial primary key, step text, res jsonb, at timestamptz default now());
delete from public.zz_selftest;
do $$
declare u1 uuid := gen_random_uuid(); r jsonb; k uuid := gen_random_uuid(); st jsonb;
begin
  insert into auth.users (id, instance_id, aud, role, is_anonymous, created_at, updated_at) values
    (u1, '00000000-0000-0000-0000-000000000000', 'authenticated', 'authenticated', true, now(), now());
  perform set_config('request.jwt.claims', json_build_object('sub', u1, 'role', 'authenticated')::text, true);
  r := public.hf_init_player(null);
  insert into public.zz_selftest(step,res) values ('start_layout_null', jsonb_build_object('layout', r->'layout', 'plots', jsonb_array_length(r->'plots')));
  r := public.hf_action('move_item', '{"u":"d_house","x":-10,"z":8,"r":1}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('move_house', jsonb_build_object('ok', r->'ok', 'n', jsonb_array_length(r->'state'->'layout'),
    'house', (select e from jsonb_array_elements(r->'state'->'layout') e where e->>'u' = 'd_house')));
  r := public.hf_action('move_item', '{"u":"d_house","x":-80,"z":8,"r":1}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('move_outside_refused', r - 'state');
  r := public.hf_action('move_item', '{"u":"nope","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('move_unknown_refused', r - 'state');
  update public.wallets set coins = 1000 where player_id = u1;
  r := public.hf_action('buy_item', '{"id":"field","u":"field_aaaa1111","x":0,"z":-12,"r":0}', k);
  r := public.hf_action('buy_item', '{"id":"field","u":"field_aaaa1111","x":0,"z":-12,"r":0}', k);
  insert into public.zz_selftest(step,res) values ('buy_field_twice_same_key', jsonb_build_object('ok', r->'ok', 'replayed', r->'replayed', 'cost', r->'cost',
    'coins', r->'state'->'coins', 'plots', jsonb_array_length(r->'state'->'plots'),
    'item', (select e from jsonb_array_elements(r->'state'->'layout') e where e->>'u' = 'field_aaaa1111')));
  r := public.hf_action('buy_item', '{"id":"field","u":"field_bbbb2222","x":5,"z":-12,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('second_field_dearer', jsonb_build_object('cost', r->'cost', 'coins', r->'state'->'coins'));
  r := public.hf_action('plant', '{"idx":7,"crop":"wheat"}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('plant_on_new_field', jsonb_build_object('ok', r->'ok', 'crop', r->'state'->'plots'->7->'crop'));
  r := public.hf_action('buy_item', '{"id":"bell_tower","u":"bell_tower_x1","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('locked_by_level', r - 'state');
  r := public.hf_action('buy_item', '{"id":"house","u":"house_x1","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('not_for_sale', r - 'state');
  r := public.hf_action('buy_item', '{"id":"tree_1","u":"Bad Id!","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('bad_uid', r - 'state');
  r := public.hf_action('buy_item', '{"id":"tree_1","u":"tree_1_ok","x":0,"z":0,"r":5}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('bad_rotation', r - 'state');
  r := public.hf_action('buy_item', '{"id":"tree_1","u":"tree_1_ok","x":3,"z":12,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('buy_tree', jsonb_build_object('ok', r->'ok', 'cost', r->'cost', 'coins', r->'state'->'coins'));
  r := public.hf_action('sell_item', '{"u":"tree_1_ok"}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('sell_tree_half', jsonb_build_object('ok', r->'ok', 'coins_back', r->'coins', 'coins', r->'state'->'coins',
    'still_there', exists (select 1 from jsonb_array_elements(r->'state'->'layout') e where e->>'u' = 'tree_1_ok')));
  r := public.hf_action('sell_item', '{"u":"d_house"}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('sell_house_refused', r - 'state');
  r := public.hf_action('sell_item', '{"u":"field_aaaa1111"}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('sell_field_refused', r - 'state');
  update public.wallets set coins = 5 where player_id = u1;
  r := public.hf_action('buy_item', '{"id":"tree_2","u":"tree_2_poor","x":3,"z":12,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('no_coins', r - 'state');
  update public.wallets set coins = 100000 where player_id = u1;
  update public.profiles set level = 20 where id = u1;
  for i in 1..3 loop
    r := public.hf_action('buy_item', jsonb_build_object('id','dog','u','dog_'||i,'x',0,'z',0,'r',0), gen_random_uuid());
  end loop;
  r := public.hf_action('buy_item', '{"id":"dog","u":"dog_4","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('dog_limit_3', r - 'state');
  r := public.hf_action('move_item', '{"u":"auto_p7","x":0,"z":0,"r":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('auto_field_unknown_when_field_exists', r - 'state');
  r := public.hf_action('harvest', '{"idx":0}', gen_random_uuid());
  insert into public.zz_selftest(step,res) values ('old_actions_still_work', jsonb_build_object('error', r->'error', 'has_layout', r->'state' ? 'layout'));
end $$;
select step, res from public.zz_selftest order by n;
