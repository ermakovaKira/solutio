using TMPro;
using UnityEngine;

namespace Solutio.Session
{
    /// <summary>
    /// Диагностический оверлей частоты кадров для проверки работы лимита FPS.
    /// Усредняет значения за окно измерения по Time.unscaledDeltaTime (не зависит от timeScale)
    /// и обновляет текст через TMP_Text.SetText без аллокаций памяти.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsCounterOverlay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text fpsText;

        [Header("Measurement")]
        [SerializeField, Range(0.1f, 2f)] private float sampleWindowSeconds = 0.5f;

        [Header("Colors")]
        [SerializeField] private Color goodColor = new Color32(0x30, 0x70, 0x82, 0xFF);
        [SerializeField] private Color warningColor = new Color32(0xE0, 0xB0, 0x40, 0xFF);
        [SerializeField] private Color criticalColor = new Color32(0xD0, 0x40, 0x40, 0xFF);
        [SerializeField] private Color uncappedColor = new Color32(0xE8, 0xF4, 0xF8, 0xFF);

        [Header("Thresholds (доля от лимита)")]
        [SerializeField, Range(0.5f, 1f)] private float goodThreshold = 0.95f;
        [SerializeField, Range(0.1f, 1f)] private float warningThreshold = 0.75f;

        private int _frameCount;
        private float _elapsedSeconds;

        private void Awake()
        {
            if (fpsText == null)
            {
                fpsText = GetComponent<TMP_Text>();
            }

            if (fpsText == null)
            {
                Debug.LogError("[FpsCounterOverlay] Не назначено поле «fpsText» в Инспекторе.");
                enabled = false;
                return;
            }

            fpsText.raycastTarget = false;
            fpsText.SetText("— FPS");
        }

        private void OnEnable()
        {
            _frameCount = 0;
            _elapsedSeconds = 0f;
        }

        private void Update()
        {
            _frameCount++;
            _elapsedSeconds += Time.unscaledDeltaTime;

            if (_elapsedSeconds < sampleWindowSeconds)
            {
                return;
            }

            float fps = _frameCount / _elapsedSeconds;
            float frameTimeMs = _elapsedSeconds * 1000f / _frameCount;

            RefreshView(fps, frameTimeMs);

            _frameCount = 0;
            _elapsedSeconds = 0f;
        }

        private void RefreshView(float fps, float frameTimeMs)
        {
            FrameRateSettings applied = SessionConfigurator.AppliedFrameRate;

            if (applied.IsUncapped || applied.TargetFrameRate <= 0)
            {
                fpsText.color = uncappedColor;
                fpsText.SetText("{0:0} FPS  |  {1:2} мс\nЛимит: без лимита", fps, frameTimeMs);
                return;
            }

            float ratio = fps / applied.TargetFrameRate;

            if (ratio >= goodThreshold)
            {
                fpsText.color = goodColor;
            }
            else if (ratio >= warningThreshold)
            {
                fpsText.color = warningColor;
            }
            else
            {
                fpsText.color = criticalColor;
            }

            fpsText.SetText("{0:0} FPS  |  {1:2} мс\nЛимит: {2:0} FPS", fps, frameTimeMs, applied.TargetFrameRate);
        }
    }
}
