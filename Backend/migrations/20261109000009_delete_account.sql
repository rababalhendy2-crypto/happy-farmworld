-- Unity version (0.7.1): "Delete my data" removes the login too (auth.users row, including a linked
-- email), not only the game data. Deleting the auth user cascades to profiles and every player table.
create or replace function public.hf_delete_my_data() returns jsonb
language plpgsql security definer set search_path = public as $$
declare uid uuid := auth.uid();
begin
  if uid is null then raise exception 'not authenticated'; end if;
  delete from public.profiles where id = uid;   -- cascades to every player table
  delete from auth.users where id = uid;        -- the login itself, with any linked email
  return public.hf_ok();
end $$;
revoke execute on function public.hf_delete_my_data() from public, anon;
grant execute on function public.hf_delete_my_data() to authenticated;
