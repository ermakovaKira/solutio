using System;
using Solutio.Settings;

namespace Solutio.Session
{
    /// <summary>
    /// Пара значений для применения к Unity: QualitySettings.vSyncCount и Application.targetFrameRate.
    /// </summary>
    public readonly struct FrameRateSettings : IEquatable<FrameRateSettings>
    {
        /// <summary>Значение Application.targetFrameRate, при котором Unity не ограничивает частоту кадров.</summary>
        public const int UnlimitedTargetFrameRate = -1;

        /// <summary>Лимит из настроек пользователя (0 — Uncapped).</summary>
        public readonly int FpsLimit;

        /// <summary>Значение для QualitySettings.vSyncCount.</summary>
        public readonly int VSyncCount;

        /// <summary>Значение для Application.targetFrameRate.</summary>
        public readonly int TargetFrameRate;

        public FrameRateSettings(int fpsLimit, int vSyncCount, int targetFrameRate)
        {
            FpsLimit = fpsLimit;
            VSyncCount = vSyncCount;
            TargetFrameRate = targetFrameRate;
        }

        /// <summary>true, если частота кадров не ограничена.</summary>
        public bool IsUncapped => TargetFrameRate == UnlimitedTargetFrameRate;

        public bool Equals(FrameRateSettings other)
        {
            return FpsLimit == other.FpsLimit
                && VSyncCount == other.VSyncCount
                && TargetFrameRate == other.TargetFrameRate;
        }

        public override bool Equals(object obj)
        {
            return obj is FrameRateSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = FpsLimit;
                hash = (hash * 397) ^ VSyncCount;
                hash = (hash * 397) ^ TargetFrameRate;
                return hash;
            }
        }

        public static bool operator ==(FrameRateSettings left, FrameRateSettings right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(FrameRateSettings left, FrameRateSettings right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"vSyncCount = {VSyncCount}, targetFrameRate = {TargetFrameRate} ({MatchCatalog.GetFpsDisplayName(FpsLimit)})";
        }
    }

    /// <summary>
    /// Политика ограничения частоты кадров. Переводит лимит из настроек пользователя в команды Unity.
    /// На настольных платформах Application.targetFrameRate игнорируется при включённом VSync,
    /// поэтому VSync всегда отключается, а ограничение задаётся только через targetFrameRate.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public static class FrameRatePolicy
    {
        /// <summary>VSync выключен: синхронизация с монитором не ограничивает выбранный лимит.</summary>
        public const int DisabledVSyncCount = 0;

        /// <summary>
        /// Рассчитывает параметры частоты кадров. Недопустимый лимит заменяется значением по умолчанию.
        /// </summary>
        public static FrameRateSettings Resolve(int fpsLimit)
        {
            int safeLimit = MatchCatalog.IsValidFps(fpsLimit) ? fpsLimit : MatchCatalog.DefaultFpsLimit;

            int targetFrameRate = safeLimit == MatchCatalog.UncappedFps
                ? FrameRateSettings.UnlimitedTargetFrameRate
                : safeLimit;

            return new FrameRateSettings(safeLimit, DisabledVSyncCount, targetFrameRate);
        }
    }
}
