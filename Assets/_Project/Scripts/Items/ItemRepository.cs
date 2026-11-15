using System;
using System.Collections.Generic;
using System.Data;
using Mono.Data.Sqlite;
using Solutio.Database;
using UnityEngine;

namespace Solutio.Items
{
    /// <summary>
    /// Источник предметов по типу. Интерфейс отделяет экран экипировки и магазин от SQLite:
    /// в NUnit-тестах (День 24) подставляется фальшивый источник.
    /// </summary>
    public interface IItemSource
    {
        /// <summary>
        /// Заполняет буфер предметами указанного типа в порядке возрастания ID.
        /// </summary>
        /// <param name="type">Тип предмета (Free — стартовая экипировка, Shop — магазин).</param>
        /// <param name="buffer">Буфер результата (очищается).</param>
        /// <returns>true, если чтение выполнено и вернуло хотя бы один предмет.</returns>
        bool TryLoadByType(ItemType type, List<ItemRecord> buffer);
    }

    /// <summary>
    /// Репозиторий предметов: чтение таблицы items через DatabaseManager.
    /// Запросы с входными данными выполняются только с типизированными SqliteParameter.
    /// </summary>
    public sealed class ItemRepository : IItemSource
    {
        private const string SelectColumns =
            "SELECT id_item, item_name, item_type, rarity, bonus_attack, bonus_armor, bonus_hp, cost FROM items ";

        private const string SelectAllQuery =
            SelectColumns + "ORDER BY id_item;";

        private const string SelectByTypeQuery =
            SelectColumns + "WHERE item_type = @item_type ORDER BY id_item;";

        private const string SelectByIdQuery =
            SelectColumns + "WHERE id_item = @id_item LIMIT 1;";

        private const string ItemTypeParameter = "@item_type";
        private const string IdItemParameter = "@id_item";

        private readonly DatabaseManager _database;

        public ItemRepository(DatabaseManager database)
        {
            _database = database != null ? database : throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Загружает все предметы в порядке возрастания идентификатора.
        /// Записи с некорректными данными пропускаются с предупреждением в консоли.
        /// </summary>
        public IReadOnlyList<ItemRecord> LoadAll()
        {
            DataTable table = _database.ExecuteQuery(SelectAllQuery);
            List<ItemRecord> items = new List<ItemRecord>(table.Rows.Count);
            MapRows(table, items);
            return items;
        }

        /// <summary>
        /// Загружает предметы указанного типа (параметризированный запрос, DbType.String).
        /// DatabaseManager.ExecuteQuery при ошибке возвращает пустую таблицу,
        /// поэтому пустой результат трактуется как сбой источника.
        /// </summary>
        public bool TryLoadByType(ItemType type, List<ItemRecord> buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();

            SqliteParameter typeParameter = new SqliteParameter(ItemTypeParameter, DbType.String)
            {
                Value = ItemCatalogInfo.GetDbValue(type)
            };

            DataTable table = _database.ExecuteQuery(SelectByTypeQuery, typeParameter);
            MapRows(table, buffer);

            for (int i = buffer.Count - 1; i >= 0; i--)
            {
                if (buffer[i].Type != type)
                {
                    Debug.LogWarning($"[ItemRepository] Предмет {buffer[i]} пропущен: тип не совпадает с запрошенным.");
                    buffer.RemoveAt(i);
                }
            }

            return buffer.Count > 0;
        }

        /// <summary>
        /// Загружает один предмет по идентификатору (параметризированный запрос, DbType.Int32).
        /// </summary>
        public bool TryGetById(int itemId, out ItemRecord item)
        {
            item = null;

            if (itemId <= 0)
            {
                return false;
            }

            SqliteParameter idParameter = new SqliteParameter(IdItemParameter, DbType.Int32)
            {
                Value = itemId
            };

            DataTable table = _database.ExecuteQuery(SelectByIdQuery, idParameter);

            if (table.Rows.Count == 0)
            {
                return false;
            }

            return TryMap(table.Rows[0], out item);
        }

        private static void MapRows(DataTable table, List<ItemRecord> buffer)
        {
            foreach (DataRow row in table.Rows)
            {
                if (TryMap(row, out ItemRecord item))
                {
                    buffer.Add(item);
                }
            }
        }

        private static bool TryMap(DataRow row, out ItemRecord item)
        {
            item = null;

            try
            {
                int id = Convert.ToInt32(row["id_item"]);
                string name = Convert.ToString(row["item_name"]);
                string typeValue = Convert.ToString(row["item_type"]);
                string rarityValue = Convert.ToString(row["rarity"]);

                if (!ItemCatalogInfo.TryParseType(typeValue, out ItemType type))
                {
                    Debug.LogWarning($"[ItemRepository] Предмет ID {id} пропущен: неизвестный тип «{typeValue}».");
                    return false;
                }

                if (!ItemCatalogInfo.TryParseRarity(rarityValue, out ItemRarity rarity))
                {
                    Debug.LogWarning($"[ItemRepository] Предмет ID {id} пропущен: неизвестная редкость «{rarityValue}».");
                    return false;
                }

                int attack = Convert.ToInt32(row["bonus_attack"]);
                int armor = Convert.ToInt32(row["bonus_armor"]);
                int hp = Convert.ToInt32(row["bonus_hp"]);
                int cost = Convert.ToInt32(row["cost"]);

                item = new ItemRecord(id, name, type, rarity, attack, armor, hp, cost);
                return true;
            }
            catch (Exception exception) when (exception is InvalidCastException
                                              || exception is FormatException
                                              || exception is OverflowException
                                              || exception is ArgumentException)
            {
                Debug.LogWarning($"[ItemRepository] Запись предмета пропущена из-за некорректных данных: {exception.Message}");
                return false;
            }
        }
    }
}
