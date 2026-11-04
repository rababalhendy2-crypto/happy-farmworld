using UnityEngine;
using UnityEngine.EventSystems;

namespace FarmWorld.World
{
    /// <summary>
    /// Isometric camera: orthographic, 35° pitch. One finger / left mouse pans, pinch / wheel zooms,
    /// two-finger twist / right mouse rotates in 45° steps-free motion. A short tap selects what is under it.
    /// </summary>
    public class IsoCameraRig : MonoBehaviour
    {
        public Rect Bounds = new Rect(-30, -30, 60, 60);
        public float MinZoom = 5f, MaxZoom = 22f;
        public float Pitch = 35f;
        public System.Action<Selectable> OnSelect;

        public Camera Cam { get; private set; }
        Vector3 _focus, _focusTarget;
        float _yaw = 45f, _yawTarget = 45f;
        float _zoom = 11f, _zoomTarget = 11f;

        // gesture state
        Vector2 _downPos; float _downTime; bool _dragging; bool _multi;
        float _lastPinch, _lastAngle;
        Selectable _selected;

        void Awake()
        {
            var old = Camera.main;
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            Cam = go.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.nearClipPlane = 0.3f;
            Cam.farClipPlane = 300f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.55f, 0.78f, 0.95f);
            go.AddComponent<AudioListener>();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }

        public void FocusImmediate(Vector3 p) { _focus = _focusTarget = Clamp(p); Apply(); }
        public void FocusOn(Vector3 p) => _focusTarget = Clamp(p);

        Vector3 Clamp(Vector3 p) => new Vector3(Mathf.Clamp(p.x, Bounds.xMin, Bounds.xMax), 0f, Mathf.Clamp(p.z, Bounds.yMin, Bounds.yMax));

        void Update()
        {
            if (Input.touchSupported && Input.touchCount > 0) HandleTouch();
            else HandleMouse();

            float t = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            _focus = Vector3.Lerp(_focus, _focusTarget, t);
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, t);
            _yaw = Mathf.LerpAngle(_yaw, _yawTarget, t);
            Apply();
        }

        void Apply()
        {
            Cam.orthographicSize = _zoom;
            var rot = Quaternion.Euler(Pitch, _yaw, 0f);
            Cam.transform.SetPositionAndRotation(_focus - rot * Vector3.forward * 80f, rot);
        }

        // ---------------------------------------------------------------- input

        static bool OverUi(int pointerId) =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);

        void HandleTouch()
        {
            if (Input.touchCount >= 2)
            {
                var a = Input.GetTouch(0); var b = Input.GetTouch(1);
                float d = Vector2.Distance(a.position, b.position);
                float ang = Mathf.Atan2(b.position.y - a.position.y, b.position.x - a.position.x) * Mathf.Rad2Deg;
                if (!_multi) { _multi = true; _lastPinch = d; _lastAngle = ang; _dragging = true; return; }
                if (_lastPinch > 1f) _zoomTarget = Mathf.Clamp(_zoomTarget * _lastPinch / Mathf.Max(d, 1f), MinZoom, MaxZoom);
                _yawTarget += Mathf.DeltaAngle(_lastAngle, ang);
                // two-finger midpoint also pans
                Vector2 mid = (a.position + b.position) * 0.5f;
                Vector2 prevMid = (a.position - a.deltaPosition + b.position - b.deltaPosition) * 0.5f;
                Pan(mid - prevMid);
                _lastPinch = d; _lastAngle = ang;
                return;
            }
            var touch = Input.GetTouch(0);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    _multi = false;
                    _dragging = OverUi(touch.fingerId);   // touches that start on UI never move the camera
                    _downPos = touch.position; _downTime = Time.unscaledTime;
                    if (_dragging) _downTime = -10f;
                    break;
                case TouchPhase.Moved:
                    if (_multi || _downTime < 0f) break;
                    if (!_dragging && (touch.position - _downPos).magnitude > Screen.dpi * 0.08f + 6f) _dragging = true;
                    if (_dragging) Pan(touch.deltaPosition);
                    break;
                case TouchPhase.Ended:
                    if (!_dragging && !_multi && _downTime > 0f && Time.unscaledTime - _downTime < 0.45f) Tap(touch.position);
                    _dragging = false;
                    if (Input.touchCount <= 1) _multi = false;
                    break;
                case TouchPhase.Canceled:
                    _dragging = false; _multi = false;
                    break;
            }
        }

        Vector3 _lastMouse;
        void HandleMouse()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUi(-1))
                _zoomTarget = Mathf.Clamp(_zoomTarget * Mathf.Pow(0.88f, wheel), MinZoom, MaxZoom);

            if (Input.GetMouseButtonDown(0))
            {
                _downPos = Input.mousePosition; _downTime = OverUi(-1) ? -10f : Time.unscaledTime;
                _dragging = false; _lastMouse = Input.mousePosition;
            }
            else if (Input.GetMouseButton(0) && _downTime > 0f)
            {
                Vector2 delta = Input.mousePosition - _lastMouse;
                if (!_dragging && ((Vector2)Input.mousePosition - _downPos).magnitude > 8f) _dragging = true;
                if (_dragging) Pan(delta);
                _lastMouse = Input.mousePosition;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                if (!_dragging && _downTime > 0f) Tap(Input.mousePosition);
                _dragging = false;
            }

            if (Input.GetMouseButtonDown(1)) _lastMouse = Input.mousePosition;
            if (Input.GetMouseButton(1))
            {
                _yawTarget += (Input.mousePosition.x - _lastMouse.x) * 0.3f;
                _lastMouse = Input.mousePosition;
            }
        }

        void Pan(Vector2 screenDelta)
        {
            // world units per pixel for an orthographic camera
            float upp = 2f * _zoom / Mathf.Max(Screen.height, 1);
            var right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            var fwd = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            float pitchK = 1f / Mathf.Sin(Pitch * Mathf.Deg2Rad);   // vertical screen motion covers more ground
            _focusTarget = Clamp(_focusTarget - right * screenDelta.x * upp - fwd * screenDelta.y * upp * pitchK);
        }

        void Tap(Vector2 screenPos)
        {
            var ray = Cam.ScreenPointToRay(screenPos);
            Selectable hit = null;
            if (Physics.Raycast(ray, out var info, 400f)) hit = info.collider.GetComponentInParent<Selectable>();
            if (_selected != null && _selected != hit) _selected.SetHighlighted(false);
            _selected = hit;
            if (hit != null) hit.SetHighlighted(true);
            OnSelect?.Invoke(hit);
        }
    }
}
