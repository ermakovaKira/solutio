using System;
using System.Collections.Generic;
using System.Globalization;
using Solutio.Heroes;
using Solutio.Session;

namespace Solutio.Draft
{
    /// <summary>Как ИИ принял решение на ходу драфта.</summary>
    public enum DraftAiDecisionMode
    {
        /// <summary>Решение не принято: свободных героев нет.</summary>
        None = 0,

        /// <summary>Бросок удачен — выбран лучший герой по оценке стратега.</summary>
        Optimal = 1,

        /// <summary>Бросок неудачен — выбран случайный свободный герой.</summary>
        Random = 2
    }

    /// <summary>
    /// Решение ИИ на одном ходу драфта с пояснением для журнала.
    /// </summary>
    public readonly struct DraftAiDecision
    {
        /// <summary>Выбранный герой (null при Mode = None).</summary>
        public readonly HeroRecord Hero;

        /// <summary>Тип хода (бан или пик).</summary>
        public readonly DraftActionType Action;

        /// <summary>Способ принятия решения.</summary>
        public readonly DraftAiDecisionMode Mode;

        /// <summary>Шанс оптимального решения из профиля сложности.</summary>
        public readonly float Chance;

        /// <summary>Значение броска решения [0; 1).</summary>
        public readonly double Roll;

        /// <summary>Итоговая оценка героя (0 для случайного выбора).</summary>
        public readonly float Score;

        /// <summary>Вклад силы по статам.</summary>
        public readonly float StatPart;

        /// <summary>Вклад баланса ролей.</summary>
        public readonly float RolePart;

        /// <summary>Вклад контрпика.</summary>
        public readonly float CounterPart;

        public DraftAiDecision(
            HeroRecord hero,
            DraftActionType action,
            DraftAiDecisionMode mode,
            float chance,
            double roll,
            float score,
            float statPart,
            float rolePart,
            float counterPart)
        {
            Hero = hero;
            Action = action;
            Mode = mode;
            Chance = chance;
            Roll = roll;
            Score = score;
            StatPart = statPart;
            RolePart = rolePart;
            CounterPart = counterPart;
        }

        /// <summary>true, если герой выбран.</summary>
        public bool IsSuccess => Hero != null;

        public override string ToString()
        {
            if (Hero == null)
            {
                return "[AiDraft] решение не принято: свободных героев нет";
            }

            string action = Action == DraftActionType.Ban ? "бан" : "пик";

            if (Mode == DraftAiDecisionMode.Random)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "[AiDraft] {0} #{1} {2} — СЛУЧАЙНО (бросок {3:0.00} ≥ шанс {4:0.00})",
                    action, Hero.Id, Hero.Name, Roll, Chance);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "[AiDraft] {0} #{1} {2} — ОПТИМАЛЬНО (бросок {3:0.00} < шанс {4:0.00}): оценка {5:0.00} = статы {6:0.00} + роль {7:0.00} + контрпик {8:0.00}",
                action, Hero.Id, Hero.Name, Roll, Chance, Score, StatPart, RolePart, CounterPart);
        }
    }

    /// <summary>
    /// Стратег драфта ИИ (День 12). Сложность влияет только на качество решений, а не на характеристики героев:
    /// 1) бросок AiStrategyProfile.Roll(DraftOptimalPickChance) решает, думает ИИ или берёт случайного героя;
    /// 2) при оптимальном решении каждый свободный герой получает оценку = статы + баланс ролей + контрпик,
    ///    где вес контрпика масштабируется CounterPickAwareness.
    /// Пик: ИИ добирает недостающие роли до состава 2 танка / 2 керри / 1 поддержка, учитывает силу героя
    /// внутри роли и берёт героев, сильных против ролей, уже выбранных пользователем.
    /// Бан: ИИ отнимает у пользователя сильного героя нужной ему роли и героев, опасных для состава ИИ.
    /// Треугольник ролей: Танк сдерживает Керри, Керри уничтожает Поддержку, Поддержка ослабляет Танка.
    /// Из генератора берётся одно число на бросок и ещё одно при случайном выборе — ход воспроизводим по seed.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class DraftAiStrategist
    {
        /// <summary>Целевое количество танков в команде.</summary>
        public const int TargetTanks = 2;

        /// <summary>Целевое количество керри в команде.</summary>
        public const int TargetCarries = 2;

        /// <summary>Целевое количество героев поддержки в команде.</summary>
        public const int TargetSupports = 1;

        /// <summary>Вес силы героя по статам (оценка статов лежит в [0; 1]).</summary>
        public const float StatWeight = 0.6f;

        /// <summary>Бонус пика за недостающую роль.</summary>
        public const float RoleNeedWeight = 1.0f;

        /// <summary>Максимальный бонус пика за контр к ролям пользователя (умножается на CounterPickAwareness).</summary>
        public const float CounterPickWeight = 0.8f;

        /// <summary>Бонус бана за роль, которую пользователю ещё нужно добрать (умножается на CounterPickAwareness).</summary>
        public const float BanDenyRoleWeight = 0.8f;

        /// <summary>Бонус бана за героя, опасного для состава ИИ (умножается на CounterPickAwareness).</summary>
        public const float BanThreatWeight = 0.6f;

        /// <summary>Коэффициент брони в эффективном здоровье: EHP = HP × (1 + 0.06 × броня).</summary>
        public const float ArmorEffectiveness = 0.06f;

        private const int RoleCount = 3;

        private static readonly int[] TargetByRole = { TargetTanks, TargetCarries, TargetSupports };

        private readonly AiStrategyProfile _profile;
        private readonly List<HeroRecord> _available = new List<HeroRecord>(32);
        private readonly int[] _userPicksByRole = new int[RoleCount];
        private readonly int[] _aiPicksByRole = new int[RoleCount];
        private readonly float[] _maxAttackByRole = new float[RoleCount];
        private readonly float[] _maxEffectiveHpByRole = new float[RoleCount];

        private IReadOnlyList<HeroRecord> _normalizedPool;

        public DraftAiStrategist(AiStrategyProfile profile)
        {
            _profile = profile;
        }

        /// <summary>Профиль сложности, по которому принимаются решения.</summary>
        public AiStrategyProfile Profile => _profile;

        /// <summary>
        /// Принимает решение ИИ на текущем ходу. Состояние драфта не меняет:
        /// результат применяется вызывающим кодом через DraftState.TryApply(..., AiChoice).
        /// </summary>
        /// <exception cref="ArgumentNullException">state или random равны null.</exception>
        /// <exception cref="InvalidOperationException">Драфт завершён или сейчас ход пользователя.</exception>
        public DraftAiDecision Decide(DraftState state, Random random)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            DraftStep step = state.CurrentStep;

            if (step.Side != DraftSide.Ai)
            {
                throw new InvalidOperationException($"Сейчас ход стороны {step.Side}, а не ИИ.");
            }

            state.GetAvailableHeroes(_available);

            if (_available.Count == 0)
            {
                return new DraftAiDecision(null, step.Action, DraftAiDecisionMode.None, _profile.DraftOptimalPickChance, 0d, 0f, 0f, 0f, 0f);
            }

            double roll = random.NextDouble();

            if (!AiStrategyProfile.Roll(_profile.DraftOptimalPickChance, roll))
            {
                HeroRecord randomHero = _available[random.Next(0, _available.Count)];
                return new DraftAiDecision(randomHero, step.Action, DraftAiDecisionMode.Random, _profile.DraftOptimalPickChance, roll, 0f, 0f, 0f, 0f);
            }

            EnsureNormalization(state.Pool);
            CountPicks(state.History);

            HeroRecord bestHero = null;
            float bestScore = float.MinValue;
            float bestStat = 0f;
            float bestRole = 0f;
            float bestCounter = 0f;

            for (int i = 0; i < _available.Count; i++)
            {
                HeroRecord hero = _available[i];
                float statPart = StatWeight * EvaluateStatScore(hero);
                float rolePart;
                float counterPart;

                if (step.Action == DraftActionType.Pick)
                {
                    rolePart = EvaluatePickRole(hero.Role);
                    counterPart = EvaluatePickCounter(hero.Role);
                }
                else
                {
                    rolePart = EvaluateBanDenyRole(hero.Role);
                    counterPart = EvaluateBanThreat(hero.Role);
                }

                float score = statPart + rolePart + counterPart;

                // Строгое «больше»: при равенстве остаётся герой с меньшим ID (пул упорядочен по ID).
                if (score > bestScore)
                {
                    bestScore = score;
                    bestHero = hero;
                    bestStat = statPart;
                    bestRole = rolePart;
                    bestCounter = counterPart;
                }
            }

            return new DraftAiDecision(bestHero, step.Action, DraftAiDecisionMode.Optimal, _profile.DraftOptimalPickChance, roll, bestScore, bestStat, bestRole, bestCounter);
        }

        /// <summary>Роль, против которой сильна указанная роль (треугольник ролей).</summary>
        public static HeroRole GetCounteredRole(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                    return HeroRole.Carry;
                case HeroRole.Carry:
                    return HeroRole.Support;
                case HeroRole.Support:
                    return HeroRole.Tank;
                default:
                    throw new ArgumentOutOfRangeException(nameof(role), role, "Неизвестная роль героя.");
            }
        }

        /// <summary>Целевое количество героев роли в команде из 5.</summary>
        public static int GetTargetCount(HeroRole role)
        {
            int index = (int)role;

            if (index < 0 || index >= RoleCount)
            {
                throw new ArgumentOutOfRangeException(nameof(role), role, "Неизвестная роль героя.");
            }

            return TargetByRole[index];
        }

        /// <summary>Эффективное здоровье героя с учётом брони.</summary>
        public static float GetEffectiveHp(HeroRecord hero)
        {
            if (hero == null)
            {
                throw new ArgumentNullException(nameof(hero));
            }

            return hero.BaseHp * (1f + ArmorEffectiveness * hero.BaseArmor);
        }

        /// <summary>
        /// Сила героя внутри своей роли в диапазоне [0; 1]: атака и эффективное здоровье нормируются
        /// по максимуму роли в пуле и смешиваются с весами роли (танку важнее живучесть, керри — атака).
        /// </summary>
        private float EvaluateStatScore(HeroRecord hero)
        {
            int roleIndex = (int)hero.Role;
            float maxAttack = _maxAttackByRole[roleIndex];
            float maxEffectiveHp = _maxEffectiveHpByRole[roleIndex];

            float attack = maxAttack > 0f ? hero.BaseAttack / maxAttack : 0f;
            float survivability = maxEffectiveHp > 0f ? GetEffectiveHp(hero) / maxEffectiveHp : 0f;

            switch (hero.Role)
            {
                case HeroRole.Tank:
                    return 0.3f * attack + 0.7f * survivability;
                case HeroRole.Carry:
                    return 0.75f * attack + 0.25f * survivability;
                default:
                    return 0.5f * attack + 0.5f * survivability;
            }
        }

        private float EvaluatePickRole(HeroRole role)
        {
            return _aiPicksByRole[(int)role] < TargetByRole[(int)role] ? RoleNeedWeight : 0f;
        }

        private float EvaluatePickCounter(HeroRole role)
        {
            int userTargets = _userPicksByRole[(int)GetCounteredRole(role)];
            return _profile.CounterPickAwareness * CounterPickWeight * userTargets / DraftSequence.TeamSize;
        }

        private float EvaluateBanDenyRole(HeroRole role)
        {
            return _userPicksByRole[(int)role] < TargetByRole[(int)role]
                ? _profile.CounterPickAwareness * BanDenyRoleWeight
                : 0f;
        }

        private float EvaluateBanThreat(HeroRole role)
        {
            int aiVictims = _aiPicksByRole[(int)GetCounteredRole(role)];
            return _profile.CounterPickAwareness * BanThreatWeight * aiVictims / DraftSequence.TeamSize;
        }

        private void CountPicks(IReadOnlyList<DraftActionRecord> history)
        {
            Array.Clear(_userPicksByRole, 0, RoleCount);
            Array.Clear(_aiPicksByRole, 0, RoleCount);

            for (int i = 0; i < history.Count; i++)
            {
                DraftActionRecord record = history[i];

                if (record.IsEmpty || record.Step.Action != DraftActionType.Pick)
                {
                    continue;
                }

                int roleIndex = (int)record.Hero.Role;

                if (record.Step.Side == DraftSide.User)
                {
                    _userPicksByRole[roleIndex]++;
                }
                else
                {
                    _aiPicksByRole[roleIndex]++;
                }
            }
        }

        /// <summary>
        /// Максимумы атаки и эффективного здоровья по ролям считаются по всему пулу один раз,
        /// поэтому оценка героя не меняется по ходу драфта.
        /// </summary>
        private void EnsureNormalization(IReadOnlyList<HeroRecord> pool)
        {
            if (ReferenceEquals(_normalizedPool, pool))
            {
                return;
            }

            Array.Clear(_maxAttackByRole, 0, RoleCount);
            Array.Clear(_maxEffectiveHpByRole, 0, RoleCount);

            for (int i = 0; i < pool.Count; i++)
            {
                HeroRecord hero = pool[i];
                int roleIndex = (int)hero.Role;

                if (hero.BaseAttack > _maxAttackByRole[roleIndex])
                {
                    _maxAttackByRole[roleIndex] = hero.BaseAttack;
                }

                float effectiveHp = GetEffectiveHp(hero);
                if (effectiveHp > _maxEffectiveHpByRole[roleIndex])
                {
                    _maxEffectiveHpByRole[roleIndex] = effectiveHp;
                }
            }

            _normalizedPool = pool;
        }
    }
}
