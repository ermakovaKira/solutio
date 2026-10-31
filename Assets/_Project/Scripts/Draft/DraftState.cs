using System;
using System.Collections.Generic;
using Solutio.Heroes;

namespace Solutio.Draft
{
    /// <summary>Каким образом был закрыт ход драфта.</summary>
    public enum DraftResolution
    {
        /// <summary>Пользователь выбрал героя сам.</summary>
        UserChoice = 0,

        /// <summary>Ход сделан логикой ИИ.</summary>
        AiChoice = 1,

        /// <summary>Время истекло во время бана — бан сгорел, слот остался пустым.</summary>
        BanForfeited = 2,

        /// <summary>Время истекло во время пика — герой назначен автоматически.</summary>
        AutoPick = 3
    }

    /// <summary>Результат попытки применить ход.</summary>
    public enum DraftApplyResult
    {
        Applied = 0,
        DraftComplete = 1,
        WrongSide = 2,
        WrongActionType = 3,
        UnknownHero = 4,
        HeroUnavailable = 5
    }

    /// <summary>
    /// Запись об одном закрытом ходе драфта.
    /// </summary>
    public readonly struct DraftActionRecord
    {
        /// <summary>Ход последовательности драфта.</summary>
        public readonly DraftStep Step;

        /// <summary>Герой (null, если бан сгорел).</summary>
        public readonly HeroRecord Hero;

        /// <summary>Способ закрытия хода.</summary>
        public readonly DraftResolution Resolution;

        public DraftActionRecord(DraftStep step, HeroRecord hero, DraftResolution resolution)
        {
            Step = step;
            Hero = hero;
            Resolution = resolution;
        }

        /// <summary>true, если ход закрыт без героя (сгоревший бан).</summary>
        public bool IsEmpty => Hero == null;

        public override string ToString()
        {
            string heroName = Hero != null ? Hero.Name : "—";
            return $"{Step} → {heroName} [{Resolution}]";
        }
    }

    /// <summary>
    /// Итог драфта: составы и баны обеих команд. Передаётся на стадию экипировки (День 13).
    /// </summary>
    public sealed class DraftResult
    {
        /// <summary>Герои команды пользователя в порядке пиков.</summary>
        public IReadOnlyList<HeroRecord> UserTeam { get; }

        /// <summary>Герои команды ИИ в порядке пиков.</summary>
        public IReadOnlyList<HeroRecord> AiTeam { get; }

        /// <summary>Герои, забаненные пользователем (сгоревшие баны не входят).</summary>
        public IReadOnlyList<HeroRecord> UserBans { get; }

        /// <summary>Герои, забаненные ИИ (сгоревшие баны не входят).</summary>
        public IReadOnlyList<HeroRecord> AiBans { get; }

        /// <summary>Полный журнал ходов драфта.</summary>
        public IReadOnlyList<DraftActionRecord> History { get; }

        public DraftResult(
            IReadOnlyList<HeroRecord> userTeam,
            IReadOnlyList<HeroRecord> aiTeam,
            IReadOnlyList<HeroRecord> userBans,
            IReadOnlyList<HeroRecord> aiBans,
            IReadOnlyList<DraftActionRecord> history)
        {
            UserTeam = userTeam ?? throw new ArgumentNullException(nameof(userTeam));
            AiTeam = aiTeam ?? throw new ArgumentNullException(nameof(aiTeam));
            UserBans = userBans ?? throw new ArgumentNullException(nameof(userBans));
            AiBans = aiBans ?? throw new ArgumentNullException(nameof(aiBans));
            History = history ?? throw new ArgumentNullException(nameof(history));
        }

        public override string ToString()
        {
            return $"Пользователь: {JoinNames(UserTeam)} | ИИ: {JoinNames(AiTeam)}";
        }

        private static string JoinNames(IReadOnlyList<HeroRecord> heroes)
        {
            string[] names = new string[heroes.Count];

            for (int i = 0; i < heroes.Count; i++)
            {
                names[i] = heroes[i].Name;
            }

            return string.Join(", ", names);
        }
    }

