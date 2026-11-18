using System.Collections.Generic;
using Solutio.Battle;
using Solutio.Session;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Solutio.EditorTools
{
    /// <summary>
    /// День 14. Сборщик боевой сцены «Solutio → Собрать BattleMap»:
    /// настраивает импорт артов карт и построек, создаёт 5 профилей MapProfile и библиотеку,
    /// профиль постобработки (Bloom, Vignette), префабы башни, базы и декоративного света,
    /// сцену BattleMap.unity (камера URP, глобальный Light2D, Volume, фон, HUD, счётчик FPS)
    /// и добавляет её в Build Settings. Повторный запуск пересобирает всё заново.
    /// </summary>
    public static class BattleMapBuilder
    {
        private const string SceneName = "BattleMap";
        private const string ScenePath = "Assets/_Project/Scenes/" + SceneName + ".unity";
        private const string MapsFolder = "Assets/_Project/Sprites/Battle/Maps";
        private const string PropsFolder = "Assets/_Project/Sprites/Battle/Props";
        private const string DataFolder = "Assets/_Project/Data/Battle";
        private const string PrefabsFolder = "Assets/_Project/Prefabs/Battle";
        private const string LibraryPath = DataFolder + "/MapProfileLibrary.asset";
        private const string VolumeProfilePath = DataFolder + "/VP_Battle.asset";

        /// <summary>Размер исходной картинки ChatGPT, в пикселях которой записаны координаты.</summary>
        private const float SourceWidth = 1536f;
        private const float SourceHeight = 1024f;

        /// <summary>Мировая ширина карты: 15.36 × 10.24 единицы при любом разрешении файла.</summary>
        private const float MapWorldWidth = 15.36f;

        // Пивоты и масштаб построек сняты с альфа-канала артов (центр каменного основания).
        private const float TowerPivotY = 0.139f;
        private const float PlinthPivotY = 0.506f;
        private const float BasePivotY = 0.242f;
        private const float TowerPixelsPerUnit = 1070f;
        private const float BasePixelsPerUnit = 575f;
        private static readonly Vector3 TowerGlowLocal = new Vector3(0f, 0.87f, 0f);
        private static readonly Vector3 BaseGlowLocal = new Vector3(0f, 0.15f, 0f);

        private static readonly Color HudBright = new Color32(0xE8, 0xF4, 0xF8, 0xFF);
        private static readonly Color HudLight = new Color32(0xC8, 0xCC, 0xD4, 0xFF);

        private sealed class MapSpec
        {
            public string Key;
            public string File;
            public Color GlobalColor;
            public float GlobalIntensity;
            public float StructureScale;
            public float PlinthScaleY;
            public float Bloom;
            public float BloomThreshold;
            public Color BloomTint;
            public Vector2 UserBase;
            public Vector2 AiBase;
            public TowerSlot[] Towers;
            public LanePath[] Lanes;
            public MapZone[] Zones;
            public AccentLight[] Accents;
            public MapFeatureType Feature;
            public string FeatureName;
            public string FeatureDescription;
            public float FeaturePeriod;
            public float FeaturePrimary;
            public float FeatureSecondary;
        }

        #region Map Data (координаты сняты с артов, пиксели 1536 × 1024, ось Y вниз)

        private static Vector2 P(float x, float y)
        {
            return new Vector2(x / SourceWidth, 1f - y / SourceHeight);
        }

        private static TowerSlot Tower(BattleSide side, LaneId lane, int tier, Vector2 position)
        {
            return new TowerSlot { Side = side, Lane = lane, Tier = tier, Position = position };
        }

        private static LanePath Lane(LaneId lane, params Vector2[] points)
        {
            return new LanePath { Lane = lane, Points = points };
        }

        private static MapZone Zone(MapZoneType type, Vector2 position, float radiusPixels)
        {
            return new MapZone { Type = type, Position = position, Radius = radiusPixels / SourceWidth };
        }

        private static AccentLight Accent(Vector2 position, Color color, float radius, float intensity, float flicker)
        {
            return new AccentLight { Position = position, Color = color, Radius = radius, Intensity = intensity, Flicker = flicker };
        }

        private static MapSpec[] CreateSpecs()
        {
            return new[]
            {
            new MapSpec
            {
                Key = "AshenCitadel",
                File = "Map_KalinovBridge",
                GlobalColor = new Color(0.62f, 0.42f, 0.48f, 1f),
                GlobalIntensity = 0.5f,
                StructureScale = 1f,
                PlinthScaleY = 0f,
                Bloom = 1.25f,
                BloomThreshold = 0.8f,
                BloomTint = new Color(1f, 0.78f, 0.6f, 1f),
                UserBase = P(205, 690),
                AiBase = P(1385, 180),
                Towers = new[]
                {
                    Tower(BattleSide.User, LaneId.Top, 2, P(154, 364)),
                    Tower(BattleSide.User, LaneId.Top, 1, P(290, 121)),
                    Tower(BattleSide.User, LaneId.Mid, 2, P(438, 612)),
                    Tower(BattleSide.User, LaneId.Mid, 1, P(630, 516)),
                    Tower(BattleSide.User, LaneId.Bot, 2, P(480, 812)),
                    Tower(BattleSide.User, LaneId.Bot, 1, P(812, 816)),
                    Tower(BattleSide.Ai, LaneId.Top, 1, P(745, 122)),
                    Tower(BattleSide.Ai, LaneId.Top, 2, P(1071, 133)),
                    Tower(BattleSide.Ai, LaneId.Mid, 1, P(963, 373)),
                    Tower(BattleSide.Ai, LaneId.Mid, 2, P(1145, 294)),
                    Tower(BattleSide.Ai, LaneId.Bot, 1, P(1325, 814)),
                    Tower(BattleSide.Ai, LaneId.Bot, 2, P(1434, 507))
                },
                Lanes = new[]
                {
                    Lane(LaneId.Top, P(205, 690), P(155, 590), P(150, 470), P(150, 362), P(170, 230), P(215, 145), P(290, 120), P(400, 125), P(520, 120), P(640, 125), P(745, 120), P(900, 130), P(1068, 128), P(1180, 135), P(1280, 160), P(1385, 180)),
                    Lane(LaneId.Mid, P(205, 690), P(330, 650), P(430, 612), P(540, 560), P(628, 515), P(720, 465), P(800, 432), P(880, 400), P(960, 372), P(1060, 330), P(1140, 292), P(1230, 262), P(1300, 225), P(1385, 180)),
                    Lane(LaneId.Bot, P(205, 690), P(290, 780), P(380, 808), P(480, 812), P(640, 812), P(810, 815), P(930, 815), P(1010, 815), P(1150, 812), P(1320, 812), P(1400, 780), P(1428, 680), P(1433, 507), P(1420, 410), P(1390, 300), P(1385, 180))
                },
                Zones = new MapZone[]
                {
                    Zone(MapZoneType.FireBridge, P(640, 125), 45),
                    Zone(MapZoneType.FireBridge, P(800, 432), 50),
                    Zone(MapZoneType.FireBridge, P(1010, 815), 50)
                },
                Accents = new[]
                {
                    Accent(P(330, 300), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(830, 265), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1260, 555), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(950, 655), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(600, 40), new Color(1f, 0.45f, 0.12f, 1f), 1.6f, 1.8f, 0.35f),
                    Accent(P(650, 240), new Color(1f, 0.45f, 0.12f, 1f), 1.6f, 1.8f, 0.35f),
                    Accent(P(760, 340), new Color(1f, 0.45f, 0.12f, 1f), 1.5f, 1.6f, 0.35f),
                    Accent(P(860, 540), new Color(1f, 0.45f, 0.12f, 1f), 1.6f, 1.8f, 0.35f),
                    Accent(P(930, 700), new Color(1f, 0.45f, 0.12f, 1f), 1.5f, 1.6f, 0.35f),
                    Accent(P(1040, 900), new Color(1f, 0.45f, 0.12f, 1f), 1.6f, 1.8f, 0.35f)
                },
                Feature = MapFeatureType.RiverFlare,
                FeatureName = "Пламя Смородины",
                FeatureDescription = "Раз в 40 секунд огненная река вспыхивает: все юниты на мостах получают урон.",
                FeaturePeriod = 40f,
                FeaturePrimary = 60f,
                FeatureSecondary = 0f
            },
            new MapSpec
            {
                Key = "CathedralRuins",
                File = "Map_Kapishche",
                GlobalColor = new Color(0.5f, 0.44f, 0.62f, 1f),
                GlobalIntensity = 0.5f,
                StructureScale = 1f,
                PlinthScaleY = 1.45f,
                Bloom = 1.1f,
                BloomThreshold = 0.85f,
                BloomTint = new Color(1f, 0.85f, 0.65f, 1f),
                UserBase = P(200, 720),
                AiBase = P(1370, 255),
                Towers = new[]
                {
                    Tower(BattleSide.User, LaneId.Top, 2, P(118, 460)),
                    Tower(BattleSide.User, LaneId.Top, 1, P(234, 162)),
                    Tower(BattleSide.User, LaneId.Mid, 2, P(314, 705)),
                    Tower(BattleSide.User, LaneId.Mid, 1, P(423, 668)),
                    Tower(BattleSide.User, LaneId.Bot, 2, P(478, 826)),
                    Tower(BattleSide.User, LaneId.Bot, 1, P(810, 842)),
                    Tower(BattleSide.Ai, LaneId.Top, 1, P(727, 160)),
                    Tower(BattleSide.Ai, LaneId.Top, 2, P(1079, 175)),
                    Tower(BattleSide.Ai, LaneId.Mid, 1, P(1122, 337)),
                    Tower(BattleSide.Ai, LaneId.Mid, 2, P(1238, 292)),
                    Tower(BattleSide.Ai, LaneId.Bot, 1, P(1310, 832)),
                    Tower(BattleSide.Ai, LaneId.Bot, 2, P(1426, 546))
                },
                Lanes = new[]
                {
                    Lane(LaneId.Top, P(200, 720), P(125, 630), P(115, 540), P(120, 455), P(130, 330), P(160, 220), P(230, 165), P(330, 150), P(450, 160), P(590, 182), P(722, 158), P(880, 165), P(1078, 178), P(1180, 182), P(1270, 215), P(1370, 255)),
                    Lane(LaneId.Mid, P(200, 720), P(315, 707), P(422, 668), P(545, 615), P(580, 540), P(640, 440), P(730, 378), P(840, 360), P(935, 378), P(995, 410), P(1060, 368), P(1120, 335), P(1236, 292), P(1300, 270), P(1370, 255)),
                    Lane(LaneId.MidAlt, P(200, 720), P(315, 707), P(422, 668), P(545, 615), P(640, 640), P(760, 660), P(870, 640), P(940, 570), P(968, 480), P(995, 410), P(1060, 368), P(1120, 335), P(1236, 292), P(1300, 270), P(1370, 255)),
                    Lane(LaneId.Bot, P(200, 720), P(300, 790), P(380, 815), P(478, 825), P(640, 830), P(808, 840), P(940, 825), P(1060, 815), P(1200, 820), P(1306, 832), P(1385, 780), P(1405, 680), P(1420, 548), P(1405, 420), P(1385, 330), P(1370, 255))
                },
                Zones = new MapZone[]
                {
                    Zone(MapZoneType.Altar, P(780, 500), 110)
                },
                Accents = new[]
                {
                    Accent(P(300, 385), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1230, 610), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(240, 470), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(860, 280), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(780, 470), new Color(1f, 0.55f, 0.2f, 1f), 2.2f, 2f, 0.4f),
                    Accent(P(610, 262), new Color(1f, 0.7f, 0.35f, 1f), 0.7f, 1.4f, 0.5f),
                    Accent(P(640, 340), new Color(1f, 0.7f, 0.35f, 1f), 0.7f, 1.3f, 0.5f),
                    Accent(P(760, 738), new Color(1f, 0.7f, 0.35f, 1f), 0.7f, 1.3f, 0.5f),
                    Accent(P(1030, 1000), new Color(1f, 0.7f, 0.35f, 1f), 0.7f, 1.2f, 0.5f),
                    Accent(P(420, 580), new Color(1f, 0.7f, 0.35f, 1f), 0.6f, 1.2f, 0.5f)
                },
                Feature = MapFeatureType.PerunStrike,
                FeatureName = "Гнев Перуна",
                FeatureDescription = "Раз в 60 секунд молния бьёт в самого сильного героя на карте, на чьей бы стороне он ни был.",
                FeaturePeriod = 60f,
                FeaturePrimary = 150f,
                FeatureSecondary = 0f
            },
            new MapSpec
            {
                Key = "CrimsonWastes",
                File = "Map_Pole",
                GlobalColor = new Color(0.78f, 0.42f, 0.44f, 1f),
                GlobalIntensity = 0.55f,
                StructureScale = 1.15f,
                PlinthScaleY = 1.3f,
                Bloom = 1f,
                BloomThreshold = 0.85f,
                BloomTint = new Color(1f, 0.7f, 0.65f, 1f),
                UserBase = P(220, 715),
                AiBase = P(1355, 265),
                Towers = new[]
                {
                    Tower(BattleSide.User, LaneId.Top, 2, P(198, 444)),
                    Tower(BattleSide.User, LaneId.Top, 1, P(293, 175)),
                    Tower(BattleSide.User, LaneId.Mid, 2, P(482, 638)),
                    Tower(BattleSide.User, LaneId.Mid, 1, P(674, 552)),
                    Tower(BattleSide.User, LaneId.Bot, 2, P(519, 820)),
                    Tower(BattleSide.User, LaneId.Bot, 1, P(835, 824)),
                    Tower(BattleSide.Ai, LaneId.Top, 1, P(737, 174)),
                    Tower(BattleSide.Ai, LaneId.Top, 2, P(1049, 178)),
                    Tower(BattleSide.Ai, LaneId.Mid, 1, P(1120, 362)),
                    Tower(BattleSide.Ai, LaneId.Mid, 2, P(1234, 309)),
                    Tower(BattleSide.Ai, LaneId.Bot, 1, P(1308, 820)),
                    Tower(BattleSide.Ai, LaneId.Bot, 2, P(1411, 548))
                },
                Lanes = new[]
                {
                    Lane(LaneId.Top, P(220, 715), P(205, 600), P(198, 447), P(205, 300), P(220, 205), P(290, 178), P(450, 176), P(600, 176), P(737, 175), P(900, 175), P(1047, 175), P(1180, 180), P(1265, 220), P(1355, 265)),
                    Lane(LaneId.Mid, P(220, 715), P(350, 672), P(482, 637), P(580, 595), P(673, 552), P(800, 495), P(928, 442), P(1030, 400), P(1118, 360), P(1235, 310), P(1300, 285), P(1355, 265)),
                    Lane(LaneId.Bot, P(220, 715), P(320, 800), P(420, 822), P(517, 822), P(680, 822), P(833, 822), P(1000, 822), P(1150, 822), P(1305, 822), P(1390, 780), P(1410, 680), P(1410, 548), P(1400, 420), P(1380, 330), P(1355, 265))
                },
                Zones = new MapZone[]
                {
                },
                Accents = new[]
                {
                    Accent(P(520, 290), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(830, 710), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1065, 715), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1270, 630), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(700, 30), new Color(1f, 0.3f, 0.15f, 1f), 2.5f, 1.6f, 0.15f),
                    Accent(P(780, 330), new Color(0.9f, 0.2f, 0.12f, 1f), 1.6f, 0.9f, 0.2f),
                    Accent(P(920, 600), new Color(0.9f, 0.2f, 0.12f, 1f), 1.6f, 0.9f, 0.2f),
                    Accent(P(1060, 860), new Color(0.9f, 0.2f, 0.12f, 1f), 1.6f, 0.9f, 0.2f)
                },
                Feature = MapFeatureType.Slaughter,
                FeatureName = "Сеча",
                FeatureDescription = "Короткие прямые линии и щедрая награда: золото за убийства ×1,5.",
                FeaturePeriod = 0f,
                FeaturePrimary = 1.5f,
                FeatureSecondary = 0f
            },
            new MapSpec
            {
                Key = "FrozenCrypt",
                File = "Map_Moroz",
                GlobalColor = new Color(0.52f, 0.62f, 0.9f, 1f),
                GlobalIntensity = 0.58f,
                StructureScale = 1f,
                PlinthScaleY = 1.3f,
                Bloom = 1.1f,
                BloomThreshold = 0.8f,
                BloomTint = new Color(0.75f, 0.9f, 1f, 1f),
                UserBase = P(232, 655),
                AiBase = P(1302, 325),
                Towers = new[]
                {
                    Tower(BattleSide.User, LaneId.Top, 2, P(66, 424)),
                    Tower(BattleSide.User, LaneId.Top, 1, P(246, 133)),
                    Tower(BattleSide.User, LaneId.Mid, 2, P(424, 598)),
                    Tower(BattleSide.User, LaneId.Mid, 1, P(618, 535)),
                    Tower(BattleSide.User, LaneId.Bot, 2, P(397, 840)),
                    Tower(BattleSide.User, LaneId.Bot, 1, P(758, 842)),
                    Tower(BattleSide.Ai, LaneId.Top, 1, P(782, 139)),
                    Tower(BattleSide.Ai, LaneId.Top, 2, P(1140, 139)),
                    Tower(BattleSide.Ai, LaneId.Mid, 1, P(916, 438)),
                    Tower(BattleSide.Ai, LaneId.Mid, 2, P(1112, 374)),
                    Tower(BattleSide.Ai, LaneId.Bot, 1, P(1294, 848)),
                    Tower(BattleSide.Ai, LaneId.Bot, 2, P(1472, 547))
                },
                Lanes = new[]
                {
                    Lane(LaneId.Top, P(232, 655), P(130, 610), P(75, 520), P(65, 425), P(80, 300), P(140, 190), P(245, 135), P(360, 120), P(460, 140), P(560, 200), P(680, 205), P(775, 140), P(900, 128), P(1010, 185), P(1138, 138), P(1255, 125), P(1320, 185), P(1325, 260), P(1302, 325)),
                    Lane(LaneId.Mid, P(232, 655), P(330, 622), P(420, 600), P(520, 567), P(615, 535), P(720, 497), P(820, 467), P(912, 438), P(1010, 405), P(1110, 375), P(1200, 348), P(1302, 325)),
                    Lane(LaneId.Bot, P(232, 655), P(195, 760), P(260, 830), P(392, 840), P(500, 822), P(620, 808), P(755, 840), P(870, 820), P(960, 785), P(1060, 792), P(1160, 832), P(1290, 848), P(1400, 805), P(1460, 700), P(1468, 545), P(1445, 430), P(1395, 350), P(1302, 325))
                },
                Zones = new MapZone[]
                {
                    Zone(MapZoneType.Ice, P(560, 200), 55),
                    Zone(MapZoneType.Ice, P(810, 470), 55),
                    Zone(MapZoneType.Ice, P(1010, 790), 55)
                },
                Accents = new[]
                {
                    Accent(P(320, 480), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(660, 295), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1210, 465), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(870, 660), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(450, 40), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f),
                    Accent(P(560, 260), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f),
                    Accent(P(690, 400), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f),
                    Accent(P(860, 620), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f),
                    Accent(P(1000, 800), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f),
                    Accent(P(1090, 960), new Color(0.35f, 0.85f, 1f, 1f), 1.5f, 1.4f, 0.15f)
                },
                Feature = MapFeatureType.Frost,
                FeatureName = "Стужа",
                FeatureDescription = "Все юниты движутся на 20 % медленнее, но по льду реки — на 25 % быстрее.",
                FeaturePeriod = 0f,
                FeaturePrimary = 0.8f,
                FeatureSecondary = 1.25f
            },
            new MapSpec
            {
                Key = "AbyssalMire",
                File = "Map_Boloto",
                GlobalColor = new Color(0.45f, 0.6f, 0.5f, 1f),
                GlobalIntensity = 0.48f,
                StructureScale = 0.95f,
                PlinthScaleY = 1.3f,
                Bloom = 1.15f,
                BloomThreshold = 0.8f,
                BloomTint = new Color(0.75f, 1f, 0.7f, 1f),
                UserBase = P(195, 660),
                AiBase = P(1370, 165),
                Towers = new[]
                {
                    Tower(BattleSide.User, LaneId.Top, 2, P(146, 357)),
                    Tower(BattleSide.User, LaneId.Top, 1, P(206, 90)),
                    Tower(BattleSide.User, LaneId.Mid, 2, P(410, 580)),
                    Tower(BattleSide.User, LaneId.Mid, 1, P(625, 524)),
                    Tower(BattleSide.User, LaneId.Bot, 2, P(491, 720)),
                    Tower(BattleSide.User, LaneId.Bot, 1, P(840, 710)),
                    Tower(BattleSide.Ai, LaneId.Top, 1, P(710, 131)),
                    Tower(BattleSide.Ai, LaneId.Top, 2, P(1053, 118)),
                    Tower(BattleSide.Ai, LaneId.Mid, 1, P(920, 286)),
                    Tower(BattleSide.Ai, LaneId.Mid, 2, P(1132, 240)),
                    Tower(BattleSide.Ai, LaneId.Bot, 1, P(1362, 754)),
                    Tower(BattleSide.Ai, LaneId.Bot, 2, P(1416, 448))
                },
                Lanes = new[]
                {
                    Lane(LaneId.Top, P(195, 660), P(135, 560), P(115, 465), P(145, 355), P(110, 250), P(130, 160), P(205, 88), P(300, 82), P(380, 108), P(458, 123), P(560, 108), P(640, 115), P(708, 128), P(800, 108), P(900, 85), P(1053, 115), P(1180, 132), P(1270, 145), P(1370, 165)),
                    Lane(LaneId.Mid, P(195, 660), P(300, 628), P(408, 578), P(445, 515), P(530, 522), P(618, 522), P(680, 452), P(760, 412), P(845, 372), P(915, 285), P(1010, 280), P(1130, 238), P(1200, 192), P(1280, 170), P(1370, 165)),
                    Lane(LaneId.Bot, P(195, 660), P(300, 712), P(400, 740), P(488, 717), P(600, 700), P(720, 712), P(838, 708), P(950, 735), P(1075, 715), P(1200, 702), P(1300, 742), P(1360, 752), P(1440, 700), P(1425, 600), P(1415, 447), P(1385, 350), P(1365, 250), P(1370, 165))
                },
                Zones = new MapZone[]
                {
                    Zone(MapZoneType.Bog, P(760, 412), 60),
                    Zone(MapZoneType.Bog, P(1000, 725), 60),
                    Zone(MapZoneType.Bog, P(560, 108), 55),
                    Zone(MapZoneType.Bog, P(475, 520), 55)
                },
                Accents = new[]
                {
                    Accent(P(255, 345), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(525, 320), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(1285, 450), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(815, 640), new Color(0.72f, 0.32f, 1f, 1f), 1f, 1.6f, 0.25f),
                    Accent(P(560, 60), new Color(0.45f, 1f, 0.35f, 1f), 1.4f, 1.4f, 0.35f),
                    Accent(P(700, 330), new Color(0.45f, 1f, 0.35f, 1f), 1.4f, 1.3f, 0.35f),
                    Accent(P(900, 560), new Color(0.45f, 1f, 0.35f, 1f), 1.5f, 1.5f, 0.35f),
                    Accent(P(960, 760), new Color(0.45f, 1f, 0.35f, 1f), 1.4f, 1.3f, 0.35f),
                    Accent(P(1040, 950), new Color(0.45f, 1f, 0.35f, 1f), 1.4f, 1.3f, 0.35f),
                    Accent(P(300, 250), new Color(0.45f, 1f, 0.35f, 1f), 0.9f, 1f, 0.5f)
                },
                Feature = MapFeatureType.Fog,
                FeatureName = "Туман",
                FeatureDescription = "Башни видят на 25 % ближе, а в топях юниты вязнут и теряют 30 % скорости.",
                FeaturePeriod = 0f,
                FeaturePrimary = 0.75f,
                FeatureSecondary = 0.7f
            }
            };
        }

        #endregion

        [MenuItem("Solutio/Собрать BattleMap")]
        public static void Build()
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
            {
                Debug.LogError("[BattleMapBuilder] URP не назначен. Сначала выполните «Solutio → Настроить URP 2D».");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[BattleMapBuilder] Сборка отменена: текущая сцена не сохранена.");
                return;
            }

            EnsureFolder("Assets/_Project", "Data");
            EnsureFolder("Assets/_Project/Data", "Battle");
            EnsureFolder("Assets/_Project/Prefabs", "Battle");
            EnsureFolder("Assets/_Project/Sprites", "Battle");
            EnsureFolder("Assets/_Project/Sprites/Battle", "Maps");
            EnsureFolder("Assets/_Project/Sprites/Battle", "Props");

            MapSpec[] specs = CreateSpecs();

            Sprite towerUser = ImportPropSprite("Tower_User", TowerPixelsPerUnit, TowerPivotY);
            Sprite towerAi = ImportPropSprite("Tower_Ai", TowerPixelsPerUnit, TowerPivotY);
            Sprite plinthUser = ImportPropSprite("Plinth_User", TowerPixelsPerUnit, PlinthPivotY);
            Sprite plinthAi = ImportPropSprite("Plinth_Ai", TowerPixelsPerUnit, PlinthPivotY);
            Sprite baseUser = ImportPropSprite("Base_User", BasePixelsPerUnit, BasePivotY);
            Sprite baseAi = ImportPropSprite("Base_Ai", BasePixelsPerUnit, BasePivotY);
            if (towerUser == null || towerAi == null || plinthUser == null || plinthAi == null || baseUser == null || baseAi == null)
            {
                return;
            }

            MapProfile[] profiles = new MapProfile[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                Sprite background = ImportMapSprite(specs[i].File);
                if (background == null)
                {
                    return;
                }

                profiles[i] = CreateProfile(specs[i], background);
            }

            MapProfileLibrary library = LoadOrCreate<MapProfileLibrary>(LibraryPath);
            library.EditorSetProfiles(profiles);
            EditorUtility.SetDirty(library);

            VolumeProfile volumeProfile = CreateVolumeProfile();
            Light2D accentPrefab = CreateAccentPrefab();
            BattleStructureView towerPrefab = CreateStructurePrefab("Tower", towerUser, plinthUser, TowerGlowLocal, 1.4f);
            BattleStructureView basePrefab = CreateStructurePrefab("Base", baseUser, null, BaseGlowLocal, 2.6f);

            AssetDatabase.SaveAssets();

            CreateScene(library, profiles[0], volumeProfile, towerPrefab, basePrefab, accentPrefab, towerUser, towerAi, plinthUser, plinthAi, baseUser, baseAi);
            AddSceneToBuildSettings();

            Debug.Log($"[BattleMapBuilder] Готово: {ScenePath}, профилей карт {profiles.Length}, префабы в {PrefabsFolder}. Сцена добавлена в Build Settings.");
        }

        #region Assets

        private static Sprite ImportMapSprite(string fileName)
        {
            string path = $"{MapsFolder}/{fileName}.jpg";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[BattleMapBuilder] Не найден арт карты {path}. Положите JPG в {MapsFolder}.");
                return null;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int width = texture != null ? texture.width : 3072;
            float pixelsPerUnit = width / MapWorldWidth;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.npotScale = TextureImporterNPOTScale.None;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite ImportPropSprite(string fileName, float pixelsPerUnit, float pivotY)
        {
            string path = $"{PropsFolder}/{fileName}.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[BattleMapBuilder] Не найден арт постройки {path}. Положите PNG в {PropsFolder}.");
                return null;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(0.5f, pivotY);
            settings.spriteMeshType = SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static MapProfile CreateProfile(MapSpec spec, Sprite background)
        {
            MapProfile profile = LoadOrCreate<MapProfile>($"{DataFolder}/MapProfile_{spec.Key}.asset");
            profile.EditorConfigure(
                spec.Key,
                background,
                spec.GlobalColor,
                spec.GlobalIntensity,
                spec.StructureScale,
                spec.PlinthScaleY,
                spec.Bloom,
                spec.BloomThreshold,
                spec.BloomTint,
                spec.UserBase,
                spec.AiBase,
                spec.Towers,
                spec.Lanes,
                spec.Zones,
                spec.Accents,
                spec.Feature,
                spec.FeatureName,
                spec.FeatureDescription,
                spec.FeaturePeriod,
                spec.FeaturePrimary,
                spec.FeatureSecondary);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static VolumeProfile CreateVolumeProfile()
        {
            VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(VolumeProfilePath);
            }

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.1f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);
            bloom.name = "Bloom";
            AssetDatabase.AddObjectToAsset(bloom, profile);

            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            vignette.name = "Vignette";
            AssetDatabase.AddObjectToAsset(vignette, profile);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static Light2D CreateAccentPrefab()
        {
            GameObject root = new GameObject("AccentLight");
            Light2D light = root.AddComponent<Light2D>();
            ConfigureLight(light, Light2D.LightType.Point, new Color(0.72f, 0.32f, 1f), 1.5f, 1f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/AccentLight.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<Light2D>();
        }

        private static BattleStructureView CreateStructurePrefab(string prefabName, Sprite sprite, Sprite plinthSprite, Vector3 glowLocal, float radius)
        {
            GameObject root = new GameObject(prefabName);
            BattleStructureView view = root.AddComponent<BattleStructureView>();

            GameObject bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(root.transform, false);
            SpriteRenderer body = bodyObject.AddComponent<SpriteRenderer>();
            body.sprite = sprite;

            SpriteRenderer plinth = null;
            if (plinthSprite != null)
            {
                GameObject plinthObject = new GameObject("Plinth");
                plinthObject.transform.SetParent(root.transform, false);
                plinth = plinthObject.AddComponent<SpriteRenderer>();
                plinth.sprite = plinthSprite;
                plinth.enabled = false;
            }

            GameObject glowObject = new GameObject("Glow");
            glowObject.transform.SetParent(root.transform, false);
            glowObject.transform.localPosition = glowLocal;
            Light2D glow = glowObject.AddComponent<Light2D>();
            ConfigureLight(glow, Light2D.LightType.Point, Color.white, radius, 1.4f);

            view.EditorAssign(body, plinth, glow);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabsFolder}/{prefabName}.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<BattleStructureView>();
        }

        /// <summary>
        /// Настройка Light2D: тип, цвет, радиус и — обязательно — все слои сортировки.
        /// Без слоёв свет, добавленный из кода, ничего не освещает.
        /// </summary>
        private static void ConfigureLight(Light2D light, Light2D.LightType type, Color color, float radius, float intensity)
        {
            light.lightType = type;
            light.color = color;
            light.intensity = intensity;

            if (type == Light2D.LightType.Point)
            {
                light.pointLightOuterRadius = radius;
                light.pointLightInnerRadius = radius * 0.15f;
                light.falloffIntensity = 0.6f;
            }

            SerializedObject serialized = new SerializedObject(light);
            SerializedProperty layers = serialized.FindProperty("m_ApplyToSortingLayers");
            if (layers == null)
            {
                Debug.LogWarning("[BattleMapBuilder] У Light2D не найдено поле m_ApplyToSortingLayers — проверьте Target Sorting Layers вручную.");
                return;
            }

            SortingLayer[] sortingLayers = SortingLayer.layers;
            layers.arraySize = sortingLayers.Length;
            for (int i = 0; i < sortingLayers.Length; i++)
            {
                layers.GetArrayElementAtIndex(i).intValue = sortingLayers[i].id;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        #endregion

        #region Scene

        private static void CreateScene(
            MapProfileLibrary library,
            MapProfile firstProfile,
            VolumeProfile volumeProfile,
            BattleStructureView towerPrefab,
            BattleStructureView basePrefab,
            Light2D accentPrefab,
            Sprite towerUser,
            Sprite towerAi,
            Sprite plinthUser,
            Sprite plinthAi,
            Sprite baseUser,
            Sprite baseAi)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Камера URP с постобработкой и управлением.
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.32f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;
            cameraObject.AddComponent<AudioListener>();
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.None;
            BattleCameraController cameraController = cameraObject.AddComponent<BattleCameraController>();

            // Глобальный свет карты (цвет и яркость задаёт профиль).
            GameObject globalObject = new GameObject("Global Light 2D");
            Light2D globalLight = globalObject.AddComponent<Light2D>();
            ConfigureLight(globalLight, Light2D.LightType.Global, firstProfile.GlobalLightColor, 0f, firstProfile.GlobalLightIntensity);

            // Постобработка.
            GameObject volumeObject = new GameObject("Post Volume");
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = volumeProfile;

            // Фон карты.
            GameObject backgroundObject = new GameObject("Background");
            SpriteRenderer background = backgroundObject.AddComponent<SpriteRenderer>();
            background.sprite = firstProfile.Background;
            background.sortingOrder = short.MinValue + 1;

            Transform structures = new GameObject("Structures").transform;
            Transform lights = new GameObject("MapLights").transform;

            GameObject controllerObject = new GameObject("BattleController");
            BattleLightAnimator animator = controllerObject.AddComponent<BattleLightAnimator>();
            BattleSceneController controller = controllerObject.AddComponent<BattleSceneController>();

            TMP_Text title;
            TMP_Text feature;
            CreateHud(out title, out feature);

            controller.EditorAssign(
                library,
                background,
                globalLight,
                volume,
                cameraController,
                animator,
                structures,
                lights,
                towerPrefab,
                basePrefab,
                accentPrefab,
                towerUser,
                towerAi,
                plinthUser,
                plinthAi,
                baseUser,
                baseAi,
                title,
                feature);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateHud(out TMP_Text title, out TMP_Text feature)
        {
            GameObject canvasObject = new GameObject("BattleHud");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject shade = new GameObject("TopShade", typeof(RectTransform));
            shade.transform.SetParent(canvasObject.transform, false);
            Image shadeImage = shade.AddComponent<Image>();
            shadeImage.color = new Color(0f, 0f, 0f, 0.5f);
            shadeImage.raycastTarget = false;
            SetRect(shade.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 128f));

            TMP_FontAsset uiFont = TMP_Settings.defaultFontAsset;
            title = CreateText(canvasObject.transform, "Txt_MapTitle", uiFont, 46f, HudBright, TextAlignmentOptions.Top,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(1200f, 60f), "Калинов мост");
            feature = CreateText(canvasObject.transform, "Txt_Feature", uiFont, 24f, HudLight, TextAlignmentOptions.Top,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(1400f, 44f), "Особенность карты");
            CreateText(canvasObject.transform, "Txt_Hint", uiFont, 20f, HudLight, TextAlignmentOptions.Bottom,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(1400f, 34f),
                "Колесо — масштаб   ·   ПКМ / СКМ — перетащить   ·   WASD — обзор   ·   Пробел — вся карта   ·   1–5 — смена карты (тест)");

            TMP_FontAsset fpsFont = FindFont("LiberationSans SDF") ?? uiFont;
            TMP_Text fps = CreateText(canvasObject.transform, "Txt_Fps", fpsFont, 22f, HudBright, TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(360f, 70f), "FPS");
            fps.rectTransform.pivot = new Vector2(0f, 0f);
            fps.rectTransform.anchoredPosition = new Vector2(30f, 30f);

            FpsCounterOverlay overlay = fps.gameObject.AddComponent<FpsCounterOverlay>();
            SerializedObject serialized = new SerializedObject(overlay);
            SerializedProperty textProperty = serialized.FindProperty("fpsText");
            if (textProperty != null)
            {
                textProperty.objectReferenceValue = fps;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[BattleMapBuilder] У FpsCounterOverlay не найдено поле fpsText — назначьте Txt_Fps вручную.");
            }
        }

        private static TMP_Text CreateText(
            Transform parent,
            string objectName,
            TMP_FontAsset font,
            float size,
            Color color,
            TextAlignmentOptions alignment,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 position,
            Vector2 sizeDelta,
            string text)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = text;
            SetRect(label.rectTransform, anchor, anchor, pivot, position, sizeDelta);
            return label;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;
        }

        private static TMP_FontAsset FindFont(string assetName)
        {
            string[] guids = AssetDatabase.FindAssets($"{assetName} t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (font != null && font.name == assetName)
                {
                    return font;
                }
            }

            return null;
        }

        private static void AddSceneToBuildSettings()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        #endregion

        #region Utility

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        #endregion
    }
}
