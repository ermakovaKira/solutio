using System;
using System.Data;
using Mono.Data.Sqlite;
using Solutio.Database;
using UnityEngine;

namespace Solutio.Settings
{
    /// <summary>
    /// Модель настроек матча, привязанная к учётной записи пользователя (таблица user_settings).
    /// </summary>
    public sealed class UserSettings
    {
        public int UserId { get; }
        public AiDifficulty Difficulty { get; set; }
        public string MapKey { get; set; }
        public int FpsLimit { get; set; }

        public UserSettings(int userId, AiDifficulty difficulty, string mapKey, int fpsLimit)
        {
            UserId = userId;
            Difficulty = difficulty;
            MapKey = mapKey;
            FpsLimit = fpsLimit;
        }

        public static UserSettings CreateDefault(int userId)
        {
            return new UserSettings(
                userId,
                MatchCatalog.DefaultDifficulty,
                MatchCatalog.DefaultMapKey,
                MatchCatalog.DefaultFpsLimit);
        }

        /// <summary>
        /// Приводит значения к допустимым по справочнику, чтобы не нарушить CHECK-ограничения БД.
        /// </summary>
        public void Sanitize()
        {
            if (!Enum.IsDefined(typeof(AiDifficulty), Difficulty))
            {
                Difficulty = MatchCatalog.DefaultDifficulty;
            }

            if (!MatchCatalog.IsValidMapKey(MapKey))
            {
                MapKey = MatchCatalog.DefaultMapKey;
            }

            if (!MatchCatalog.IsValidFps(FpsLimit))
            {
                FpsLimit = MatchCatalog.DefaultFpsLimit;
            }
        }
    }

    /// <summary>
    /// Репозиторий чтения и записи настроек пользователя в SQLite.
    /// Все запросы строго параметризированы типизированными SqliteParameter.
    /// </summary>
    public sealed class UserSettingsRepository
    {
        private const string SelectQuery =
            "SELECT ai_difficulty, preferred_map, fps_limit FROM user_settings WHERE id_user = @id_user;";

        private const string UpsertQuery =
            @"INSERT OR REPLACE INTO user_settings (id_user, ai_difficulty, preferred_map, fps_limit, updated_at)
              VALUES (@id_user, @ai_difficulty, @preferred_map, @fps_limit, CURRENT_TIMESTAMP);";

        private readonly DatabaseManager _database;

        public UserSettingsRepository(DatabaseManager database)
        {
            _database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Загружает настройки пользователя. Если строки ещё нет (например, у только что
        /// зарегистрированного пользователя), создаёт её со значениями по умолчанию.
        /// </summary>
        public UserSettings LoadOrCreate(int userId)
        {
            if (userId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(userId), "Идентификатор пользователя должен быть положительным.");
            }

            DataTable table = _database.ExecuteQuery(SelectQuery, CreateIntParameter("@id_user", userId));

            if (table == null || table.Rows.Count == 0)
            {
                UserSettings defaults = UserSettings.CreateDefault(userId);

                if (!Save(defaults))
                {
                    Debug.LogWarning($"[UserSettingsRepository] Не удалось создать настройки по умолчанию для пользователя ID {userId}.");
                }

                return defaults;
            }

            DataRow row = table.Rows[0];

            MatchCatalog.TryParseDifficulty(Convert.ToString(row["ai_difficulty"]), out AiDifficulty difficulty);
            string mapKey = Convert.ToString(row["preferred_map"]);
            int fpsLimit = row["fps_limit"] == DBNull.Value
                ? MatchCatalog.DefaultFpsLimit
                : Convert.ToInt32(row["fps_limit"]);

            UserSettings settings = new UserSettings(userId, difficulty, mapKey, fpsLimit);
            settings.Sanitize();
            return settings;
        }

        /// <summary>
        /// Сохраняет настройки пользователя (вставка или замена строки по id_user).
        /// </summary>
        public bool Save(UserSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            settings.Sanitize();

            int affectedRows = _database.ExecuteNonQuery(
                UpsertQuery,
                CreateIntParameter("@id_user", settings.UserId),
                CreateTextParameter("@ai_difficulty", settings.Difficulty.ToString()),
                CreateTextParameter("@preferred_map", settings.MapKey),
                CreateIntParameter("@fps_limit", settings.FpsLimit));

            return affectedRows > 0;
        }

        private static SqliteParameter CreateIntParameter(string name, int value)
        {
            return new SqliteParameter(name, DbType.Int32) { Value = value };
        }

        private static SqliteParameter CreateTextParameter(string name, string value)
        {
            return new SqliteParameter(name, DbType.String) { Value = value };
        }
    }
}
