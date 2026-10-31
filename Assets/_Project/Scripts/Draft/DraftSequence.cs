using System;
using System.Collections.Generic;

namespace Solutio.Draft
{
    /// <summary>Сторона драфта: пользователь или ИИ.</summary>
    public enum DraftSide
    {
        User = 0,
        Ai = 1
    }

    /// <summary>Тип хода драфта: бан или пик героя.</summary>
    public enum DraftActionType
    {
        Ban = 0,
        Pick = 1
    }

    /// <summary>
    /// Один ход драфта: кто ходит, бан или пик, номер фазы и слот команды, который заполняется.
    /// </summary>
    public readonly struct DraftStep : IEquatable<DraftStep>
    {
        /// <summary>Порядковый номер хода (с нуля).</summary>
        public readonly int StepIndex;

        /// <summary>Сторона, которая делает ход.</summary>
        public readonly DraftSide Side;

        /// <summary>Бан или пик.</summary>
        public readonly DraftActionType Action;

        /// <summary>Номер фазы драфта (с нуля).</summary>
        public readonly int PhaseIndex;

        /// <summary>Индекс слота стороны: 0–2 для банов, 0–4 для пиков.</summary>
        public readonly int SlotIndex;

        public DraftStep(int stepIndex, DraftSide side, DraftActionType action, int phaseIndex, int slotIndex)
        {
            StepIndex = stepIndex;
            Side = side;
            Action = action;
            PhaseIndex = phaseIndex;
            SlotIndex = slotIndex;
        }

        public bool Equals(DraftStep other)
        {
            return StepIndex == other.StepIndex
                   && Side == other.Side
                   && Action == other.Action
                   && PhaseIndex == other.PhaseIndex
                   && SlotIndex == other.SlotIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is DraftStep other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StepIndex;
                hash = (hash * 397) ^ (int)Side;
                hash = (hash * 397) ^ (int)Action;
                hash = (hash * 397) ^ PhaseIndex;
                hash = (hash * 397) ^ SlotIndex;
                return hash;
            }
        }

        public override string ToString()
        {
            return $"Ход {StepIndex + 1}: {Side} {Action} (фаза {PhaseIndex + 1}, слот {SlotIndex + 1})";
        }
    }

    /// <summary>
    /// Последовательность драфта по мотивам режима Captains Mode из Dota 2 (облегчённая версия под пул 20 героев):
    /// Баны 1: П, ИИ, П, ИИ → Пики 1: П, ИИ, ИИ, П → Баны 2: ИИ, П → Пики 2: ИИ, П, П, ИИ → Пики 3: П, ИИ.
    /// Итого 6 банов (по 3) и 10 пиков (по 5), последний пик у второй пикающей стороны.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public static class DraftSequence
    {
        /// <summary>Количество героев в команде.</summary>
        public const int TeamSize = 5;

        /// <summary>Количество банов у каждой стороны.</summary>
        public const int BansPerSide = 3;

        /// <summary>Общее количество ходов драфта.</summary>
        public const int TotalSteps = (TeamSize + BansPerSide) * 2;

        /// <summary>Минимальный размер пула героев, при котором драфт возможен.</summary>
        public const int RequiredHeroCount = TotalSteps;

        private static readonly string[] PhaseDisplayNames =
        {
            "ФАЗА БАНОВ I",
            "ФАЗА ПИКОВ I",
            "ФАЗА БАНОВ II",
            "ФАЗА ПИКОВ II",
            "ФАЗА ПИКОВ III"
        };

        private static readonly DraftStep[] Steps = BuildSteps();

        /// <summary>Все ходы драфта по порядку.</summary>
        public static IReadOnlyList<DraftStep> All => Steps;

        /// <summary>Количество фаз драфта.</summary>
        public static int PhaseCount => PhaseDisplayNames.Length;

        /// <summary>Возвращает ход по номеру (с нуля).</summary>
        public static DraftStep GetStep(int stepIndex)
        {
            if (!IsValidStepIndex(stepIndex))
            {
                throw new ArgumentOutOfRangeException(nameof(stepIndex), stepIndex, $"Номер хода должен лежать в диапазоне [0; {TotalSteps - 1}].");
            }

            return Steps[stepIndex];
        }

        /// <summary>true, если номер хода допустим.</summary>
        public static bool IsValidStepIndex(int stepIndex)
        {
            return stepIndex >= 0 && stepIndex < TotalSteps;
        }

        /// <summary>Название фазы для интерфейса.</summary>
        public static string GetPhaseDisplayName(int phaseIndex)
        {
            if (phaseIndex < 0 || phaseIndex >= PhaseDisplayNames.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(phaseIndex), phaseIndex, "Неизвестная фаза драфта.");
            }

            return PhaseDisplayNames[phaseIndex];
        }

        private static DraftStep[] BuildSteps()
        {
            const DraftSide u = DraftSide.User;
            const DraftSide a = DraftSide.Ai;
            const DraftActionType ban = DraftActionType.Ban;
            const DraftActionType pick = DraftActionType.Pick;

            DraftSide[] sides =
            {
                u, a, u, a,
                u, a, a, u,
                a, u,
                a, u, u, a,
                u, a
            };

            DraftActionType[] actions =
            {
                ban, ban, ban, ban,
                pick, pick, pick, pick,
                ban, ban,
                pick, pick, pick, pick,
                pick, pick
            };

            int[] phases =
            {
                0, 0, 0, 0,
                1, 1, 1, 1,
                2, 2,
                3, 3, 3, 3,
                4, 4
            };

            DraftStep[] steps = new DraftStep[TotalSteps];
            int[] banCounters = new int[2];
            int[] pickCounters = new int[2];

            for (int i = 0; i < TotalSteps; i++)
            {
                int sideIndex = (int)sides[i];
                int slotIndex = actions[i] == DraftActionType.Ban
                    ? banCounters[sideIndex]++
                    : pickCounters[sideIndex]++;

                steps[i] = new DraftStep(i, sides[i], actions[i], phases[i], slotIndex);
            }

            return steps;
        }
    }
}
