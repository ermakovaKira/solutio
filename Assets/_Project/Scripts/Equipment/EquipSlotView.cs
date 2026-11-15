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
    /// Получатель событий Drag-and-Drop экрана экипировки (реализует EquipViewController).
    /// Ячейки и предметы только сообщают о действиях указателя, решения принимает хост.
    /// </summary>
    public interface IEquipDragHost
    {
        /// <summary>Можно ли начать перетаскивание предмета из ячейки.</summary>
        bool CanBeginDrag(EquipSlotView source);

        /// <summary>Начато перетаскивание предмета из ячейки.</summary>
        void BeginItemDrag(EquipSlotView source, PointerEventData eventData);

        /// <summary>Указатель перемещается во время перетаскивания.</summary>
        void DragItem(PointerEventData eventData);

        /// <summary>Перетаскивание завершено (вызывается после DropOnSlot, если сброс был).</summary>
        void EndItemDrag(PointerEventData eventData);

        /// <summary>Предмет сброшен на ячейку.</summary>
        void DropOnSlot(EquipSlotView target);

        /// <summary>Указатель вошёл в ячейку или покинул её.</summary>
        void HoverSlot(EquipSlotView slot, bool entered);

        /// <summary>Указатель вошёл на предмет или покинул его (подсказка).</summary>
        void HoverItem(EquipSlotView slot, bool entered);

        /// <summary>Клик по предмету (двойной — быстрая экипировка, правый — снять).</summary>
        void ClickItem(EquipSlotView slot, PointerEventData eventData);
    }

    /// <summary>
    /// Ячейка экрана экипировки: слот героя или ячейка хранилища.
    /// Принимает сброс предмета (IDropHandler) и показывает подсказку перетаскивания цветом рамки.
    /// Внутри лежит EquipItemView, который виден только при заполненной ячейке (через CanvasGroup).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipSlotView : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("References")]
        [SerializeField] private RectTransform content;
        [SerializeField] private Image background;
        [SerializeField] private Image frame;
        [SerializeField] private Image hintGlow;
        [SerializeField] private TMP_Text placeholderText;
        [SerializeField] private EquipItemView itemView;

        [Header("Hint")]
        [SerializeField, Range(0f, 1f)] private float availableGlowAlpha = 0.25f;
        [SerializeField, Range(0f, 1f)] private float activeGlowAlpha = 0.85f;
        [SerializeField, Range(0.05f, 0.5f)] private float hintDuration = 0.12f;

        [Header("Placeholder")]
        [SerializeField] private string placeholder = "+";

        private IEquipDragHost _host;
        private Tween _hintTween;
        private Tween _punchTween;
        private bool _isInitialized;

        /// <summary>Адрес ячейки в раскладке.</summary>
        public EquipLocation Location { get; private set; }

        /// <summary>Предмет в ячейке или null.</summary>
        public ItemRecord Item { get; private set; }

        /// <summary>Иконка текущего предмета.</summary>
        public Sprite ItemIcon { get; private set; }

        /// <summary>true, если в ячейке есть предмет.</summary>
        public bool IsFilled => Item != null;

        /// <summary>Текущая подсказка перетаскивания.</summary>
        public EquipDropHint Hint { get; private set; }

        /// <summary>Прямоугольник ячейки (для полёта призрака предмета).</summary>
        public RectTransform Rect => content != null ? content : (RectTransform)transform;

        /// <summary>Представление предмета внутри ячейки.</summary>
        public EquipItemView ItemView => itemView;

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

        /// <summary>Назначает ячейке адрес и получателя событий.</summary>
        public void Setup(EquipLocation location, IEquipDragHost host)
        {
            if (!Initialize())
            {
                return;
            }

            Location = location;
            _host = host;
            itemView.AttachTo(this);
        }

        /// <summary>Обновляет только адрес (индексы хранилища смещаются после перемещений).</summary>
        public void SetLocation(EquipLocation location)
        {
            Location = location;
        }

        /// <summary>Кладёт предмет в ячейку.</summary>
        public void Bind(ItemRecord item, Sprite icon)
        {
            if (!Initialize())
            {
                return;
            }

            if (item == null)
            {
                Clear();
                return;
            }

            Item = item;
            ItemIcon = icon;
            itemView.Bind(item, icon);
            placeholderText.enabled = false;
            background.color = ItemRarityPalette.GetRarityBackground(item.Rarity);
            ApplyFrameColor();
        }

        /// <summary>Очищает ячейку.</summary>
        public void Clear()
        {
            if (!Initialize())
            {
                return;
            }

            Item = null;
            ItemIcon = null;
            itemView.Unbind();
            placeholderText.enabled = true;
            placeholderText.SetText(placeholder);
            background.color = ItemRarityPalette.SlotEmpty;
            ApplyFrameColor();
        }

        /// <summary>Подсветка ячейки во время перетаскивания.</summary>
        public void SetDropHint(EquipDropHint hint, bool instant = false)
        {
            if (!Initialize() || Hint == hint)
            {
                return;
            }

            Hint = hint;
            ApplyFrameColor();

            float targetAlpha;

            switch (hint)
            {
                case EquipDropHint.None:
                    targetAlpha = 0f;
                    break;
                case EquipDropHint.Available:
                    targetAlpha = availableGlowAlpha;
                    break;
                default:
                    targetAlpha = activeGlowAlpha;
                    break;
            }

            Color glowColor = ItemRarityPalette.GetHintColor(hint);
            glowColor.a = hintGlow.color.a;
            hintGlow.color = glowColor;

            _hintTween?.Kill();

            if (instant)
            {
                SetAlpha(hintGlow, targetAlpha);
                return;
            }

            _hintTween = hintGlow
                .DOFade(targetAlpha, hintDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        /// <summary>Короткий «толчок» ячейки при успешном сбросе.</summary>
        public void PlayAcceptPunch()
        {
            if (!Initialize())
            {
                return;
            }

            _punchTween?.Kill();
            content.localScale = Vector3.one;
            _punchTween = content
                .DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.25f, 6, 0.6f)
                .Owned(gameObject)
                .OnKill(() => content.localScale = Vector3.one);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_host != null && eventData != null && eventData.button == PointerEventData.InputButton.Left)
            {
                _host.DropOnSlot(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _host?.HoverSlot(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _host?.HoverSlot(this, false);
        }

        // Вызывается EquipItemView: события предмета передаются хосту от имени ячейки.
        internal bool CanBeginDrag()
        {
            return _host != null && IsFilled && _host.CanBeginDrag(this);
        }

        internal void ForwardBeginDrag(PointerEventData eventData)
        {
            _host?.BeginItemDrag(this, eventData);
        }

        internal void ForwardDrag(PointerEventData eventData)
        {
            _host?.DragItem(eventData);
        }

        internal void ForwardEndDrag(PointerEventData eventData)
        {
            _host?.EndItemDrag(eventData);
        }

        internal void ForwardHover(bool entered)
        {
            _host?.HoverItem(this, entered);
        }

        internal void ForwardClick(PointerEventData eventData)
        {
            _host?.ClickItem(this, eventData);
        }

        private void ApplyFrameColor()
        {
            if (Hint != EquipDropHint.None)
            {
                frame.color = ItemRarityPalette.GetHintColor(Hint);
                return;
            }

            frame.color = Item != null
                ? ItemRarityPalette.GetRarityColor(Item.Rarity)
                : (Color)ItemRarityPalette.SlotFrameIdle;
        }

        private bool Initialize()
        {
            if (_isInitialized)
            {
                return true;
            }

            if (content == null)
            {
                content = (RectTransform)transform;
            }

            if (background == null || frame == null || hintGlow == null || placeholderText == null || itemView == null)
            {
                Debug.LogError($"[EquipSlotView] Не назначены ссылки на элементы ячейки «{name}».", this);
                return false;
            }

            hintGlow.raycastTarget = false;
            SetAlpha(hintGlow, 0f);
            Hint = EquipDropHint.None;
            _isInitialized = true;
            return true;
        }

        private void KillTweens()
        {
            _hintTween?.Kill();
            _punchTween?.Kill();
            _hintTween = null;
            _punchTween = null;

            if (content != null)
            {
                content.localScale = Vector3.one;
            }
        }

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
