using Solutio.Items;
using UnityEngine;

namespace Solutio.Equipment
{
    /// <summary>Подсказка ячейки во время перетаскивания предмета.</summary>
    public enum EquipDropHint
    {
        /// <summary>Перетаскивания нет — обычный вид.</summary>
        None = 0,

        /// <summary>Идёт перетаскивание, ячейка может принять предмет (слабая подсветка).</summary>
        Available = 1,

        /// <summary>Курсор над ячейкой: предмет ляжет в пустой слот или вернётся в хранилище.</summary>
        Accept = 2,

        /// <summary>Курсор над занятым слотом: предметы поменяются местами.</summary>
        Swap = 3,

        /// <summary>Сброс в ячейку невозможен.</summary>
        Reject = 4
    }

    /// <summary>
    /// Палитра экрана экипировки (День 13). Цвета редкости «Кровавая легенда» (выбор Киры):
    /// Common — серебро, Rare — сапфир, Epic — аметист, Legendary — багровый.
    /// Цвета подсказок перетаскивания согласованы с палитрой драфта.
    /// </summary>
    public static class ItemRarityPalette
    {
        public static readonly Color32 Common = new Color32(0x9A, 0xA3, 0xAE, 0xFF);
        public static readonly Color32 Rare = new Color32(0x3E, 0x8E, 0xD0, 0xFF);
        public static readonly Color32 Epic = new Color32(0x9B, 0x59, 0xD0, 0xFF);
        public static readonly Color32 Legendary = new Color32(0xD0, 0x34, 0x3C, 0xFF);

        public static readonly Color32 CommonGlow = new Color32(0xC4, 0xCC, 0xD6, 0xFF);
        public static readonly Color32 RareGlow = new Color32(0x6F, 0xB6, 0xF0, 0xFF);
        public static readonly Color32 EpicGlow = new Color32(0xC0, 0x86, 0xF0, 0xFF);
        public static readonly Color32 LegendaryGlow = new Color32(0xFF, 0x5A, 0x60, 0xFF);

        public static readonly Color32 SlotEmpty = new Color32(0x10, 0x15, 0x20, 0xF0);
        public static readonly Color32 SlotFrameIdle = new Color32(0x2A, 0x34, 0x45, 0xFF);

        public static readonly Color32 HintAvailable = new Color32(0x30, 0x70, 0x82, 0xFF);
        public static readonly Color32 HintAccept = new Color32(0x4F, 0xC3, 0xE0, 0xFF);
        public static readonly Color32 HintSwap = new Color32(0xE0, 0xB0, 0x40, 0xFF);
        public static readonly Color32 HintReject = new Color32(0xD0, 0x40, 0x40, 0xFF);

        public static readonly Color32 StatAttack = new Color32(0xD0, 0x7A, 0x45, 0xFF);
        public static readonly Color32 StatArmor = new Color32(0x7A, 0x93, 0xAE, 0xFF);
        public static readonly Color32 StatHp = new Color32(0x5D, 0xB0, 0x8C, 0xFF);
        public static readonly Color32 StatBonus = new Color32(0x4F, 0xC3, 0xE0, 0xFF);

        /// <summary>Основной цвет редкости (рамка, название в подсказке).</summary>
        public static Color GetRarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Rare:
                    return Rare;
                case ItemRarity.Epic:
                    return Epic;
                case ItemRarity.Legendary:
                    return Legendary;
                default:
                    return Common;
            }
        }

        /// <summary>Цвет свечения редкости.</summary>
        public static Color GetRarityGlowColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Rare:
                    return RareGlow;
                case ItemRarity.Epic:
                    return EpicGlow;
                case ItemRarity.Legendary:
                    return LegendaryGlow;
                default:
                    return CommonGlow;
            }
        }

        /// <summary>Тёмная подложка ячейки с оттенком редкости.</summary>
        public static Color GetRarityBackground(ItemRarity rarity)
        {
            Color background = Color.Lerp(SlotEmpty, GetRarityColor(rarity), 0.22f);
            background.a = 0.96f;
            return background;
        }

        /// <summary>Цвет рамки ячейки для подсказки перетаскивания.</summary>
        public static Color GetHintColor(EquipDropHint hint)
        {
            switch (hint)
            {
                case EquipDropHint.Available:
                    return HintAvailable;
                case EquipDropHint.Accept:
                    return HintAccept;
                case EquipDropHint.Swap:
                    return HintSwap;
                case EquipDropHint.Reject:
                    return HintReject;
                default:
                    return SlotFrameIdle;
            }
        }

        /// <summary>Шестнадцатеричный код цвета для rich text TMP (без решётки).</summary>
        public static string ToHex(Color color)
        {
            return ColorUtility.ToHtmlStringRGB(color);
        }
    }
}
