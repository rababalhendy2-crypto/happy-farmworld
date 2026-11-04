#!/usr/bin/env bash
# Runs the Supabase migrations + backend tests on a local PostgreSQL (needs psql + a superuser).
set -euo pipefail
cd "$(dirname "$0")/../.."
DB=hfw_test
PSQL="psql -v ON_ERROR_STOP=1 -q"
dropdb --if-exists $DB 2>/dev/null || true
createdb $DB
$PSQL -d $DB -f backend/tests/local_supabase_stub.sql
for m in backend/supabase/migrations/*.sql; do $PSQL -d $DB -f "$m"; done
$PSQL -d $DB -f backend/tests/backend_tests.sql
