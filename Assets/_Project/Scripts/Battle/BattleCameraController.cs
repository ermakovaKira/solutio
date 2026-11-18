using UnityEngine;

namespace Solutio.Battle
{
    /// <summary>
    /// Камера поля боя: колесо — масштаб к курсору, ПКМ/СКМ — перетаскивание карты,
    /// WASD/стрелки — прокрутка, Пробел/Home — общий вид. Кадр никогда не выходит за границы карты.
    /// Использует unscaledDeltaTime, чтобы камера работала и на паузе боя.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class BattleCameraController : MonoBehaviour
    {
        [Header("Zoom")]
        [Tooltip("Во сколько раз можно приблизить относительно общего вида.")]
        [SerializeField, Range(1f, 5f)] private float maxZoomFactor = 2.6f;
        [Tooltip("Изменение масштаба за один щелчок колеса (доля текущего размера).")]
        [SerializeField, Range(0.02f, 0.5f)] private float zoomStep = 0.15f;
        [SerializeField, Range(0.01f, 1f)] private float zoomSmoothTime = 0.12f;

        [Header("Pan")]
        [Tooltip("Скорость прокрутки клавишами: доля высоты кадра в секунду.")]
        [SerializeField, Range(0.1f, 5f)] private float keyboardPanSpeed = 1.1f;
        [SerializeField, Range(0.01f, 1f)] private float moveSmoothTime = 0.1f;
        [SerializeField] private bool enableKeyboard = true;
        [SerializeField] private bool enableMouseDrag = true;

        private Camera _camera;
        private Rect _bounds;
        private bool _hasBounds;
        private float _targetSize;
        private float _sizeVelocity;
        private Vector2 _targetCenter;
        private Vector2 _centerVelocity;
        private bool _isDragging;
        private Vector2 _dragAnchorWorld;

        /// <summary>Размер «общего вида», при котором вся ширина карты в кадре.</summary>
        public float FitSize { get; private set; }

        /// <summary>Минимальный ортографический размер (максимальное приближение).</summary>
        public float MinSize => FitSize / maxZoomFactor;

        /// <summary>Текущее приближение: 1 — общий вид.</summary>
        public float CurrentZoom => _camera != null && _camera.orthographicSize > 0f ? FitSize / _camera.orthographicSize : 1f;

        public Rect Bounds => _bounds;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
        }

        /// <summary>Задаёт границы карты в мировых координатах и (по желанию) показывает общий вид.</summary>
        public void SetBounds(Rect bounds, bool resetView)
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            _bounds = bounds;
            _hasBounds = bounds.width > 0f && bounds.height > 0f;
            if (!_hasBounds)
            {
                Debug.LogError("[BattleCamera] Границы карты пустые — камера не настроена.", this);
                return;
            }

            FitSize = BattleMapMath.GetFitOrthographicSize(bounds.size, _camera.aspect);

            if (resetView)
            {
                ResetView(true);
            }
        }

        /// <summary>Общий вид: вся карта по ширине, центр карты.</summary>
        public void ResetView(bool instant)
        {
            if (!_hasBounds)
            {
                return;
            }

            _targetSize = FitSize;
            _targetCenter = _bounds.center;
            _isDragging = false;

            if (instant)
            {
                _sizeVelocity = 0f;
                _centerVelocity = Vector2.zero;
                _camera.orthographicSize = _targetSize;
                Vector2 center = BattleMapMath.ClampCameraCenter(_targetCenter, _targetSize, _camera.aspect, _bounds);
                transform.position = new Vector3(center.x, center.y, transform.position.z);
            }
        }

        private void LateUpdate()
        {
            if (!_hasBounds)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            float aspect = _camera.aspect;

            // Окно могло сменить размер — общий вид пересчитывается без аллокаций.
            float fit = BattleMapMath.GetFitOrthographicSize(_bounds.size, aspect);
            if (!Mathf.Approximately(fit, FitSize))
            {
                FitSize = fit;
            }

            HandleZoom(aspect);
            HandleDrag(aspect);
            HandleKeyboard(deltaTime);

            _targetSize = Mathf.Clamp(_targetSize, MinSize, FitSize);
            _targetCenter = BattleMapMath.ClampCameraCenter(_targetCenter, _targetSize, aspect, _bounds);

            float size = Mathf.SmoothDamp(_camera.orthographicSize, _targetSize, ref _sizeVelocity, zoomSmoothTime, Mathf.Infinity, deltaTime);
            Vector2 current = transform.position;
            Vector2 center = _isDragging
                ? _targetCenter
                : Vector2.SmoothDamp(current, _targetCenter, ref _centerVelocity, moveSmoothTime, Mathf.Infinity, deltaTime);

            center = BattleMapMath.ClampCameraCenter(center, size, aspect, _bounds);

            _camera.orthographicSize = size;
            transform.position = new Vector3(center.x, center.y, transform.position.z);
        }

        private void HandleZoom(float aspect)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f))
            {
                return;
            }

            Vector2 mouse = Input.mousePosition;
            Vector2 before = ScreenToWorld(mouse, _targetSize, _targetCenter, aspect);

            float factor = Mathf.Pow(1f - zoomStep, wheel);
            _targetSize = Mathf.Clamp(_targetSize * factor, MinSize, FitSize);

            // Точка под курсором остаётся на месте — масштаб «к курсору».
            Vector2 after = ScreenToWorld(mouse, _targetSize, _targetCenter, aspect);
            _targetCenter += before - after;
        }

        private void HandleDrag(float aspect)
        {
            if (!enableMouseDrag)
            {
                _isDragging = false;
                return;
            }

            bool dragHeld = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            Vector2 mouse = Input.mousePosition;
            Vector2 center = transform.position;
            float size = _camera.orthographicSize;

            if (!_isDragging && (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
            {
                _isDragging = true;
                _dragAnchorWorld = ScreenToWorld(mouse, size, center, aspect);
                _centerVelocity = Vector2.zero;
            }

            if (_isDragging && !dragHeld)
            {
                _isDragging = false;
                return;
            }

            if (_isDragging)
            {
                Vector2 under = ScreenToWorld(mouse, size, center, aspect);
                _targetCenter = center + (_dragAnchorWorld - under);
            }
        }

        private void HandleKeyboard(float deltaTime)
        {
            if (!enableKeyboard)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Home))
            {
                ResetView(false);
                return;
            }

            float x = 0f;
            float y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;

            if (x == 0f && y == 0f)
            {
                return;
            }

            Vector2 direction = new Vector2(x, y).normalized;
            _targetCenter += direction * (keyboardPanSpeed * _targetSize * 2f * deltaTime);
        }

        private static Vector2 ScreenToWorld(Vector2 screen, float size, Vector2 center, float aspect)
        {
            float nx = Screen.width > 0 ? screen.x / Screen.width * 2f - 1f : 0f;
            float ny = Screen.height > 0 ? screen.y / Screen.height * 2f - 1f : 0f;
            return center + new Vector2(nx * size * aspect, ny * size);
        }
    }
}
