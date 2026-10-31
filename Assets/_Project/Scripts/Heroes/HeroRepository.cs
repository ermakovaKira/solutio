using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;
using Solutio.Database;
using UnityEngine;

namespace Solutio.Heroes
{
    /// <summary>
    /// Репозиторий героев: чтение таблицы heroes через DatabaseManager.
    /// Запросы с входными данными выполняются только с типизированными SqliteParameter.
    /// </summary>
    public sealed class HeroRepository
    {
        private const string SelectAllQuery =
            "SELECT id_hero, hero_name, hero_role, base_attack, base_armor, base_hp " +
            "FROM heroes ORDER BY id_hero;";

        private const string SelectByIdQuery =
            "SELECT id_hero, hero_name, hero_role, base_attack, base_armor, base_hp " +
            "FROM heroes WHERE id_hero = @id_hero LIMIT 1;";

        private const string IdHeroParameter = "@id_hero";

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
