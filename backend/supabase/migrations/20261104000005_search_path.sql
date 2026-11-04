-- Supabase advisor fix: pin search_path on every game function.
do $$
declare r record;
begin
  for r in select p.oid::regprocedure as sig from pg_proc p join pg_namespace n on n.oid = p.pronamespace
           where n.nspname = 'public' and p.proname like 'hf\_%'
  loop
    execute format('alter function %s set search_path = public, pg_temp', r.sig);
  end loop;
end $$;

-- Helper functions are internal: only the SECURITY DEFINER entry points are callable through the API.
do $$
declare r record;
begin
  for r in select p.oid::regprocedure as sig from pg_proc p join pg_namespace n on n.oid = p.pronamespace
           where n.nspname = 'public' and p.proname like 'hf\_%' and not p.prosecdef
  loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
  end loop;
end $$;
