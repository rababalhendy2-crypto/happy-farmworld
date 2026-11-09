-- Unity version (0.6.0): friends, farm visits, likes and gifts. Child-safe by design: no chat, no free text
-- between players. Players find each other only by a short friend code they share themselves.
--  * profiles.friend_code: 6 characters, no look-alike letters (0/O, 1/I/L).
--  * friendships: one row per pair (a < b). status 'pending' (requested_by asked) or 'accepted'.
--  * social_log: one like and one gift per friend per day. A gift pays the friend coins at once; a player
--    receives at most cfg.social.gifts_per_day gifts a day.
--  * A visit returns a read-only copy of the friend's farm; the visitor cannot change anything.

update public.game_config set data = data || jsonb_build_object('social', jsonb_build_object(
  'max_friends', 50, 'max_pending', 20, 'gift_coins', 10, 'gifts_per_day', 10)), updated_at = now() where id = 1;

alter table public.profiles add column if not exists friend_code text unique;
alter table public.profiles add column if not exists likes int not null default 0;

create table if not exists public.friendships (
  a uuid not null references public.profiles(id) on delete cascade,
  b uuid not null references public.profiles(id) on delete cascade,
  requested_by uuid not null,
  status text not null default 'pending' check (status in ('pending', 'accepted')),
  created_at timestamptz not null default now(),
  primary key (a, b),
  check (a < b));
create index if not exists friendships_b on public.friendships (b);

create table if not exists public.social_log (
  from_id uuid not null references public.profiles(id) on delete cascade,
  to_id uuid not null references public.profiles(id) on delete cascade,
  kind text not null check (kind in ('like', 'gift')),
  day int not null,
  seen boolean not null default false,
  created_at timestamptz not null default now(),
  primary key (from_id, to_id, kind, day));
create index if not exists social_log_to on public.social_log (to_id, kind, day);

alter table public.friendships enable row level security;
alter table public.social_log enable row level security;
-- no policies: only the security-definer functions below touch these tables.

-- ------------------------------------------------------------------ helpers

create or replace function public.hf_ensure_code(p uuid) returns text
language plpgsql security definer set search_path = public as $$
declare c text; abc text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'; i int;
begin
  select friend_code into c from public.profiles where id = p;
  if c is not null then return c; end if;
  loop
    c := '';
    for i in 1 .. 6 loop c := c || substr(abc, 1 + floor(random() * length(abc))::int, 1); end loop;
    begin
      update public.profiles set friend_code = c where id = p and friend_code is null;
      exit;
    exception when unique_violation then -- try another code
    end;
  end loop;
  return (select friend_code from public.profiles where id = p);
end $$;

create or replace function public.hf_by_code(p_code text) returns uuid
language sql stable security definer set search_path = public as $$
  select id from public.profiles where friend_code = upper(trim(coalesce(p_code, ''))) $$;

create or replace function public.hf_are_friends(x uuid, y uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.friendships where a = least(x, y) and b = greatest(x, y) and status = 'accepted') $$;

-- ------------------------------------------------------------------ public RPCs

create or replace function public.hf_friends() returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); d int := public.hf_day(public.hf_now()); res jsonb;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if not exists (select 1 from public.profiles where id = uid) then return public.hf_err('no_player'); end if;
  perform public.hf_ensure_code(uid);
  select jsonb_build_object(
    'ok', true,
    'code', (select friend_code from public.profiles where id = uid),
    'likes', (select likes from public.profiles where id = uid),
    'friends', coalesce(jsonb_agg(jsonb_build_object(
        'code', o.friend_code, 'name', o.display_name, 'level', o.level,
        'state', case when f.status = 'accepted' then 'friend' when f.requested_by = uid then 'outgoing' else 'incoming' end,
        'liked', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o.id and s.kind = 'like' and s.day = d),
        'gifted', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o.id and s.kind = 'gift' and s.day = d))
      order by f.status, o.level desc), '[]'::jsonb),
    'new_gifts', (select count(*) from public.social_log s where s.to_id = uid and s.kind = 'gift' and not s.seen),
    'new_likes', (select count(*) from public.social_log s where s.to_id = uid and s.kind = 'like' and not s.seen))
  into res
  from public.friendships f
  join public.profiles o on o.id = case when f.a = uid then f.b else f.a end
  where f.a = uid or f.b = uid;
  update public.social_log set seen = true where to_id = uid and not seen;
  return res;
end $$;

