using System;

namespace Solutio.Draft
{
    /// <summary>
    /// Снимок состояния часов хода в конкретный момент времени.
    /// </summary>
    public readonly struct DraftClockSnapshot
    {
        /// <summary>Сторона, чей ход отсчитывается.</summary>
        public readonly DraftSide Side;

        /// <summary>Остаток основного времени хода, секунды.</summary>
        public readonly double TurnSecondsLeft;

        /// <summary>Остаток запаса времени стороны на весь драфт, секунды.</summary>
        public readonly double ReserveSecondsLeft;

        /// <summary>Доля оставшегося основного времени хода в диапазоне [0; 1].</summary>
        public readonly float TurnNormalized;

        /// <summary>Доля оставшегося запаса в диапазоне [0; 1].</summary>
        public readonly float ReserveNormalized;

        /// <summary>true, если основное время хода истекло и тает запас.</summary>
        public readonly bool IsUsingReserve;

        /// <summary>true, если истекли и время хода, и запас стороны.</summary>
        public readonly bool IsExpired;

        public DraftClockSnapshot(
            DraftSide side,
            double turnSecondsLeft,
            double reserveSecondsLeft,
            float turnNormalized,
            float reserveNormalized,
            bool isUsingReserve,
            bool isExpired)
        {
            Side = side;
            TurnSecondsLeft = turnSecondsLeft;
            ReserveSecondsLeft = reserveSecondsLeft;
            TurnNormalized = turnNormalized;
            ReserveNormalized = reserveNormalized;
            IsUsingReserve = isUsingReserve;
            IsExpired = isExpired;
        }

        public override string ToString()
        {
            return $"{Side}: ход {TurnSecondsLeft:0.00} с, запас {ReserveSecondsLeft:0.00} с{(IsExpired ? " (истекло)" : string.Empty)}";
        }
    }

    /// <summary>
    /// Часы ходов драфта на неблокирующих временных метках.
    /// Начало хода запоминается как метка времени, остаток вычисляется как разность «дедлайн − сейчас»,
    /// поэтому сворачивание окна или пропуск кадров не «замораживают» таймер и не рассинхронизируют его:
    /// при следующем опросе остаток сразу соответствует реально прошедшему времени.
    /// Сначала тает основное время хода, после его истечения — запас стороны на весь драфт.
    /// Корутины и WaitForSeconds не используются. Источник времени внедряется через конструктор,
    /// что позволяет покрыть класс NUnit-тестами с управляемыми часами (День 24).
    /// </summary>
    public sealed class DraftTurnClock
    {
        /// <summary>Основное время хода по умолчанию, секунды.</summary>
        public const double DefaultTurnSeconds = 30.0;

        /// <summary>Запас времени каждой стороны на весь драфт по умолчанию, секунды.</summary>
        public const double DefaultReserveSeconds = 60.0;

        private const int SideCount = 2;

        private readonly Func<double> _timeSource;
        private readonly double[] _reserveLeft = new double[SideCount];

        private double _turnStartedAt;
        private double _reserveAtTurnStart;

        /// <summary>Основное время одного хода, секунды.</summary>
        public double TurnDuration { get; }

        /// <summary>Полный запас времени одной стороны на весь драфт, секунды.</summary>
        public double ReserveDuration { get; }

        /// <summary>true, если сейчас идёт отсчёт хода.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Сторона, чей ход отсчитывается (актуально при IsRunning).</summary>
        public DraftSide ActiveSide { get; private set; }

        /// <param name="timeSource">Монотонный источник времени в секундах (например, Time.unscaledTimeAsDouble).</param>
        /// <param name="turnDuration">Основное время хода, секунды (больше нуля).</param>
        /// <param name="reserveDuration">Запас стороны на весь драфт, секунды (не меньше нуля).</param>
        public DraftTurnClock(Func<double> timeSource, double turnDuration = DefaultTurnSeconds, double reserveDuration = DefaultReserveSeconds)
        {
            _timeSource = timeSource ?? throw new ArgumentNullException(nameof(timeSource));

            if (double.IsNaN(turnDuration) || double.IsInfinity(turnDuration) || turnDuration <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(turnDuration), turnDuration, "Время хода должно быть конечным положительным числом.");
            }

            if (double.IsNaN(reserveDuration) || double.IsInfinity(reserveDuration) || reserveDuration < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(reserveDuration), reserveDuration, "Запас времени должен быть конечным неотрицательным числом.");
            }

