using System;
using System.Collections.Generic;
using UnityEngine;

namespace Solutio.Battle
{
    /// <summary>
    /// Чистая математика поля боя (без MonoBehaviour, кандидат на NUnit, День 24).
    /// Позиции в профиле карты хранятся в нормализованных координатах картинки:
    /// (0; 0) — левый нижний угол, (1; 1) — правый верхний. Мир центрирован в нуле.
    /// </summary>
    public static class BattleMapMath
    {
        /// <summary>Базовый порядок сортировки построек и юнитов (выше фона и декора).</summary>
        public const int DepthSortBase = 10000;

        /// <summary>Сколько единиц sortingOrder приходится на одну мировую единицу по Y.</summary>
        public const float DepthSortScale = 100f;

        /// <summary>Переводит нормализованную точку картинки в мировые координаты.</summary>
        public static Vector2 ToWorld(Vector2 normalized, Vector2 worldSize)
        {
            return new Vector2((normalized.x - 0.5f) * worldSize.x, (normalized.y - 0.5f) * worldSize.y);
        }

        /// <summary>Переводит мировую точку обратно в нормализованные координаты картинки.</summary>
        public static Vector2 ToNormalized(Vector2 world, Vector2 worldSize)
        {
            if (worldSize.x <= 0f || worldSize.y <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(worldSize), "Размер мира должен быть положительным.");
            }

            return new Vector2(world.x / worldSize.x + 0.5f, world.y / worldSize.y + 0.5f);
        }

        /// <summary>
        /// Порядок отрисовки по глубине: кто ниже на экране, тот ближе к зрителю и рисуется поверх.
        /// </summary>
        public static int GetDepthSortingOrder(float worldY, int offset = 0)
        {
            float raw = DepthSortBase - worldY * DepthSortScale + offset;
            if (raw > short.MaxValue)
            {
                return short.MaxValue;
            }

            if (raw < short.MinValue)
            {
                return short.MinValue;
            }

            return Mathf.RoundToInt(raw);
        }

        /// <summary>Длина ломаной в нормализованных координатах, пересчитанная в мировые единицы.</summary>
        public static float GetPathLength(IReadOnlyList<Vector2> normalizedPoints, Vector2 worldSize)
        {
            if (normalizedPoints == null || normalizedPoints.Count < 2)
            {
                return 0f;
            }

            float length = 0f;
            Vector2 previous = ToWorld(normalizedPoints[0], worldSize);

            for (int i = 1; i < normalizedPoints.Count; i++)
            {
                Vector2 current = ToWorld(normalizedPoints[i], worldSize);
                length += Vector2.Distance(previous, current);
                previous = current;
            }

            return length;
        }

        /// <summary>
        /// Ортографический размер камеры, при котором вся ширина (или высота) карты помещается в кадр.
        /// </summary>
        public static float GetFitOrthographicSize(Vector2 worldSize, float aspect)
        {
            if (aspect <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(aspect), "Соотношение сторон должно быть положительным.");
            }

            float byWidth = worldSize.x * 0.5f / aspect;
            float byHeight = worldSize.y * 0.5f;
            return Mathf.Min(byWidth, byHeight);
        }

        /// <summary>
        /// Ограничивает центр камеры так, чтобы кадр не выходил за границы карты.
        /// Если кадр шире карты по одной из осей, центр по этой оси фиксируется на середине.
        /// </summary>
        public static Vector2 ClampCameraCenter(Vector2 center, float orthographicSize, float aspect, Rect bounds)
        {
            float halfHeight = orthographicSize;
            float halfWidth = orthographicSize * aspect;

            float minX = bounds.xMin + halfWidth;
            float maxX = bounds.xMax - halfWidth;
            float minY = bounds.yMin + halfHeight;
            float maxY = bounds.yMax - halfHeight;

            float x = minX > maxX ? bounds.center.x : Mathf.Clamp(center.x, minX, maxX);
            float y = minY > maxY ? bounds.center.y : Mathf.Clamp(center.y, minY, maxY);
            return new Vector2(x, y);
        }
    }
}
