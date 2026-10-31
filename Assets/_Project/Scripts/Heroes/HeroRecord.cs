using System;

namespace Solutio.Heroes
{
    /// <summary>
    /// Боевая роль героя. Значения совпадают с ограничением CHECK(hero_role IN ('Tank', 'Carry', 'Support')).
    /// </summary>
    public enum HeroRole
    {
        Tank = 0,
        Carry = 1,
        Support = 2
    }

    /// <summary>
    /// Справочник ролей героев: разбор значения из базы данных и отображаемые названия.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public static class HeroRoleInfo
    {
        public const string TankDbValue = "Tank";
        public const string CarryDbValue = "Carry";
        public const string SupportDbValue = "Support";

        /// <summary>
        /// Преобразует значение столбца heroes.hero_role в перечисление HeroRole.
        /// </summary>
        public static bool TryParse(string dbValue, out HeroRole role)
        {
            switch (dbValue)
            {
                case TankDbValue:
                    role = HeroRole.Tank;
                    return true;
                case CarryDbValue:
                    role = HeroRole.Carry;
                    return true;
                case SupportDbValue:
                    role = HeroRole.Support;
                    return true;
                default:
                    role = HeroRole.Tank;
                    return false;
            }
        }

        /// <summary>Значение роли для записи в базу данных.</summary>
        public static string GetDbValue(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                    return TankDbValue;
                case HeroRole.Carry:
                    return CarryDbValue;
                case HeroRole.Support:
                    return SupportDbValue;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "Неизвестная роль героя.");
            }
        }

        /// <summary>Название роли для интерфейса.</summary>
        public static string GetDisplayName(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                    return "Танк";
                case HeroRole.Carry:
                    return "Керри";
                case HeroRole.Support:
                    return "Поддержка";
                default:
                    return "—";
            }
        }
    }

    /// <summary>
    /// Неизменяемая запись о герое из таблицы heroes.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class HeroRecord : IEquatable<HeroRecord>
    {
        /// <summary>Идентификатор героя (heroes.id_hero).</summary>
        public int Id { get; }

        /// <summary>Имя героя (heroes.hero_name).</summary>
        public string Name { get; }

        /// <summary>Боевая роль (heroes.hero_role).</summary>
        public HeroRole Role { get; }

        /// <summary>Базовая атака (heroes.base_attack).</summary>
        public int BaseAttack { get; }

        /// <summary>Базовая броня (heroes.base_armor).</summary>
        public int BaseArmor { get; }

        /// <summary>Базовый запас здоровья (heroes.base_hp).</summary>
        public int BaseHp { get; }

        public HeroRecord(int id, string name, HeroRole role, int baseAttack, int baseArmor, int baseHp)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "Идентификатор героя должен быть положительным.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Имя героя не может быть пустым.", nameof(name));
            }

            if (!Enum.IsDefined(typeof(HeroRole), role))
            {
                throw new ArgumentOutOfRangeException(nameof(role), role, "Неизвестная роль героя.");
            }

            if (baseAttack < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseAttack), baseAttack, "Атака не может быть отрицательной.");
            }

            if (baseArmor < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseArmor), baseArmor, "Броня не может быть отрицательной.");
            }

            if (baseHp <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseHp), baseHp, "Здоровье должно быть положительным.");
            }

            Id = id;
            Name = name;
            Role = role;
            BaseAttack = baseAttack;
            BaseArmor = baseArmor;
            BaseHp = baseHp;
        }

        public bool Equals(HeroRecord other)
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
                   && Role == other.Role
                   && BaseAttack == other.BaseAttack
                   && BaseArmor == other.BaseArmor
                   && BaseHp == other.BaseHp;
        }

        public override bool Equals(object obj)
        {
            return obj is HeroRecord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Id;
                hash = (hash * 397) ^ Name.GetHashCode();
                hash = (hash * 397) ^ (int)Role;
                hash = (hash * 397) ^ BaseAttack;
                hash = (hash * 397) ^ BaseArmor;
                hash = (hash * 397) ^ BaseHp;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"#{Id} {Name} ({HeroRoleInfo.GetDisplayName(Role)}): АТК {BaseAttack}, БРН {BaseArmor}, ОЗ {BaseHp}";
        }
    }
}
