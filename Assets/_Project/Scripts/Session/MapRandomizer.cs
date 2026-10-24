using System;
using System.Collections.Generic;
using Solutio.Settings;

namespace Solutio.Session
{
    /// <summary>
    /// Алгоритм случайного подбора карт на основе «мешка» (Shuffle Bag) с перемешиванием Фишера–Йетса.
    /// Гарантии:
    ///   1) внутри одного мешка карты не повторяются — за N случайных матчей пользователь увидит все N карт;
    ///   2) на стыке мешков и после ручного выбора карта не повторяет последнюю сыгранную;
    ///   3) при фиксированном seed последовательность детерминирована (NUnit, День 24).
    /// Чистый C#-класс без зависимостей от Unity; выбор карты выполняется без аллокаций.
    /// </summary>
    public sealed class MapRandomizer
    {
        private const int NoMapIndex = -1;

        private readonly Random _random;
        private readonly IReadOnlyList<MapDefinition> _maps;
        private readonly int[] _bag;

        private int _cursor;
        private int _lastPlayedIndex = NoMapIndex;

        /// <summary>Создаёт рандомайзер с непредсказуемым зерном.</summary>
        public MapRandomizer()
            : this(CreateEntropySeed())
        {
        }

        /// <summary>Создаёт детерминированный рандомайзер с заданным зерном.</summary>
        public MapRandomizer(int seed)
            : this(new Random(seed), MatchCatalog.Maps)
        {
        }

        /// <summary>
        /// Создаёт рандомайзер с внешним генератором и произвольным набором карт (для тестов).
        /// </summary>
        public MapRandomizer(Random random, IReadOnlyList<MapDefinition> maps)
        {
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _maps = maps ?? throw new ArgumentNullException(nameof(maps));

            if (_maps.Count == 0)
            {
                throw new ArgumentException("Набор карт не может быть пустым.", nameof(maps));
            }

            _bag = new int[_maps.Count];
            RefillBag();
        }

        /// <summary>Количество карт, участвующих в розыгрыше.</summary>
        public int MapCount => _maps.Count;

        /// <summary>Количество карт, оставшихся в текущем мешке до перемешивания.</summary>
        public int RemainingInBag => _bag.Length - _cursor;

        /// <summary>Ключ последней сыгранной карты или null, если матчей ещё не было.</summary>
        public string LastPlayedMapKey => _lastPlayedIndex == NoMapIndex ? null : _maps[_lastPlayedIndex].Key;

        /// <summary>
        /// Разрешает ключ из настроек пользователя в конкретную карту.
        /// «Random» разыгрывается через мешок, конкретный ключ возвращается как есть.
        /// Неизвестный ключ считается запросом случайной карты.
        /// </summary>
        /// <param name="requestedMapKey">Значение UserSettings.MapKey.</param>
        /// <param name="wasRandomized">true, если карта была выбрана алгоритмом случайного подбора.</param>
        public MapDefinition Resolve(string requestedMapKey, out bool wasRandomized)
        {
            int explicitIndex = IndexOf(requestedMapKey);

            if (explicitIndex >= 0)
            {
                wasRandomized = false;
                _lastPlayedIndex = explicitIndex;
                return _maps[explicitIndex];
            }

            wasRandomized = true;
            int drawnIndex = DrawIndex();
            _lastPlayedIndex = drawnIndex;
            return _maps[drawnIndex];
        }

        /// <summary>
        /// Достаёт следующую случайную карту из мешка.
        /// </summary>
        public MapDefinition Draw()
        {
            int drawnIndex = DrawIndex();
            _lastPlayedIndex = drawnIndex;
            return _maps[drawnIndex];
        }

        /// <summary>
        /// Сбрасывает историю и перемешивает мешок заново (например, при смене пользователя).
        /// </summary>
        public void Reset()
        {
            _lastPlayedIndex = NoMapIndex;
            RefillBag();
        }

        private int DrawIndex()
        {
            int remaining = _bag.Length - _cursor;

            // В мешке осталась ровно та карта, что только что игралась (возможно после ручного выбора) —
            // перемешиваем полный мешок заново, чтобы не допустить повтора.
            if (remaining == 0 || (remaining == 1 && _bag[_cursor] == _lastPlayedIndex && _bag.Length > 1))
            {
                RefillBag();
                remaining = _bag.Length;
            }

            // Следующая карта совпадает с последней сыгранной — меняем её местами
            // со случайной картой из оставшейся части мешка.
            if (remaining > 1 && _bag[_cursor] == _lastPlayedIndex)
            {
                int swapIndex = _random.Next(_cursor + 1, _bag.Length);
                Swap(_cursor, swapIndex);
            }

            int result = _bag[_cursor];
            _cursor++;
            return result;
        }

        private void RefillBag()
        {
            for (int i = 0; i < _bag.Length; i++)
            {
                _bag[i] = i;
            }

            // Перемешивание Фишера–Йетса: каждая перестановка равновероятна, сложность O(n)
            for (int i = _bag.Length - 1; i > 0; i--)
            {
                int j = _random.Next(0, i + 1);
                Swap(i, j);
            }

            _cursor = 0;
        }

        private void Swap(int a, int b)
        {
            if (a == b)
            {
                return;
            }

            int temp = _bag[a];
            _bag[a] = _bag[b];
            _bag[b] = temp;
        }

        private int IndexOf(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey) || string.Equals(mapKey, MatchCatalog.RandomMapKey, StringComparison.Ordinal))
            {
                return NoMapIndex;
            }

            for (int i = 0; i < _maps.Count; i++)
            {
                if (string.Equals(_maps[i].Key, mapKey, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return NoMapIndex;
        }

        private static int CreateEntropySeed()
        {
            unchecked
            {
                return Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            }
        }
    }
}
