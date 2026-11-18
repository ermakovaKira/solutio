using System;
using System.Collections.Generic;
using Solutio.Equipment;
using Solutio.Session;
using Solutio.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Solutio.Battle
{
    /// <summary>
    /// Корневой контроллер сцены BattleMap (День 14). При старте берёт карту из
    /// SessionConfigurator.ActiveSession, находит её MapProfile и собирает поле:
    /// фон, глобальный свет, Bloom/Vignette, базы и башни сторон с Light2D,
    /// декоративные источники (лава, свечи, руны) и границы камеры.
    /// Составы с предметами (EquipmentController.LastResult) пока только логируются — юниты появятся на Днях 15–16.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleSceneController : MonoBehaviour
    {
        private const string LogTag = "<color=#B04CE0>[Battle]</color>";

        [Header("Data")]
        [SerializeField] private MapProfileLibrary library;
        [Tooltip("Карта для запуска сцены напрямую из редактора, без главного меню.")]
        [SerializeField] private string editorFallbackMapKey = "AshenCitadel";

        [Header("Scene")]
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private Light2D globalLight;
        [SerializeField] private Volume postVolume;
        [SerializeField] private BattleCameraController cameraController;
        [SerializeField] private BattleLightAnimator lightAnimator;
        [SerializeField] private Transform structuresRoot;
        [SerializeField] private Transform lightsRoot;

        [Header("Prefabs")]
        [SerializeField] private BattleStructureView towerPrefab;
        [SerializeField] private BattleStructureView basePrefab;
        [SerializeField] private Light2D accentLightPrefab;

        [Header("Art")]
        [SerializeField] private Sprite userTowerSprite;
        [SerializeField] private Sprite aiTowerSprite;
        [SerializeField] private Sprite userPlinthSprite;
        [SerializeField] private Sprite aiPlinthSprite;
        [SerializeField] private Sprite userBaseSprite;
        [SerializeField] private Sprite aiBaseSprite;

        [Header("Light Animation")]
        [SerializeField, Range(0f, 1f)] private float structureFlicker = 0.12f;
        [SerializeField, Range(0.1f, 10f)] private float structureFlickerSpeed = 1.6f;
        [SerializeField, Range(0.1f, 10f)] private float accentFlickerSpeed = 2.6f;

        [Header("HUD")]
        [SerializeField] private TMP_Text mapTitleText;
        [SerializeField] private TMP_Text featureText;

        [Header("Debug")]
        [Tooltip("Клавиши 1–5 переключают карту прямо в бою (для тестов и скриншотов, в релизе выключить).")]
        [SerializeField] private bool enableMapHotkeys = true;
        [SerializeField] private bool drawLaneGizmos = true;
        [SerializeField] private bool enableDetailedLogging = true;

        private readonly List<BattleStructureView> _structures = new List<BattleStructureView>(16);
        private readonly List<Light2D> _accentLights = new List<Light2D>(16);

        private Bloom _bloom;
        private Vignette _vignette;
        private bool _isReady;

        /// <summary>Профиль текущей собранной карты.</summary>
        public MapProfile ActiveProfile { get; private set; }

        /// <summary>Мировой размер картинки текущей карты.</summary>
        public Vector2 WorldSize { get; private set; }

        /// <summary>Все постройки текущей карты (обе стороны).</summary>
        public IReadOnlyList<BattleStructureView> Structures => _structures;

        /// <summary>Поле собрано (после старта или переключения карты).</summary>
        public event Action<MapProfile> OnMapBuilt;

        private void Awake()
        {
            _isReady = ValidateReferences();

            if (_isReady && postVolume != null && postVolume.profile != null)
            {
                // profile (а не sharedProfile) — копия, изменения не портят ассет.
                postVolume.profile.TryGet(out _bloom);
                postVolume.profile.TryGet(out _vignette);
            }
        }

        private void Start()
        {
            if (!_isReady)
            {
                Debug.LogError($"{LogTag} Сцена боя не собрана: не назначены обязательные ссылки (см. ошибки выше). Пересоберите «Solutio → Собрать BattleMap».", this);
                return;
            }

            MatchSessionConfig session = SessionConfigurator.ActiveSession;
            string requestedKey = session != null ? session.Map.Key : editorFallbackMapKey;

            if (!library.TryGet(requestedKey, out MapProfile profile))
            {
                Debug.LogError($"{LogTag} Нет профиля для карты «{requestedKey}» — загружаю «{editorFallbackMapKey}».", this);

                if (!library.TryGet(editorFallbackMapKey, out profile))
                {
                    Debug.LogError($"{LogTag} Резервная карта «{editorFallbackMapKey}» тоже не найдена в библиотеке профилей.", this);
                    return;
                }
            }

            if (enableDetailedLogging)
            {
                LogSession(session);
            }

            BuildMap(profile);
        }

        private void Update()
        {
            if (!enableMapHotkeys || !_isReady)
            {
                return;
            }

            int count = Mathf.Min(library.Count, 9);
            for (int i = 0; i < count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    MapProfile profile = library.GetAt(i);
                    if (profile != null && profile != ActiveProfile)
                    {
                        BuildMap(profile);
                    }

                    return;
                }
            }
        }

        /// <summary>Собирает поле боя по профилю (повторный вызов пересобирает без утечек).</summary>
        public void BuildMap(MapProfile profile)
        {
            if (profile == null)
            {
                Debug.LogError($"{LogTag} BuildMap: профиль карты не передан.", this);
                return;
            }

            if (profile.Background == null)
            {
                Debug.LogError($"{LogTag} У профиля «{profile.name}» нет картинки поля.", profile);
                return;
            }

            float startedAt = Time.realtimeSinceStartup;
            ClearMap();
            ActiveProfile = profile;

            background.sprite = profile.Background;
            background.transform.position = Vector3.zero;
            WorldSize = profile.Background.bounds.size;

            globalLight.color = profile.GlobalLightColor;
            globalLight.intensity = profile.GlobalLightIntensity;
            ApplyPostProcessing(profile);

            SpawnBase(profile, BattleSide.User);
            SpawnBase(profile, BattleSide.Ai);

            TowerSlot[] towers = profile.Towers;
            for (int i = 0; i < towers.Length; i++)
            {
                SpawnTower(profile, towers[i]);
            }

            AccentLight[] accents = profile.AccentLights;
            for (int i = 0; i < accents.Length; i++)
            {
                SpawnAccent(accents[i]);
            }

            cameraController.SetBounds(new Rect(-WorldSize * 0.5f, WorldSize), true);
            UpdateHud(profile);

            if (enableDetailedLogging)
            {
                LogMap(profile, (Time.realtimeSinceStartup - startedAt) * 1000f);
            }

            OnMapBuilt?.Invoke(profile);
        }

        private void ClearMap()
        {
            lightAnimator.Clear();

            for (int i = 0; i < _structures.Count; i++)
            {
                if (_structures[i] != null)
                {
                    Destroy(_structures[i].gameObject);
                }
            }

            for (int i = 0; i < _accentLights.Count; i++)
            {
                if (_accentLights[i] != null)
                {
                    Destroy(_accentLights[i].gameObject);
                }
            }

            _structures.Clear();
            _accentLights.Clear();
            ActiveProfile = null;
        }

        private void SpawnBase(MapProfile profile, BattleSide side)
        {
            Vector2 position = BattleMapMath.ToWorld(profile.GetBasePosition(side), WorldSize);
            BattleStructureView view = Instantiate(basePrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, structuresRoot);
            view.name = side == BattleSide.User ? "Base_User" : "Base_Ai";
            view.Setup(
                BattleStructureKind.Base,
                side,
                LaneId.Mid,
                0,
                side == BattleSide.User ? userBaseSprite : aiBaseSprite,
                null,
                0f,
                profile.GetSideLightColor(side),
                profile.BaseLightRadius,
                profile.BaseLightIntensity,
                profile.StructureScale);

            _structures.Add(view);
            lightAnimator.Register(view.Glow, structureFlicker, structureFlickerSpeed);
        }

        private void SpawnTower(MapProfile profile, TowerSlot slot)
        {
            Vector2 position = BattleMapMath.ToWorld(slot.Position, WorldSize);
            BattleStructureView view = Instantiate(towerPrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, structuresRoot);
            view.name = $"Tower_{slot.Side}_{slot.Lane}_T{slot.Tier}";
            view.Setup(
                BattleStructureKind.Tower,
                slot.Side,
                slot.Lane,
                slot.Tier,
                slot.Side == BattleSide.User ? userTowerSprite : aiTowerSprite,
                slot.Side == BattleSide.User ? userPlinthSprite : aiPlinthSprite,
                profile.TowerPlinthScaleY,
                profile.GetSideLightColor(slot.Side),
                profile.TowerLightRadius,
                profile.TowerLightIntensity,
                profile.StructureScale);

            _structures.Add(view);
            lightAnimator.Register(view.Glow, structureFlicker, structureFlickerSpeed);
        }

        private void SpawnAccent(AccentLight accent)
        {
            Vector2 position = BattleMapMath.ToWorld(accent.Position, WorldSize);
            Light2D light = Instantiate(accentLightPrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity, lightsRoot);
            light.name = $"Accent_{_accentLights.Count + 1}";
            light.color = accent.Color;
            light.intensity = accent.Intensity;
            light.pointLightOuterRadius = accent.Radius;
            light.pointLightInnerRadius = accent.Radius * 0.1f;

            _accentLights.Add(light);
            lightAnimator.Register(light, accent.Flicker, accentFlickerSpeed);
        }

        private void ApplyPostProcessing(MapProfile profile)
        {
            if (_bloom != null)
            {
                _bloom.active = profile.BloomIntensity > 0f;
                _bloom.intensity.Override(profile.BloomIntensity);
                _bloom.threshold.Override(profile.BloomThreshold);
                _bloom.tint.Override(profile.BloomTint);
            }

            if (_vignette != null)
            {
                _vignette.active = profile.VignetteIntensity > 0f;
                _vignette.intensity.Override(profile.VignetteIntensity);
            }
        }

        private void UpdateHud(MapProfile profile)
        {
            if (mapTitleText != null)
            {
                mapTitleText.SetText(MatchCatalog.GetMapDisplayName(profile.MapKey));
            }

            if (featureText != null)
            {
                featureText.SetText(string.IsNullOrEmpty(profile.FeatureName)
                    ? string.Empty
                    : $"{profile.FeatureName} — {profile.FeatureDescription}");
            }
        }

        private bool ValidateReferences()
        {
            bool ok = true;
            ok &= Require(library, nameof(library));
            ok &= Require(background, nameof(background));
            ok &= Require(globalLight, nameof(globalLight));
            ok &= Require(cameraController, nameof(cameraController));
            ok &= Require(lightAnimator, nameof(lightAnimator));
            ok &= Require(structuresRoot, nameof(structuresRoot));
            ok &= Require(lightsRoot, nameof(lightsRoot));
            ok &= Require(towerPrefab, nameof(towerPrefab));
            ok &= Require(basePrefab, nameof(basePrefab));
            ok &= Require(accentLightPrefab, nameof(accentLightPrefab));
            ok &= Require(userTowerSprite, nameof(userTowerSprite));
            ok &= Require(aiTowerSprite, nameof(aiTowerSprite));
            ok &= Require(userBaseSprite, nameof(userBaseSprite));
            ok &= Require(aiBaseSprite, nameof(aiBaseSprite));

            if (postVolume == null)
            {
                Debug.LogWarning($"{LogTag} Volume не назначен — Bloom и Vignette по картам настраиваться не будут.", this);
            }

            return ok;
        }

        private bool Require(UnityEngine.Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"{LogTag} Не назначено поле «{fieldName}» у BattleSceneController.", this);
            return false;
        }

        private void LogSession(MatchSessionConfig session)
        {
            if (session == null)
            {
                Debug.Log($"{LogTag} Сцена запущена без сессии матча (прямой Play) — карта «{editorFallbackMapKey}». Клавиши 1–{library.Count} переключают карты.");
                return;
            }

            Debug.Log($"{LogTag} Сессия: пользователь «{session.UserLogin}», сложность {MatchCatalog.GetDifficultyDisplayName(session.Difficulty)}, " +
                      $"карта «{session.Map.DisplayName}» ({session.Map.Key}), seed {session.MatchSeed}.");

            EquipmentResult equipment = EquipmentController.LastResult;
            if (equipment == null)
            {
                Debug.LogWarning($"{LogTag} Итог экипировки отсутствует — бой открыт не через «В БОЙ».");
                return;
            }

            Debug.Log($"{LogTag} Составы получены: пользователь {equipment.UserTeam.Count} героев / {equipment.UserEquippedCount} предметов, " +
                      $"ИИ {equipment.AiTeam.Count} героев / {equipment.AiEquippedCount} предметов (юниты — Дни 15–16).");
        }

        private void LogMap(MapProfile profile, float buildMilliseconds)
        {
            string lanes = string.Empty;
            LanePath[] paths = profile.Lanes;
            for (int i = 0; i < paths.Length; i++)
            {
                float length = BattleMapMath.GetPathLength(paths[i].Points, WorldSize);
                lanes += (i == 0 ? string.Empty : ", ") + $"{paths[i].Lane} {length:0.0}";
            }

            Debug.Log($"{LogTag} Карта «{MatchCatalog.GetMapDisplayName(profile.MapKey)}» ({profile.MapKey}) собрана за {buildMilliseconds:0.0} мс: " +
                      $"поле {WorldSize.x:0.00}×{WorldSize.y:0.00} ед., башен {profile.CountTowers(BattleSide.User)} + {profile.CountTowers(BattleSide.Ai)}, " +
                      $"Light2D: глобальный 1 + построек {_structures.Count} + декоративных {_accentLights.Count}. " +
                      $"Длины линий (ед.): {lanes}. Особенность: {profile.FeatureName} ({profile.FeatureType}).");
        }

        private void OnDrawGizmos()
        {
            if (!drawLaneGizmos)
            {
                return;
            }

            MapProfile profile = ActiveProfile;
            if (profile == null && library != null)
            {
                library.TryGet(editorFallbackMapKey, out profile);
            }

            if (profile == null || profile.Background == null)
            {
                return;
            }

            Vector2 size = profile.Background.bounds.size;
            LanePath[] lanes = profile.Lanes;
            for (int l = 0; l < lanes.Length; l++)
            {
                Gizmos.color = GetLaneGizmoColor(lanes[l].Lane);
                Vector2[] points = lanes[l].Points;
                for (int i = 1; i < points.Length; i++)
                {
                    Gizmos.DrawLine(BattleMapMath.ToWorld(points[i - 1], size), BattleMapMath.ToWorld(points[i], size));
                }
            }

            MapZone[] zones = profile.Zones;
            Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
            for (int i = 0; i < zones.Length; i++)
            {
                Gizmos.DrawWireSphere(BattleMapMath.ToWorld(zones[i].Position, size), zones[i].Radius * size.x);
            }
        }

        private static Color GetLaneGizmoColor(LaneId lane)
        {
            switch (lane)
            {
                case LaneId.Top: return new Color(0.3f, 0.8f, 1f);
                case LaneId.Mid: return new Color(1f, 0.9f, 0.35f);
                case LaneId.MidAlt: return new Color(1f, 0.6f, 0.25f);
                default: return new Color(0.45f, 1f, 0.55f);
            }
        }

#if UNITY_EDITOR
        public void EditorAssign(
            MapProfileLibrary profileLibrary,
            SpriteRenderer backgroundRenderer,
            Light2D global,
            Volume volume,
            BattleCameraController cameraRig,
            BattleLightAnimator animator,
            Transform structures,
            Transform lights,
            BattleStructureView tower,
            BattleStructureView baseView,
            Light2D accent,
            Sprite towerUser,
            Sprite towerAi,
            Sprite plinthUser,
            Sprite plinthAi,
            Sprite baseUser,
            Sprite baseAi,
            TMP_Text title,
            TMP_Text feature)
        {
            library = profileLibrary;
            background = backgroundRenderer;
            globalLight = global;
            postVolume = volume;
            cameraController = cameraRig;
            lightAnimator = animator;
            structuresRoot = structures;
            lightsRoot = lights;
            towerPrefab = tower;
            basePrefab = baseView;
            accentLightPrefab = accent;
            userTowerSprite = towerUser;
            aiTowerSprite = towerAi;
            userPlinthSprite = plinthUser;
            aiPlinthSprite = plinthAi;
            userBaseSprite = baseUser;
            aiBaseSprite = baseAi;
            mapTitleText = title;
            featureText = feature;
        }
#endif
    }
}
