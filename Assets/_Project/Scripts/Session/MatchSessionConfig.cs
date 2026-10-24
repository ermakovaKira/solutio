using System;
using System.Globalization;
using Solutio.Settings;

namespace Solutio.Session
{
    /// <summary>
    /// Неизменяемый «паспорт» матча, формируемый в момент нажатия «НАЧАТЬ БИТВУ».
    /// Содержит уже разрешённую конкретную карту (без ключа «Random»), стратегический профиль ИИ,
    /// параметры частоты кадров и зерно генератора случайных чисел матча.
    /// Зерно позволяет воспроизвести драфт ИИ (День 12) и симуляцию боя (День 17) один в один.
    /// Чистый C#-класс без зависимостей от Unity — покрывается NUnit-тестами (День 24).
    /// </summary>
    public sealed class MatchSessionConfig
    {
        /// <summary>Идентификатор пользователя (users.id_user).</summary>
        public int UserId { get; }

        /// <summary>Логин пользователя для отображения в интерфейсе матча.</summary>
        public string UserLogin { get; }

        /// <summary>Выбранная сложность ИИ.</summary>
        public AiDifficulty Difficulty { get; }

        /// <summary>Стратегические коэффициенты ИИ, соответствующие сложности.</summary>
        public AiStrategyProfile AiProfile { get; }

        /// <summary>Ключ карты из настроек пользователя (может быть «Random»).</summary>
        public string RequestedMapKey { get; }

        /// <summary>Конкретная карта, на которой пройдёт матч.</summary>
        public MapDefinition Map { get; }

        /// <summary>true, если карта выбрана алгоритмом случайного подбора.</summary>
        public bool WasMapRandomized { get; }

        /// <summary>Параметры частоты кадров на время матча.</summary>
        public FrameRateSettings FrameRate { get; }

        /// <summary>Зерно генератора случайных чисел матча.</summary>
        public int MatchSeed { get; }

        /// <summary>Момент формирования конфигурации (UTC).</summary>
        public DateTime CreatedAtUtc { get; }

        public MatchSessionConfig(
            int userId,
            string userLogin,
            AiDifficulty difficulty,
            AiStrategyProfile aiProfile,
            string requestedMapKey,
            MapDefinition map,
            bool wasMapRandomized,
            FrameRateSettings frameRate,
            int matchSeed,
            DateTime createdAtUtc)
        {
            if (userId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(userId), userId, "Идентификатор пользователя должен быть положительным.");
            }

            if (!Enum.IsDefined(typeof(AiDifficulty), difficulty))
            {
                throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Неизвестный уровень сложности ИИ.");
            }

            if (aiProfile.Difficulty != difficulty)
            {
                throw new ArgumentException("Стратегический профиль ИИ не соответствует выбранной сложности.", nameof(aiProfile));
            }

            if (MatchCatalog.IndexOfMap(map.Key) < 0)
            {
                throw new ArgumentException("Карта матча должна быть конкретной картой из справочника, а не «Random».", nameof(map));
            }

            if (createdAtUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Время формирования конфигурации должно быть задано в UTC.", nameof(createdAtUtc));
            }

            UserId = userId;
            UserLogin = userLogin ?? string.Empty;
            Difficulty = difficulty;
            AiProfile = aiProfile;
            RequestedMapKey = string.IsNullOrEmpty(requestedMapKey) ? MatchCatalog.RandomMapKey : requestedMapKey;
            Map = map;
            WasMapRandomized = wasMapRandomized;
            FrameRate = frameRate;
            MatchSeed = matchSeed;
            CreatedAtUtc = createdAtUtc;
        }

        /// <summary>
        /// Создаёт независимый генератор случайных чисел матча на основе зерна.
        /// Каждый модуль (драфт, симуляция) получает свою копию с одинаковой стартовой последовательностью.
        /// </summary>
        public Random CreateMatchRandom()
        {
            return new Random(MatchSeed);
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Пользователь {0} (ID {1}) | ИИ: {2} | Карта: {3}{4} | {5} | Seed: {6} | {7:yyyy-MM-dd HH:mm:ss} UTC",
                UserLogin,
                UserId,
                MatchCatalog.GetDifficultyDisplayName(Difficulty),
                Map.DisplayName,
                WasMapRandomized ? " (случайно)" : string.Empty,
                MatchCatalog.GetFpsDisplayName(FrameRate.FpsLimit),
                MatchSeed,
                CreatedAtUtc);
        }
    }
}
