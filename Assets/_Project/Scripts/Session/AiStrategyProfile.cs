using System;
using System.Collections.Generic;
using System.Globalization;
using Solutio.Settings;

namespace Solutio.Session
{
    /// <summary>
    /// Неизменяемый набор стратегических коэффициентов ИИ для одного уровня сложности.
    /// Сложность определяет ТОЛЬКО качество решений ИИ (драфт, закупка, выбор целей, стратегия),
    /// а не боевые характеристики: юниты и экономика у ИИ и пользователя одинаковы.
    /// Все коэффициенты лежат в диапазоне [0; 1].
    /// Чистый C#-тип без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public readonly struct AiStrategyProfile : IEquatable<AiStrategyProfile>
    {
        /// <summary>Уровень сложности, которому принадлежит профиль.</summary>
        public readonly AiDifficulty Difficulty;

        /// <summary>Вероятность взять сильнейшего доступного героя вместо случайного (День 12, драфт).</summary>
        public readonly float DraftOptimalPickChance;

        /// <summary>Степень учёта пиков пользователя при выборе контр-героя (День 12, драфт).</summary>
        public readonly float CounterPickAwareness;

        /// <summary>Вероятность выбрать лучший из 3 предложенных модификаторов магазина (Дни 19–20).</summary>
        public readonly float ShopOptimalPurchaseChance;

        /// <summary>Степень учёта синергии предметов с конкретным героем при сборке билда (Дни 19–20).</summary>
        public readonly float ItemSynergyAwareness;

        /// <summary>Точность выбора приоритетной цели (ослабленной или наиболее опасной) (День 16).</summary>
        public readonly float TargetPriorityAccuracy;

        /// <summary>
        /// Коэффициент стратегической агрессии: 0 — глухая оборона, 1 — постоянное давление и пуш линий (Дни 16–17).
        /// </summary>
        public readonly float AggressionCoefficient;

        public AiStrategyProfile(
            AiDifficulty difficulty,
            float draftOptimalPickChance,
            float counterPickAwareness,
            float shopOptimalPurchaseChance,
            float itemSynergyAwareness,
            float targetPriorityAccuracy,
            float aggressionCoefficient)
        {
            if (!Enum.IsDefined(typeof(AiDifficulty), difficulty))
            {
                throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Неизвестный уровень сложности ИИ.");
            }

            ValidateCoefficient(draftOptimalPickChance, nameof(draftOptimalPickChance));
            ValidateCoefficient(counterPickAwareness, nameof(counterPickAwareness));
            ValidateCoefficient(shopOptimalPurchaseChance, nameof(shopOptimalPurchaseChance));
            ValidateCoefficient(itemSynergyAwareness, nameof(itemSynergyAwareness));
            ValidateCoefficient(targetPriorityAccuracy, nameof(targetPriorityAccuracy));
            ValidateCoefficient(aggressionCoefficient, nameof(aggressionCoefficient));

            Difficulty = difficulty;
            DraftOptimalPickChance = draftOptimalPickChance;
            CounterPickAwareness = counterPickAwareness;
            ShopOptimalPurchaseChance = shopOptimalPurchaseChance;
            ItemSynergyAwareness = itemSynergyAwareness;
            TargetPriorityAccuracy = targetPriorityAccuracy;
            AggressionCoefficient = aggressionCoefficient;
        }

        /// <summary>
        /// Бросок решения: возвращает true, если ИИ принимает оптимальное решение.
        /// Значение броска передаётся снаружи, поэтому метод детерминирован и тестируем.
        /// </summary>
        /// <param name="chance">Коэффициент профиля в диапазоне [0; 1].</param>
        /// <param name="roll01">Случайное число в диапазоне [0; 1).</param>
        public static bool Roll(float chance, double roll01)
        {
            if (float.IsNaN(chance) || chance < 0f || chance > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(chance), chance, "Коэффициент должен лежать в диапазоне [0; 1].");
            }

            if (double.IsNaN(roll01) || roll01 < 0d || roll01 >= 1d)
            {
                throw new ArgumentOutOfRangeException(nameof(roll01), roll01, "Бросок должен лежать в диапазоне [0; 1).");
            }

            return roll01 < chance;
        }

        public bool Equals(AiStrategyProfile other)
        {
            return Difficulty == other.Difficulty
                && DraftOptimalPickChance.Equals(other.DraftOptimalPickChance)
                && CounterPickAwareness.Equals(other.CounterPickAwareness)
                && ShopOptimalPurchaseChance.Equals(other.ShopOptimalPurchaseChance)
                && ItemSynergyAwareness.Equals(other.ItemSynergyAwareness)
                && TargetPriorityAccuracy.Equals(other.TargetPriorityAccuracy)
                && AggressionCoefficient.Equals(other.AggressionCoefficient);
        }

