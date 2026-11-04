using System;
using Solutio.Database;
using Solutio.Heroes;
using Solutio.Session;
using Solutio.Settings;
using UnityEngine;

namespace Solutio.Draft
{
    /// <summary>Источник времени для часов драфта.</summary>
    public enum DraftTimeSource
    {
        /// <summary>Time.unscaledTimeAsDouble — не зависит от Time.timeScale.</summary>
        UnscaledTime = 0,

        /// <summary>Time.realtimeSinceStartupAsDouble — реальное время с запуска приложения.</summary>
        RealtimeSinceStartup = 1
    }

    /// <summary>
    /// Контроллер стадии драфта: поочерёдные ходы пользователя и ИИ с таймером на неблокирующих временных метках.
    /// Корутины и WaitForSeconds не используются: начало хода и момент решения ИИ хранятся как метки времени,
    /// а в Update сравниваются с текущим временем. Поэтому сворачивание окна и потеря фокуса не «замораживают»
    /// таймер: после возвращения в игру остаток сразу соответствует реально прошедшему времени.
    /// Логика вынесена в чистые C#-классы, MonoBehaviour только связывает их с представлением DraftViewController:
    /// DraftState — правила и журнал ходов, DraftTurnClock — время хода и запас,
    /// DraftAiStrategist — решения ИИ по профилю сложности (статы, баланс ролей, контрпик),
    /// DraftAutoPickService — принудительный выбор случайного ID героя из таблицы heroes SQLite при истечении времени.
    /// Все случайные решения берутся из Random матча (MatchSessionConfig.CreateMatchRandom), поэтому драфт
    /// воспроизводим по seed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DraftController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DraftViewController view;

        [Header("Timer")]
        [SerializeField] private DraftTimeSource timeSource = DraftTimeSource.RealtimeSinceStartup;

        [Header("AI (время «раздумья» перед ходом)")]
        [SerializeField, Range(0.3f, 10f)] private float aiThinkMinSeconds = 1.5f;
        [SerializeField, Range(0.3f, 15f)] private float aiThinkMaxSeconds = 3.5f;

        [Header("Debug")]
        [SerializeField] private bool enableDetailedLogging = true;

        /// <summary>Драфт завершён, итог команд собран (используется стадией экипировки, День 13).</summary>
        public event Action<DraftResult> OnDraftCompleted;

        /// <summary>Итог последнего завершённого драфта (null, если драфт не завершён).</summary>
        public static DraftResult LastResult { get; private set; }

        /// <summary>true, если драфт идёт.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Состояние текущего драфта (null, если драфт не запущен).</summary>
        public DraftState State => _state;

        private Func<double> _timeReader;
        private DraftState _state;
        private DraftTurnClock _clock;
        private DraftAiStrategist _strategist;
        private DraftAutoPickService _autoPick;
        private System.Random _random;
        private double _aiDecisionAt;
        private bool _isSubscribed;

        private bool _isFocusLost;
        private bool _isFocusReportPending;
        private double _focusLostAtClock;
        private double _focusLostAtUnscaled;
        private double _focusLostAtRealtime;

