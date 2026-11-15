using System;
using System.Text;

namespace Solutio.Items
{
    /// <summary>
    /// Тип предмета. Значения совпадают с ограничением CHECK(item_type IN ('Free', 'Shop')).
    /// </summary>
    public enum ItemType
    {
        /// <summary>Бесплатная стартовая экипировка (распределяется на экране подготовки).</summary>
        Free = 0,

        /// <summary>Предмет магазина (покупается за золото во время боя, Дни 19–20).</summary>
        Shop = 1
    }

    /// <summary>
    /// Редкость предмета. Значения совпадают с ограничением
    /// CHECK(rarity IN ('Common', 'Rare', 'Epic', 'Legendary')).
    /// </summary>
    public enum ItemRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    /// <summary>
    /// Справочник значений таблицы items: разбор строк из базы данных и отображаемые названия.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public static class ItemCatalogInfo
    {
        public const string FreeDbValue = "Free";
        public const string ShopDbValue = "Shop";

        public const string CommonDbValue = "Common";
        public const string RareDbValue = "Rare";
        public const string EpicDbValue = "Epic";
        public const string LegendaryDbValue = "Legendary";

        /// <summary>Преобразует значение столбца items.item_type в перечисление ItemType.</summary>
        public static bool TryParseType(string dbValue, out ItemType type)
        {
            switch (dbValue)
            {
                case FreeDbValue:
                    type = ItemType.Free;
                    return true;
                case ShopDbValue:
                    type = ItemType.Shop;
                    return true;
                default:
                    type = ItemType.Free;
                    return false;
            }
        }

        /// <summary>Преобразует значение столбца items.rarity в перечисление ItemRarity.</summary>
        public static bool TryParseRarity(string dbValue, out ItemRarity rarity)
        {
            switch (dbValue)
            {
                case CommonDbValue:
                    rarity = ItemRarity.Common;
                    return true;
                case RareDbValue:
                    rarity = ItemRarity.Rare;
                    return true;
                case EpicDbValue:
                    rarity = ItemRarity.Epic;
                    return true;
                case LegendaryDbValue:
                    rarity = ItemRarity.Legendary;
                    return true;
                default:
                    rarity = ItemRarity.Common;
                    return false;
            }
        }

        /// <summary>Значение типа предмета для параметра запроса к базе данных.</summary>
        public static string GetDbValue(ItemType type)
        {
            switch (type)
            {
                case ItemType.Free:
                    return FreeDbValue;
                case ItemType.Shop:
                    return ShopDbValue;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип предмета.");
            }
        }

        /// <summary>Значение редкости для параметра запроса к базе данных.</summary>
        public static string GetDbValue(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common:
                    return CommonDbValue;
                case ItemRarity.Rare:
                    return RareDbValue;
                case ItemRarity.Epic:
                    return EpicDbValue;
                case ItemRarity.Legendary:
                    return LegendaryDbValue;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Неизвестная редкость предмета.");
            }
        }

        /// <summary>Название редкости для интерфейса.</summary>
        public static string GetRarityDisplayName(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Common:
                    return "Обычный";
                case ItemRarity.Rare:
                    return "Редкий";
                case ItemRarity.Epic:
                    return "Эпический";
                case ItemRarity.Legendary:
                    return "Легендарный";
                default:
                    return "—";
            }
        }

        /// <summary>Название типа предмета для интерфейса.</summary>
        public static string GetTypeDisplayName(ItemType type)
        {
            switch (type)
            {
                case ItemType.Free:
                    return "Стартовый";
                case ItemType.Shop:
                    return "Магазин";
                default:
                    return "—";
            }
        }
    }

    /// <summary>
    /// Неизменяемая запись о предмете из таблицы items.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class ItemRecord : IEquatable<ItemRecord>
    {
        /// <summary>Идентификатор предмета (items.id_item).</summary>
        public int Id { get; }

        /// <summary>Название предмета (items.item_name).</summary>
        public string Name { get; }

        /// <summary>Тип предмета (items.item_type).</summary>
        public ItemType Type { get; }

        /// <summary>Редкость (items.rarity).</summary>
        public ItemRarity Rarity { get; }

        /// <summary>Бонус к атаке (items.bonus_attack).</summary>
        public int BonusAttack { get; }

        /// <summary>Бонус к броне (items.bonus_armor).</summary>
        public int BonusArmor { get; }

        /// <summary>Бонус к здоровью (items.bonus_hp).</summary>
        public int BonusHp { get; }

        /// <summary>Цена в золоте (items.cost), у стартовых предметов 0.</summary>
        public int Cost { get; }

        public ItemRecord(
            int id,
            string name,
            ItemType type,
            ItemRarity rarity,
            int bonusAttack,
            int bonusArmor,
            int bonusHp,
            int cost)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Идентификатор предмета должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Название предмета не может быть пустым.", nameof(name));
            }

            if (!Enum.IsDefined(typeof(ItemType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type), type, "Неизвестный тип предмета.");
            }

            if (!Enum.IsDefined(typeof(ItemRarity), rarity))
            {
                throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Неизвестная редкость предмета.");
            }

            if (bonusAttack < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bonusAttack), bonusAttack, "Бонус атаки не может быть отрицательным.");
            }

            if (bonusArmor < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bonusArmor), bonusArmor, "Бонус брони не может быть отрицательным.");
            }

            if (bonusHp < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bonusHp), bonusHp, "Бонус здоровья не может быть отрицательным.");
            }

            if (cost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cost), cost, "Цена не может быть отрицательной.");
            }

            Id = id;
            Name = name;
            Type = type;
            Rarity = rarity;
            BonusAttack = bonusAttack;
            BonusArmor = bonusArmor;
            BonusHp = bonusHp;
            Cost = cost;
        }

        /// <summary>
        /// Краткое описание бонусов для подсказки, например «+14 АТК · +25 ОЗ».
        /// Нулевые бонусы не выводятся.
        /// </summary>
        public string FormatBonuses()
        {
            StringBuilder builder = new StringBuilder(32);
            AppendBonus(builder, BonusAttack, "АТК");
            AppendBonus(builder, BonusArmor, "БРН");
            AppendBonus(builder, BonusHp, "ОЗ");
            return builder.Length > 0 ? builder.ToString() : "без бонусов";
        }

        private static void AppendBonus(StringBuilder builder, int value, string label)
        {
            if (value == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(" · ");
            }

            builder.Append('+').Append(value).Append(' ').Append(label);
        }

        public bool Equals(ItemRecord other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Id == other.Id
                   && string.Equals(Name, other.Name, StringComparison.Ordinal)
                   && Type == other.Type
                   && Rarity == other.Rarity
                   && BonusAttack == other.BonusAttack
                   && BonusArmor == other.BonusArmor
                   && BonusHp == other.BonusHp
                   && Cost == other.Cost;
        }

        public override bool Equals(object obj)
        {
            return obj is ItemRecord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id;
                hash = (hash * 397) ^ Name.GetHashCode();
                hash = (hash * 397) ^ (int)Type;
                hash = (hash * 397) ^ (int)Rarity;
                hash = (hash * 397) ^ BonusAttack;
                hash = (hash * 397) ^ BonusArmor;
                hash = (hash * 397) ^ BonusHp;
                hash = (hash * 397) ^ Cost;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"#{Id} {Name} ({ItemCatalogInfo.GetRarityDisplayName(Rarity)}): {FormatBonuses()}";
        }
    }
}