        public override bool Equals(object obj)
        {
            return obj is AiStrategyProfile other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Difficulty;
                hash = (hash * 397) ^ DraftOptimalPickChance.GetHashCode();
                hash = (hash * 397) ^ CounterPickAwareness.GetHashCode();
                hash = (hash * 397) ^ ShopOptimalPurchaseChance.GetHashCode();
                hash = (hash * 397) ^ ItemSynergyAwareness.GetHashCode();
                hash = (hash * 397) ^ TargetPriorityAccuracy.GetHashCode();
                hash = (hash * 397) ^ AggressionCoefficient.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(AiStrategyProfile left, AiStrategyProfile right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AiStrategyProfile left, AiStrategyProfile right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: драфт {1:0%}, контрпик {2:0%}, магазин {3:0%}, синергия {4:0%}, цели {5:0%}, агрессия {6:0.00}",
                Difficulty,
                DraftOptimalPickChance,
                CounterPickAwareness,
                ShopOptimalPurchaseChance,
                ItemSynergyAwareness,
                TargetPriorityAccuracy,
                AggressionCoefficient);
        }

        private static void ValidateCoefficient(float value, string paramName)
        {
            if (float.IsNaN(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(paramName, value, "Коэффициент должен лежать в диапазоне [0; 1].");
            }
        }
    }

    /// <summary>
    /// Справочник стратегических профилей ИИ для всех уровней сложности.
    /// Профили хранятся в массиве, индексированном значением AiDifficulty: выборка O(1) без аллокаций.
    /// </summary>
    public static class AiStrategyTable
    {
        private static readonly AiStrategyProfile[] ProfilesByDifficulty =
        {
            // Послушник: пикает почти наугад, закупается хаотично, осторожничает
            new AiStrategyProfile(AiDifficulty.Easy,
                draftOptimalPickChance: 0.20f,
                counterPickAwareness: 0.10f,
                shopOptimalPurchaseChance: 0.25f,
                itemSynergyAwareness: 0.15f,
                targetPriorityAccuracy: 0.30f,
                aggressionCoefficient: 0.35f),

            // Страж: здравый смысл без глубокого анализа, сбалансированная стратегия
            new AiStrategyProfile(AiDifficulty.Normal,
                draftOptimalPickChance: 0.50f,
                counterPickAwareness: 0.40f,
                shopOptimalPurchaseChance: 0.55f,
                itemSynergyAwareness: 0.45f,
                targetPriorityAccuracy: 0.60f,
                aggressionCoefficient: 0.50f),

            // Инквизитор: уверенно контрит пики пользователя и собирает связные билды
            new AiStrategyProfile(AiDifficulty.Hard,
                draftOptimalPickChance: 0.75f,
                counterPickAwareness: 0.70f,
                shopOptimalPurchaseChance: 0.80f,
                itemSynergyAwareness: 0.70f,
                targetPriorityAccuracy: 0.80f,
                aggressionCoefficient: 0.65f),

            // Кошмар: почти безошибочные решения и постоянное стратегическое давление
            new AiStrategyProfile(AiDifficulty.Nightmare,
                draftOptimalPickChance: 0.95f,
                counterPickAwareness: 0.90f,
                shopOptimalPurchaseChance: 0.95f,
                itemSynergyAwareness: 0.90f,
                targetPriorityAccuracy: 0.95f,
                aggressionCoefficient: 0.80f)
        };

        /// <summary>Все профили в порядке возрастания сложности.</summary>
        public static IReadOnlyList<AiStrategyProfile> Profiles => ProfilesByDifficulty;

        /// <summary>
        /// Возвращает стратегический профиль для указанной сложности.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Если значение сложности не определено.</exception>
        public static AiStrategyProfile Get(AiDifficulty difficulty)
        {
            int index = (int)difficulty;

            if (index < 0 || index >= ProfilesByDifficulty.Length || ProfilesByDifficulty[index].Difficulty != difficulty)
            {
                throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Для указанной сложности нет стратегического профиля ИИ.");
            }

            return ProfilesByDifficulty[index];
        }

        /// <summary>
        /// Безопасный вариант выборки: при неизвестной сложности возвращает профиль по умолчанию.
        /// </summary>
        public static bool TryGet(AiDifficulty difficulty, out AiStrategyProfile profile)
        {
            int index = (int)difficulty;

            if (index >= 0 && index < ProfilesByDifficulty.Length && ProfilesByDifficulty[index].Difficulty == difficulty)
            {
                profile = ProfilesByDifficulty[index];
                return true;
            }

            profile = ProfilesByDifficulty[(int)MatchCatalog.DefaultDifficulty];
            return false;
        }
    }
}
