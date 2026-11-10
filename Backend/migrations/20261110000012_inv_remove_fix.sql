-- Fix: removing a whole stack (sell all, feeding the last wheat, an order that uses everything) failed.
-- The old version first set quantity to 0, which breaks the check "quantity > 0", so the whole action
-- was rolled back and the client silently got its old storage back. Now the row is deleted directly.
create or replace function public.hf_inv_remove(p uuid, item text, qty integer, reason text)
 returns boolean
 language plpgsql
 security definer
 set search_path to 'public', 'pg_temp'
as $function$
declare n int;
begin
  if qty <= 0 then return false; end if;
  delete from public.inventory where player_id = p and item_id = item and quantity = qty;
  get diagnostics n = row_count;
  if n = 0 then
    update public.inventory set quantity = quantity - qty
     where player_id = p and item_id = item and quantity > qty;
    get diagnostics n = row_count;
  end if;
  if n = 0 then return false; end if;
  insert into public.transactions (player_id, transaction_type, item_id, item_qty) values (p, reason, item, -qty);
  return true;
end $function$;
