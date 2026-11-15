using System;
using System.Collections.Generic;
using System.Text;
using Solutio.Items;

namespace Solutio.Equipment
{
    /// <summary>
    /// Итог стадии подготовки (День 13): зафиксированная экипировка обеих команд.
    /// Передаётся в автоматический бой (Дни 14–17); запись билда в SQLite — Дни 20–21.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class EquipmentResult
    {
        /// <summary>Seed матча (MatchSessionConfig.MatchSeed) — для воспроизводимости.</summary>
        public int MatchSeed { get; }

        /// <summary>Экипировка героев пользователя в порядке пиков.</summary>
        public IReadOnlyList<HeroLoadout> UserTeam { get; }

        /// <summary>Экипировка героев ИИ в порядке пиков.</summary>
        public IReadOnlyList<HeroLoadout> AiTeam { get; }

        /// <summary>Стартовые предметы, которые пользователь не надел.</summary>
        public IReadOnlyList<ItemRecord> UnassignedUserItems { get; }

        /// <summary>Оптимальных решений ИИ при раздаче предметов.</summary>
        public int AiOptimalDecisions { get; }

        /// <summary>Всего решений ИИ при раздаче предметов.</summary>
        public int AiTotalDecisions { get; }

        public EquipmentResult(
            int matchSeed,
            IReadOnlyList<HeroLoadout> userTeam,
            IReadOnlyList<HeroLoadout> aiTeam,
            IReadOnlyList<ItemRecord> unassignedUserItems,
            int aiOptimalDecisions,
            int aiTotalDecisions)
        {
            if (aiOptimalDecisions < 0 || aiOptimalDecisions > aiTotalDecisions)
            {
                throw new ArgumentOutOfRangeException(nameof(aiOptimalDecisions), aiOptimalDecisions,
                    "Оптимальных решений не может быть меньше нуля или больше общего числа.");
            }

            MatchSeed = matchSeed;
            UserTeam = userTeam ?? throw new ArgumentNullException(nameof(userTeam));
            AiTeam = aiTeam ?? throw new ArgumentNullException(nameof(aiTeam));
            UnassignedUserItems = unassignedUserItems ?? throw new ArgumentNullException(nameof(unassignedUserItems));
            AiOptimalDecisions = aiOptimalDecisions;
            AiTotalDecisions = aiTotalDecisions;
        }

        /// <summary>Сколько предметов надето на героев пользователя.</summary>
        public int UserEquippedCount => CountEquipped(UserTeam);

        /// <summary>Сколько предметов надето на героев ИИ.</summary>
        public int AiEquippedCount => CountEquipped(AiTeam);

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append("Пользователь: надето ").Append(UserEquippedCount)
                .Append(", в хранилище ").Append(UnassignedUserItems.Count)
                .Append(" | ИИ: надето ").Append(AiEquippedCount)
                .Append(", оптимальных решений ").Append(AiOptimalDecisions)
                .Append('/').Append(AiTotalDecisions);
            return builder.ToString();
        }

        private static int CountEquipped(IReadOnlyList<HeroLoadout> team)
        {
            int count = 0;

            for (int i = 0; i < team.Count; i++)
            {
                count += team[i].EquippedCount;
            }

            return count;
        }
    }
}
