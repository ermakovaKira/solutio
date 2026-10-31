using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Draft
{
    /// <summary>
    /// Анимированная шкала таймера хода одной стороны драфта (как в Dota 2):
    /// основная шкала — время текущего хода, нижняя тонкая шкала — запасное время на весь драфт.
    /// Цвет основной шкалы плавно уходит из сапфира в жёлтый и красный, на последних секундах
    /// цифры пульсируют. Компонент только отображает значения: отсчёт по временным меткам — День 11.
    /// Обновление без аллокаций (TMP_Text.SetText с числовыми аргументами).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DraftTimerBar : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image turnFill;
        [SerializeField] private Image turnFillGlow;
        [SerializeField] private Image reserveFill;
        [SerializeField] private TMP_Text secondsText;
        [SerializeField] private TMP_Text reserveText;
        [SerializeField] private RectTransform pulseTarget;

        [Header("State")]
        [SerializeField, Range(0f, 1f)] private float inactiveAlpha = 0.35f;
        [SerializeField, Range(0.05f, 1f)] private float fadeDuration = 0.25f;

        [Header("Thresholds (доля хода)")]
        [SerializeField, Range(0f, 1f)] private float warningThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.2f;
        [SerializeField, Range(0, 15)] private int criticalPulseSeconds = 5;

        [Header("Colors")]
        [SerializeField] private Color normalColor = new Color32(0x4F, 0xC3, 0xE0, 0xFF);
        [SerializeField] private Color warningColor = new Color32(0xE0, 0xB0, 0x40, 0xFF);
        [SerializeField] private Color criticalColor = new Color32(0xD0, 0x40, 0x40, 0xFF);
        [SerializeField] private Color reserveColor = new Color32(0x8A, 0x7A, 0xC8, 0xFF);

        /// <summary>true, если сейчас ход этой стороны.</summary>
        public bool IsActive { get; private set; }

        private int _lastTurnSecond = int.MinValue;
        private int _lastReserveSecond = int.MinValue;
        private bool _isReserveDraining;
        private Tween _fadeTween;
        private Tween _pulseTween;
        private Tween _previewTween;
        private float _previewValue;
        private bool _isInitialized;

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            KillTweens();
        }

        private void OnDestroy()
        {
            KillTweens();
        }

        /// <summary>
        /// Делает шкалу активной (ход этой стороны) или приглушённой.
        /// </summary>
        public void SetActive(bool isActive, bool instant = false)
        {
            if (!Initialize())
            {
                return;
            }

            IsActive = isActive;
            float targetAlpha = isActive ? 1f : inactiveAlpha;
            _fadeTween?.Kill();

            if (!isActive)
            {
                StopPreview();
            }

            if (instant)
            {
                canvasGroup.alpha = targetAlpha;
                return;
            }

            _fadeTween = canvasGroup
                .DOFade(targetAlpha, fadeDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        /// <summary>
        /// Отображает состояние таймера. Вызывается контроллером драфта каждый кадр.
        /// </summary>
        /// <param name="turnNormalized">Доля оставшегося времени хода [0; 1].</param>
        /// <param name="turnSecondsLeft">Оставшиеся секунды хода.</param>
        /// <param name="reserveNormalized">Доля оставшегося запасного времени [0; 1].</param>
        /// <param name="reserveSecondsLeft">Оставшиеся секунды запасного времени.</param>
        public void SetProgress(float turnNormalized, float turnSecondsLeft, float reserveNormalized, float reserveSecondsLeft)
        {
            if (!Initialize())
            {
                return;
            }

            float turn = Mathf.Clamp01(turnNormalized);
            float reserve = Mathf.Clamp01(reserveNormalized);

            turnFill.fillAmount = turn;
            turnFillGlow.fillAmount = turn;
            reserveFill.fillAmount = reserve;

            _isReserveDraining = turn <= 0f;
            Color barColor = _isReserveDraining ? reserveColor : EvaluateTurnColor(turn);
            turnFill.color = barColor;

            Color glowColor = barColor;
            glowColor.a = 0.45f;
            turnFillGlow.color = glowColor;
            secondsText.color = barColor;

            int turnSecond = Mathf.CeilToInt(Mathf.Max(0f, _isReserveDraining ? reserveSecondsLeft : turnSecondsLeft));
            if (turnSecond != _lastTurnSecond)
            {
                _lastTurnSecond = turnSecond;
                secondsText.SetText("{0:0}", turnSecond);

                if (IsActive && turnSecond > 0 && turnSecond <= criticalPulseSeconds && (turn <= criticalThreshold || _isReserveDraining))
                {
                    PlayPulse();
                }
            }

            int reserveSecond = Mathf.CeilToInt(Mathf.Max(0f, reserveSecondsLeft));
            if (reserveSecond != _lastReserveSecond)
            {
                _lastReserveSecond = reserveSecond;
                reserveText.SetText("ЗАПАС {0:0}:{1:00}", reserveSecond / 60, reserveSecond % 60);
            }
        }

        /// <summary>
        /// Сбрасывает шкалу в полное состояние (начало хода).
        /// </summary>
        public void ResetBar(float turnSeconds, float reserveNormalized, float reserveSecondsLeft)
        {
            SetProgress(1f, turnSeconds, reserveNormalized, reserveSecondsLeft);
        }

        /// <summary>
        /// Демонстрационная анимация шкалы хода для проверки вёрстки (День 10).
        /// Шкала циклически убывает от полной до нуля за указанное время.
        /// </summary>
        public void PlayPreview(float turnSeconds, float reserveNormalized, float reserveSecondsLeft)
        {
            if (!Initialize() || turnSeconds <= 0f)
            {
                return;
            }

            StopPreview();

            _previewValue = 1f;
            ResetBar(turnSeconds, reserveNormalized, reserveSecondsLeft);

            _previewTween = DOTween
                .To(() => _previewValue, value =>
                {
                    _previewValue = value;
                    SetProgress(value, value * turnSeconds, reserveNormalized, reserveSecondsLeft);
                }, 0f, turnSeconds)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Restart)
                .Owned(gameObject);
        }

        /// <summary>Останавливает демонстрационную анимацию.</summary>
        public void StopPreview()
        {
            _previewTween?.Kill();
            _previewTween = null;
        }

        private Color EvaluateTurnColor(float turn)
        {
            if (turn >= warningThreshold)
            {
                return normalColor;
            }

            if (turn >= criticalThreshold)
            {
                float t = Mathf.InverseLerp(warningThreshold, criticalThreshold, turn);
                return Color.Lerp(normalColor, warningColor, t);
            }

            float criticalT = Mathf.InverseLerp(criticalThreshold, 0f, turn);
            return Color.Lerp(warningColor, criticalColor, criticalT);
        }

        private void PlayPulse()
        {
            _pulseTween?.Kill();
            pulseTarget.localScale = Vector3.one;
            _pulseTween = pulseTarget
                .DOPunchScale(Vector3.one * 0.25f, 0.3f, 4, 0.5f)
                .Owned(gameObject);
        }

        private void KillTweens()
        {
            _fadeTween?.Kill();
            _fadeTween = null;
            _pulseTween?.Kill();
            _pulseTween = null;
            StopPreview();
        }

        private bool Initialize()
        {
            if (_isInitialized)
            {
                return true;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            if (turnFill == null || turnFillGlow == null || reserveFill == null || secondsText == null || reserveText == null || pulseTarget == null)
            {
                Debug.LogError($"[DraftTimerBar] На шкале «{name}» не назначены ссылки в Инспекторе.", this);
                enabled = false;
                return false;
            }

            turnFill.type = Image.Type.Filled;
            turnFill.fillMethod = Image.FillMethod.Horizontal;
            turnFillGlow.type = Image.Type.Filled;
            turnFillGlow.fillMethod = Image.FillMethod.Horizontal;
            reserveFill.type = Image.Type.Filled;
            reserveFill.fillMethod = Image.FillMethod.Horizontal;

            turnFill.raycastTarget = false;
            turnFillGlow.raycastTarget = false;
            reserveFill.raycastTarget = false;
            secondsText.raycastTarget = false;
            reserveText.raycastTarget = false;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            _isInitialized = true;
            return true;
        }
    }
}
