using System;
using System.Collections.Generic;

namespace Solutio.Settings
{
    /// <summary>
    /// Уровни сложности ИИ. Строковые имена совпадают с CHECK-ограничением
    /// колонки user_settings.ai_difficulty в базе данных SQLite.
    /// </summary>
    public enum AiDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Nightmare = 3
    }

    /// <summary>
    /// Неизменяемое описание игровой карты.
    /// </summary>
    public readonly struct MapDefinition
    {
        public readonly string Key;
        public readonly string DisplayName;
        public readonly string Description;

        public MapDefinition(string key, string displayName, string description)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
        }
    }

    /// <summary>
    /// Статический справочник параметров матча: сложности ИИ, 5 карт и лимиты FPS.
    /// Чистый C#-класс без зависимостей от Unity — легко покрывается NUnit-тестами (День 24).
    /// </summary>
    public static class MatchCatalog
    {
        public const string RandomMapKey = "Random";
        public const string RandomMapDisplayName = "Случайная карта";

        public const int UncappedFps = 0;

        public const AiDifficulty DefaultDifficulty = AiDifficulty.Normal;
        public const string DefaultMapKey = RandomMapKey;
        public const int DefaultFpsLimit = 144;

        private static readonly MapDefinition[] MapDefinitions =
        {
            new MapDefinition("CathedralRuins", "Руины Собора", "Обрушенные своды древнего собора, освещённые витражами."),
            new MapDefinition("CrimsonWastes", "Багровые Пустоши", "Выжженная равнина под кровавым небом."),
            new MapDefinition("FrozenCrypt", "Ледяной Склеп", "Подземные гробницы, скованные вечным инеем."),
            new MapDefinition("AbyssalMire", "Топи Бездны", "Ядовитые болота, над которыми клубится туман."),
            new MapDefinition("AshenCitadel", "Пепельная Цитадель", "Крепость павшего ордена в облаках пепла.")
        };

        private static readonly int[] FpsValues = { 60, 144, 240, 360, UncappedFps };

        private static readonly AiDifficulty[] DifficultyValues =
        {
            AiDifficulty.Easy,
            AiDifficulty.Normal,
            AiDifficulty.Hard,
            AiDifficulty.Nightmare
        };

        public static IReadOnlyList<MapDefinition> Maps => MapDefinitions;
        public static IReadOnlyList<int> FpsOptions => FpsValues;
        public static IReadOnlyList<AiDifficulty> Difficulties => DifficultyValues;

        public static string GetDifficultyDisplayName(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Easy: return "Послушник";
                case AiDifficulty.Normal: return "Страж";
                case AiDifficulty.Hard: return "Инквизитор";
                case AiDifficulty.Nightmare: return "Кошмар";
                default: return difficulty.ToString();
            }
        }

        public static bool TryParseDifficulty(string rawValue, out AiDifficulty difficulty)
        {
            if (!string.IsNullOrWhiteSpace(rawValue)
                && Enum.TryParse(rawValue.Trim(), true, out difficulty)
                && Enum.IsDefined(typeof(AiDifficulty), difficulty))
            {
                return true;
            }

            difficulty = DefaultDifficulty;
            return false;
        }

        public static bool IsValidFps(int fpsLimit)
        {
            return IndexOfFps(fpsLimit) >= 0;
        }

        public static int IndexOfFps(int fpsLimit)
        {
            for (int i = 0; i < FpsValues.Length; i++)
            {
                if (FpsValues[i] == fpsLimit)
                {
                    return i;
                }
            }

            return -1;
        }

        public static string GetFpsDisplayName(int fpsLimit)
        {
            return fpsLimit == UncappedFps ? "Без лимита" : $"{fpsLimit} FPS";
        }

        public static bool IsValidMapKey(string mapKey)
        {
            if (string.Equals(mapKey, RandomMapKey, StringComparison.Ordinal))
            {
                return true;
            }

            return IndexOfMap(mapKey) >= 0;
        }

        public static int IndexOfMap(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey))
            {
                return -1;
            }

            for (int i = 0; i < MapDefinitions.Length; i++)
            {
                if (string.Equals(MapDefinitions[i].Key, mapKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public static string GetMapDisplayName(string mapKey)
        {
            if (string.Equals(mapKey, RandomMapKey, StringComparison.Ordinal))
            {
                return RandomMapDisplayName;
            }

            int index = IndexOfMap(mapKey);
            return index >= 0 ? MapDefinitions[index].DisplayName : RandomMapDisplayName;
        }
    }
}
