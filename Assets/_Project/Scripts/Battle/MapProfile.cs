using System;
using UnityEngine;

namespace Solutio.Battle
{
    /// <summary>Маршрут линии: точки в нормализованных координатах картинки от базы пользователя к базе ИИ.</summary>
    [Serializable]
    public struct LanePath
    {
        public LaneId Lane;
        public Vector2[] Points;
    }

    /// <summary>Место башни: сторона, линия, ярус (1 — внешняя, 2 — внутренняя у базы) и позиция основания.</summary>
    [Serializable]
    public struct TowerSlot
    {
        public BattleSide Side;
        public LaneId Lane;
        [Range(1, 2)] public int Tier;
        public Vector2 Position;
    }

    /// <summary>Особая зона карты (мосты, алтарь, лёд, топь). Радиус — доля ширины картинки.</summary>
    [Serializable]
    public struct MapZone
    {
        public MapZoneType Type;
        public Vector2 Position;
        [Range(0.005f, 0.2f)] public float Radius;
    }

    /// <summary>Декоративный источник света карты (лава, свечи, руны). Радиус в мировых единицах.</summary>
    [Serializable]
    public struct AccentLight
    {
        public Vector2 Position;
        [ColorUsage(false, true)] public Color Color;
        [Range(0.2f, 6f)] public float Radius;
        [Range(0f, 6f)] public float Intensity;
        [Range(0f, 1f)] public float Flicker;
    }

    /// <summary>
    /// Профиль боевой карты (День 14): картинка поля, освещение, базы, башни, маршруты линий,
    /// особые зоны и параметры особенности карты. Один ассет на каждую из 5 карт MatchCatalog.
    /// Маршруты, зоны и особенность используются на Днях 16–18 (движение, TowerAI, симуляция, золото).
    /// </summary>
    [CreateAssetMenu(fileName = "MapProfile", menuName = "Solutio/Battle/Map Profile")]
    public sealed class MapProfile : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Ключ карты из MatchCatalog (CathedralRuins, CrimsonWastes, FrozenCrypt, AbyssalMire, AshenCitadel).")]
        [SerializeField] private string mapKey;
        [SerializeField] private Sprite background;

        [Header("Global Light")]
        [SerializeField] private Color globalLightColor = new Color(0.5f, 0.45f, 0.6f, 1f);
        [SerializeField, Range(0f, 2f)] private float globalLightIntensity = 0.55f;

