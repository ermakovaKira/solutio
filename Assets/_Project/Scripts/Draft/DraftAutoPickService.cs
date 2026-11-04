using System;
using System.Collections.Generic;
using Solutio.Heroes;

namespace Solutio.Draft
{
    /// <summary>Откуда взят герой автопика.</summary>
    public enum AutoPickSource
    {
        /// <summary>Автопик не выполнен: свободных героев нет.</summary>
        None = 0,

        /// <summary>ID получен параметризированным запросом к таблице heroes SQLite.</summary>
        Database = 1,

        /// <summary>База недоступна или вернула пустой результат — выбор из пула в памяти.</summary>
        MemoryFallback = 2
    }

    /// <summary>
    /// Результат автопика: выбранный герой, источник и число кандидатов.
    /// </summary>
    public readonly struct AutoPickOutcome
    {
        /// <summary>Выбранный герой (null при Source = None).</summary>
        public readonly HeroRecord Hero;

        /// <summary>Источник выбора.</summary>
        public readonly AutoPickSource Source;

        /// <summary>Сколько свободных героев было среди кандидатов.</summary>
        public readonly int CandidateCount;

        /// <summary>Сколько героев было исключено как занятые.</summary>
        public readonly int ExcludedCount;

        /// <summary>Причина перехода на резервный выбор (null, если не было).</summary>
        public readonly string FallbackReason;

        public AutoPickOutcome(HeroRecord hero, AutoPickSource source, int candidateCount, int excludedCount, string fallbackReason)
        {
            Hero = hero;
            Source = source;
            CandidateCount = candidateCount;
            ExcludedCount = excludedCount;
            FallbackReason = fallbackReason;
        }

        /// <summary>true, если герой выбран.</summary>
        public bool IsSuccess => Hero != null;

        public override string ToString()
        {
            if (Hero == null)
            {
                return $"[AutoPick] герой не выбран: свободных нет (исключено {ExcludedCount})";
            }

            string source = Source == AutoPickSource.Database ? "SQLite heroes" : "пул в памяти";
            string reason = FallbackReason != null ? $", причина резерва: {FallbackReason}" : string.Empty;
            return $"[AutoPick] ID {Hero.Id} {Hero.Name} — источник {source}, кандидатов {CandidateCount}, исключено {ExcludedCount}{reason}";
        }
    }

    /// <summary>
    /// Сервис принудительного автопика при истечении времени хода (День 12).
    /// Получает из источника (таблица heroes SQLite) ID свободных героев, отбрасывает ID, которых нет
    /// в пуле драфта, и выбирает один через Random матча. Порядок кандидатов фиксирован (по возрастанию ID),
    /// поэтому при одном seed автопик повторяется. Если источник недоступен, выбор идёт из пула DraftState:
    /// он тоже упорядочен по ID, поэтому результат совпадает с выбором через базу.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class DraftAutoPickService
    {
        private readonly IAvailableHeroIdSource _source;
        private readonly List<int> _excludedIds = new List<int>(DraftSequence.TotalSteps);
        private readonly List<int> _sourceIds = new List<int>(32);
        private readonly List<int> _candidateIds = new List<int>(32);
        private readonly List<HeroRecord> _fallbackHeroes = new List<HeroRecord>(32);

        /// <param name="source">Источник ID (может быть null — тогда всегда резервный выбор из памяти).</param>
        public DraftAutoPickService(IAvailableHeroIdSource source)
        {
            _source = source;
        }

        /// <summary>true, если подключён источник данных (SQLite).</summary>
        public bool HasSource => _source != null;

        /// <summary>
        /// Выбирает случайного свободного героя. Состояние драфта не меняет:
        /// результат применяется вызывающим кодом через DraftState.TryApply(..., AutoPick).
        /// Из генератора берётся ровно одно число при любом источнике.
        /// </summary>
        /// <exception cref="ArgumentNullException">state или random равны null.</exception>
        public AutoPickOutcome Pick(DraftState state, Random random)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            state.GetTakenHeroIds(_excludedIds);
            int excludedCount = _excludedIds.Count;

            string fallbackReason;

            if (_source == null)
            {
                fallbackReason = "источник SQLite не подключён";
            }
            else if (!TryLoadFromSource(out fallbackReason))
            {
                // причина уже записана
            }
            else
            {
                CollectCandidates(state);

                if (_candidateIds.Count > 0)
                {
                    int index = random.Next(0, _candidateIds.Count);

                    if (state.TryGetHero(_candidateIds[index], out HeroRecord hero))
                    {
                        return new AutoPickOutcome(hero, AutoPickSource.Database, _candidateIds.Count, excludedCount, null);
                    }
                }

                fallbackReason = "ID из базы не совпали с пулом драфта";
            }

            state.GetAvailableHeroes(_fallbackHeroes);

            if (_fallbackHeroes.Count == 0)
            {
                return new AutoPickOutcome(null, AutoPickSource.None, 0, excludedCount, fallbackReason);
            }

            HeroRecord fallbackHero = _fallbackHeroes[random.Next(0, _fallbackHeroes.Count)];
            return new AutoPickOutcome(fallbackHero, AutoPickSource.MemoryFallback, _fallbackHeroes.Count, excludedCount, fallbackReason);
        }

        private bool TryLoadFromSource(out string failureReason)
        {
            failureReason = null;

            try
            {
                if (_source.TryLoadAvailableIds(_excludedIds, _sourceIds))
                {
                    return true;
                }

                failureReason = "запрос к heroes вернул пустой результат";
                return false;
            }
            catch (Exception exception)
            {
                _sourceIds.Clear();
                failureReason = $"ошибка источника: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// Оставляет только ID, которые есть в пуле драфта и свободны (защита от рассинхронизации
        /// базы и пула, загруженного при открытии драфта).
        /// </summary>
        private void CollectCandidates(DraftState state)
        {
            _candidateIds.Clear();

            for (int i = 0; i < _sourceIds.Count; i++)
            {
                int heroId = _sourceIds[i];

                if (state.IsHeroAvailable(heroId))
                {
                    _candidateIds.Add(heroId);
                }
            }
        }
    }
}
