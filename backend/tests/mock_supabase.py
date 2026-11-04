#!/usr/bin/env python3
"""Tiny local stand-in for Supabase Auth + PostgREST RPC, for end-to-end tests of the Godot client
against the REAL SQL migrations on a local PostgreSQL. Not for production.
Usage: python3 mock_supabase.py <port> <dsn>"""
import json, sys, uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import psycopg2

PORT, DSN = int(sys.argv[1]), sys.argv[2]
REFRESH = {}

def db():
    c = psycopg2.connect(DSN); c.autocommit = False; return c

class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def send(self, code, obj):
        b = json.dumps(obj).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b))); self.end_headers(); self.wfile.write(b)
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
        if not self.headers.get("apikey"):
            return self.send(401, {"message": "no apikey"})
        if self.path.startswith("/auth/v1/signup"):
            uid = str(uuid.uuid4())
            with db() as c, c.cursor() as cur:
                cur.execute("insert into auth.users (id) values (%s)", (uid,))
            ref = "ref-" + uid; REFRESH[ref] = uid
            return self.send(200, {"access_token": "tok-" + uid, "refresh_token": ref, "expires_in": 3600, "user": {"id": uid}})
        if self.path.startswith("/auth/v1/token"):
            uid = REFRESH.get(body.get("refresh_token"))
            if not uid: return self.send(400, {"message": "invalid refresh"})
            return self.send(200, {"access_token": "tok-" + uid, "refresh_token": body["refresh_token"], "expires_in": 3600, "user": {"id": uid}})
        if self.path.startswith("/rest/v1/rpc/"):
            auth = self.headers.get("Authorization", "")
            if not auth.startswith("Bearer tok-"): return self.send(401, {"message": "jwt"})
            uid = auth[len("Bearer tok-"):]
            fn = self.path.rsplit("/", 1)[1]
            if not fn.isidentifier(): return self.send(404, {})
            args, vals = [], []
            for k, v in body.items():
                if not k.isidentifier(): return self.send(400, {})
                cast = "::uuid" if k == "p_key" else ("::jsonb" if isinstance(v, (dict, list)) else "")
                args.append(f"{k} => %s{cast}"); vals.append(json.dumps(v) if isinstance(v, (dict, list)) else v)
            try:
                with db() as c, c.cursor() as cur:
                    cur.execute("set local role authenticated")
                    cur.execute("select set_config('request.jwt.claim.sub', %s, true)", (uid,))
                    cur.execute(f"select public.{fn}({', '.join(args)})", vals)
                    return self.send(200, cur.fetchone()[0])
            except psycopg2.Error as e:
                return self.send(400, {"message": str(e).strip()})
        self.send(404, {})

ThreadingHTTPServer(("127.0.0.1", PORT), H).serve_forever()
