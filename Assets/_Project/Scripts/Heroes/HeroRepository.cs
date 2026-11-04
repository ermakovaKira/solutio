using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using Mono.Data.Sqlite;
using Solutio.Database;
using UnityEngine;

namespace Solutio.Heroes
{
    /// <summary>
    /// Источник идентификаторов свободных героев для автопика драфта.
    /// Интерфейс отделяет логику выбора от SQLite: в NUnit-тестах (День 24) подставляется фальшивый источник.
    /// </summary>
    public interface IAvailableHeroIdSource
    {
        /// <summary>
        /// Заполняет буфер идентификаторами героев, которых нет в списке исключений, в порядке возрастания ID.
        /// </summary>
        /// <param name="excludedIds">Занятые герои (забаненные и выбранные).</param>
        /// <param name="buffer">Буфер результата (очищается).</param>
        /// <returns>true, если чтение выполнено и вернуло хотя бы один ID.</returns>
        bool TryLoadAvailableIds(IReadOnlyList<int> excludedIds, List<int> buffer);
    }

    /// <summary>
    /// Репозиторий героев: чтение таблицы heroes через DatabaseManager.
    /// Запросы с входными данными выполняются только с типизированными SqliteParameter.
    /// </summary>
    public sealed class HeroRepository : IAvailableHeroIdSource
    {
        /// <summary>Максимальное число исключаемых ID в одном запросе автопика.</summary>
        public const int MaxExcludedIds = 64;

        private const string SelectAllQuery =
            "SELECT id_hero, hero_name, hero_role, base_attack, base_armor, base_hp " +
            "FROM heroes ORDER BY id_hero;";

        private const string SelectByIdQuery =
            "SELECT id_hero, hero_name, hero_role, base_attack, base_armor, base_hp " +
            "FROM heroes WHERE id_hero = @id_hero LIMIT 1;";

        private const string SelectAllIdsQuery =
            "SELECT id_hero FROM heroes ORDER BY id_hero;";

        private const string SelectAvailableIdsPrefix =
            "SELECT id_hero FROM heroes WHERE id_hero NOT IN (";

        private const string SelectAvailableIdsSuffix =
            ") ORDER BY id_hero;";

        private const string IdHeroParameter = "@id_hero";
        private const string ExcludedParameterPrefix = "@excluded_";

        /// <summary>Имена параметров @excluded_0 … @excluded_63 (создаются один раз).</summary>
        private static readonly string[] ExcludedParameterNames = BuildExcludedParameterNames();

        /// <summary>
        /// Кэш текстов запроса по количеству исключений. В текст попадают только имена параметров,
        /// значения ID передаются через SqliteParameter — конкатенации данных нет.
        /// </summary>
        private static readonly string[] AvailableIdsQueryCache = new string[MaxExcludedIds + 1];

        private readonly DatabaseManager _database;

