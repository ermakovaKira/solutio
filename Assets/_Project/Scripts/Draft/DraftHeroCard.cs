using System;
using DG.Tweening;
using Solutio.Heroes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solutio.Draft
{
    /// <summary>Визуальное состояние карточки героя в сетке драфта.</summary>
    public enum DraftCardState
    {
        Available = 0,
        Selected = 1,
        PickedByUser = 2,
        PickedByAi = 3,
        Banned = 4
    }

    /// <summary>
    /// Графическая карточка героя в сетке драфта с эффектом свечения.
    /// Слои: внешнее свечение (glow), фон, ореол цвета роли за портретом (halo), портрет,
    /// нижняя плашка имени, блик (shine), затемнение (shade), лента состояния и рамка.
    /// Наведение — подъём карточки, свечение и пробегающий блик; выбор — пульсация свечения;
    /// пик пользователя — сапфир, пик ИИ — багрянец, бан — затемнение и перечёркивание.
    /// Все анимации DOTween с SetUpdate(true) и без переиспользования твинов. Видимость через CanvasGroup.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DraftHeroCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform content;
        [SerializeField] private Image glow;
        [SerializeField] private Image frame;
        [SerializeField] private Image halo;
        [SerializeField] private Image portrait;
        [SerializeField] private Image roleStripe;
        [SerializeField] private Image shade;
        [SerializeField] private RectTransform shine;
        [SerializeField] private Image shineImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private CanvasGroup stateOverlay;
        [SerializeField] private Image stateOverlayBackground;
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private Image banStrike;

        [Header("Glow")]
        [SerializeField, Range(0f, 1f)] private float hoverGlowAlpha = 0.8f;
        [SerializeField, Range(0f, 1f)] private float selectedGlowMin = 0.6f;
        [SerializeField, Range(0f, 1f)] private float selectedGlowMax = 1f;
        [SerializeField, Range(0f, 1f)] private float pickedGlowAlpha = 0.55f;
        [SerializeField, Range(0f, 1f)] private float idleHaloAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float activeHaloAlpha = 0.7f;
        [SerializeField, Range(0.2f, 3f)] private float pulseDuration = 0.7f;

        [Header("Animation")]
        [SerializeField, Range(1f, 1.2f)] private float hoverScale = 1.07f;
        [SerializeField, Range(0f, 20f)] private float hoverLift = 8f;
        [SerializeField, Range(0.05f, 0.5f)] private float hoverDuration = 0.18f;
        [SerializeField, Range(0.1f, 1.5f)] private float shineDuration = 0.5f;
        [SerializeField, Range(0f, 0.4f)] private float lockPunch = 0.15f;
        [SerializeField, Range(0f, 1f)] private float pickedShadeAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float bannedShadeAlpha = 0.7f;

        /// <summary>Клик левой кнопкой по доступной карточке.</summary>
        public event Action<DraftHeroCard> OnClicked;

        /// <summary>Герой, привязанный к карточке, или null.</summary>
        public HeroRecord Hero { get; private set; }

        /// <summary>Текущее визуальное состояние.</summary>
        public DraftCardState State { get; private set; }

        /// <summary>true, если к карточке привязан герой.</summary>
        public bool IsBound => Hero != null;

        /// <summary>true, если героя ещё можно забанить или выбрать.</summary>
        public bool IsAvailable => IsBound && (State == DraftCardState.Available || State == DraftCardState.Selected);

        private Color _actionGlowColor = DraftPalette.SapphireGlow;
        private Color _portraitTint = Color.white;
        private Color _roleColor = Color.white;
        private bool _isInteractable;
        private bool _isHovered;
        private bool _isInitialized;
        private float _shineTravel;
        private Tween _glowTween;
        private Tween _scaleTween;
        private Tween _liftTween;
        private Tween _haloTween;
        private Tween _shadeTween;
        private Tween _shineTween;
        private Tween _shineFadeTween;

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
        /// Привязывает героя к карточке и сбрасывает состояние в «доступен».
        /// </summary>
        public void Bind(HeroRecord hero, Sprite portraitSprite, Color portraitTint)
        {
            if (!Initialize())
            {
                return;
            }

            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            _portraitTint = portraitTint;
            _roleColor = DraftPalette.GetRoleColor(hero.Role);

            portrait.sprite = portraitSprite;
            nameText.SetText(hero.Name);
            roleStripe.color = _roleColor;

            KillTweens();
            _isHovered = false;
            content.localScale = Vector3.one;
            content.anchoredPosition = Vector2.zero;

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            SetState(DraftCardState.Available, true);
        }

        /// <summary>
        /// Отвязывает героя и полностью скрывает карточку (лишняя ячейка сетки).
        /// </summary>
        public void Unbind()
        {
            if (!Initialize())
            {
                return;
            }

            KillTweens();

            Hero = null;
            _isHovered = false;
            content.localScale = Vector3.one;
            content.anchoredPosition = Vector2.zero;
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        /// <summary>
        /// Разрешает или запрещает пользователю взаимодействие с карточкой (ход ИИ, конец драфта).
        /// </summary>
        public void SetInteractable(bool isInteractable)
        {
            if (!Initialize())
            {
                return;
            }

            _isInteractable = isInteractable;
            canvasGroup.interactable = isInteractable && IsAvailable;

            if (!isInteractable && _isHovered)
            {
                ExitHover();
            }
        }

        /// <summary>
        /// Задаёт цвет свечения под текущий тип хода: сапфир для пика, красный для бана.
        /// </summary>
        public void SetActionHint(DraftActionType action)
        {
            if (!Initialize())
            {
                return;
            }

            _actionGlowColor = action == DraftActionType.Ban ? (Color)DraftPalette.Critical : DraftPalette.SapphireGlow;

            if (State == DraftCardState.Available || State == DraftCardState.Selected)
            {
                SetColorKeepAlpha(glow, _actionGlowColor);
            }

            if (State == DraftCardState.Selected)
            {
                frame.color = _actionGlowColor;
            }
        }

        /// <summary>
        /// Переводит карточку в новое визуальное состояние.
        /// </summary>
        public void SetState(DraftCardState state, bool instant = false)
        {
            if (!Initialize())
            {
                return;
            }

            DraftCardState previousState = State;
            State = state;
            KillTween(ref _glowTween);

            switch (state)
            {
                case DraftCardState.Available:
                    ApplyAvailable(instant);
                    break;
                case DraftCardState.Selected:
                    ApplySelected();
                    break;
                case DraftCardState.PickedByUser:
                    ApplyTaken(DraftPalette.SapphireGlow, DraftPalette.Sapphire, "ВЫ", false, instant);
                    break;
                case DraftCardState.PickedByAi:
                    ApplyTaken(DraftPalette.CrimsonGlow, DraftPalette.Crimson, "ИИ", false, instant);
                    break;
                case DraftCardState.Banned:
                    ApplyTaken(DraftPalette.Critical, DraftPalette.Obsidian, "БАН", true, instant);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Неизвестное состояние карточки.");
            }

            canvasGroup.interactable = _isInteractable && IsAvailable;

            if (IsLocked(state) && !IsLocked(previousState) && !instant)
            {
                PlayLockPunch();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isInitialized || !_isInteractable || !IsAvailable)
            {
                return;
            }

            _isHovered = true;
            AnimateHover(hoverScale, hoverLift);
            PlayShine();
            FadeImage(ref _haloTween, halo, activeHaloAlpha, hoverDuration);

            if (State == DraftCardState.Available)
            {
                SetColorKeepAlpha(glow, _actionGlowColor);
                FadeImage(ref _glowTween, glow, hoverGlowAlpha, hoverDuration);
                frame.color = Color.Lerp(DraftPalette.FrameIdle, _actionGlowColor, 0.6f);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_isHovered)
            {
                ExitHover();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (!_isInteractable || !IsAvailable)
            {
                return;
            }

            OnClicked?.Invoke(this);
        }

        #region State Visuals

        private void ApplyAvailable(bool instant)
        {
            frame.color = _isHovered ? Color.Lerp(DraftPalette.FrameIdle, _actionGlowColor, 0.6f) : (Color)DraftPalette.FrameIdle;
            portrait.color = _portraitTint;
            banStrike.enabled = false;
            stateOverlay.alpha = 0f;

            SetColorKeepAlpha(glow, _actionGlowColor);
            SetColorKeepAlpha(halo, _roleColor);

            float glowTarget = _isHovered ? hoverGlowAlpha : 0f;
            float haloTarget = _isHovered ? activeHaloAlpha : idleHaloAlpha;

            if (instant)
            {
                SetAlpha(glow, glowTarget);
                SetAlpha(halo, haloTarget);
                SetAlpha(shade, 0f);
                return;
            }

            FadeImage(ref _glowTween, glow, glowTarget, hoverDuration);
            FadeImage(ref _haloTween, halo, haloTarget, hoverDuration);
            FadeImage(ref _shadeTween, shade, 0f, hoverDuration);
        }

        private void ApplySelected()
        {
            frame.color = _actionGlowColor;
            portrait.color = _portraitTint;
            banStrike.enabled = false;
            stateOverlay.alpha = 0f;

            SetColorKeepAlpha(halo, _roleColor);
            FadeImage(ref _haloTween, halo, activeHaloAlpha, hoverDuration);
            FadeImage(ref _shadeTween, shade, 0f, hoverDuration);

            SetColorKeepAlpha(glow, _actionGlowColor);
            SetAlpha(glow, selectedGlowMin);

            _glowTween = glow
                .DOFade(selectedGlowMax, pulseDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .Owned(gameObject);
        }

        private void ApplyTaken(Color accentColor, Color ribbonColor, string label, bool isBan, bool instant)
        {
            frame.color = accentColor;

            portrait.color = isBan ? new Color(0.5f, 0.5f, 0.5f, 1f) : _portraitTint;
            banStrike.enabled = isBan;
            banStrike.color = accentColor;

            Color ribbon = ribbonColor;
            ribbon.a = 0.92f;
            stateOverlayBackground.color = ribbon;
            stateText.SetText(label);
            stateText.color = isBan ? accentColor : (Color)DraftPalette.TextBright;

            // Лента состояния выставляется сразу — она должна быть видна при любом порядке вызовов
            stateOverlay.alpha = 1f;

            SetColorKeepAlpha(glow, accentColor);
            SetColorKeepAlpha(halo, isBan ? (Color)DraftPalette.Muted : accentColor);

            float shadeTarget = isBan ? bannedShadeAlpha : pickedShadeAlpha;
            float haloTarget = isBan ? 0.1f : idleHaloAlpha;

            if (instant)
            {
                SetAlpha(glow, pickedGlowAlpha);
                SetAlpha(halo, haloTarget);
                SetAlpha(shade, shadeTarget);
            }
            else
            {
                FadeImage(ref _glowTween, glow, pickedGlowAlpha, 0.25f);
                FadeImage(ref _haloTween, halo, haloTarget, 0.25f);
                FadeImage(ref _shadeTween, shade, shadeTarget, 0.25f);
            }

            if (_isHovered)
            {
                _isHovered = false;
                AnimateHover(1f, 0f);
            }
        }

        private void ExitHover()
        {
            _isHovered = false;
            AnimateHover(1f, 0f);

            if (State == DraftCardState.Available)
            {
                FadeImage(ref _glowTween, glow, 0f, hoverDuration);
                FadeImage(ref _haloTween, halo, idleHaloAlpha, hoverDuration);
                frame.color = DraftPalette.FrameIdle;
            }
        }

        private static bool IsLocked(DraftCardState state)
        {
            return state == DraftCardState.PickedByUser || state == DraftCardState.PickedByAi || state == DraftCardState.Banned;
        }

        #endregion

        #region Tweens

        private void FadeImage(ref Tween tween, Image image, float alpha, float duration)
        {
            KillTween(ref tween);
            tween = image
                .DOFade(alpha, duration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        private void AnimateHover(float targetScale, float lift)
        {
            KillTween(ref _scaleTween);
            KillTween(ref _liftTween);

            _scaleTween = content
                .DOScale(targetScale, hoverDuration)
                .SetEase(Ease.OutBack)
                .Owned(gameObject);

            _liftTween = content
                .DOAnchorPosY(lift, hoverDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        private void PlayShine()
        {
            KillTween(ref _shineTween);
            KillTween(ref _shineFadeTween);

            shine.anchoredPosition = new Vector2(-_shineTravel, 0f);
            SetAlpha(shineImage, 0.55f);

            _shineTween = shine
                .DOAnchorPosX(_shineTravel, shineDuration)
                .SetEase(Ease.OutCubic)
                .Owned(gameObject);

            _shineFadeTween = shineImage
                .DOFade(0f, shineDuration)
                .SetEase(Ease.InQuad)
                .Owned(gameObject);
        }

        private void PlayLockPunch()
        {
            KillTween(ref _scaleTween);
            KillTween(ref _liftTween);
            content.localScale = Vector3.one;
            content.anchoredPosition = Vector2.zero;

            _scaleTween = content
                .DOPunchScale(Vector3.one * lockPunch, 0.35f, 6, 0.6f)
                .Owned(gameObject);
        }

        private static void KillTween(ref Tween tween)
        {
            if (tween != null)
            {
                tween.Kill();
                tween = null;
            }
        }

        private void KillTweens()
        {
            KillTween(ref _glowTween);
            KillTween(ref _scaleTween);
            KillTween(ref _liftTween);
            KillTween(ref _haloTween);
            KillTween(ref _shadeTween);
            KillTween(ref _shineTween);
            KillTween(ref _shineFadeTween);
        }

        #endregion

        #region Helpers

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        private static void SetColorKeepAlpha(Graphic graphic, Color color)
        {
            color.a = graphic.color.a;
            graphic.color = color;
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

            bool isValid = content != null && glow != null && frame != null && halo != null && portrait != null
                           && roleStripe != null && shade != null && shine != null && shineImage != null
                           && nameText != null && stateOverlay != null && stateOverlayBackground != null
                           && stateText != null && banStrike != null;

            if (!isValid)
            {
                Debug.LogError($"[DraftHeroCard] На карточке «{name}» не назначены ссылки в Инспекторе.", this);
                enabled = false;
                return false;
            }

            glow.raycastTarget = false;
            frame.raycastTarget = false;
            halo.raycastTarget = false;
            portrait.raycastTarget = false;
            roleStripe.raycastTarget = false;
            shade.raycastTarget = false;
            shineImage.raycastTarget = false;
            nameText.raycastTarget = false;
            stateOverlayBackground.raycastTarget = false;
            stateText.raycastTarget = false;
            banStrike.raycastTarget = false;
            stateOverlay.blocksRaycasts = false;
            stateOverlay.interactable = false;

            _shineTravel = ((RectTransform)transform).rect.width + shine.rect.width;
            SetAlpha(glow, 0f);
            SetAlpha(shade, 0f);
            SetAlpha(shineImage, 0f);
            stateOverlay.alpha = 0f;
            banStrike.enabled = false;

            _isInitialized = true;
            return true;
        }

        #endregion
    }
}
