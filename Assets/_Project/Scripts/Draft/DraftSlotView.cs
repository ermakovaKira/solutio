using DG.Tweening;
using Solutio.Heroes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Draft
{
    /// <summary>
    /// Слот команды в верхней полосе драфта: пик (портрет + имя) или бан (малый портрет + перечёркивание).
    /// Активный слот текущего хода пульсирует свечением цвета стороны.
    /// Все анимации DOTween с SetUpdate(true), видимость портрета через CanvasGroup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DraftSlotView : MonoBehaviour
    {
        [Header("Mode")]
        [SerializeField] private DraftActionType slotType = DraftActionType.Pick;

        [Header("References")]
        [SerializeField] private RectTransform content;
        [SerializeField] private Image glow;
        [SerializeField] private Image frame;
        [SerializeField] private CanvasGroup portraitGroup;
        [SerializeField] private Image halo;
        [SerializeField] private Image portrait;
        [SerializeField] private TMP_Text placeholderText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image banStrike;

        [Header("Glow")]
        [SerializeField, Range(0f, 1f)] private float activeGlowMin = 0.25f;
        [SerializeField, Range(0f, 1f)] private float activeGlowMax = 0.9f;
        [SerializeField, Range(0f, 1f)] private float filledGlowAlpha = 0.3f;
        [SerializeField, Range(0.2f, 3f)] private float pulseDuration = 0.6f;

        /// <summary>Пик или бан.</summary>
        public DraftActionType SlotType => slotType;

        /// <summary>Сторона-владелец слота.</summary>
        public DraftSide Side { get; private set; }

        /// <summary>Индекс слота внутри стороны.</summary>
        public int SlotIndex { get; private set; }

        /// <summary>Герой в слоте или null.</summary>
        public HeroRecord Hero { get; private set; }

        /// <summary>true, если слот заполнен.</summary>
        public bool IsFilled => Hero != null;

        private Tween _glowTween;
        private Tween _portraitTween;
        private Tween _punchTween;
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
        /// Назначает слоту сторону и индекс и очищает его.
        /// </summary>
        public void Setup(DraftSide side, int slotIndex)
        {
            if (!Initialize())
            {
                return;
            }

            Side = side;
            SlotIndex = slotIndex;
            SetEmpty();
        }

        /// <summary>Очищает слот.</summary>
        public void SetEmpty()
        {
            if (!Initialize())
            {
                return;
            }

            KillTweens();
            Hero = null;

            content.localScale = Vector3.one;
            frame.color = DraftPalette.FrameIdle;
            portraitGroup.alpha = 0f;
            banStrike.enabled = false;
            SetGlow(DraftPalette.GetSideGlowColor(Side), 0f);

            if (placeholderText != null)
            {
                placeholderText.SetText(slotType == DraftActionType.Ban ? "БАН" : "{0:0}", SlotIndex + 1);
                placeholderText.color = DraftPalette.Muted;
            }

            if (nameText != null)
            {
                nameText.SetText(string.Empty);
            }
        }

        /// <summary>
        /// Включает или выключает подсветку слота как цели текущего хода.
        /// </summary>
        public void SetActive(bool isActive)
        {
            if (!Initialize() || IsFilled)
            {
                return;
            }

            _glowTween?.Kill();
            Color glowColor = slotType == DraftActionType.Ban ? (Color)DraftPalette.Critical : DraftPalette.GetSideGlowColor(Side);

            if (!isActive)
            {
                frame.color = DraftPalette.FrameIdle;
                _glowTween = glow.DOFade(0f, 0.2f).Owned(gameObject);
                return;
            }

            frame.color = glowColor;
            SetGlow(glowColor, activeGlowMin);
            _glowTween = glow
                .DOFade(activeGlowMax, pulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .Owned(gameObject);
        }

        /// <summary>
        /// Заполняет слот героем с анимацией появления портрета.
        /// </summary>
        public void Fill(HeroRecord hero, Sprite portraitSprite, Color portraitTint, bool instant = false)
        {
            if (!Initialize() || hero == null)
            {
                return;
            }

            KillTweens();
            Hero = hero;

            bool isBan = slotType == DraftActionType.Ban;
            Color sideColor = isBan ? (Color)DraftPalette.Critical : DraftPalette.GetSideGlowColor(Side);

            portrait.sprite = portraitSprite;
            portrait.color = isBan ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : portraitTint;

            if (halo != null)
            {
                Color haloColor = isBan ? (Color)DraftPalette.Muted : DraftPalette.GetRoleColor(hero.Role);
                haloColor.a = isBan ? 0.15f : 0.55f;
                halo.color = haloColor;
            }
            frame.color = sideColor;
            banStrike.enabled = isBan;
            banStrike.color = DraftPalette.Critical;
            SetGlow(sideColor, filledGlowAlpha);

            if (placeholderText != null)
            {
                placeholderText.SetText(string.Empty);
            }

            if (nameText != null)
            {
                nameText.SetText(hero.Name);
                nameText.color = DraftPalette.TextBright;
            }

            if (instant)
            {
                portraitGroup.alpha = 1f;
                return;
            }

            portraitGroup.alpha = 0f;
            _portraitTween = portraitGroup
                .DOFade(1f, 0.3f)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);

            content.localScale = Vector3.one;
            _punchTween = content
                .DOPunchScale(Vector3.one * 0.18f, 0.4f, 6, 0.6f)
                .Owned(gameObject);
        }

        private void SetGlow(Color color, float alpha)
        {
            color.a = alpha;
            glow.color = color;
        }

        private void KillTweens()
        {
            _glowTween?.Kill();
            _glowTween = null;
            _portraitTween?.Kill();
            _portraitTween = null;
            _punchTween?.Kill();
            _punchTween = null;
        }

        private bool Initialize()
        {
            if (_isInitialized)
            {
                return true;
            }

            if (content == null || glow == null || frame == null || portraitGroup == null || portrait == null || banStrike == null)
            {
                Debug.LogError($"[DraftSlotView] На слоте «{name}» не назначены ссылки в Инспекторе.", this);
                enabled = false;
                return false;
            }

            glow.raycastTarget = false;
            frame.raycastTarget = false;
            portrait.raycastTarget = false;
            banStrike.raycastTarget = false;
            portraitGroup.blocksRaycasts = false;
            portraitGroup.interactable = false;

            if (placeholderText != null)
            {
                placeholderText.raycastTarget = false;
            }

            if (halo != null)
            {
                halo.raycastTarget = false;
            }

            if (nameText != null)
            {
                nameText.raycastTarget = false;
            }

            _isInitialized = true;
            return true;
        }
    }
}