        public HeroRepository(DatabaseManager database)
        {
            _database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Загружает весь пул героев в порядке возрастания идентификатора.
        /// Записи с некорректными данными пропускаются с предупреждением в консоли.
        /// </summary>
        public IReadOnlyList<HeroRecord> LoadAll()
        {
            DataTable table = _database.ExecuteQuery(SelectAllQuery);
            List<HeroRecord> heroes = new List<HeroRecord>(table.Rows.Count);

            foreach (DataRow row in table.Rows)
            {
                if (TryMap(row, out HeroRecord hero))
                {
                    heroes.Add(hero);
                }
            }

            return heroes;
        }

        /// <summary>
        /// Загружает одного героя по идентификатору (параметризированный запрос).
        /// </summary>
        public bool TryGetById(int heroId, out HeroRecord hero)
        {
            hero = null;

            if (heroId <= 0)
            {
                return false;
            }

            SqliteParameter idParameter = new SqliteParameter(IdHeroParameter, DbType.Int32)
            {
                Value = heroId
            };

            DataTable table = _database.ExecuteQuery(SelectByIdQuery, idParameter);

            if (table.Rows.Count == 0)
            {
                return false;
            }

            return TryMap(table.Rows[0], out hero);
        }

        /// <summary>
        /// Выбирает из таблицы heroes идентификаторы свободных героев (День 12, автопик по таймауту).
        /// Каждый занятый ID передаётся отдельным параметром @excluded_N с DbType.Int32.
        /// Порядок результата фиксирован (ORDER BY id_hero), поэтому выбор через Random матча
        /// воспроизводим по seed; ORDER BY RANDOM() не используется намеренно.
        /// </summary>
        public bool TryLoadAvailableIds(IReadOnlyList<int> excludedIds, List<int> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();

            int excludedCount = excludedIds != null ? excludedIds.Count : 0;

            if (excludedCount > MaxExcludedIds)
            {
                Debug.LogError($"[HeroRepository] Слишком много исключений для автопика: {excludedCount} (максимум {MaxExcludedIds}).");
                return false;
            }

            SqliteParameter[] parameters = new SqliteParameter[excludedCount];

            for (int i = 0; i < excludedCount; i++)
            {
                parameters[i] = new SqliteParameter(ExcludedParameterNames[i], DbType.Int32)
                {
                    Value = excludedIds[i]
                };
            }

            DataTable table = _database.ExecuteQuery(GetAvailableIdsQuery(excludedCount), parameters);

            foreach (DataRow row in table.Rows)
            {
                try
                {
                    buffer.Add(Convert.ToInt32(row["id_hero"]));
                }
                catch (Exception exception) when (exception is InvalidCastException
                                                  || exception is FormatException
                                                  || exception is OverflowException
                                                  || exception is ArgumentException)
                {
                    Debug.LogWarning($"[HeroRepository] Некорректный id_hero пропущен при автопике: {exception.Message}");
                }
            }

            return buffer.Count > 0;
        }

        private static string GetAvailableIdsQuery(int excludedCount)
        {
            if (excludedCount == 0)
            {
                return SelectAllIdsQuery;
            }

            string cached = AvailableIdsQueryCache[excludedCount];
            if (cached != null)
            {
                return cached;
            }

            StringBuilder builder = new StringBuilder(
                SelectAvailableIdsPrefix.Length + SelectAvailableIdsSuffix.Length + excludedCount * 16);

            builder.Append(SelectAvailableIdsPrefix);

            for (int i = 0; i < excludedCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(ExcludedParameterNames[i]);
            }

            builder.Append(SelectAvailableIdsSuffix);

            cached = builder.ToString();
            AvailableIdsQueryCache[excludedCount] = cached;
            return cached;
        }

        private static string[] BuildExcludedParameterNames()
        {
            string[] names = new string[MaxExcludedIds];

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = ExcludedParameterPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        private static bool TryMap(DataRow row, out HeroRecord hero)
        {
            hero = null;

            try
            {
                int id = Convert.ToInt32(row["id_hero"]);
                string name = Convert.ToString(row["hero_name"]);
                string roleValue = Convert.ToString(row["hero_role"]);

                if (!HeroRoleInfo.TryParse(roleValue, out HeroRole role))
                {
                    Debug.LogWarning($"[HeroRepository] Герой ID {id} пропущен: неизвестная роль «{roleValue}».");
                    return false;
                }

                int attack = Convert.ToInt32(row["base_attack"]);
                int armor = Convert.ToInt32(row["base_armor"]);
                int hp = Convert.ToInt32(row["base_hp"]);

                hero = new HeroRecord(id, name, role, attack, armor, hp);
                return true;
            }
            catch (Exception exception) when (exception is InvalidCastException
                                              || exception is FormatException
                                              || exception is OverflowException
                                              || exception is ArgumentException)
            {
                Debug.LogWarning($"[HeroRepository] Запись героя пропущена из-за некорректных данных: {exception.Message}");
                return false;
            }
        }
    }
}
