using System;
using System.IO;
using System.Data;
using Mono.Data.Sqlite;
using UnityEngine;

namespace Solutio.Database
{
    /// <summary>
    /// Центральный модуль управления базой данных SQLite.
    /// Обеспечивает прямое ADO.NET подключение, строго параметризированные запросы
    /// (защита от SQL-инъекций) и сохранение состояния между сценами.
    /// </summary>
    public class DatabaseManager : MonoBehaviour
    {
        public static DatabaseManager Instance { get; private set; }

        private const string DatabaseFileName = "solutio_game.db";
        private string _connectionString;

        private void Awake()
        {
            // Паттерн Singleton для единой точки доступа во всем проекте
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeDatabasePath();
        }

        private void Start()
        {
            // Автоматический тест соединения при старте игры
            TestDatabaseConnection();
        }

        /// <summary>
        /// Формирует URI-строку подключения ADO.NET к файлу в StreamingAssets.
        /// </summary>
        private void InitializeDatabasePath()
        {
            string dbPath = Path.Combine(Application.streamingAssetsPath, DatabaseFileName);

            // Проверка на случай запуска из корня в режиме редактора
            if (!File.Exists(dbPath))
            {
                string rootDbPath = Path.Combine(Directory.GetCurrentDirectory(), DatabaseFileName);
                if (File.Exists(rootDbPath))
                {
                    dbPath = rootDbPath;
                }
                else
                {
                    Debug.LogWarning($"[DatabaseManager] Файл базы данных не найден по пути: {dbPath}. Будет создан новый при первом подключении.");
                }
            }

            _connectionString = $"URI=file:{dbPath}";
            Debug.Log($"[DatabaseManager] Строка подключения сформирована: {_connectionString}");
        }

        /// <summary>
        /// Создает и открывает новое ADO.NET соединение с активированной поддержкой внешних ключей.
        /// </summary>
        public SqliteConnection GetOpenConnection()
        {
            SqliteConnection connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Включаем валидацию Foreign Keys для поддержания целостности данных
            using (SqliteCommand pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            return connection;
        }

        /// <summary>
        /// Выполняет параметризированный запрос без возврата строк (INSERT, UPDATE, DELETE).
        /// </summary>
        public int ExecuteNonQuery(string query, params SqliteParameter[] parameters)
        {
            try
            {
                using (SqliteConnection connection = GetOpenConnection())
                using (SqliteCommand command = new SqliteCommand(query, connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    return command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] Ошибка ExecuteNonQuery: {ex.Message}\nЗапрос: {query}");
                return -1;
            }
        }

        /// <summary>
        /// Выполняет параметризированный запрос и возвращает единичное скалярное значение (COUNT, MAX, ID).
        /// </summary>
        public object ExecuteScalar(string query, params SqliteParameter[] parameters)
        {
            try
            {
                using (SqliteConnection connection = GetOpenConnection())
                using (SqliteCommand command = new SqliteCommand(query, connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    return command.ExecuteScalar();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatabaseManager] Ошибка ExecuteScalar: {ex.Message}\nЗапрос: {query}");
                return null;
            }
        }

        /// <summary>
        /// Выполняет запрос на чтение (SELECT) и возвращает заполненную таблицу DataTable.
        /// </summary>
        public DataTable ExecuteQuery(string query, params SqliteParameter[] parameters)
        {
            DataTable dataTable = new DataTable();

            try
            {
                using (SqliteConnection connection = GetOpenConnection())
                using (SqliteCommand command = new SqliteCommand(query, connection))
                {
                    if (parameters != null && parameters.Length > 0)
                    {
                        command.Parameters.AddRange(parameters);
                    }

                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        dataTable.Load(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Datab" +
                    $"aseManager] Ошибка ExecuteQuery: {ex.Message}\nЗапрос: {query}");
            }

            return dataTable;
        }

        /// <summary>
        /// Тестовая проверка работоспособности соединения и считывания пула героев.
        /// </summary>
        private void TestDatabaseConnection()
        {
            try
            {
                object countResult = ExecuteScalar("SELECT COUNT(*) FROM heroes;");
                if (countResult != null && Convert.ToInt32(countResult) > 0)
                {
                    Debug.Log($"<color=#00FF66><b>[DatabaseManager] УСПЕШНОЕ ПОДКЛЮЧЕНИЕ К SQLite!</b></color> В пуле базы данных найдено героев: <b>{countResult}</b>");
                }
                else
                {
                    Debug.LogWarning("[DatabaseManager] Подключение успешно, но таблица heroes пуста.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"<color=red><b>[DatabaseManager] Ошибка подключения к базе данных:</b></color> {ex.Message}");
            }
        }
    }
}