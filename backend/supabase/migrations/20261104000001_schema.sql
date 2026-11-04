-- Happy Farm World: core schema, Row Level Security.
-- Design:
--   * Every player-owned table has RLS enabled. Clients may only SELECT their own rows.
--   * Clients can NEVER insert/update/delete directly. All writes go through SECURITY DEFINER
--     functions (see 0002) that validate the action server-side, lock the player's rows,
--     and log an auditable transaction. Coins / premium / inventory are never taken from the client.
--   * No personal data: no email, no real name, no birthday. Display names are short and
--     restricted to letters, digits, spaces and a few symbols (no links, no free text).

create extension if not exists pgcrypto;

-- ------------------------------------------------------------------ config (readable by players)
create table if not exists public.game_config (
  id int primary key default 1 check (id = 1),
  data jsonb not null,
  updated_at timestamptz not null default now()
);

-- ------------------------------------------------------------------ players
create table if not exists public.profiles (
  id uuid primary key references auth.users (id) on delete cascade,
  display_name text not null default 'Farmer'
    check (char_length(display_name) between 2 and 16
           and display_name ~ '^[[:alnum:] _.-]+$'),
  level int not null default 1 check (level >= 1),
  experience int not null default 0 check (experience >= 0),
  stats jsonb not null default '{}'::jsonb,
  missions jsonb not null default '{}'::jsonb,
  daily_reward jsonb not null default '{"last_day": -1, "step": 0}'::jsonb,
  achievements jsonb not null default '[]'::jsonb,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists public.wallets (
  player_id uuid primary key references public.profiles (id) on delete cascade,
  coins int not null default 0 check (coins >= 0),
  premium_currency int not null default 0 check (premium_currency >= 0),
  updated_at timestamptz not null default now()
);

create table if not exists public.inventory (
  player_id uuid not null references public.profiles (id) on delete cascade,
  item_id text not null,
  quantity int not null check (quantity > 0),
  primary key (player_id, item_id)
);

-- Farm layout and timers. Times are server epoch seconds (double precision).
create table if not exists public.farms (
  player_id uuid primary key references public.profiles (id) on delete cascade,
  plots jsonb not null default '[]'::jsonb,
  animals jsonb not null default '[]'::jsonb,
  workshops jsonb not null default '{}'::jsonb,
  expansions jsonb not null default '[]'::jsonb,
  decorations jsonb not null default '[]'::jsonb,
  storage_level int not null default 0 check (storage_level >= 0),
  updated_at timestamptz not null default now()
);

-- Auditable history of every economy change.
create table if not exists public.transactions (
  id bigint generated always as identity primary key,
  player_id uuid not null references public.profiles (id) on delete cascade,
  transaction_type text not null,
  currency text,               -- 'coins' | 'premium' | null (item-only)
  amount int not null default 0,
  item_id text,
  item_qty int,
  reference_id text,
  created_at timestamptz not null default now()
);
create index if not exists transactions_player_idx on public.transactions (player_id, created_at desc);

-- Idempotency: the same action key is applied at most once; a retry returns the stored result.
create table if not exists public.processed_actions (
  player_id uuid not null references public.profiles (id) on delete cascade,
  idempotency_key uuid not null,
  action text not null,
  result jsonb not null,
  created_at timestamptz not null default now(),
  primary key (player_id, idempotency_key)
);

-- ------------------------------------------------------------------ RLS
alter table public.game_config enable row level security;
alter table public.profiles enable row level security;
alter table public.wallets enable row level security;
alter table public.inventory enable row level security;
alter table public.farms enable row level security;
alter table public.transactions enable row level security;
alter table public.processed_actions enable row level security;

drop policy if exists "config readable" on public.game_config;
create policy "config readable" on public.game_config for select to authenticated using (true);

drop policy if exists "own profile" on public.profiles;
create policy "own profile" on public.profiles for select to authenticated using (id = auth.uid());
drop policy if exists "own wallet" on public.wallets;
create policy "own wallet" on public.wallets for select to authenticated using (player_id = auth.uid());
drop policy if exists "own inventory" on public.inventory;
create policy "own inventory" on public.inventory for select to authenticated using (player_id = auth.uid());
drop policy if exists "own farm" on public.farms;
create policy "own farm" on public.farms for select to authenticated using (player_id = auth.uid());
drop policy if exists "own transactions" on public.transactions;
create policy "own transactions" on public.transactions for select to authenticated using (player_id = auth.uid());
-- processed_actions: no client policy at all (server-only).

-- Belt and braces: remove direct write privileges from client roles.
revoke insert, update, delete on all tables in schema public from anon, authenticated;
revoke all on public.processed_actions from anon, authenticated;
grant select on public.game_config, public.profiles, public.wallets, public.inventory,
  public.farms, public.transactions to authenticated;