            TurnDuration = turnDuration;
            ReserveDuration = reserveDuration;
            Reset();
        }

        /// <summary>
        /// Возвращает часы в исходное состояние: полный запас обеим сторонам, отсчёт остановлен.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < SideCount; i++)
            {
                _reserveLeft[i] = ReserveDuration;
            }

            _turnStartedAt = 0.0;
            _reserveAtTurnStart = 0.0;
            ActiveSide = DraftSide.User;
            IsRunning = false;
        }

        /// <summary>
        /// Остаток запаса стороны с учётом идущего хода (для активной стороны — на текущий момент).
        /// </summary>
        public double GetReserveSecondsLeft(DraftSide side)
        {
            if (IsRunning && side == ActiveSide)
            {
                return Evaluate(ReadTime()).ReserveSecondsLeft;
            }

            return _reserveLeft[ToIndex(side)];
        }

        /// <summary>
        /// Начинает отсчёт хода стороны: запоминает метку времени начала хода.
        /// </summary>
        /// <exception cref="InvalidOperationException">Предыдущий ход не завершён через CompleteTurn.</exception>
        public void StartTurn(DraftSide side)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException($"Ход стороны {ActiveSide} ещё идёт: сначала вызовите CompleteTurn.");
            }

            ActiveSide = side;
            _reserveAtTurnStart = _reserveLeft[ToIndex(side)];
            _turnStartedAt = ReadTime();
            IsRunning = true;
        }

        /// <summary>
        /// Возвращает состояние текущего хода на данный момент. Ничего не меняет — можно вызывать каждый кадр.
        /// </summary>
        /// <exception cref="InvalidOperationException">Ход не запущен.</exception>
        public DraftClockSnapshot Sample()
        {
            EnsureRunning();
            return Evaluate(ReadTime());
        }

        /// <summary>
        /// Завершает текущий ход: фиксирует израсходованный запас стороны и останавливает отсчёт.
        /// </summary>
        /// <returns>Итоговое состояние хода в момент завершения.</returns>
        /// <exception cref="InvalidOperationException">Ход не запущен.</exception>
        public DraftClockSnapshot CompleteTurn()
        {
            EnsureRunning();

            DraftClockSnapshot snapshot = Evaluate(ReadTime());
            _reserveLeft[ToIndex(ActiveSide)] = snapshot.ReserveSecondsLeft;
            IsRunning = false;

            return snapshot;
        }

        private DraftClockSnapshot Evaluate(double now)
        {
            double elapsed = now - _turnStartedAt;
            if (elapsed < 0.0)
            {
                elapsed = 0.0;
            }

            double turnLeft = TurnDuration - elapsed;
            if (turnLeft < 0.0)
            {
                turnLeft = 0.0;
            }

            double overflow = elapsed - TurnDuration;
            if (overflow < 0.0)
            {
                overflow = 0.0;
            }

            double reserveLeft = _reserveAtTurnStart - overflow;
            if (reserveLeft < 0.0)
            {
                reserveLeft = 0.0;
            }

            bool isUsingReserve = turnLeft <= 0.0;
            bool isExpired = isUsingReserve && reserveLeft <= 0.0;

            float turnNormalized = (float)(turnLeft / TurnDuration);
            float reserveNormalized = ReserveDuration > 0.0 ? (float)(reserveLeft / ReserveDuration) : 0f;

            return new DraftClockSnapshot(
                ActiveSide,
                turnLeft,
                reserveLeft,
                Clamp01(turnNormalized),
                Clamp01(reserveNormalized),
                isUsingReserve,
                isExpired);
        }

        private double ReadTime()
        {
            double now = _timeSource();

            if (double.IsNaN(now) || double.IsInfinity(now))
            {
                throw new InvalidOperationException("Источник времени вернул некорректное значение.");
            }

            return now;
        }

        private void EnsureRunning()
        {
            if (!IsRunning)
            {
                throw new InvalidOperationException("Ход не запущен: вызовите StartTurn.");
            }
        }

        private static int ToIndex(DraftSide side)
        {
            int index = (int)side;

            if (index < 0 || index >= SideCount)
            {
                throw new ArgumentOutOfRangeException(nameof(side), side, "Неизвестная сторона драфта.");
            }

            return index;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
