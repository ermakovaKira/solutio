using DG.Tweening;
using Solutio.Draft;
using Solutio.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solutio.Equipment
{
    /// <summary>
    /// Предмет внутри ячейки: иконка, свечение цвета редкости и главный бонус.
    /// Источник Drag-and-Drop: сам предмет не перемещается — во время перетаскивания он полупрозрачен,
    /// а за курсором летит призрак на верхнем слое канваса (EquipDragGhost).
    /// Видимость — через CanvasGroup, без SetActive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class EquipItemView : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform body;
        [SerializeField] private Image glow;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text bonusText;

        [Header("Animation")]
        [SerializeField, Range(1f, 1.3f)] private float hoverScale = 1.08f;
        [SerializeField, Range(0f, 1f)] private float idleGlowAlpha = 0.35f;
        [SerializeField, Range(0f, 1f)] private float hoverGlowAlpha = 0.9f;
        [SerializeField, Range(0f, 1f)] private float draggingAlpha = 0.3f;
        [SerializeField, Range(0.05f, 0.5f)] private float hoverDuration = 0.12f;

        private EquipSlotView _owner;
        private Tween _scaleTween;
        private Tween _glowTween;
        private Tween _alphaTween;
        private bool _isBound;
        private bool _isHovered;
        private bool _isInitialized;

        // Жизненный цикл перетаскивания (OnBeginDrag → OnEndDrag). Не сбрасывается в Bind/Unbind:
        // во время сброса контроллер перерисовывает ячейки, и исходная ячейка получает другой предмет
        // или пустеет — но OnEndDrag всё равно должен дойти до хоста.
        private bool _isPointerDragging;

        /// <summary>Отображаемый предмет или null.</summary>
        public ItemRecord Item { get; private set; }

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            KillTweens();
            _isHovered = false;

            if (_isPointerDragging)
            {
                _isPointerDragging = false;
                _owner?.ForwardEndDrag(null);
            }
        }

        private void OnDestroy()
        {
            KillTweens();
        }

        /// <summary>Связывает предмет с ячейкой-владельцем (вызывается из EquipSlotView.Setup).</summary>
        public void AttachTo(EquipSlotView owner)
        {
            _owner = owner;
        }

        /// <summary>Показывает предмет.</summary>
        public void Bind(ItemRecord item, Sprite sprite)
        {
            if (!Initialize())
            {
                return;
            }

            Item = item;
            _isBound = item != null;

            if (!_isBound)
            {
                Unbind();
                return;
            }

            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = ItemRarityPalette.GetRarityGlowColor(item.Rarity);

            Color glowColor = ItemRarityPalette.GetRarityGlowColor(item.Rarity);
            glowColor.a = idleGlowAlpha;
            glow.color = glowColor;

            bonusText.SetText(FormatMainBonus(item));

            KillTweens();
            body.localScale = Vector3.one;
            _isHovered = false;

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        /// <summary>Скрывает предмет (ячейка пуста).</summary>
        public void Unbind()
        {
            if (!Initialize())
            {
                return;
            }

            Item = null;
            _isBound = false;
            _isHovered = false;

            KillTweens();
            body.localScale = Vector3.one;
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        /// <summary>Полупрозрачность исходного предмета, пока за курсором летит призрак.</summary>
        public void SetDragging(bool isDragging)
        {
            if (!Initialize() || !_isBound)
            {
                return;
            }

            _alphaTween?.Kill();
            _alphaTween = canvasGroup
                .DOFade(isDragging ? draggingAlpha : 1f, hoverDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);

            if (isDragging)
            {
                AnimateHover(false);
            }
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Небольшой порог, чтобы клик и двойной клик не превращались в перетаскивание.
            eventData.useDragThreshold = true;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_isBound || _owner == null || eventData.button != PointerEventData.InputButton.Left || !_owner.CanBeginDrag())
            {
                // Отмена: EventSystem не будет присылать OnDrag/OnEndDrag этому объекту.
                eventData.pointerDrag = null;
                return;
            }

            _isPointerDragging = true;
            _owner.ForwardHover(false);
            _owner.ForwardBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_isPointerDragging)
            {
                _owner.ForwardDrag(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isPointerDragging)
            {
                return;
            }

            _isPointerDragging = false;
            _owner.ForwardEndDrag(eventData);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isBound || _isPointerDragging || eventData.dragging)
            {
                return;
            }

            _isHovered = true;
            AnimateHover(true);
            _owner?.ForwardHover(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_isHovered)
            {
                return;
            }

            _isHovered = false;
            AnimateHover(false);
            _owner?.ForwardHover(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_isBound && !_isPointerDragging)
            {
                _owner?.ForwardClick(eventData);
            }
        }

        /// <summary>Главный бонус предмета для подписи под иконкой, например «+14 АТК».</summary>
        public static string FormatMainBonus(ItemRecord item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            // Сравнение по «весу» характеристики: 1 брони ≈ 10 ОЗ ≈ 1.5 атаки (только для подписи).
            float attack = item.BonusAttack * 1.5f;
            float armor = item.BonusArmor * 10f;
            float hp = item.BonusHp;

            if (attack >= armor && attack >= hp && item.BonusAttack > 0)
            {
                return $"+{item.BonusAttack} АТК";
            }

            if (armor >= hp && item.BonusArmor > 0)
            {
                return $"+{item.BonusArmor} БРН";
            }

            return item.BonusHp > 0 ? $"+{item.BonusHp} ЗДР" : string.Empty;
        }

        private void AnimateHover(bool hovered)
        {
            _scaleTween?.Kill();
            _scaleTween = body
                .DOScale(hovered ? hoverScale : 1f, hoverDuration)
                .SetEase(hovered ? Ease.OutBack : Ease.OutQuad)
                .Owned(gameObject);

            _glowTween?.Kill();
            _glowTween = glow
                .DOFade(hovered ? hoverGlowAlpha : idleGlowAlpha, hoverDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
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

            if (body == null)
            {
                body = (RectTransform)transform;
            }

            if (glow == null || icon == null || bonusText == null)
            {
                Debug.LogError($"[EquipItemView] Не назначены ссылки на элементы предмета «{name}».", this);
                return false;
            }

            glow.raycastTarget = false;
            bonusText.raycastTarget = false;
            _isInitialized = true;
            return true;
        }

        private void KillTweens()
        {
            _scaleTween?.Kill();
            _glowTween?.Kill();
            _alphaTween?.Kill();
            _scaleTween = null;
            _glowTween = null;
            _alphaTween = null;
        }
    }
}
