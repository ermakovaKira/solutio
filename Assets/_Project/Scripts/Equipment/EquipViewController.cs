using System;
using System.Collections.Generic;
using DG.Tweening;
using Solutio.Draft;
using Solutio.Heroes;
using Solutio.Items;
using Solutio.Session;
using Solutio.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solutio.Equipment
{
    /// <summary>Иконка конкретного предмета (заменяет глиф характеристики, когда появятся арты).</summary>
    [Serializable]
    public struct ItemIconEntry
    {
        public int itemId;
        public Sprite icon;
    }

    /// <summary>
    /// Представление экрана предматчевой подготовки (День 13): 5 колонок героев пользователя
    /// по 4 слота, хранилище стартовых предметов, подсказка, призрак перетаскивания
    /// и панель команды ИИ (экипировка противника скрыта до боя).
    /// Хост Drag-and-Drop: проверяет перемещения через EquipmentLoadout.Preview (только чтение),
    /// а изменения запрашивает событиями — раскладку меняет EquipmentController и вызывает Refresh.
    /// Показ и скрытие через CanvasGroup и DOTween с SetUpdate(true), без SetActive.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class EquipViewController : MonoBehaviour, IEquipDragHost
    {
        private const string StashControlsHint = "Перетащи на героя · двойной клик — выбранному герою";
        private const string SlotControlsHint = "Перетащи в другой слот · ПКМ или двойной клик — снять";
        private const string DefaultFooterHint =
            "ЛКМ — перетащить предмет · двойной клик — надеть выбранному герою или снять · ПКМ — снять";

        [Header("Root")]
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField, Range(0.1f, 1.5f)] private float fadeDuration = 0.4f;

        [Header("Header")]
        [SerializeField] private TMP_Text matchInfoText;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private TMP_Text footerHintText;

        [Header("User Team")]
        [SerializeField] private EquipHeroPanel[] heroPanels = Array.Empty<EquipHeroPanel>();

        [Header("Stash")]
        [SerializeField] private EquipSlotView[] stashSlots = Array.Empty<EquipSlotView>();

        [Header("Drag & Tooltip")]
        [SerializeField] private EquipDragGhost dragGhost;
        [SerializeField] private EquipTooltip tooltip;

        [Header("AI Team (экипировка скрыта)")]
        [SerializeField] private Image[] aiPortraits = Array.Empty<Image>();
        [SerializeField] private TMP_Text[] aiNames = Array.Empty<TMP_Text>();
        [SerializeField] private TMP_Text aiStatusText;

        [Header("Buttons")]
        [SerializeField] private Button battleButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private TMP_Text battleButtonText;

        [Header("Portraits")]
        [SerializeField] private Sprite placeholderPortrait;
        [SerializeField] private HeroPortraitEntry[] portraits = Array.Empty<HeroPortraitEntry>();

        [Header("Item Icons")]
        [SerializeField] private Sprite attackGlyph;
        [SerializeField] private Sprite armorGlyph;
        [SerializeField] private Sprite hpGlyph;
        [SerializeField] private ItemIconEntry[] itemIcons = Array.Empty<ItemIconEntry>();

        [Header("Status")]
        [SerializeField, Range(0.5f, 6f)] private float statusSeconds = 2.5f;

        [Header("Debug")]
        [SerializeField] private bool enableDetailedLogging = true;

        /// <summary>Запрос на перемещение предмета (Drag-and-Drop).</summary>
        public event Action<EquipLocation, EquipLocation> OnMoveRequested;

        /// <summary>Запрос быстрой экипировки: индекс в хранилище, индекс героя.</summary>
        public event Action<int, int> OnQuickEquipRequested;

        /// <summary>Запрос снятия предмета: индекс героя, индекс слота.</summary>
        public event Action<int, int> OnUnequipRequested;

        /// <summary>Кнопка «СБРОСИТЬ» — вернуть все предметы в хранилище.</summary>
        public event Action OnResetRequested;

        /// <summary>Кнопка «В БОЙ» — зафиксировать экипировку.</summary>
        public event Action OnBattleRequested;

        /// <summary>Кнопка «ПОКИНУТЬ» — выход в главное меню.</summary>
        public event Action OnLeaveRequested;

        /// <summary>true, если экран показан.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>Конфигурация матча, для которой открыт экран.</summary>
        public MatchSessionConfig Session { get; private set; }

        /// <summary>Индекс героя, выбранного для быстрой экипировки.</summary>
        public int SelectedHeroIndex { get; private set; }

        private readonly Dictionary<int, Sprite> _portraitsByHeroId = new Dictionary<int, Sprite>(32);
        private readonly Dictionary<int, Sprite> _iconsByItemId = new Dictionary<int, Sprite>(32);

        private EquipmentLoadout _loadout;
        private EquipSlotView _dragSource;
        private EquipSlotView _hoverTarget;
        private EquipSlotView _dropTarget;
        private bool _isDragging;
        private bool _dropAccepted;
        private float _statusClearAt = -1f;
        private Tween _fadeTween;
        private bool _isValid;
        private bool _isSubscribed;

        #region Unity Lifecycle

        private void Awake()
        {
            if (rootCanvasGroup == null)
            {
                rootCanvasGroup = GetComponent<CanvasGroup>();
            }

            _isValid = ValidateReferences();
            ApplyHiddenState();

            if (!_isValid)
            {
                return;
            }

            BuildLookups();
        }

        private void OnEnable()
        {
            if (!_isValid || _isSubscribed)
            {
                return;
            }

            battleButton.onClick.AddListener(HandleBattleClicked);
            resetButton.onClick.AddListener(HandleResetClicked);
            leaveButton.onClick.AddListener(HandleLeaveClicked);

            for (int i = 0; i < heroPanels.Length; i++)
            {
                heroPanels[i].OnClicked += HandleHeroPanelClicked;
            }

            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_isSubscribed)
            {
                battleButton.onClick.RemoveListener(HandleBattleClicked);
                resetButton.onClick.RemoveListener(HandleResetClicked);
                leaveButton.onClick.RemoveListener(HandleLeaveClicked);

                for (int i = 0; i < heroPanels.Length; i++)
                {
                    if (heroPanels[i] != null)
                    {
                        heroPanels[i].OnClicked -= HandleHeroPanelClicked;
                    }
                }
            }

            _isSubscribed = false;
            CancelDrag();
            _fadeTween?.Kill();
            _fadeTween = null;
        }

        private void Update()
        {
            // Возврат подсказки внизу экрана после статусного сообщения (метка времени, без корутин).
            if (_statusClearAt > 0f && Time.unscaledTime >= _statusClearAt)
            {
                _statusClearAt = -1f;
                footerHintText.color = DraftPalette.Muted;
                footerHintText.SetText(DefaultFooterHint);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Открывает экран подготовки для раскладки пользователя и состава ИИ.
        /// </summary>
        public void Show(MatchSessionConfig config, EquipmentLoadout loadout, IReadOnlyList<HeroRecord> aiTeam)
        {
            if (!_isValid)
            {
                Debug.LogError($"[EquipView] Экран подготовки не может открыться: ссылки не прошли проверку или у объекта «{name}» не вызывался Awake (ссылка на префаб-ассет вместо объекта сцены?).", this);
                return;
            }

            Session = config ?? throw new ArgumentNullException(nameof(config));
            _loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));

            if (aiTeam == null)
            {
                throw new ArgumentNullException(nameof(aiTeam));
            }

            if (loadout.HeroCount > heroPanels.Length)
            {
                Debug.LogError($"[EquipView] Героев ({loadout.HeroCount}) больше, чем колонок на экране ({heroPanels.Length}).", this);
                return;
            }

            if (loadout.TotalItems > stashSlots.Length)
            {
                Debug.LogWarning($"[EquipView] Предметов ({loadout.TotalItems}) больше, чем ячеек хранилища ({stashSlots.Length}) — лишние не поместятся при возврате.", this);
            }

            CancelDrag();
            BindHeroes();
            BindStash();
            BindAiTeam(aiTeam);
            SelectHero(0);

            matchInfoText.SetText($"{MatchCatalog.GetDifficultyDisplayName(config.Difficulty)}  ·  {config.Map.DisplayName}");
            aiStatusText.SetText("Экипировка противника скрыта до начала боя");
            battleButtonText.SetText("В БОЙ");
            footerHintText.color = DraftPalette.Muted;
            footerHintText.SetText(DefaultFooterHint);
            _statusClearAt = -1f;

            Refresh();

            IsVisible = true;
            rootCanvasGroup.interactable = true;
            rootCanvasGroup.blocksRaycasts = true;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(1f, fadeDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[EquipView]</color> Экран подготовки открыт: героев {loadout.HeroCount}, стартовых предметов {loadout.TotalItems}.");
            }
        }

        /// <summary>Плавно скрывает экран.</summary>
        public void Hide(Action onComplete = null)
        {
            CancelDrag();

            if (_isValid)
            {
                tooltip.HideInstant();
            }

            IsVisible = false;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad)
                .Owned(gameObject)
                .OnComplete(() =>
                {
                    _loadout = null;
                    onComplete?.Invoke();
                });
        }

        /// <summary>Перерисовывает слоты, хранилище, характеристики и счётчики по раскладке.</summary>
        public void Refresh()
        {
            if (!_isValid || _loadout == null)
            {
                return;
            }

            for (int h = 0; h < _loadout.HeroCount; h++)
            {
                EquipHeroPanel panel = heroPanels[h];
                EquipSlotView[] slots = panel.Slots;

                for (int s = 0; s < slots.Length; s++)
                {
                    ItemRecord item = _loadout.GetItem(h, s);

                    if (item != null)
                    {
                        slots[s].Bind(item, GetIcon(item));
                    }
                    else
                    {
                        slots[s].Clear();
                    }
                }

                panel.SetStats(_loadout.GetStats(h));
                panel.SetCapacity(_loadout.CountEquipped(h), EquipmentLoadout.SlotsPerHero);
            }

            IReadOnlyList<ItemRecord> stash = _loadout.Stash;

            for (int i = 0; i < stashSlots.Length; i++)
            {
                stashSlots[i].SetLocation(EquipLocation.InStash(i));

                if (i < stash.Count)
                {
                    stashSlots[i].Bind(stash[i], GetIcon(stash[i]));
                }
                else
                {
                    stashSlots[i].Clear();
                }
            }

            progressText.SetText("Распределено {0} / {1}", _loadout.EquippedItems, _loadout.TotalItems);

            bool unlocked = !_loadout.IsLocked;
            resetButton.interactable = unlocked && _loadout.EquippedItems > 0;
            battleButton.interactable = unlocked;
        }

        /// <summary>Кратко показывает сообщение вместо подсказки внизу экрана.</summary>
        public void ShowStatus(string message, bool isWarning)
        {
            if (!_isValid)
            {
                return;
            }

            footerHintText.color = isWarning ? (Color)DraftPalette.Warning : (Color)DraftPalette.SapphireGlow;
            footerHintText.SetText(message);
            _statusClearAt = Time.unscaledTime + statusSeconds;
        }

        /// <summary>Экипировка зафиксирована: перетаскивание и кнопки отключены.</summary>
        public void SetLocked(string statusMessage)
        {
            if (!_isValid)
            {
                return;
            }

            CancelDrag();
            tooltip.HideInstant();
            battleButtonText.SetText("ГОТОВО");
            Refresh();

            footerHintText.color = DraftPalette.SapphireGlow;
            footerHintText.SetText(statusMessage);
            _statusClearAt = -1f;
        }

        /// <summary>Подсвечивает ячейку, в которую только что лёг предмет (после быстрой экипировки).</summary>
        public void PunchSlot(int heroIndex, int slotIndex)
        {
            if (_isValid && heroIndex >= 0 && heroIndex < heroPanels.Length
                && slotIndex >= 0 && slotIndex < EquipmentLoadout.SlotsPerHero)
            {
                heroPanels[heroIndex].Slots[slotIndex].PlayAcceptPunch();
            }
        }

        #endregion

        #region IEquipDragHost

        public bool CanBeginDrag(EquipSlotView source)
        {
            return IsVisible && _loadout != null && !_loadout.IsLocked && source != null && source.IsFilled;
        }

        public void BeginItemDrag(EquipSlotView source, PointerEventData eventData)
        {
            if (!CanBeginDrag(source))
            {
                return;
            }

            // Страховка: если прошлое перетаскивание не получило OnEndDrag, состояние сбрасывается.
            if (_isDragging)
            {
                Debug.LogWarning("[EquipView] Предыдущее перетаскивание не завершилось — состояние сброшено.", this);
                CancelDrag();
            }

            _isDragging = true;
            _dragSource = source;
            _hoverTarget = null;
            _dropTarget = null;
            _dropAccepted = false;

            tooltip.HideInstant();
            source.ItemView.SetDragging(true);
            dragGhost.Show(source.Item, source.ItemIcon, source.Rect, eventData);
            ApplyBaselineHints();
        }

        public void DragItem(PointerEventData eventData)
        {
            if (_isDragging)
            {
                dragGhost.Follow(eventData);
            }
        }

        public void DropOnSlot(EquipSlotView target)
        {
            if (!_isDragging || _dragSource == null || target == null || _loadout == null)
            {
                return;
            }

            EquipLocation from = _dragSource.Location;
            EquipLocation to = ToTarget(target);
            ItemRecord movingItem = _dragSource.Item;
            int version = _loadout.Version;

            OnMoveRequested?.Invoke(from, to);

            _dropAccepted = _loadout.Version != version;
            _dropTarget = _dropAccepted ? FindCellWithItem(movingItem, to) : null;
        }

        public void EndItemDrag(PointerEventData eventData)
        {
            if (!_isDragging)
            {
                return;
            }

            EquipSlotView source = _dragSource;
            EquipSlotView landing = _dropTarget;
            bool accepted = _dropAccepted;

            _isDragging = false;
            _dragSource = null;
            _hoverTarget = null;
            _dropTarget = null;
            _dropAccepted = false;
            ClearHints();

            if (accepted && landing != null)
            {
                dragGhost.LandOn(landing.Rect, null);
                landing.PlayAcceptPunch();
                return;
            }

            if (accepted)
            {
                dragGhost.HideInstant();
                return;
            }

            dragGhost.ReturnTo(source != null ? source.Rect : null, () =>
            {
                if (source != null && source.IsFilled)
                {
                    source.ItemView.SetDragging(false);
                }
            });
        }

        public void HoverSlot(EquipSlotView slot, bool entered)
        {
            if (!_isDragging || slot == null)
            {
                return;
            }

            if (entered)
            {
                if (_hoverTarget != null && _hoverTarget != slot)
                {
                    _hoverTarget.SetDropHint(GetBaselineHint(_hoverTarget));
                }

                _hoverTarget = slot;
                slot.SetDropHint(ComputeHoverHint(slot));
                return;
            }

            if (_hoverTarget == slot)
            {
                _hoverTarget = null;
                slot.SetDropHint(GetBaselineHint(slot));
            }
        }

        public void HoverItem(EquipSlotView slot, bool entered)
        {
            if (_isDragging || slot == null || !IsVisible)
            {
                return;
            }

            if (entered && slot.IsFilled)
            {
                tooltip.Show(slot.Item, slot.Rect, slot.Location.IsStash ? StashControlsHint : SlotControlsHint);
            }
            else if (!entered && tooltip.Item == slot.Item)
            {
                tooltip.Hide();
            }
        }

        public void ClickItem(EquipSlotView slot, PointerEventData eventData)
        {
            if (!IsVisible || _isDragging || slot == null || !slot.IsFilled || _loadout == null || _loadout.IsLocked)
            {
                return;
            }

            EquipLocation location = slot.Location;
            bool isRight = eventData.button == PointerEventData.InputButton.Right;
            bool isDouble = eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2;

            if (!isRight && !isDouble)
            {
                return;
            }

            tooltip.HideInstant();

            if (location.IsStash)
            {
                if (isDouble)
                {
                    OnQuickEquipRequested?.Invoke(location.SlotIndex, SelectedHeroIndex);
                }

                return;
            }

            OnUnequipRequested?.Invoke(location.HeroIndex, location.SlotIndex);
        }

        #endregion

        #region Drag Helpers

        private static EquipLocation ToTarget(EquipSlotView target)
        {
            return target.Location.IsStash ? EquipLocation.Stash : target.Location;
        }

        private EquipDropHint ComputeHoverHint(EquipSlotView slot)
        {
            if (slot == _dragSource)
            {
                return EquipDropHint.None;
            }

            switch (_loadout.Preview(_dragSource.Location, ToTarget(slot)))
            {
                case EquipMoveResult.Moved:
                case EquipMoveResult.ReturnedToStash:
                    return EquipDropHint.Accept;
                case EquipMoveResult.Swapped:
                    return EquipDropHint.Swap;
                case EquipMoveResult.NoChange:
                    return EquipDropHint.None;
                default:
                    return EquipDropHint.Reject;
            }
        }

        private EquipDropHint GetBaselineHint(EquipSlotView slot)
        {
            if (!_isDragging || _dragSource == null || slot == _dragSource)
            {
                return EquipDropHint.None;
            }

            // Из хранилища в хранилище переносить нечего — ячейки хранилища не подсвечиваются.
            if (slot.Location.IsStash && _dragSource.Location.IsStash)
            {
                return EquipDropHint.None;
            }

            return EquipDropHint.Available;
        }

        private void ApplyBaselineHints()
        {
            for (int h = 0; h < heroPanels.Length; h++)
            {
                EquipSlotView[] slots = heroPanels[h].Slots;

                for (int s = 0; s < slots.Length; s++)
                {
                    slots[s].SetDropHint(GetBaselineHint(slots[s]));
                }
            }

            for (int i = 0; i < stashSlots.Length; i++)
            {
                stashSlots[i].SetDropHint(GetBaselineHint(stashSlots[i]));
            }
        }

        private void ClearHints()
        {
            for (int h = 0; h < heroPanels.Length; h++)
            {
                EquipSlotView[] slots = heroPanels[h].Slots;

                for (int s = 0; s < slots.Length; s++)
                {
                    slots[s].SetDropHint(EquipDropHint.None);
                }
            }

            for (int i = 0; i < stashSlots.Length; i++)
            {
                stashSlots[i].SetDropHint(EquipDropHint.None);
            }
        }

        private EquipSlotView FindCellWithItem(ItemRecord item, EquipLocation preferred)
        {
            if (item == null)
            {
                return null;
            }

            if (!preferred.IsStash && preferred.HeroIndex >= 0 && preferred.HeroIndex < heroPanels.Length)
            {
                EquipSlotView slot = heroPanels[preferred.HeroIndex].Slots[preferred.SlotIndex];

                if (ReferenceEquals(slot.Item, item))
                {
                    return slot;
                }
            }

            for (int i = 0; i < stashSlots.Length; i++)
            {
                if (ReferenceEquals(stashSlots[i].Item, item))
                {
                    return stashSlots[i];
                }
            }

            return null;
        }

        private void CancelDrag()
        {
            if (_dragSource != null && _dragSource.IsFilled)
            {
                _dragSource.ItemView.SetDragging(false);
            }

            _isDragging = false;
            _dragSource = null;
            _hoverTarget = null;
            _dropTarget = null;
            _dropAccepted = false;

            if (_isValid)
            {
                dragGhost.HideInstant();
                ClearHints();
            }
        }

        #endregion

        #region Binding

        private void BindHeroes()
        {
            IReadOnlyList<HeroRecord> heroes = _loadout.Heroes;

            for (int i = 0; i < heroPanels.Length; i++)
            {
                if (i >= heroes.Count)
                {
                    continue;
                }

                Sprite sprite = GetPortrait(heroes[i], out Color tint);
                heroPanels[i].Bind(i, heroes[i], sprite, tint, this);
            }
        }

        private void BindStash()
        {
            for (int i = 0; i < stashSlots.Length; i++)
            {
                stashSlots[i].Setup(EquipLocation.InStash(i), this);
                stashSlots[i].Clear();
            }
        }

        private void BindAiTeam(IReadOnlyList<HeroRecord> aiTeam)
        {
            for (int i = 0; i < aiPortraits.Length; i++)
            {
                bool hasHero = i < aiTeam.Count;
                Image portrait = aiPortraits[i];
                TMP_Text nameText = i < aiNames.Length ? aiNames[i] : null;

                if (!hasHero)
                {
                    portrait.sprite = placeholderPortrait;
                    portrait.color = DraftPalette.Muted;
                    nameText?.SetText("—");
                    continue;
                }

                portrait.sprite = GetPortrait(aiTeam[i], out Color tint);
                portrait.color = tint;
                nameText?.SetText(aiTeam[i].Name);
            }
        }

        private void SelectHero(int heroIndex)
        {
            int count = _loadout != null ? _loadout.HeroCount : heroPanels.Length;
            SelectedHeroIndex = Mathf.Clamp(heroIndex, 0, Mathf.Max(0, count - 1));

            for (int i = 0; i < heroPanels.Length; i++)
            {
                heroPanels[i].SetSelected(i == SelectedHeroIndex);
            }
        }

        private Sprite GetPortrait(HeroRecord hero, out Color tint)
        {
            if (_portraitsByHeroId.TryGetValue(hero.Id, out Sprite sprite) && sprite != null)
            {
                tint = Color.white;
                return sprite;
            }

            tint = DraftPalette.GetRoleColor(hero.Role);
            return placeholderPortrait;
        }

        /// <summary>Иконка предмета: своя из списка или глиф главной характеристики.</summary>
        private Sprite GetIcon(ItemRecord item)
        {
            if (_iconsByItemId.TryGetValue(item.Id, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }

            // Тот же «вес» характеристик, что и в подписи EquipItemView.FormatMainBonus.
            float attack = item.BonusAttack * 1.5f;
            float armor = item.BonusArmor * 10f;
            float hp = item.BonusHp;

            if (attack >= armor && attack >= hp && item.BonusAttack > 0)
            {
                return attackGlyph;
            }

            return armor >= hp && item.BonusArmor > 0 ? armorGlyph : hpGlyph;
        }

        private void BuildLookups()
        {
            _portraitsByHeroId.Clear();

            for (int i = 0; i < portraits.Length; i++)
            {
                if (portraits[i].portrait != null)
                {
                    _portraitsByHeroId[portraits[i].heroId] = portraits[i].portrait;
                }
            }

            _iconsByItemId.Clear();

            for (int i = 0; i < itemIcons.Length; i++)
            {
                if (itemIcons[i].icon != null)
                {
                    _iconsByItemId[itemIcons[i].itemId] = itemIcons[i].icon;
                }
            }
        }

        #endregion

        #region Buttons

        private void HandleHeroPanelClicked(EquipHeroPanel panel)
        {
            if (IsVisible && !_isDragging && panel != null && panel.HeroIndex >= 0)
            {
                SelectHero(panel.HeroIndex);
            }
        }

        private void HandleBattleClicked()
        {
            if (IsVisible && !_isDragging)
            {
                OnBattleRequested?.Invoke();
            }
        }

        private void HandleResetClicked()
        {
            if (IsVisible && !_isDragging)
            {
                OnResetRequested?.Invoke();
            }
        }

        private void HandleLeaveClicked()
        {
            if (IsVisible)
            {
                OnLeaveRequested?.Invoke();
            }
        }

        #endregion

        #region Validation

        private void ApplyHiddenState()
        {
            IsVisible = false;
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;
        }

        private bool ValidateReferences()
        {
            bool isValid = true;
            isValid &= Check(matchInfoText, nameof(matchInfoText));
            isValid &= Check(progressText, nameof(progressText));
            isValid &= Check(footerHintText, nameof(footerHintText));
            isValid &= Check(dragGhost, nameof(dragGhost));
            isValid &= Check(tooltip, nameof(tooltip));
            isValid &= Check(aiStatusText, nameof(aiStatusText));
            isValid &= Check(battleButton, nameof(battleButton));
            isValid &= Check(resetButton, nameof(resetButton));
            isValid &= Check(leaveButton, nameof(leaveButton));
            isValid &= Check(battleButtonText, nameof(battleButtonText));
            isValid &= Check(placeholderPortrait, nameof(placeholderPortrait));
            isValid &= Check(attackGlyph, nameof(attackGlyph));
            isValid &= Check(armorGlyph, nameof(armorGlyph));
            isValid &= Check(hpGlyph, nameof(hpGlyph));
            isValid &= CheckArray(heroPanels, nameof(heroPanels));
            isValid &= CheckArray(stashSlots, nameof(stashSlots));
            isValid &= CheckArray(aiPortraits, nameof(aiPortraits));

            return isValid;
        }

        private bool Check(UnityEngine.Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"[EquipView] Не назначено поле «{fieldName}» в Инспекторе.", this);
            return false;
        }

        private bool CheckArray<T>(T[] array, string fieldName) where T : UnityEngine.Object
        {
            if (array == null || array.Length == 0)
            {
                Debug.LogError($"[EquipView] Массив «{fieldName}» пуст.", this);
                return false;
            }

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                {
                    Debug.LogError($"[EquipView] Элемент {i} массива «{fieldName}» не назначен.", this);
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
