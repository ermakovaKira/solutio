using System;
using System.Collections.Generic;
using Solutio.Heroes;
using Solutio.Items;

namespace Solutio.Equipment
{
    /// <summary>Где лежит предмет: в общем хранилище команды или в слоте героя.</summary>
    public enum EquipContainer
    {
        Stash = 0,
        HeroSlot = 1
    }

    /// <summary>
    /// Адрес ячейки экрана экипировки. Для хранилища HeroIndex = -1, SlotIndex — позиция в списке
    /// (при сбросе в хранилище позиция не важна: предметы всегда упорядочены по ID).
    /// </summary>
    public readonly struct EquipLocation : IEquatable<EquipLocation>
    {
        public readonly EquipContainer Container;
        public readonly int HeroIndex;
        public readonly int SlotIndex;

        private EquipLocation(EquipContainer container, int heroIndex, int slotIndex)
        {
            Container = container;
            HeroIndex = heroIndex;
            SlotIndex = slotIndex;
        }

        /// <summary>Предмет в хранилище по индексу списка.</summary>
        public static EquipLocation InStash(int stashIndex)
        {
            return new EquipLocation(EquipContainer.Stash, -1, stashIndex);
        }

        /// <summary>Хранилище как цель сброса (позиция определяется сортировкой по ID).</summary>
        public static EquipLocation Stash => new EquipLocation(EquipContainer.Stash, -1, -1);

        /// <summary>Слот героя.</summary>
        public static EquipLocation InSlot(int heroIndex, int slotIndex)
        {
            return new EquipLocation(EquipContainer.HeroSlot, heroIndex, slotIndex);
        }

        public bool IsStash => Container == EquipContainer.Stash;

        public bool Equals(EquipLocation other)
        {
            return Container == other.Container && HeroIndex == other.HeroIndex && SlotIndex == other.SlotIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is EquipLocation other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Container;
                hash = (hash * 397) ^ HeroIndex;
                hash = (hash * 397) ^ SlotIndex;
                return hash;
            }
        }

