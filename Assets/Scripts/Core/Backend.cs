using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace FarmWorld.Core
{
    /// <summary>
    /// Supabase client: anonymous player account, server-checked actions, cloud save.
    ///
    ///  * The game applies an action locally first (instant feedback), then queues it here with a random
    ///    idempotency key. The queue is stored on the device, so actions made offline are sent later and
    ///    a retried request is never applied twice.
    ///  * The server re-checks every action with its own clock and config (hf_action). Its answer holds the
    ///    full farm state, which replaces the local state once the queue is empty. So a modified app or
    ///    save file cannot create coins: the server state always wins.
    ///  * Only the publishable key ships in the app (Resources/Data/backend.json). No email, name or
    ///    birthday is collected: the account is an anonymous one, bound to this install.
    /// </summary>
    public class Backend : MonoBehaviour
    {
        public enum Status { Offline, Connecting, Online, Error }

        public static Backend I { get; private set; }
        public Status State { get; private set; } = Status.Offline;
        public static event Action<Status> StatusChanged;

        [Serializable] class Session { public string access_token; public string refresh_token; public long expires_at; public string user_id; }
        [Serializable] class Pending { public string action; public string args; public string key; }
        [Serializable] class PendingList { public List<Pending> items = new(); }

        const string SessionKey = "fw_session", QueueKey = "fw_queue";
        string _url = "", _key = "";
        Session _session;
        PendingList _queue = new();
        bool _flushing, _connecting;
        float _retry = 2f;

        public bool Enabled => _url != "" && _key != "";
        public int PendingCount => _queue.items.Count;

        void Awake()
        {
            I = this;
            var ta = Resources.Load<TextAsset>("Data/backend");
            var cfg = ta != null ? MiniJson.Parse(ta.text) : null;
            _url = MiniJson.S(cfg, "url").TrimEnd('/');
            _key = MiniJson.S(cfg, "anon_key");
            _session = Load<Session>(SessionKey);
            _queue = Load<PendingList>(QueueKey) ?? new PendingList();
        }

        void Start()
        {
            if (Enabled) StartCoroutine(Connect());
        }

        void OnApplicationPause(bool paused)
        {
            // coming back to the app: fetch the latest state (crops grew, another device may have played)
            if (!paused && Enabled && State != Status.Connecting) StartCoroutine(Connect());
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Queues a server action. argsJson is a JSON object, e.g. {"idx":2}.</summary>
        public void Enqueue(string action, string argsJson)
        {
            if (!Enabled) return;
            _queue.items.Add(new Pending { action = action, args = argsJson ?? "{}", key = Guid.NewGuid().ToString() });
            Store(QueueKey, _queue);
            if (State == Status.Online && !_flushing) StartCoroutine(Flush());
        }

        public static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // ------------------------------------------------------------------ connection

        IEnumerator Connect()
        {
            if (_connecting) yield break;
            _connecting = true;
            SetStatus(Status.Connecting);
            bool ok = false;
            yield return EnsureSession(r => ok = r);
            if (ok)
            {
                string body = null; long code = 0;
                yield return Post("/rest/v1/rpc/hf_init_player", "{}", true, (c, b) => { code = c; body = b; });
                if (code == 200 && body != null)
                {
                    _connecting = false;
                    SetStatus(Status.Online);
                    _retry = 2f;
                    if (_queue.items.Count == 0) Apply(MiniJson.Parse(body));
                    else if (!_flushing) StartCoroutine(Flush());
                    yield break;
                }
                if (code == 401 && _session != null) _session.expires_at = 0;   // stale token: refresh it on the next try
            }
            _connecting = false;
            SetStatus(Status.Error);
            StartCoroutine(RetryLater());
        }

        IEnumerator RetryLater()
        {
            yield return new WaitForSecondsRealtime(_retry);
            _retry = Mathf.Min(60f, _retry * 2f);
            if (State != Status.Online) StartCoroutine(Connect());
        }

        IEnumerator EnsureSession(Action<bool> done)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_session != null && !string.IsNullOrEmpty(_session.access_token) && _session.expires_at - now > 60) { done(true); yield break; }

            long code = 0; string body = null;
            if (_session != null && !string.IsNullOrEmpty(_session.refresh_token))
            {
                yield return Post("/auth/v1/token?grant_type=refresh_token", "{\"refresh_token\":" + Str(_session.refresh_token) + "}", false,
                    (c, b) => { code = c; body = b; });
                if (code == 200) { done(ReadSession(body)); yield break; }
                // A network error keeps the old account for the next try. Only a refused refresh token
                // (revoked or expired for good) starts a new anonymous account.
                if (code == 0 || code >= 500) { done(false); yield break; }
            }
            yield return Post("/auth/v1/signup", "{\"data\":{}}", false, (c, b) => { code = c; body = b; });
            done(code == 200 && ReadSession(body));
        }

        bool ReadSession(string body)
        {
            var o = MiniJson.Parse(body);
            var token = MiniJson.S(o, "access_token");
            if (token == "") return false;
            _session = new Session
            {
                access_token = token,
                refresh_token = MiniJson.S(o, "refresh_token"),
                expires_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)MiniJson.N(o, "expires_in", 3600),
                user_id = MiniJson.S(MiniJson.O(o, "user"), "id"),
            };
            Store(SessionKey, _session);
            return true;
        }

        // ------------------------------------------------------------------ queue

        IEnumerator Flush()
        {
            _flushing = true;
            object lastState = null;
            while (_queue.items.Count > 0)
            {
                bool ok = false;
                yield return EnsureSession(r => ok = r);
                if (!ok) break;
                var p = _queue.items[0];
                string body = "{\"p_action\":" + Str(p.action) + ",\"p_args\":" + p.args + ",\"p_key\":" + Str(p.key) + "}";
                long code = 0; string text = null;
                yield return Post("/rest/v1/rpc/hf_action", body, true, (c, b) => { code = c; text = b; });
                if (code == 401) { if (_session != null) _session.expires_at = 0; continue; }
                if (code == 0 || code >= 500 || code == 429) break;   // offline or server busy: keep the queue, retry later
                // 200 = applied or refused by the game rules; other 4xx = malformed. Either way it is done.
                _queue.items.RemoveAt(0);
                Store(QueueKey, _queue);
                if (code == 200) lastState = MiniJson.O(MiniJson.Parse(text), "state");
                else Debug.LogWarning($"[Backend] {p.action} rejected ({code}): {text}");
            }
            _flushing = false;
            if (_queue.items.Count == 0)
            {
                if (lastState != null) Apply(lastState);
            }
            else
            {
                SetStatus(Status.Error);
                StartCoroutine(RetryLater());
            }
        }

        void Apply(object state)
        {
            if (state is Dictionary<string, object> d && GameState.I != null) GameState.I.ApplyServerState(d);
        }

        // ------------------------------------------------------------------ HTTP

        IEnumerator Post(string path, string json, bool authed, Action<long, string> done)
        {
            using var req = new UnityWebRequest(_url + path, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = 15;
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("apikey", _key);
            if (authed && _session != null) req.SetRequestHeader("Authorization", "Bearer " + _session.access_token);
            yield return req.SendWebRequest();
            long code = req.result == UnityWebRequest.Result.ConnectionError ? 0 : req.responseCode;
            done(code, req.downloadHandler != null ? req.downloadHandler.text : null);
        }

        void SetStatus(Status s)
        {
            if (State == s) return;
            State = s;
            StatusChanged?.Invoke(s);
        }

        static T Load<T>(string key) where T : class
        {
            try
            {
                var s = PlayerPrefs.GetString(key, "");
                return s == "" ? null : JsonUtility.FromJson<T>(s);
            }
            catch { return null; }
        }

        static void Store(object key, object value)
        {
            PlayerPrefs.SetString((string)key, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
        }
    }
}
