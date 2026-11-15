using System;
using System.Collections.Generic;
using System.Globalization;
using Solutio.Heroes;
using Solutio.Items;
using Solutio.Session;

namespace Solutio.Equipment
{
    /// <summary>Как ИИ выбрал героя для предмета.</summary>
    public enum EquipAiDecisionMode
    {
        /// <summary>Решение не принято: свободных слотов нет.</summary>
        None = 0,

        /// <summary>Бросок удачен — предмет отдан герою с лучшей синергией.</summary>
        Optimal = 1,

        /// <summary>Бросок неудачен — предмет отдан случайному герою со свободным слотом.</summary>
        Random = 2
    }

    /// <summary>Решение ИИ по одному предмету с пояснением для журнала.</summary>
    public readonly struct EquipAiDecision
    {
        /// <summary>Распределяемый предмет.</summary>
        public readonly ItemRecord Item;

        /// <summary>Получатель (null при Mode = None).</summary>
        public readonly HeroRecord Hero;

        /// <summary>Индекс героя в команде.</summary>
        public readonly int HeroIndex;

        /// <summary>Индекс слота.</summary>
        public readonly int SlotIndex;

        /// <summary>Способ принятия решения.</summary>
        public readonly EquipAiDecisionMode Mode;

        /// <summary>Шанс оптимального решения (ItemSynergyAwareness).</summary>
        public readonly float Chance;

        /// <summary>Значение броска [0; 1).</summary>
        public readonly double Roll;

        /// <summary>Синергия «роль — предмет» выбранного героя [0; 1].</summary>
        public readonly float Synergy;

        public EquipAiDecision(
            ItemRecord item,
            HeroRecord hero,
            int heroIndex,
            int slotIndex,
            EquipAiDecisionMode mode,
            float chance,
            double roll,
            float synergy)
        {
            Item = item;
            Hero = hero;
            HeroIndex = heroIndex;
            SlotIndex = slotIndex;
            Mode = mode;
            Chance = chance;
            Roll = roll;
            Synergy = synergy;
        }

        public override string ToString()
        {
            if (Hero == null || Item == null)
            {
                return "[AiEquip] решение не принято: свободных слотов нет";
            }

            string role = HeroRoleInfo.GetDisplayName(Hero.Role);

            if (Mode == EquipAiDecisionMode.Random)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "[AiEquip] {0} → {1} ({2}), слот {3} — СЛУЧАЙНО (бросок {4:0.00} ≥ шанс {5:0.00}), синергия {6:0.00}",
                    Item.Name, Hero.Name, role, SlotIndex + 1, Roll, Chance, Synergy);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "[AiEquip] {0} → {1} ({2}), слот {3} — ОПТИМАЛЬНО (бросок {4:0.00} < шанс {5:0.00}), синергия {6:0.00}",
                Item.Name, Hero.Name, role, SlotIndex + 1, Roll, Chance, Synergy);
        }
    }

    /// <summary>
    /// Распределение стартовой экипировки ИИ (День 13). Сложность влияет только на качество решений:
    /// с вероятностью ItemSynergyAwareness предмет получает герой с лучшей синергией «роль — предмет»,
    /// иначе — случайный герой со свободным слотом. Бонусов к характеристикам ИИ нет.
    /// Предметы раздаются от самого сильного к слабому; штраф за уже надетые предметы
    /// не даёт ИИ «свалить» всё на одного героя.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class EquipmentAiPlanner
    {
        /// <summary>Штраф синергии за каждый предмет, уже надетый на героя.</summary>
        public const float SpreadPenalty = 0.12f;

        // Веса характеристик по ролям: атака / броня / здоровье (сумма 1).
        private static readonly float[] TankWeights = { 0.10f, 0.45f, 0.45f };
        private static readonly float[] CarryWeights = { 0.80f, 0.05f, 0.15f };
        private static readonly float[] SupportWeights = { 0.35f, 0.30f, 0.35f };

        private readonly List<ItemRecord> _order = new List<ItemRecord>(16);
        private readonly List<int> _candidates = new List<int>(8);

        public EquipmentAiPlanner(AiStrategyProfile profile)
        {
            Profile = profile;
        }

        /// <summary>Профиль сложности ИИ.</summary>
        public AiStrategyProfile Profile { get; }

        /// <summary>
        /// Распределяет все предметы хранилища по героям раскладки.
        /// На каждый предмет из генератора берётся бросок решения и, при случайном пути, индекс героя.
        /// </summary>
        /// <param name="loadout">Раскладка команды ИИ (изменяется).</param>
        /// <param name="random">Генератор матча (MatchSessionConfig.CreateMatchRandom) — детерминизм по seed.</param>
        /// <param name="decisions">Буфер журнала решений (очищается), может быть null.</param>
        /// <returns>Количество оптимальных решений.</returns>
        public int Distribute(EquipmentLoadout loadout, Random random, List<EquipAiDecision> decisions)
        {
            if (loadout == null)
            {
                throw new ArgumentNullException(nameof(loadout));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            decisions?.Clear();

            if (loadout.IsLocked || loadout.Stash.Count == 0)
            {
                return 0;
            }

            _order.Clear();

            for (int i = 0; i < loadout.Stash.Count; i++)
            {
                _order.Add(loadout.Stash[i]);
            }

            ComputeNormalization(_order, out float maxAttack, out float maxArmor, out float maxHp);
            SortByPower(_order, maxAttack, maxArmor, maxHp);

            int optimalCount = 0;

            for (int i = 0; i < _order.Count; i++)
            {
                ItemRecord item = _order[i];
                CollectCandidates(loadout);

                if (_candidates.Count == 0)
                {
                    decisions?.Add(new EquipAiDecision(item, null, -1, -1, EquipAiDecisionMode.None,
                        Profile.ItemSynergyAwareness, 0d, 0f));
                    continue;
                }

                double roll = random.NextDouble();
                bool optimal = AiStrategyProfile.Roll(Profile.ItemSynergyAwareness, roll);
                int heroIndex;

                if (optimal)
                {
                    heroIndex = FindBestHero(loadout, item, maxAttack, maxArmor, maxHp);
                    optimalCount++;
                }
                else
                {
                    heroIndex = _candidates[random.Next(_candidates.Count)];
                }

                int stashIndex = IndexOfInstance(loadout.Stash, item);
                EquipMoveResult result = loadout.EquipFromStash(stashIndex, heroIndex, out int slotIndex);

                if (!result.IsChange())
                {
                    throw new InvalidOperationException(
                        $"ИИ не смог надеть {item.Name} на героя {heroIndex}: {result}.");
                }

                float synergy = GetSynergy(loadout.Heroes[heroIndex].Role, item, maxAttack, maxArmor, maxHp);

                decisions?.Add(new EquipAiDecision(
                    item,
                    loadout.Heroes[heroIndex],
                    heroIndex,
                    slotIndex,
                    optimal ? EquipAiDecisionMode.Optimal : EquipAiDecisionMode.Random,
                    Profile.ItemSynergyAwareness,
                    roll,
                    synergy));
            }

            return optimalCount;
        }

        /// <summary>
        /// Синергия «роль — предмет» [0; 1]: доли нормированных бонусов предмета, взвешенные по роли.
        /// Не зависит от силы предмета — только от того, насколько он «подходит» роли.
        /// </summary>
        public static float GetSynergy(HeroRole role, ItemRecord item, float maxAttack, float maxArmor, float maxHp)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            float attack = Normalize(item.BonusAttack, maxAttack);
            float armor = Normalize(item.BonusArmor, maxArmor);
            float hp = Normalize(item.BonusHp, maxHp);
            float sum = attack + armor + hp;

            if (sum <= 0f)
            {
                return 0f;
            }

            float[] weights = GetRoleWeights(role);
            return (weights[0] * attack + weights[1] * armor + weights[2] * hp) / sum;
        }

        /// <summary>Максимумы бонусов по набору предметов (минимум 1 — защита от деления на ноль).</summary>
        public static void ComputeNormalization(IReadOnlyList<ItemRecord> items, out float maxAttack, out float maxArmor, out float maxHp)
        {
            maxAttack = 1f;
            maxArmor = 1f;
            maxHp = 1f;

            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemRecord item = items[i];
                maxAttack = Math.Max(maxAttack, item.BonusAttack);
                maxArmor = Math.Max(maxArmor, item.BonusArmor);
                maxHp = Math.Max(maxHp, item.BonusHp);
            }
        }

        private int FindBestHero(EquipmentLoadout loadout, ItemRecord item, float maxAttack, float maxArmor, float maxHp)
        {
            int bestIndex = _candidates[0];
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < _candidates.Count; i++)
            {
                int heroIndex = _candidates[i];
                float score = GetSynergy(loadout.Heroes[heroIndex].Role, item, maxAttack, maxArmor, maxHp)
                              - SpreadPenalty * loadout.CountEquipped(heroIndex);

                // При равенстве остаётся меньший индекс: кандидаты перебираются по возрастанию.
                if (score > bestScore + 1e-6f)
                {
                    bestScore = score;
                    bestIndex = heroIndex;
                }
            }

            return bestIndex;
        }

        private void CollectCandidates(EquipmentLoadout loadout)
        {
            _candidates.Clear();

            for (int h = 0; h < loadout.HeroCount; h++)
            {
                if (loadout.CountEquipped(h) < EquipmentLoadout.SlotsPerHero)
                {
                    _candidates.Add(h);
                }
            }
        }

        private static void SortByPower(List<ItemRecord> items, float maxAttack, float maxArmor, float maxHp)
        {
            // Сортировка вставками: предметов не больше 20, порядок детерминирован (при равенстве — меньший ID).
            for (int i = 1; i < items.Count; i++)
            {
                ItemRecord current = items[i];
                float currentPower = GetPower(current, maxAttack, maxArmor, maxHp);
                int j = i - 1;

                while (j >= 0 && IsWeaker(items[j], current, currentPower, maxAttack, maxArmor, maxHp))
                {
                    items[j + 1] = items[j];
                    j--;
                }

                items[j + 1] = current;
            }
        }

        private static bool IsWeaker(ItemRecord candidate, ItemRecord current, float currentPower, float maxAttack, float maxArmor, float maxHp)
        {
            float power = GetPower(candidate, maxAttack, maxArmor, maxHp);

            if (Math.Abs(power - currentPower) > 1e-6f)
            {
                return power < currentPower;
            }

            return candidate.Id > current.Id;
        }

        private static float GetPower(ItemRecord item, float maxAttack, float maxArmor, float maxHp)
        {
            return Normalize(item.BonusAttack, maxAttack) + Normalize(item.BonusArmor, maxArmor) + Normalize(item.BonusHp, maxHp);
        }

        private static float Normalize(int value, float max)
        {
            return max > 0f ? value / max : 0f;
        }

        private static float[] GetRoleWeights(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                    return TankWeights;
                case HeroRole.Carry:
                    return CarryWeights;
                default:
                    return SupportWeights;
            }
        }

        private static int IndexOfInstance(IReadOnlyList<ItemRecord> stash, ItemRecord item)
        {
            for (int i = 0; i < stash.Count; i++)
            {
                if (ReferenceEquals(stash[i], item))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