        public override string ToString()
        {
            return IsStash ? $"Хранилище[{SlotIndex}]" : $"Герой {HeroIndex}, слот {SlotIndex}";
        }
    }

    /// <summary>Итог попытки переместить предмет.</summary>
    public enum EquipMoveResult
    {
        /// <summary>Предмет помещён в пустой слот.</summary>
        Moved = 0,

        /// <summary>Целевой слот был занят: предметы поменялись местами (или прежний вернулся в хранилище).</summary>
        Swapped = 1,

        /// <summary>Предмет снят с героя и возвращён в хранилище.</summary>
        ReturnedToStash = 2,

        /// <summary>Источник и цель совпадают — ничего не изменилось.</summary>
        NoChange = 3,

        /// <summary>Источник вне допустимого диапазона.</summary>
        InvalidSource = 4,

        /// <summary>Цель вне допустимого диапазона.</summary>
        InvalidTarget = 5,

        /// <summary>В ячейке-источнике нет предмета.</summary>
        EmptySource = 6,

        /// <summary>У героя нет свободного слота (быстрая экипировка).</summary>
        NoFreeSlot = 7,

        /// <summary>Раскладка зафиксирована (нажата кнопка «В бой») — изменения запрещены.</summary>
        Locked = 8
    }

    /// <summary>Расширения для результата перемещения.</summary>
    public static class EquipMoveResultExtensions
    {
        /// <summary>true, если раскладка изменилась.</summary>
        public static bool IsChange(this EquipMoveResult result)
        {
            return result == EquipMoveResult.Moved
                   || result == EquipMoveResult.Swapped
                   || result == EquipMoveResult.ReturnedToStash;
        }
    }

    /// <summary>
    /// Итоговые характеристики героя: базовые из heroes + бонусы надетых предметов.
    /// </summary>
    public readonly struct HeroCombatStats : IEquatable<HeroCombatStats>
    {
        public readonly int BaseAttack;
        public readonly int BaseArmor;
        public readonly int BaseHp;
        public readonly int BonusAttack;
        public readonly int BonusArmor;
        public readonly int BonusHp;

        public HeroCombatStats(int baseAttack, int baseArmor, int baseHp, int bonusAttack, int bonusArmor, int bonusHp)
        {
            BaseAttack = baseAttack;
            BaseArmor = baseArmor;
            BaseHp = baseHp;
            BonusAttack = bonusAttack;
            BonusArmor = bonusArmor;
            BonusHp = bonusHp;
        }

        public int Attack => BaseAttack + BonusAttack;
        public int Armor => BaseArmor + BonusArmor;
        public int Hp => BaseHp + BonusHp;

        public bool Equals(HeroCombatStats other)
        {
            return BaseAttack == other.BaseAttack
                   && BaseArmor == other.BaseArmor
                   && BaseHp == other.BaseHp
                   && BonusAttack == other.BonusAttack
                   && BonusArmor == other.BonusArmor
                   && BonusHp == other.BonusHp;
        }

        public override bool Equals(object obj)
        {
            return obj is HeroCombatStats other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BaseAttack;
                hash = (hash * 397) ^ BaseArmor;
                hash = (hash * 397) ^ BaseHp;
                hash = (hash * 397) ^ BonusAttack;
                hash = (hash * 397) ^ BonusArmor;
                hash = (hash * 397) ^ BonusHp;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"АТК {Attack} (+{BonusAttack}), БРН {Armor} (+{BonusArmor}), ОЗ {Hp} (+{BonusHp})";
        }
    }

    /// <summary>
    /// Зафиксированная экипировка одного героя (передаётся в бой, Дни 14–17).
    /// </summary>
    public sealed class HeroLoadout
    {
        /// <summary>Герой из таблицы heroes.</summary>
        public HeroRecord Hero { get; }

        /// <summary>Содержимое слотов по порядку; пустой слот — null.</summary>
        public IReadOnlyList<ItemRecord> Slots { get; }

        /// <summary>Итоговые характеристики с учётом предметов.</summary>
        public HeroCombatStats Stats { get; }

        public HeroLoadout(HeroRecord hero, IReadOnlyList<ItemRecord> slots, HeroCombatStats stats)
        {
            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            Stats = stats;
        }

        /// <summary>Количество надетых предметов.</summary>
        public int EquippedCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < Slots.Count; i++)
                {
                    if (Slots[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public override string ToString()
        {
            string[] names = new string[Slots.Count];

            for (int i = 0; i < Slots.Count; i++)
            {
                names[i] = Slots[i] != null ? Slots[i].Name : "—";
            }

            return $"{Hero.Name}: [{string.Join(", ", names)}] → {Stats}";
        }
    }

    /// <summary>
    /// Раскладка стартовой экипировки одной команды (День 13).
    /// Общий пул предметов (каждый — отдельный экземпляр), у каждого героя SlotsPerHero слотов.
    /// Правила перемещения:
    /// хранилище → пустой слот — предмет надевается;
    /// хранилище → занятый слот — предмет надевается, прежний возвращается в хранилище;
    /// слот → пустой слот — перенос; слот → занятый слот — обмен местами;
    /// слот → хранилище — предмет снимается. Хранилище всегда упорядочено по ID предмета.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class EquipmentLoadout
    {
        /// <summary>Слотов инвентаря у героя (план Дня 20: до 4 слотов).</summary>
        public const int SlotsPerHero = 4;

        private readonly List<HeroRecord> _heroes;
        private readonly ItemRecord[] _slots;
        private readonly List<ItemRecord> _stash;
        private readonly int _totalItems;

        /// <summary>
        /// Создаёт раскладку: все стартовые предметы лежат в хранилище, слоты пустые.
        /// </summary>
        /// <param name="team">Герои команды в порядке пиков (из DraftResult).</param>
        /// <param name="startingItems">Стартовые предметы команды (каждый элемент — отдельный экземпляр).</param>
        public EquipmentLoadout(IReadOnlyList<HeroRecord> team, IReadOnlyList<ItemRecord> startingItems)
        {
            if (team == null)
            {
                throw new ArgumentNullException(nameof(team));
            }

            if (startingItems == null)
            {
                throw new ArgumentNullException(nameof(startingItems));
            }

            if (team.Count == 0)
            {
                throw new ArgumentException("Команда не может быть пустой.", nameof(team));
            }

            HashSet<int> heroIds = new HashSet<int>();
            _heroes = new List<HeroRecord>(team.Count);

            for (int i = 0; i < team.Count; i++)
            {
                HeroRecord hero = team[i];

                if (hero == null)
                {
                    throw new ArgumentException($"Герой с индексом {i} равен null.", nameof(team));
                }

                if (!heroIds.Add(hero.Id))
                {
                    throw new ArgumentException($"Герой ID {hero.Id} повторяется в команде.", nameof(team));
                }

                _heroes.Add(hero);
            }

            if (startingItems.Count > team.Count * SlotsPerHero)
            {
                throw new ArgumentException(
                    $"Предметов ({startingItems.Count}) больше, чем слотов у команды ({team.Count * SlotsPerHero}).",
                    nameof(startingItems));
            }

            _stash = new List<ItemRecord>(startingItems.Count);

            for (int i = 0; i < startingItems.Count; i++)
            {
                ItemRecord item = startingItems[i];

                if (item == null)
                {
                    throw new ArgumentException($"Предмет с индексом {i} равен null.", nameof(startingItems));
                }

                InsertSorted(_stash, item);
            }

            _totalItems = _stash.Count;
            _slots = new ItemRecord[_heroes.Count * SlotsPerHero];
        }

        /// <summary>Герои команды в порядке пиков.</summary>
        public IReadOnlyList<HeroRecord> Heroes => _heroes;

        /// <summary>Количество героев.</summary>
        public int HeroCount => _heroes.Count;

        /// <summary>Нераспределённые предметы (упорядочены по ID).</summary>
        public IReadOnlyList<ItemRecord> Stash => _stash;

        /// <summary>Всего предметов в раскладке (не меняется при перемещениях).</summary>
        public int TotalItems => _totalItems;

        /// <summary>Сколько предметов уже надето.</summary>
        public int EquippedItems => _totalItems - _stash.Count;

        /// <summary>Раскладка зафиксирована — изменения запрещены.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>Счётчик изменений: растёт после каждого успешного перемещения (для обновления представления).</summary>
        public int Version { get; private set; }

        /// <summary>Предмет в слоте героя или null.</summary>
        public ItemRecord GetItem(int heroIndex, int slotIndex)
        {
            ValidateSlotArguments(heroIndex, slotIndex);
            return _slots[heroIndex * SlotsPerHero + slotIndex];
        }

        /// <summary>Предмет по адресу или null (пустой слот / неверный адрес).</summary>
        public ItemRecord GetItem(EquipLocation location)
        {
            if (!IsValidSource(location))
            {
                return null;
            }

            return location.IsStash
                ? _stash[location.SlotIndex]
                : _slots[location.HeroIndex * SlotsPerHero + location.SlotIndex];
        }

        /// <summary>Сколько предметов надето на героя.</summary>
        public int CountEquipped(int heroIndex)
        {
            ValidateHeroIndex(heroIndex);
            int count = 0;
            int offset = heroIndex * SlotsPerHero;

            for (int i = 0; i < SlotsPerHero; i++)
            {
                if (_slots[offset + i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Первый свободный слот героя.</summary>
        public bool TryFindFreeSlot(int heroIndex, out int slotIndex)
        {
            ValidateHeroIndex(heroIndex);
            int offset = heroIndex * SlotsPerHero;

            for (int i = 0; i < SlotsPerHero; i++)
            {
                if (_slots[offset + i] == null)
                {
                    slotIndex = i;
                    return true;
                }
            }

            slotIndex = -1;
            return false;
        }

        /// <summary>Индекс героя в команде по ID или -1.</summary>
        public int IndexOfHero(int heroId)
        {
            for (int i = 0; i < _heroes.Count; i++)
            {
                if (_heroes[i].Id == heroId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Проверяет перемещение без изменения состояния (подсветка ячеек при перетаскивании).
        /// </summary>
        public EquipMoveResult Preview(EquipLocation from, EquipLocation to)
        {
            if (IsLocked)
            {
                return EquipMoveResult.Locked;
            }

            if (!IsValidSource(from))
            {
                return EquipMoveResult.InvalidSource;
            }

            if (!IsValidTarget(to))
            {
                return EquipMoveResult.InvalidTarget;
            }

            if (GetItem(from) == null)
            {
                return EquipMoveResult.EmptySource;
            }

            if (from.IsStash && to.IsStash)
            {
                return EquipMoveResult.NoChange;
            }

            if (from.Equals(to))
            {
                return EquipMoveResult.NoChange;
            }

            if (to.IsStash)
            {
                return EquipMoveResult.ReturnedToStash;
            }

            return _slots[to.HeroIndex * SlotsPerHero + to.SlotIndex] == null
                ? EquipMoveResult.Moved
                : EquipMoveResult.Swapped;
        }

        /// <summary>
        /// Перемещает предмет (основная операция Drag-and-Drop).
        /// </summary>
        public EquipMoveResult Move(EquipLocation from, EquipLocation to)
        {
            EquipMoveResult result = Preview(from, to);

            if (!result.IsChange())
            {
                return result;
            }

            if (to.IsStash)
            {
                int fromIndex = from.HeroIndex * SlotsPerHero + from.SlotIndex;
                InsertSorted(_stash, _slots[fromIndex]);
                _slots[fromIndex] = null;
            }
            else if (from.IsStash)
            {
                int toIndex = to.HeroIndex * SlotsPerHero + to.SlotIndex;
                ItemRecord item = _stash[from.SlotIndex];
                ItemRecord displaced = _slots[toIndex];

                _stash.RemoveAt(from.SlotIndex);
                _slots[toIndex] = item;

                if (displaced != null)
                {
                    InsertSorted(_stash, displaced);
                }
            }
            else
            {
                int fromIndex = from.HeroIndex * SlotsPerHero + from.SlotIndex;
                int toIndex = to.HeroIndex * SlotsPerHero + to.SlotIndex;
                ItemRecord moving = _slots[fromIndex];
                _slots[fromIndex] = _slots[toIndex];
                _slots[toIndex] = moving;
            }

            Version++;
            return result;
        }

        /// <summary>
        /// Быстрая экипировка (двойной клик по предмету в хранилище): первый свободный слот героя.
        /// </summary>
        public EquipMoveResult EquipFromStash(int stashIndex, int heroIndex, out int slotIndex)
        {
            slotIndex = -1;

            if (IsLocked)
            {
                return EquipMoveResult.Locked;
            }

            if (stashIndex < 0 || stashIndex >= _stash.Count)
            {
                return EquipMoveResult.InvalidSource;
            }

            if (heroIndex < 0 || heroIndex >= _heroes.Count)
            {
                return EquipMoveResult.InvalidTarget;
            }

            if (!TryFindFreeSlot(heroIndex, out slotIndex))
            {
                return EquipMoveResult.NoFreeSlot;
            }

            return Move(EquipLocation.InStash(stashIndex), EquipLocation.InSlot(heroIndex, slotIndex));
        }

        /// <summary>Снимает предмет со слота в хранилище (правый клик).</summary>
        public EquipMoveResult Unequip(int heroIndex, int slotIndex)
        {
            return Move(EquipLocation.InSlot(heroIndex, slotIndex), EquipLocation.Stash);
        }

        /// <summary>Возвращает все предметы в хранилище (кнопка «Сбросить»).</summary>
        /// <returns>Сколько предметов снято.</returns>
        public int UnequipAll()
        {
            if (IsLocked)
            {
                return 0;
            }

            int removed = 0;

            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null)
                {
                    InsertSorted(_stash, _slots[i]);
                    _slots[i] = null;
                    removed++;
                }
            }

            if (removed > 0)
            {
                Version++;
            }

            return removed;
        }

        /// <summary>Итоговые характеристики героя с учётом надетых предметов.</summary>
        public HeroCombatStats GetStats(int heroIndex)
        {
            ValidateHeroIndex(heroIndex);
            HeroRecord hero = _heroes[heroIndex];
            int attack = 0;
            int armor = 0;
            int hp = 0;
            int offset = heroIndex * SlotsPerHero;

            for (int i = 0; i < SlotsPerHero; i++)
            {
                ItemRecord item = _slots[offset + i];

                if (item == null)
                {
                    continue;
                }

                attack += item.BonusAttack;
                armor += item.BonusArmor;
                hp += item.BonusHp;
            }

            return new HeroCombatStats(hero.BaseAttack, hero.BaseArmor, hero.BaseHp, attack, armor, hp);
        }

        /// <summary>Фиксирует раскладку (кнопка «В бой»). Повторный вызов безопасен.</summary>
        public void Lock()
        {
            IsLocked = true;
        }

        /// <summary>Снимок экипировки всех героев для передачи в бой.</summary>
        public IReadOnlyList<HeroLoadout> BuildSnapshot()
        {
            List<HeroLoadout> result = new List<HeroLoadout>(_heroes.Count);

            for (int h = 0; h < _heroes.Count; h++)
            {
                ItemRecord[] slots = new ItemRecord[SlotsPerHero];
                Array.Copy(_slots, h * SlotsPerHero, slots, 0, SlotsPerHero);
                result.Add(new HeroLoadout(_heroes[h], slots, GetStats(h)));
            }

            return result;
        }

        private bool IsValidSource(EquipLocation location)
        {
            if (location.IsStash)
            {
                return location.SlotIndex >= 0 && location.SlotIndex < _stash.Count;
            }

            return IsValidSlot(location.HeroIndex, location.SlotIndex);
        }

        private bool IsValidTarget(EquipLocation location)
        {
            return location.IsStash || IsValidSlot(location.HeroIndex, location.SlotIndex);
        }

        private bool IsValidSlot(int heroIndex, int slotIndex)
        {
            return heroIndex >= 0 && heroIndex < _heroes.Count && slotIndex >= 0 && slotIndex < SlotsPerHero;
        }

        private void ValidateHeroIndex(int heroIndex)
        {
            if (heroIndex < 0 || heroIndex >= _heroes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(heroIndex), heroIndex, "Индекс героя вне команды.");
            }
        }

        private void ValidateSlotArguments(int heroIndex, int slotIndex)
        {
            ValidateHeroIndex(heroIndex);

            if (slotIndex < 0 || slotIndex >= SlotsPerHero)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Индекс слота вне диапазона.");
            }
        }

        /// <summary>Вставка с сохранением порядка по ID (стабильно для одинаковых ID).</summary>
        private static void InsertSorted(List<ItemRecord> list, ItemRecord item)
        {
            int index = list.Count;

            while (index > 0 && list[index - 1].Id > item.Id)
            {
                index--;
            }

            list.Insert(index, item);
        }
    }
}