        [Header("Structures")]
        [SerializeField, Range(0.5f, 2f)] private float structureScale = 1f;
        [Tooltip("Растяжение каменного цоколя башни по вертикали, чтобы он закрыл круглую платформу карты (0 — без цоколя).")]
        [SerializeField, Range(0f, 2f)] private float towerPlinthScaleY;
        [SerializeField, ColorUsage(false, true)] private Color userLightColor = new Color(0.25f, 0.6f, 1f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color aiLightColor = new Color(1f, 0.18f, 0.2f, 1f);
        [SerializeField, Range(0.2f, 5f)] private float towerLightRadius = 1.4f;
        [SerializeField, Range(0f, 5f)] private float towerLightIntensity = 1.4f;
        [SerializeField, Range(0.5f, 8f)] private float baseLightRadius = 2.6f;
        [SerializeField, Range(0f, 5f)] private float baseLightIntensity = 1.6f;

        [Header("Post Processing")]
        [SerializeField, Range(0f, 5f)] private float bloomIntensity = 1.1f;
        [SerializeField, Range(0f, 2f)] private float bloomThreshold = 0.85f;
        [SerializeField] private Color bloomTint = Color.white;
        [SerializeField, Range(0f, 1f)] private float vignetteIntensity = 0.32f;

        [Header("Layout (normalized image coordinates, origin bottom-left)")]
        [SerializeField] private Vector2 userBase;
        [SerializeField] private Vector2 aiBase;
        [SerializeField] private TowerSlot[] towers = Array.Empty<TowerSlot>();
        [SerializeField] private LanePath[] lanes = Array.Empty<LanePath>();
        [SerializeField] private MapZone[] zones = Array.Empty<MapZone>();
        [SerializeField] private AccentLight[] accentLights = Array.Empty<AccentLight>();

        [Header("Feature")]
        [SerializeField] private MapFeatureType featureType = MapFeatureType.None;
        [SerializeField] private string featureName;
        [SerializeField, TextArea(2, 4)] private string featureDescription;
        [Tooltip("Период срабатывания особенности, секунды (0 — действует постоянно).")]
        [SerializeField, Min(0f)] private float featurePeriodSeconds;
        [Tooltip("Основной параметр: урон, множитель золота, множитель скорости или радиуса агро.")]
        [SerializeField] private float featurePrimaryValue;
        [Tooltip("Дополнительный параметр (например, скорость на льду или в топи).")]
        [SerializeField] private float featureSecondaryValue;

        public string MapKey => mapKey;
        public Sprite Background => background;
        public Color GlobalLightColor => globalLightColor;
        public float GlobalLightIntensity => globalLightIntensity;
        public float StructureScale => structureScale;
        public float TowerPlinthScaleY => towerPlinthScaleY;
        public Color UserLightColor => userLightColor;
        public Color AiLightColor => aiLightColor;
        public float TowerLightRadius => towerLightRadius;
        public float TowerLightIntensity => towerLightIntensity;
        public float BaseLightRadius => baseLightRadius;
        public float BaseLightIntensity => baseLightIntensity;
        public float BloomIntensity => bloomIntensity;
        public float BloomThreshold => bloomThreshold;
        public Color BloomTint => bloomTint;
        public float VignetteIntensity => vignetteIntensity;
        public Vector2 UserBase => userBase;
        public Vector2 AiBase => aiBase;
        public TowerSlot[] Towers => towers;
        public LanePath[] Lanes => lanes;
        public MapZone[] Zones => zones;
        public AccentLight[] AccentLights => accentLights;
        public MapFeatureType FeatureType => featureType;
        public string FeatureName => featureName;
        public string FeatureDescription => featureDescription;
        public float FeaturePeriodSeconds => featurePeriodSeconds;
        public float FeaturePrimaryValue => featurePrimaryValue;
        public float FeatureSecondaryValue => featureSecondaryValue;

        public Color GetSideLightColor(BattleSide side)
        {
            return side == BattleSide.User ? userLightColor : aiLightColor;
        }

        public Vector2 GetBasePosition(BattleSide side)
        {
            return side == BattleSide.User ? userBase : aiBase;
        }

        /// <summary>Ищет маршрут линии. Для MidAlt на картах без развилки вернёт false.</summary>
        public bool TryGetLane(LaneId lane, out LanePath path)
        {
            for (int i = 0; i < lanes.Length; i++)
            {
                if (lanes[i].Lane == lane)
                {
                    path = lanes[i];
                    return true;
                }
            }

            path = default;
            return false;
        }

        public int CountTowers(BattleSide side)
        {
            int count = 0;
            for (int i = 0; i < towers.Length; i++)
            {
                if (towers[i].Side == side)
                {
                    count++;
                }
            }

            return count;
        }

#if UNITY_EDITOR
        /// <summary>Заполнение профиля сборщиком BattleMapBuilder (только в редакторе).</summary>
        public void EditorConfigure(
            string key,
            Sprite backgroundSprite,
            Color globalColor,
            float globalIntensity,
            float scale,
            float plinthScaleY,
            float bloom,
            float bloomThresholdValue,
            Color bloomTintValue,
            Vector2 userBasePosition,
            Vector2 aiBasePosition,
            TowerSlot[] towerSlots,
            LanePath[] lanePaths,
            MapZone[] mapZones,
            AccentLight[] accents,
            MapFeatureType feature,
            string featureTitle,
            string featureText,
            float period,
            float primary,
            float secondary)
        {
            mapKey = key;
            background = backgroundSprite;
            globalLightColor = globalColor;
            globalLightIntensity = globalIntensity;
            structureScale = scale;
            towerPlinthScaleY = plinthScaleY;
            bloomIntensity = bloom;
            bloomThreshold = bloomThresholdValue;
            bloomTint = bloomTintValue;
            userBase = userBasePosition;
            aiBase = aiBasePosition;
            towers = towerSlots ?? Array.Empty<TowerSlot>();
            lanes = lanePaths ?? Array.Empty<LanePath>();
            zones = mapZones ?? Array.Empty<MapZone>();
            accentLights = accents ?? Array.Empty<AccentLight>();
            featureType = feature;
            featureName = featureTitle;
            featureDescription = featureText;
            featurePeriodSeconds = period;
            featurePrimaryValue = primary;
            featureSecondaryValue = secondary;
        }
#endif
    }
}