-- Sends a request, or accepts one the other player already sent.
create or replace function public.hf_friend_request(p_code text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid := public.hf_by_code(p_code); cfg jsonb := public.hf_cfg(); f public.friendships%rowtype;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if o is null then return public.hf_err('not_found'); end if;
  if o = uid then return public.hf_err('invalid'); end if;
  select * into f from public.friendships where a = least(uid, o) and b = greatest(uid, o) for update;
  if found then
    if f.status = 'accepted' then return public.hf_err('already'); end if;
    if f.requested_by = uid then return public.hf_err('already'); end if;
    update public.friendships set status = 'accepted' where a = f.a and b = f.b;
    return public.hf_ok(jsonb_build_object('accepted', true));
  end if;
  if (select count(*) from public.friendships where (a = uid or b = uid) and status = 'accepted') >= (cfg->'social'->>'max_friends')::int
    then return public.hf_err('full'); end if;
  if (select count(*) from public.friendships where (a = uid or b = uid) and status = 'pending' and requested_by = uid) >= (cfg->'social'->>'max_pending')::int
    then return public.hf_err('full'); end if;
  insert into public.friendships (a, b, requested_by) values (least(uid, o), greatest(uid, o), uid);
  return public.hf_ok(jsonb_build_object('accepted', false));
end $$;

-- accept = false declines a request, cancels your own, or removes a friend.
create or replace function public.hf_friend_respond(p_code text, p_accept boolean) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid := public.hf_by_code(p_code); f public.friendships%rowtype;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if o is null then return public.hf_err('not_found'); end if;
  select * into f from public.friendships where a = least(uid, o) and b = greatest(uid, o) for update;
  if not found then return public.hf_err('not_found'); end if;
  if p_accept then
    if f.status = 'accepted' or f.requested_by = uid then return public.hf_err('invalid'); end if;
    update public.friendships set status = 'accepted' where a = f.a and b = f.b;
  else
    delete from public.friendships where a = f.a and b = f.b;
  end if;
  return public.hf_ok();
end $$;

-- Read-only copy of a friend's farm.
create or replace function public.hf_visit(p_code text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid := public.hf_by_code(p_code); d int := public.hf_day(public.hf_now());
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if o is null or not public.hf_are_friends(uid, o) then return public.hf_err('not_found'); end if;
  return (select jsonb_build_object('ok', true,
      'code', pr.friend_code, 'display_name', pr.display_name, 'level', pr.level, 'likes', pr.likes,
      'plots', f.plots, 'animals', f.animals, 'workshops', f.workshops, 'storage_level', f.storage_level,
      'liked', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o and s.kind = 'like' and s.day = d),
      'gifted', exists (select 1 from public.social_log s where s.from_id = uid and s.to_id = o and s.kind = 'gift' and s.day = d),
      'server_time', public.hf_now())
    from public.profiles pr join public.farms f on f.player_id = pr.id where pr.id = o);
end $$;

-- kind 'like' or 'gift'; one of each per friend per day.
create or replace function public.hf_social(p_code text, p_kind text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid(); o uuid := public.hf_by_code(p_code); cfg jsonb := public.hf_cfg();
        d int := public.hf_day(public.hf_now()); coins int;
begin
  if uid is null then raise exception 'not authenticated'; end if;
  if p_kind not in ('like', 'gift') then return public.hf_err('invalid'); end if;
  if o is null or not public.hf_are_friends(uid, o) then return public.hf_err('not_found'); end if;
  perform 1 from public.wallets where player_id = o for update;   -- serialises gifts to the same friend
  if exists (select 1 from public.social_log where from_id = uid and to_id = o and kind = p_kind and day = d) then
    return public.hf_err('already'); end if;
  if p_kind = 'gift' and (select count(*) from public.social_log where to_id = o and kind = 'gift' and day = d)
       >= (cfg->'social'->>'gifts_per_day')::int then
    return public.hf_err('full'); end if;
  insert into public.social_log (from_id, to_id, kind, day) values (uid, o, p_kind, d);
  if p_kind = 'like' then
    update public.profiles set likes = likes + 1 where id = o;
    return public.hf_ok();
  end if;
  coins := (cfg->'social'->>'gift_coins')::int;
  perform public.hf_money(o, 'coins', coins, 'friend_gift', uid::text);
  return public.hf_ok(jsonb_build_object('coins', coins));
end $$;

-- the player's own state carries the friend code from now on
create or replace function public.hf_state(p uuid) returns jsonb
language sql stable security definer set search_path = public as $$
  select public.hf_state_core(p) || jsonb_build_object(
    'orders', (select orders from public.farms where player_id = p),
    'friend_code', (select friend_code from public.profiles where id = p),
    'likes', (select likes from public.profiles where id = p)) $$;


create or replace function public.hf_init_player(p_display_name text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  perform public.hf_init_player_core(p_display_name);
  perform public.hf_prepare_farm(auth.uid(), public.hf_cfg(), public.hf_now());
  perform public.hf_ensure_code(auth.uid());
  return public.hf_state(auth.uid());
end $$;

-- ------------------------------------------------------------------ permissions
revoke execute on function public.hf_ensure_code(uuid), public.hf_by_code(text), public.hf_are_friends(uuid, uuid)
  from public, anon, authenticated;
revoke execute on function public.hf_friends(), public.hf_friend_request(text), public.hf_friend_respond(text, boolean),
  public.hf_visit(text), public.hf_social(text, text) from public, anon;
grant execute on function public.hf_friends(), public.hf_friend_request(text), public.hf_friend_respond(text, boolean),
  public.hf_visit(text), public.hf_social(text, text) to authenticated;
revoke execute on function public.hf_state(uuid) from public, anon, authenticated;