    /// <summary>
    /// Состояние драфта: текущий ход, занятые герои, журнал ходов и итог команд.
    /// Проверяет очерёдность сторон и тип хода, не допускает повторного выбора героя.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class DraftState
    {
        private readonly Dictionary<int, HeroRecord> _heroesById;
        private readonly HashSet<int> _takenHeroIds;
        private readonly List<HeroRecord> _pool;
        private readonly List<DraftActionRecord> _history;

        /// <summary>Номер текущего хода (с нуля) или TotalSteps, если драфт завершён.</summary>
        public int CurrentStepIndex { get; private set; }

        /// <summary>true, если все ходы драфта закрыты.</summary>
        public bool IsComplete => CurrentStepIndex >= DraftSequence.TotalSteps;

        /// <summary>Пул героев драфта.</summary>
        public IReadOnlyList<HeroRecord> Pool => _pool;

        /// <summary>Журнал закрытых ходов.</summary>
        public IReadOnlyList<DraftActionRecord> History => _history;

        /// <summary>Количество героев, ещё доступных для бана или пика.</summary>
        public int AvailableCount => _pool.Count - _takenHeroIds.Count;

        /// <param name="heroPool">Пул героев из таблицы heroes (не меньше DraftSequence.RequiredHeroCount, ID без повторов).</param>
        public DraftState(IReadOnlyList<HeroRecord> heroPool)
        {
            if (heroPool == null)
            {
                throw new ArgumentNullException(nameof(heroPool));
            }

            if (heroPool.Count < DraftSequence.RequiredHeroCount)
            {
                throw new ArgumentException($"Для драфта нужно не меньше {DraftSequence.RequiredHeroCount} героев, получено {heroPool.Count}.", nameof(heroPool));
            }

            _heroesById = new Dictionary<int, HeroRecord>(heroPool.Count);
            _takenHeroIds = new HashSet<int>();
            _pool = new List<HeroRecord>(heroPool.Count);
            _history = new List<DraftActionRecord>(DraftSequence.TotalSteps);

            for (int i = 0; i < heroPool.Count; i++)
            {
                HeroRecord hero = heroPool[i];

                if (hero == null)
                {
                    throw new ArgumentException($"Элемент {i} пула героев равен null.", nameof(heroPool));
                }

                if (_heroesById.ContainsKey(hero.Id))
                {
                    throw new ArgumentException($"ID героя {hero.Id} встречается в пуле дважды.", nameof(heroPool));
                }

                _heroesById.Add(hero.Id, hero);
                _pool.Add(hero);
            }

            CurrentStepIndex = 0;
        }

        /// <summary>
        /// Текущий ход драфта.
        /// </summary>
        /// <exception cref="InvalidOperationException">Драфт уже завершён.</exception>
        public DraftStep CurrentStep
        {
            get
            {
                if (IsComplete)
                {
                    throw new InvalidOperationException("Драфт завершён: текущего хода нет.");
                }

                return DraftSequence.GetStep(CurrentStepIndex);
            }
        }

        /// <summary>true, если герой есть в пуле и ещё не забанен и не выбран.</summary>
        public bool IsHeroAvailable(int heroId)
        {
            return _heroesById.ContainsKey(heroId) && !_takenHeroIds.Contains(heroId);
        }

        /// <summary>Возвращает героя пула по ID.</summary>
        public bool TryGetHero(int heroId, out HeroRecord hero)
        {
            return _heroesById.TryGetValue(heroId, out hero);
        }

        /// <summary>
        /// Заполняет буфер доступными героями без выделения памяти (буфер очищается).
        /// </summary>
        public void GetAvailableHeroes(List<HeroRecord> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();

            for (int i = 0; i < _pool.Count; i++)
            {
                if (!_takenHeroIds.Contains(_pool[i].Id))
                {
                    buffer.Add(_pool[i]);
                }
            }
        }

        /// <summary>
        /// Проверяет, можно ли стороне закрыть текущий ход указанным героем. Состояние не меняет.
        /// </summary>
        public DraftApplyResult Validate(DraftSide side, int heroId)
        {
            if (IsComplete)
            {
                return DraftApplyResult.DraftComplete;
            }

            if (CurrentStep.Side != side)
            {
                return DraftApplyResult.WrongSide;
            }

            if (!_heroesById.ContainsKey(heroId))
            {
                return DraftApplyResult.UnknownHero;
            }

            return _takenHeroIds.Contains(heroId) ? DraftApplyResult.HeroUnavailable : DraftApplyResult.Applied;
        }

        /// <summary>
        /// Закрывает текущий ход выбранным героем (бан или пик — по последовательности драфта).
        /// </summary>
        /// <param name="side">Сторона, которая делает ход.</param>
        /// <param name="heroId">ID героя.</param>
        /// <param name="resolution">UserChoice, AiChoice или AutoPick.</param>
        /// <param name="record">Запись о закрытом ходе (при успехе).</param>
        public DraftApplyResult TryApply(DraftSide side, int heroId, DraftResolution resolution, out DraftActionRecord record)
        {
            record = default;

            if (resolution == DraftResolution.BanForfeited)
            {
                throw new ArgumentException("Сгоревший бан закрывается методом ForfeitBan.", nameof(resolution));
            }

            DraftApplyResult result = Validate(side, heroId);
            if (result != DraftApplyResult.Applied)
            {
                return result;
            }

            DraftStep step = CurrentStep;

            if (resolution == DraftResolution.AutoPick && step.Action != DraftActionType.Pick)
            {
                return DraftApplyResult.WrongActionType;
            }

            HeroRecord hero = _heroesById[heroId];
            _takenHeroIds.Add(heroId);

            record = new DraftActionRecord(step, hero, resolution);
            _history.Add(record);
            CurrentStepIndex++;

            return DraftApplyResult.Applied;
        }

        /// <summary>
        /// Закрывает текущий бан без героя (время стороны истекло — бан сгорает).
        /// </summary>
        /// <param name="record">Запись о сгоревшем бане (при успехе).</param>
        public DraftApplyResult ForfeitBan(DraftSide side, out DraftActionRecord record)
        {
            record = default;

            if (IsComplete)
            {
                return DraftApplyResult.DraftComplete;
            }

            DraftStep step = CurrentStep;

            if (step.Side != side)
            {
                return DraftApplyResult.WrongSide;
            }

            if (step.Action != DraftActionType.Ban)
            {
                return DraftApplyResult.WrongActionType;
            }

            record = new DraftActionRecord(step, null, DraftResolution.BanForfeited);
            _history.Add(record);
            CurrentStepIndex++;

            return DraftApplyResult.Applied;
        }

        /// <summary>
        /// Собирает итог драфта.
        /// </summary>
        /// <exception cref="InvalidOperationException">Драфт ещё не завершён.</exception>
        public DraftResult BuildResult()
        {
            if (!IsComplete)
            {
                throw new InvalidOperationException($"Драфт не завершён: закрыто {CurrentStepIndex} из {DraftSequence.TotalSteps} ходов.");
            }

            List<HeroRecord> userTeam = new List<HeroRecord>(DraftSequence.TeamSize);
            List<HeroRecord> aiTeam = new List<HeroRecord>(DraftSequence.TeamSize);
            List<HeroRecord> userBans = new List<HeroRecord>(DraftSequence.BansPerSide);
            List<HeroRecord> aiBans = new List<HeroRecord>(DraftSequence.BansPerSide);

            for (int i = 0; i < _history.Count; i++)
            {
                DraftActionRecord record = _history[i];

                if (record.IsEmpty)
                {
                    continue;
                }

                bool isUser = record.Step.Side == DraftSide.User;

                if (record.Step.Action == DraftActionType.Pick)
                {
                    (isUser ? userTeam : aiTeam).Add(record.Hero);
                }
                else
                {
                    (isUser ? userBans : aiBans).Add(record.Hero);
                }
            }

            return new DraftResult(
                userTeam.AsReadOnly(),
                aiTeam.AsReadOnly(),
                userBans.AsReadOnly(),
                aiBans.AsReadOnly(),
                new List<DraftActionRecord>(_history).AsReadOnly());
        }
    }
}
