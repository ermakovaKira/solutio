using System;
using DG.Tweening;
using Solutio.Heroes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Draft
{
    /// <summary>
    /// Панель выбранного героя внизу экрана драфта (аналог Select → Lock In в Dota 2):
    /// портрет, имя, роль, базовые характеристики и кнопка подтверждения «ВЫБРАТЬ» / «ЗАБАНИТЬ».
    /// Показ и скрытие через CanvasGroup и DOTween с SetUpdate(true).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DraftSelectionPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image halo;
        [SerializeField] private Image portrait;
        [SerializeField] private Image portraitFrame;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text roleText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Image confirmButtonImage;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Image confirmGlow;

        [Header("Animation")]
        [SerializeField, Range(0.05f, 1f)] private float fadeDuration = 0.2f;

        /// <summary>Нажата кнопка подтверждения бана или пика.</summary>
        public event Action OnConfirmClicked;

        /// <summary>Выбранный герой или null.</summary>
        public HeroRecord Hero { get; private set; }

        /// <summary>true, если панель показана.</summary>
        public bool IsVisible { get; private set; }

        private Tween _fadeTween;
        private Tween _glowTween;
        private bool _isInitialized;

        private void Awake()
        {
            if (Initialize())
            {
                HideInstant();
            }
        }

        private void OnDestroy()
        {
            _fadeTween?.Kill();
            _glowTween?.Kill();

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(HandleConfirmClicked);
            }
        }

        /// <summary>
        /// Показывает выбранного героя и настраивает кнопку под тип хода.
        /// </summary>
        public void Show(HeroRecord hero, Sprite portraitSprite, Color portraitTint, DraftActionType action)
        {
            if (!Initialize() || hero == null)
            {
                return;
            }

            Hero = hero;

            bool isBan = action == DraftActionType.Ban;
            Color accent = isBan ? (Color)DraftPalette.Critical : DraftPalette.Sapphire;

            portrait.sprite = portraitSprite;
            portrait.color = portraitTint;
            portraitFrame.color = isBan ? (Color)DraftPalette.Critical : DraftPalette.SapphireGlow;

            Color haloColor = DraftPalette.GetRoleColor(hero.Role);
            haloColor.a = 0.55f;
            halo.color = haloColor;

            Color glowColor = isBan ? (Color)DraftPalette.Critical : DraftPalette.SapphireGlow;
            glowColor.a = 0.25f;
            confirmGlow.color = glowColor;
            _glowTween?.Kill();
            _glowTween = confirmGlow
                .DOFade(0.85f, 0.8f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .Owned(gameObject);

            nameText.SetText(hero.Name);
            roleText.SetText(HeroRoleInfo.GetDisplayName(hero.Role));
            roleText.color = DraftPalette.GetRoleColor(hero.Role);
            statsText.SetText("Атака {0:0}   ·   Броня {1:0}   ·   Здоровье {2:0}", hero.BaseAttack, hero.BaseArmor, hero.BaseHp);

            confirmButtonImage.color = accent;
            confirmLabel.SetText(isBan ? "ЗАБАНИТЬ" : "ВЫБРАТЬ");

            IsVisible = true;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            _fadeTween?.Kill();
            _fadeTween = canvasGroup
                .DOFade(1f, fadeDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        /// <summary>Плавно скрывает панель.</summary>
        public void Hide()
        {
            if (!Initialize())
            {
                return;
            }

            Hero = null;
            IsVisible = false;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            _glowTween?.Kill();
            _glowTween = null;
            _fadeTween?.Kill();
            _fadeTween = canvasGroup
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad)
                .Owned(gameObject);
        }

        /// <summary>Мгновенно скрывает панель.</summary>
        public void HideInstant()
        {
            if (!Initialize())
            {
                return;
            }

            _fadeTween?.Kill();
            _glowTween?.Kill();
            _glowTween = null;
            Hero = null;
            IsVisible = false;
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private void HandleConfirmClicked()
        {
            if (IsVisible && Hero != null)
            {
                OnConfirmClicked?.Invoke();
            }
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

            if (halo == null || confirmGlow == null || portrait == null || portraitFrame == null || nameText == null || roleText == null || statsText == null
                || confirmButton == null || confirmButtonImage == null || confirmLabel == null)
            {
                Debug.LogError("[DraftSelectionPanel] Не назначены ссылки в Инспекторе.", this);
                enabled = false;
                return false;
            }

            halo.raycastTarget = false;
            confirmGlow.raycastTarget = false;
            portrait.raycastTarget = false;
            portraitFrame.raycastTarget = false;
            confirmButton.onClick.AddListener(HandleConfirmClicked);

            _isInitialized = true;
            return true;
        }
    }
}