        #region Unity Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LastResult = null;
        }

        private void Awake()
        {
            _timeReader = ReadTime;

            if (view == null)
            {
                Debug.LogError("[DraftController] Не назначено поле «view» (DraftCanvas) в Инспекторе.", this);
            }
        }

        private void OnEnable()
        {
            if (view == null || _isSubscribed)
            {
                return;
            }

            view.OnDraftShown += HandleDraftShown;
            view.OnDraftHidden += HandleDraftHidden;
            view.OnActionConfirmed += HandleUserConfirmed;
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_isSubscribed && view != null)
            {
                view.OnDraftShown -= HandleDraftShown;
                view.OnDraftHidden -= HandleDraftHidden;
                view.OnActionConfirmed -= HandleUserConfirmed;
            }

            _isSubscribed = false;
            StopDraft();
        }

        private void OnValidate()
        {
            if (aiThinkMaxSeconds < aiThinkMinSeconds)
            {
                aiThinkMaxSeconds = aiThinkMinSeconds;
            }
        }

        private void Update()
        {
            if (_isFocusReportPending)
            {
                _isFocusReportPending = false;
                ReportFocusReturn();
            }

            if (!IsRunning || !_clock.IsRunning)
            {
                return;
            }

            DraftClockSnapshot snapshot = _clock.Sample();
            PushActiveTimer(snapshot);

            if (snapshot.Side == DraftSide.Ai && _timeReader() >= _aiDecisionAt)
            {
                MakeAiMove();
                return;
            }

            if (snapshot.IsExpired)
            {
                HandleTimeout(snapshot.Side);
            }
        }

        /// <summary>
        /// Фиксирует момент потери фокуса (переключение на другое окно, сворачивание).
        /// Замер после возврата откладывается до первого Update: в момент вызова OnApplicationFocus(true)
        /// Time.unscaledTime ещё хранит время начала прошлого кадра и не отражает время отсутствия.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                _isFocusLost = true;
                _isFocusReportPending = false;
                _focusLostAtClock = ReadTime();
                _focusLostAtUnscaled = Time.unscaledTimeAsDouble;
                _focusLostAtRealtime = Time.realtimeSinceStartupAsDouble;

                if (enableDetailedLogging && IsRunning && _clock.IsRunning)
                {
                    Debug.Log($"<color=#307082>[DraftController]</color> Окно потеряло фокус. {_clock.Sample()}");
                }

                return;
            }

            if (_isFocusLost)
            {
                _isFocusLost = false;
                _isFocusReportPending = enableDetailedLogging;
            }
        }

        private void ReportFocusReturn()
        {
            double clockGap = ReadTime() - _focusLostAtClock;
            double unscaledGap = Time.unscaledTimeAsDouble - _focusLostAtUnscaled;
            double realtimeGap = Time.realtimeSinceStartupAsDouble - _focusLostAtRealtime;
            string turnInfo = IsRunning && _clock.IsRunning ? _clock.Sample().ToString() : "драфт не идёт";

            Debug.Log($"<color=#307082>[DraftController]</color> Фокус возвращён (первый кадр). Отсутствие: часы драфта ({timeSource}) {clockGap:0.00} с, unscaledTime {unscaledGap:0.00} с, реальное время {realtimeGap:0.00} с. {turnInfo}");
        }

        #endregion

        #region Draft Flow

        private void HandleDraftShown(MatchSessionConfig config)
        {
            StopDraft();

            if (config == null)
            {
                return;
            }

            try
            {
                _state = new DraftState(view.HeroPool);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[DraftController] Драфт не запущен: {exception.Message}", this);
                return;
            }

            _clock = new DraftTurnClock(_timeReader, view.TurnDurationSeconds, view.ReserveDurationSeconds);
            _random = config.CreateMatchRandom();
            _strategist = new DraftAiStrategist(config.AiProfile);
            _autoPick = new DraftAutoPickService(CreateHeroIdSource());
            LastResult = null;
            IsRunning = true;

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[DraftController]</color> Драфт начат: seed {config.MatchSeed}, ход {_clock.TurnDuration:0} с, запас {_clock.ReserveDuration:0} с, источник времени {timeSource}.");
                Debug.Log($"<color=#307082>[DraftController]</color> ИИ «{MatchCatalog.GetDifficultyDisplayName(config.Difficulty)}»: {config.AiProfile}. Автопик: {(_autoPick.HasSource ? "SQLite heroes" : "только пул в памяти")}.");
            }

            BeginTurn();
        }

        private void HandleDraftHidden()
        {
            if (enableDetailedLogging && IsRunning)
            {
                Debug.Log($"<color=#307082>[DraftController]</color> Драфт прерван на ходу {_state.CurrentStepIndex + 1} из {DraftSequence.TotalSteps}.");
            }

            StopDraft();
        }

        private void HandleUserConfirmed(int heroId)
        {
            if (!IsRunning || !_clock.IsRunning || _clock.ActiveSide != DraftSide.User)
            {
                return;
            }

            if (_clock.Sample().IsExpired)
            {
                HandleTimeout(DraftSide.User);
                return;
            }

            DraftApplyResult validation = _state.Validate(DraftSide.User, heroId);
            if (validation != DraftApplyResult.Applied)
            {
                Debug.LogWarning($"[DraftController] Ход пользователя отклонён: {validation} (герой {heroId}).", this);
                return;
            }

            CommitChoice(DraftSide.User, heroId, DraftResolution.UserChoice);
        }

        private void BeginTurn()
        {
            if (_state.IsComplete)
            {
                FinishDraft();
                return;
            }

            DraftStep step = _state.CurrentStep;
            DraftSide idleSide = step.Side == DraftSide.User ? DraftSide.Ai : DraftSide.User;

            view.SetStep(_state.CurrentStepIndex);
            _clock.StartTurn(step.Side);

            PushIdleTimer(idleSide);
            PushActiveTimer(_clock.Sample());

            if (step.Side == DraftSide.Ai)
            {
                double thinkSeconds = aiThinkMinSeconds + (aiThinkMaxSeconds - aiThinkMinSeconds) * _random.NextDouble();
                _aiDecisionAt = _timeReader() + thinkSeconds;
            }
        }

        /// <summary>
        /// Ход ИИ: решение принимает DraftAiStrategist по профилю сложности матча.
        /// </summary>
        private void MakeAiMove()
        {
            DraftAiDecision decision = _strategist.Decide(_state, _random);

            if (!decision.IsSuccess)
            {
                HandleTimeout(DraftSide.Ai);
                return;
            }

            if (enableDetailedLogging)
            {
                string color = decision.Mode == DraftAiDecisionMode.Optimal ? "#C8A040" : "#8A8F99";
                Debug.Log($"<color={color}>[DraftController]</color> {decision}");
            }

            CommitChoice(DraftSide.Ai, decision.Hero.Id, DraftResolution.AiChoice);
        }

        /// <summary>
        /// Истечение времени хода: бан сгорает, пик закрывается принудительным выбором случайного ID героя
        /// из таблицы heroes SQLite (DraftAutoPickService) с проверкой через DraftState.TryApply(..., AutoPick).
        /// </summary>
        private void HandleTimeout(DraftSide side)
        {
            DraftStep step = _state.CurrentStep;
            DraftClockSnapshot snapshot = _clock.CompleteTurn();

            if (step.Action == DraftActionType.Ban)
            {
                if (_state.ForfeitBan(side, out DraftActionRecord record) == DraftApplyResult.Applied && enableDetailedLogging)
                {
                    Debug.Log($"<color=#C04040>[DraftController]</color> Время вышло: {record}. {snapshot}");
                }

                BeginTurn();
                return;
            }

            AutoPickOutcome outcome = _autoPick.Pick(_state, _random);

            if (!outcome.IsSuccess)
            {
                Debug.LogError($"[DraftController] {outcome} — драфт остановлен.", this);
                StopDraft();
                return;
            }

            if (outcome.Source == AutoPickSource.MemoryFallback)
            {
                Debug.LogWarning($"[DraftController] Автопик выполнен без базы данных: {outcome}", this);
            }

            DraftApplyResult result = _state.TryApply(side, outcome.Hero.Id, DraftResolution.AutoPick, out DraftActionRecord pickRecord);
            if (result != DraftApplyResult.Applied)
            {
                Debug.LogError($"[DraftController] Автопик героя {outcome.Hero.Id} не применён ({result}) — драфт остановлен.", this);
                StopDraft();
                return;
            }

            view.ApplyAction(pickRecord.Step.StepIndex, outcome.Hero.Id);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#C04040>[DraftController]</color> Время вышло. {outcome}. Ход: {pickRecord}. {snapshot}");
            }

            BeginTurn();
        }

        private void CommitChoice(DraftSide side, int heroId, DraftResolution resolution)
        {
            DraftClockSnapshot snapshot = _clock.CompleteTurn();

            DraftApplyResult result = _state.TryApply(side, heroId, resolution, out DraftActionRecord record);
            if (result != DraftApplyResult.Applied)
            {
                Debug.LogError($"[DraftController] Ход не применён: {result} (сторона {side}, герой {heroId}).", this);
                _clock.StartTurn(side);
                return;
            }

            view.ApplyAction(record.Step.StepIndex, heroId);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[DraftController]</color> {record}. Осталось: {snapshot}");
            }

            BeginTurn();
        }

        private void FinishDraft()
        {
            IsRunning = false;
            view.SetStep(DraftSequence.TotalSteps);

            DraftResult result = _state.BuildResult();
            LastResult = result;

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[DraftController]</color> Драфт завершён. {result}. Запас: пользователь {_clock.GetReserveSecondsLeft(DraftSide.User):0.0} с, ИИ {_clock.GetReserveSecondsLeft(DraftSide.Ai):0.0} с.");
            }

            OnDraftCompleted?.Invoke(result);
        }

        private void StopDraft()
        {
            IsRunning = false;
            _state = null;
            _clock = null;
            _strategist = null;
            _autoPick = null;
            _random = null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Источник ID для автопика: репозиторий таблицы heroes. Создаётся при каждом запуске драфта,
        /// потому что DatabaseManager может инициализироваться позже DraftController.
        /// </summary>
        private IAvailableHeroIdSource CreateHeroIdSource()
        {
            DatabaseManager database = DatabaseManager.Instance;

            if (database == null)
            {
                Debug.LogWarning("[DraftController] DatabaseManager не найден — автопик будет выбирать из пула в памяти.", this);
                return null;
            }

            return new HeroRepository(database);
        }

        private void PushActiveTimer(DraftClockSnapshot snapshot)
        {
            view.SetTimer(
                snapshot.Side,
                snapshot.TurnNormalized,
                (float)snapshot.TurnSecondsLeft,
                snapshot.ReserveNormalized,
                (float)snapshot.ReserveSecondsLeft);
        }

        private void PushIdleTimer(DraftSide side)
        {
            double reserveLeft = _clock.GetReserveSecondsLeft(side);
            float reserveNormalized = _clock.ReserveDuration > 0.0 ? (float)(reserveLeft / _clock.ReserveDuration) : 0f;

            view.SetTimer(side, 1f, (float)_clock.TurnDuration, reserveNormalized, (float)reserveLeft);
        }

        private double ReadTime()
        {
            return timeSource == DraftTimeSource.RealtimeSinceStartup
                ? Time.realtimeSinceStartupAsDouble
                : Time.unscaledTimeAsDouble;
        }

        #endregion
    }
}
