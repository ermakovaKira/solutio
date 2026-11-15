using System;
using DG.Tweening;
using Solutio.Draft;
using Solutio.Heroes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solutio.Equipment
{
    /// <summary>
    /// Колонка героя на экране подготовки: портрет, имя и роль, итоговые характеристики
    /// (база + бонусы предметов) и 4 слота инвентаря. Клик по колонке выбирает героя
    /// для быстрой экипировки двойным кликом из хранилища.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipHeroPanel : MonoBehaviour, IPointerClickHandler
    {
        [Header("References")]
        [SerializeField] private RectTransform content;
        [SerializeField] private Image selectionGlow;
        [SerializeField] private Image frame;
        [SerializeField] private Image halo;
        [SerializeField] private Image portrait;
        [SerializeField] private Image roleStripe;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text roleText;
        [SerializeField] private TMP_Text attackText;
        [SerializeField] private TMP_Text armorText;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text capacityText;
        [SerializeField] private EquipSlotView[] slots = Array.Empty<EquipSlotView>();

        [Header("Animation")]
        [SerializeField, Range(0f, 1f)] private float selectedGlowAlpha = 0.75f;
        [SerializeField, Range(0.05f, 0.6f)] private float selectDuration = 0.18f;

        private Tween _selectTween;
        private Tween _attackPunch;
        private Tween _armorPunch;
        private Tween _hpPunch;
        private HeroCombatStats _lastStats;
        private bool _hasStats;
        private bool _isInitialized;

        /// <summary>Клик по колонке (выбор героя для быстрой экипировки).</summary>
        public event Action<EquipHeroPanel> OnClicked;

        /// <summary>Индекс героя в команде.</summary>
        public int HeroIndex { get; private set; } = -1;

        /// <summary>Герой колонки.</summary>
        public HeroRecord Hero { get; private set; }

        /// <summary>Выбран ли герой.</summary>
        public bool IsSelected { get; private set; }

        /// <summary>Слоты инвентаря героя (по порядку).</summary>
        public EquipSlotView[] Slots => slots;

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

        /// <summary>Показывает героя и назначает слотам адреса раскладки.</summary>
        public void Bind(int heroIndex, HeroRecord hero, Sprite portraitSprite, Color portraitTint, IEquipDragHost host)
        {
            if (!Initialize())
            {
                return;
            }

            HeroIndex = heroIndex;
            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            _hasStats = false;

            Color roleColor = DraftPalette.GetRoleColor(hero.Role);
            portrait.sprite = portraitSprite;
            portrait.color = portraitTint;
            roleStripe.color = roleColor;

            Color haloColor = roleColor;
            haloColor.a = halo.color.a;
            halo.color = haloColor;

            nameText.SetText(hero.Name);
            roleText.color = roleColor;
            roleText.SetText(HeroRoleInfo.GetDisplayName(hero.Role));

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Setup(EquipLocation.InSlot(heroIndex, i), host);
                slots[i].Clear();
            }

            SetSelected(false, true);
        }

        /// <summary>Обновляет характеристики; изменившиеся значения «подпрыгивают».</summary>
        public void SetStats(HeroCombatStats stats)
        {
            if (!Initialize())
            {
                return;
            }

            bool animate = _hasStats;

            attackText.SetText(FormatStat("АТК", stats.Attack, stats.BonusAttack, ItemRarityPalette.StatAttack));
            armorText.SetText(FormatStat("БРН", stats.Armor, stats.BonusArmor, ItemRarityPalette.StatArmor));
            hpText.SetText(FormatStat("ЗДР", stats.Hp, stats.BonusHp, ItemRarityPalette.StatHp));

            if (animate)
            {
                if (stats.Attack != _lastStats.Attack)
                {
                    Punch(ref _attackPunch, attackText.rectTransform);
                }

                if (stats.Armor != _lastStats.Armor)
                {
                    Punch(ref _armorPunch, armorText.rectTransform);
                }

                if (stats.Hp != _lastStats.Hp)
                {
                    Punch(ref _hpPunch, hpText.rectTransform);
                }
            }

            _lastStats = stats;
            _hasStats = true;
        }

        /// <summary>Счётчик занятых слотов, например «2 / 4».</summary>
        public void SetCapacity(int equipped, int total)
        {
            if (!Initialize())
            {
                return;
            }

            capacityText.color = equipped >= total ? (Color)DraftPalette.Warning : (Color)DraftPalette.TextLight;
            capacityText.SetText("{0} / {1}", equipped, total);
        }

        /// <summary>Подсветка выбранного героя.</summary>
        public void SetSelected(bool selected, bool instant = false)
        {
            if (!Initialize())
            {
                return;
            }

            IsSelected = selected;
            frame.color = selected ? (Color)DraftPalette.SapphireGlow : (Color)DraftPalette.FrameIdle;

            _selectTween?.Kill();

            float alpha = selected ? selectedGlowAlpha : 0f;

            if (instant)
            {
                Color color = selectionGlow.color;
                color.a = alpha;
                selectionGlow.color = color;
                return;
            }

            _selectTween = selectionGlow
                .DOFade(alpha, selectDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnClicked?.Invoke(this);
            }
        }

        private static string FormatStat(string label, int total, int bonus, Color color)
        {
            string hex = ItemRarityPalette.ToHex(color);

            if (bonus <= 0)
            {
                return $"<color=#{hex}>{label}</color>  {total}";
            }

            return $"<color=#{hex}>{label}</color>  {total}  <color=#{ItemRarityPalette.ToHex(ItemRarityPalette.StatBonus)}>+{bonus}</color>";
        }

        private void Punch(ref Tween tween, RectTransform target)
        {
            tween?.Kill();
            target.localScale = Vector3.one;
            tween = target
                .DOPunchScale(new Vector3(0.15f, 0.15f, 0f), 0.3f, 6, 0.5f)
                .Owned(gameObject)
                .OnKill(() => target.localScale = Vector3.one);
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

            if (selectionGlow == null || frame == null || halo == null || portrait == null || roleStripe == null
                || nameText == null || roleText == null || attackText == null || armorText == null
                || hpText == null || capacityText == null || slots == null || slots.Length != EquipmentLoadout.SlotsPerHero)
            {
                Debug.LogError($"[EquipHeroPanel] Не назначены ссылки или слотов не {EquipmentLoadout.SlotsPerHero} у «{name}».", this);
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    Debug.LogError($"[EquipHeroPanel] Слот {i} не назначен у «{name}».", this);
                    return false;
                }
            }

            selectionGlow.raycastTarget = false;
            _isInitialized = true;
            return true;
        }

        private void KillTweens()
        {
            _selectTween?.Kill();
            _attackPunch?.Kill();
            _armorPunch?.Kill();
            _hpPunch?.Kill();
            _selectTween = null;
            _attackPunch = null;
            _armorPunch = null;
            _hpPunch = null;
        }
    }
}
