using System.IO;
using Solutio.Draft;
using Solutio.Equipment;
using Solutio.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Solutio.EditorTools
{
    /// <summary>
    /// Сборщик экрана предматчевой подготовки (День 13). Пункт меню «Solutio → Собрать EquipMenu»:
    /// 1) генерирует процедурные глифы предметов (меч — атака, щит — броня, сердце — здоровье),
    ///    остальные спрайты переиспользует из Sprites/UI/Draft (единый стиль с драфтом);
    /// 2) строит иерархию EquipMenu в открытой сцене с ручной раскладкой без Layout Group
    ///    (нет пересчёта разметки во время игры — важно для 360+ FPS);
    /// 3) связывает поля компонентов через SerializedObject и сохраняет префаб EquipMenu.prefab;
    /// 4) создаёт (или находит) в корне сцены объект EquipmentController и назначает ему ссылки
    ///    на экземпляр EquipMenu в сцене (не на префаб-ассет).
    /// Учтён техдолг DraftCanvasBuilder (№ 11, 16): у префаба нет ссылок на объекты сцены —
    /// все они лежат на EquipmentController, обычном объекте сцены, поэтому сохраняются без override-ов;
    /// поиск объектов сцены — через корневые объекты, без устаревшего FindFirstObjectByType.
    /// </summary>
    public static class EquipCanvasBuilder
    {
        private const string DraftSpriteFolder = "Assets/_Project/Sprites/UI/Draft";
        private const string EquipSpriteFolder = "Assets/_Project/Sprites/UI/Equip";
        private const string PrefabFolder = "Assets/_Project/Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/EquipMenu.prefab";
        private const string FallbackFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string VideoTexturePath = "Assets/_Project/Materials/RT_VideoBg.renderTexture";
        private const string CanvasName = "EquipMenu";
        private const string ControllerName = "EquipmentController";
        private const int SortingOrder = 25;

        private const int HeroCount = 5;
        private const int StashCellCount = 10;
        private const int AiHeroCount = 5;

        private const float HeroPanelWidth = 270f;
        private const float HeroPanelHeight = 490f;
        private const float HeroPanelStep = 290f;
        private const float HeroAreaLeft = 60f;
        private const float HeroAreaTop = -168f;

        private const float SlotWidth = 56f;
        private const float SlotHeight = 70f;
        private const float SlotStep = 62f;

        private const float StashCellWidth = 100f;
        private const float StashCellHeight = 112f;
        private const float StashCellStep = 112f;

        private static readonly Color32 AiAccent = new Color32(0xE0, 0x45, 0x50, 0xFF);

        private static TMP_FontAsset _font;
        private static Sprite _glowSprite;
        private static Sprite _panelSprite;
        private static Sprite _outlineSprite;
        private static Sprite _silhouetteSprite;
        private static Sprite _radialSprite;
        private static Sprite _gradientSprite;
        private static Sprite _lineSprite;
        private static Sprite _lineFadeRightSprite;
        private static Sprite _vignetteSprite;
        private static Sprite _swordSprite;
        private static Sprite _shieldSprite;
        private static Sprite _heartSprite;
        private static int _uiLayer;

        [MenuItem("Solutio/Собрать EquipMenu")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Solutio", "Остановите режим Play — изменения в Play не сохраняются.", "OK");
                return;
            }

            GameObject existing = FindRootByName(CanvasName);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "EquipMenu уже есть в сцене",
                    "Пересобрать EquipMenu? Старый объект в сцене будет удалён, префаб перезаписан. Ссылки EquipmentController будут назначены заново.",
                    "Пересобрать",
                    "Отмена");

                if (!replace)
                {
                    return;
                }

                Object.DestroyImmediate(existing);
            }

            if (!PrepareAssets())
            {
                return;
            }

            _uiLayer = LayerMask.NameToLayer("UI");

            GameObject canvasObject = BuildCanvas();

            Directory.CreateDirectory(PrefabFolder);
            // ВАЖНО: SaveAsPrefabAssetAndConnect возвращает корень ПРЕФАБ-АССЕТА (файла), а экземпляром
            // в сцене становится сам canvasObject. Ссылки на сцену назначаются только экземпляру —
            // именно из-за этой путаницы DraftCanvasBuilder «терял» ссылки (техдолг № 11).
            PrefabUtility.SaveAsPrefabAssetAndConnect(canvasObject, PrefabPath, InteractionMode.UserAction);
            EquipViewController instanceView = canvasObject.GetComponent<EquipViewController>();

            WireController(instanceView, out bool draftControllerFound, out bool draftViewFound, out bool mainMenuFound);

            EditorSceneManager.MarkSceneDirty(canvasObject.scene);
            Selection.activeGameObject = canvasObject;

            Debug.Log($"<color=#307082>[EquipCanvasBuilder]</color> EquipMenu собран и сохранён: {PrefabPath}. " +
                      $"DraftController: {Found(draftControllerFound)}, DraftCanvas: {Found(draftViewFound)}, MainMenuController: {Found(mainMenuFound)}. " +
                      "Сохраните сцену (Ctrl+S).");
        }

        #region Scene Wiring

        private static void WireController(EquipViewController view, out bool draftControllerFound, out bool draftViewFound, out bool mainMenuFound)
        {
            DraftController draftController = FindInScene<DraftController>();
            DraftViewController draftView = FindInScene<DraftViewController>();
            MainMenuController mainMenu = FindInScene<MainMenuController>();

            draftControllerFound = draftController != null;
            draftViewFound = draftView != null;
            mainMenuFound = mainMenu != null;

            EquipmentController controller = FindInScene<EquipmentController>();

            if (controller == null)
            {
                GameObject controllerObject = new GameObject(ControllerName);
                SceneManager.MoveGameObjectToScene(controllerObject, SceneManager.GetActiveScene());
                controller = controllerObject.AddComponent<EquipmentController>();
                Undo.RegisterCreatedObjectUndo(controllerObject, "Create EquipmentController");
            }

            SerializedObject so = new SerializedObject(controller);
            SetRef(so, "draftController", draftController);
            SetRef(so, "draftView", draftView);
            SetRef(so, "view", view);
            SetRef(so, "mainMenu", mainMenu);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        private static T FindInScene<T>() where T : Component
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                T component = roots[i].GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static GameObject FindRootByName(string objectName)
        {
            GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == objectName)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static string Found(bool value)
        {
            return value ? "найден" : "НЕ найден";
        }

        #endregion

        #region Assets

        private static bool PrepareAssets()
        {
            // Шрифт интерфейса задаётся в TMP Settings (Solutio → Применить шрифты); LiberationSans — запасной.
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null)
            {
                _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
            }

            if (_font == null)
            {
                EditorUtility.DisplayDialog("Шрифт не найден", "Не найден шрифт TMP по умолчанию. Запустите «Solutio → Применить шрифты».", "OK");
                return false;
            }

            _glowSprite = LoadDraftSprite("Draft_Glow.png");
            _panelSprite = LoadDraftSprite("Draft_Panel.png");
            _outlineSprite = LoadDraftSprite("Draft_Outline.png");
            _silhouetteSprite = LoadDraftSprite("Draft_Silhouette.png");
            _radialSprite = LoadDraftSprite("Draft_Radial.png");
            _gradientSprite = LoadDraftSprite("Draft_GradientV.png");
            _lineSprite = LoadDraftSprite("Draft_Line.png");
            _lineFadeRightSprite = LoadDraftSprite("Draft_LineFadeRight.png");
            _vignetteSprite = LoadDraftSprite("Draft_Vignette.png");

            if (_glowSprite == null || _panelSprite == null || _outlineSprite == null || _silhouetteSprite == null
                || _radialSprite == null || _gradientSprite == null || _lineSprite == null
                || _lineFadeRightSprite == null || _vignetteSprite == null)
            {
                EditorUtility.DisplayDialog("Спрайты драфта не найдены",
                    "Не найдены спрайты в " + DraftSpriteFolder + ". Сначала соберите DraftCanvas (Solutio → Собрать DraftCanvas).", "OK");
                return false;
            }

            Directory.CreateDirectory(EquipSpriteFolder);
            _swordSprite = CreateGlyph("Equip_Sword.png", SwordInside);
            _shieldSprite = CreateGlyph("Equip_Shield.png", ShieldInside);
            _heartSprite = CreateGlyph("Equip_Heart.png", HeartInside);

            return _swordSprite != null && _shieldSprite != null && _heartSprite != null;
        }

        private static Sprite LoadDraftSprite(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(DraftSpriteFolder + "/" + fileName);
        }

        private delegate bool InsideTest(float x, float y);

        /// <summary>
        /// Белый глиф 128×128 с антиалиасингом (суперсэмплинг 4×4). Координаты фигуры — от −64 до 64, Y вверх.
        /// </summary>
        private static Sprite CreateGlyph(string fileName, InsideTest inside)
        {
            const int size = 128;
            const int samples = 4;
            string assetPath = EquipSpriteFolder + "/" + fileName;

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int hits = 0;

                    for (int sy = 0; sy < samples; sy++)
                    {
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float x = px + (sx + 0.5f) / samples - half;
                            float y = py + (sy + 0.5f) / samples - half;

                            if (inside(x, y))
                            {
                                hits++;
                            }
                        }
                    }

                    byte alpha = (byte)Mathf.RoundToInt(255f * hits / (samples * samples));
                    pixels[py * size + px] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.GetFullPath(assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>Меч под 45°: клинок с острием, гарда, рукоять и навершие.</summary>
        private static bool SwordInside(float x, float y)
        {
            // Поворот на −45°: клинок смотрит в правый верхний угол; масштаб 1.25 — меч на всю ячейку.
            const float c = 0.70710678f * 0.8f;
            float u = x * c - y * c;
            float v = x * c + y * c;

            bool blade = Mathf.Abs(u) <= 8f && v >= -16f && v <= 34f;
            bool tip = v > 34f && v <= 54f && Mathf.Abs(u) <= 8f * (54f - v) / 20f;
            bool fuller = Mathf.Abs(u) <= 1.6f && v >= -10f && v <= 30f;
            bool guard = Mathf.Abs(u) <= 26f && v >= -24f && v <= -16f;
            bool grip = Mathf.Abs(u) <= 5f && v >= -46f && v < -24f;
            bool pommel = u * u + (v + 52f) * (v + 52f) <= 64f;

            return ((blade && !fuller) || tip || guard || grip || pommel);
        }

        /// <summary>Геральдический щит с вертикальной полосой-рёбром.</summary>
        private static bool ShieldInside(float x, float y)
        {
            float ax = Mathf.Abs(x);
            bool top = y <= 50f && y >= -6f && ax <= 46f;
            bool topNotch = y > 42f && ax < 14f && y > 50f - (14f - ax);
            bool bottom = y < -6f && y >= -58f && ax <= 46f * Mathf.Sqrt(Mathf.Clamp01((y + 58f) / 52f));
            bool rib = ax <= 2.5f && y > -48f && y < 44f;
            bool band = Mathf.Abs(y - 14f) <= 2.5f && ax < 40f;

            return (top || bottom) && !topNotch && !rib && !band;
        }

        /// <summary>Сердце: (x² + y² − 1)³ − x²·y³ ≤ 0 в нормированных координатах.</summary>
        private static bool HeartInside(float x, float y)
        {
            float nx = x / 46f;
            float ny = (y + 6f) / 46f;
            float a = nx * nx + ny * ny - 1f;
            return a * a * a - nx * nx * ny * ny * ny <= 0f;
        }

        #endregion

        #region Canvas

        private static GameObject BuildCanvas()
        {
            GameObject root = new GameObject(CanvasName, typeof(RectTransform));
            root.layer = _uiLayer;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();
            CanvasGroup rootGroup = root.AddComponent<CanvasGroup>();
            rootGroup.alpha = 0f;
            rootGroup.interactable = false;
            rootGroup.blocksRaycasts = false;

            EquipViewController view = root.AddComponent<EquipViewController>();
            RectTransform rootRect = (RectTransform)root.transform;

            BuildBackground(rootRect);
            BuildHeader(rootRect, out TMP_Text matchInfoText, out TMP_Text progressText);

            EquipHeroPanel[] heroPanels = BuildHeroArea(rootRect);
            EquipSlotView[] stashSlots = BuildStash(rootRect);
            BuildAiPanel(rootRect, out Image[] aiPortraits, out TMP_Text[] aiNames, out TMP_Text aiStatusText);

            BuildBottom(rootRect, out TMP_Text footerHintText, out Button battleButton, out TMP_Text battleButtonText,
                out Button resetButton, out Button leaveButton);

            // Верхний слой: подсказка и призрак перетаскивания (последний — поверх всего)
            RectTransform overlay = CreateRect("Overlay", rootRect);
            Stretch(overlay, 0f, 0f, 0f, 0f);
            EquipTooltip tooltip = BuildTooltip(overlay);
            EquipDragGhost dragGhost = BuildDragGhost(overlay);

            SerializedObject so = new SerializedObject(view);
            SetRef(so, "rootCanvasGroup", rootGroup);
            SetRef(so, "matchInfoText", matchInfoText);
            SetRef(so, "progressText", progressText);
            SetRef(so, "footerHintText", footerHintText);
            SetArray(so, "heroPanels", heroPanels);
            SetArray(so, "stashSlots", stashSlots);
            SetRef(so, "dragGhost", dragGhost);
            SetRef(so, "tooltip", tooltip);
            SetArray(so, "aiPortraits", aiPortraits);
            SetArray(so, "aiNames", aiNames);
            SetRef(so, "aiStatusText", aiStatusText);
            SetRef(so, "battleButton", battleButton);
            SetRef(so, "resetButton", resetButton);
            SetRef(so, "leaveButton", leaveButton);
            SetRef(so, "battleButtonText", battleButtonText);
            SetRef(so, "placeholderPortrait", _silhouetteSprite);
            SetRef(so, "attackGlyph", _swordSprite);
            SetRef(so, "armorGlyph", _shieldSprite);
            SetRef(so, "hpGlyph", _heartSprite);
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static void BuildBackground(RectTransform rootRect)
        {
            RenderTexture videoTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(VideoTexturePath);
            if (videoTexture != null)
            {
                RectTransform videoRect = CreateRect("Bg_Video", rootRect);
                Stretch(videoRect, 0f, 0f, 0f, 0f);
                RawImage video = videoRect.gameObject.AddComponent<RawImage>();
                video.texture = videoTexture;
                video.raycastTarget = false;
            }

            // Затемнение перехватывает клики, чтобы они не уходили в канвасы ниже
            RectTransform dimRect = CreateRect("Bg_Dim", rootRect);
            Stretch(dimRect, 0f, 0f, 0f, 0f);
            Image dim = AddImage(dimRect, null, WithAlpha(DraftPalette.Obsidian, 0.7f));
            dim.raycastTarget = true;

            RectTransform vignetteRect = CreateRect("Bg_Vignette", rootRect);
            Stretch(vignetteRect, 0f, 0f, 0f, 0f);
            AddImage(vignetteRect, _vignetteSprite, Color.black);

            RectTransform topShade = CreateRect("Bg_TopShade", rootRect);
            Place(topShade, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -150f), new Vector2(0f, 300f), new Vector2(0.5f, 0.5f));
            topShade.localRotation = Quaternion.Euler(0f, 0f, 180f);
            AddImage(topShade, _gradientSprite, WithAlpha(DraftPalette.Obsidian, 0.9f));

            RectTransform bottomShade = CreateRect("Bg_BottomShade", rootRect);
            Place(bottomShade, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 220f), new Vector2(0.5f, 0f));
            AddImage(bottomShade, _gradientSprite, WithAlpha(DraftPalette.Obsidian, 0.95f));
        }

        private static void BuildHeader(RectTransform parent, out TMP_Text matchInfoText, out TMP_Text progressText)
        {
            RectTransform header = CreateRect("Header", parent);
            Place(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(900f, 140f));

            RectTransform glowRect = CreateRect("TitleGlow", header);
            Place(glowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 10f), new Vector2(760f, 110f));
            AddImage(glowRect, _radialSprite, WithAlpha(DraftPalette.SapphireGlow, 0.22f));

            TMP_Text title = CreateText("Txt_Title", header, "ПОДГОТОВКА К БИТВЕ", 40f, DraftPalette.SapphireGlow, TextAlignmentOptions.Center, true);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(900f, 54f));
            title.characterSpacing = 5f;

            RectTransform ornament = CreateRect("Ornament_Line", header);
            Place(ornament, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(520f, 3f));
            AddImage(ornament, _lineSprite, WithAlpha(DraftPalette.SapphireGlow, 0.8f));

            RectTransform diamond = CreateRect("Ornament_Diamond", header);
            Place(diamond, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(10f, 10f));
            diamond.pivot = new Vector2(0.5f, 0.5f);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            AddImage(diamond, null, DraftPalette.SapphireGlow);

            matchInfoText = CreateText("Txt_MatchInfo", header, "Страж  ·  Руины Собора", 20f, DraftPalette.TextLight, TextAlignmentOptions.Center, false);
            Place(matchInfoText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -74f), new Vector2(900f, 30f));

            progressText = CreateText("Txt_Progress", header, "Распределено 0 / 10", 18f, DraftPalette.Muted, TextAlignmentOptions.Center, false);
            Place(progressText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(900f, 28f));
        }

        #endregion

        #region Heroes

        private static EquipHeroPanel[] BuildHeroArea(RectTransform parent)
        {
            Vector2 topLeft = new Vector2(0f, 1f);

            TMP_Text teamTitle = CreateText("Txt_UserTeam", parent, "ВАША КОМАНДА", 24f, DraftPalette.SapphireGlow, TextAlignmentOptions.MidlineLeft, true);
            Place(teamTitle.rectTransform, topLeft, topLeft, new Vector2(HeroAreaLeft, -122f), new Vector2(500f, 34f));
            teamTitle.characterSpacing = 4f;

            RectTransform underline = CreateRect("Underline_UserTeam", parent);
            Place(underline, topLeft, topLeft, new Vector2(HeroAreaLeft, -156f), new Vector2(520f, 3f));
            AddImage(underline, _lineFadeRightSprite, WithAlpha(DraftPalette.SapphireGlow, 0.9f));

            RectTransform area = CreateRect("HeroPanels", parent);
            Place(area, topLeft, topLeft, new Vector2(HeroAreaLeft, HeroAreaTop), new Vector2(HeroPanelStep * (HeroCount - 1) + HeroPanelWidth, HeroPanelHeight));

            EquipHeroPanel[] panels = new EquipHeroPanel[HeroCount];

            for (int i = 0; i < panels.Length; i++)
            {
                RectTransform panelRect = CreateRect($"HeroPanel_{i + 1}", area);
                Place(panelRect, topLeft, topLeft, new Vector2(i * HeroPanelStep, 0f), new Vector2(HeroPanelWidth, HeroPanelHeight));
                panels[i] = BuildHeroPanel(panelRect);
            }

            return panels;
        }

        private static EquipHeroPanel BuildHeroPanel(RectTransform root)
        {
            Vector2 top = new Vector2(0.5f, 1f);
            Vector2 topLeft = new Vector2(0f, 1f);
            Vector2 topRight = new Vector2(1f, 1f);

            RectTransform content = CreateRect("Content", root);
            Stretch(content, 0f, 0f, 0f, 0f);

            RectTransform glowRect = CreateRect("SelectionGlow", content);
            Stretch(glowRect, -34f, -34f, -34f, -34f);
            Image selectionGlow = AddImage(glowRect, _glowSprite, WithAlpha(DraftPalette.SapphireGlow, 0f));
            selectionGlow.type = Image.Type.Sliced;

            RectTransform bodyRect = CreateRect("Body", content);
            Stretch(bodyRect, 0f, 0f, 0f, 0f);
            Image body = AddImage(bodyRect, _panelSprite, DraftPalette.Panel);
            body.type = Image.Type.Sliced;
            body.raycastTarget = true;

            RectTransform clip = CreateRect("PortraitClip", content);
            Place(clip, top, top, new Vector2(0f, -4f), new Vector2(HeroPanelWidth - 8f, 196f));
            clip.gameObject.AddComponent<RectMask2D>();

            RectTransform haloRect = CreateRect("Halo", clip);
            Place(haloRect, top, top, new Vector2(0f, 20f), new Vector2(300f, 240f));
            Image halo = AddImage(haloRect, _radialSprite, WithAlpha(DraftPalette.RoleTank, 0.4f));

            RectTransform portraitRect = CreateRect("Portrait", clip);
            Stretch(portraitRect, 20f, 20f, 10f, 0f);
            Image portrait = AddImage(portraitRect, _silhouetteSprite, DraftPalette.RoleTank);
            portrait.preserveAspect = true;

            RectTransform shadeRect = CreateRect("PortraitShade", clip);
            Place(shadeRect, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 70f), new Vector2(0.5f, 0f));
            AddImage(shadeRect, _gradientSprite, WithAlpha(DraftPalette.Panel, 1f));

            RectTransform stripeRect = CreateRect("RoleStripe", content);
            Place(stripeRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), new Vector2(-28f, 3f), new Vector2(0.5f, 1f));
            Image roleStripe = AddImage(stripeRect, _lineSprite, DraftPalette.RoleTank);

            TMP_Text nameText = CreateText("Txt_Name", content, "Hero", 24f, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            Place(nameText.rectTransform, top, top, new Vector2(0f, -204f), new Vector2(HeroPanelWidth - 20f, 34f));
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 14f;
            nameText.fontSizeMax = 24f;

            TMP_Text roleText = CreateText("Txt_Role", content, "Танк", 18f, DraftPalette.RoleTank, TextAlignmentOptions.Center, true);
            Place(roleText.rectTransform, top, top, new Vector2(0f, -238f), new Vector2(HeroPanelWidth - 20f, 26f));
            roleText.characterSpacing = 3f;

            RectTransform divider = CreateRect("Divider", content);
            Place(divider, top, top, new Vector2(0f, -270f), new Vector2(HeroPanelWidth - 40f, 2f));
            AddImage(divider, _lineSprite, WithAlpha(DraftPalette.FrameIdle, 1f));

            TMP_Text attackText = CreateStatText("Txt_Attack", content, -280f);
            TMP_Text armorText = CreateStatText("Txt_Armor", content, -310f);
            TMP_Text hpText = CreateStatText("Txt_Hp", content, -340f);

            TMP_Text slotsLabel = CreateText("Txt_SlotsLabel", content, "СЛОТЫ", 14f, DraftPalette.Muted, TextAlignmentOptions.MidlineLeft, true);
            Place(slotsLabel.rectTransform, topLeft, topLeft, new Vector2(18f, -378f), new Vector2(120f, 24f));
            slotsLabel.characterSpacing = 4f;

            TMP_Text capacityText = CreateText("Txt_Capacity", content, "0 / 4", 16f, DraftPalette.TextLight, TextAlignmentOptions.MidlineRight, true);
            Place(capacityText.rectTransform, topRight, topRight, new Vector2(-18f, -378f), new Vector2(120f, 24f));

            RectTransform slotsRow = CreateRect("Slots", content);
            float rowWidth = SlotStep * (EquipmentLoadout.SlotsPerHero - 1) + SlotWidth;
            Place(slotsRow, top, top, new Vector2(0f, -406f), new Vector2(rowWidth, SlotHeight));

            EquipSlotView[] slots = new EquipSlotView[EquipmentLoadout.SlotsPerHero];

            for (int i = 0; i < slots.Length; i++)
            {
                RectTransform slotRect = CreateRect($"Slot_{i + 1}", slotsRow);
                Place(slotRect, topLeft, topLeft, new Vector2(i * SlotStep, 0f), new Vector2(SlotWidth, SlotHeight));
                slots[i] = BuildSlot(slotRect, "+", 22f, 11f);
            }

            RectTransform frameRect = CreateRect("Frame", content);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.FrameIdle);
            frame.type = Image.Type.Sliced;

            EquipHeroPanel panel = root.gameObject.AddComponent<EquipHeroPanel>();
            SerializedObject so = new SerializedObject(panel);
            SetRef(so, "content", content);
            SetRef(so, "selectionGlow", selectionGlow);
            SetRef(so, "frame", frame);
            SetRef(so, "halo", halo);
            SetRef(so, "portrait", portrait);
            SetRef(so, "roleStripe", roleStripe);
            SetRef(so, "nameText", nameText);
            SetRef(so, "roleText", roleText);
            SetRef(so, "attackText", attackText);
            SetRef(so, "armorText", armorText);
            SetRef(so, "hpText", hpText);
            SetRef(so, "capacityText", capacityText);
            SetArray(so, "slots", slots);
            so.ApplyModifiedPropertiesWithoutUndo();

            return panel;
        }

        private static TMP_Text CreateStatText(string objectName, RectTransform parent, float y)
        {
            Vector2 topLeft = new Vector2(0f, 1f);
            TMP_Text text = CreateText(objectName, parent, "АТК  0", 20f, DraftPalette.TextLight, TextAlignmentOptions.MidlineLeft, false);
            Place(text.rectTransform, topLeft, topLeft, new Vector2(22f, y), new Vector2(HeroPanelWidth - 44f, 28f));
            text.richText = true;
            return text;
        }

        #endregion

        #region Slots

        /// <summary>
        /// Ячейка (слот героя или хранилища): HintGlow → Background → Frame → Placeholder → Item.
        /// Пропорции предмета заданы якорями, поэтому одна и та же разметка подходит для разных размеров.
        /// </summary>
        private static EquipSlotView BuildSlot(RectTransform root, string placeholder, float placeholderSize, float bonusMaxSize)
        {
            RectTransform content = CreateRect("Content", root);
            Stretch(content, 0f, 0f, 0f, 0f);

            RectTransform hintRect = CreateRect("HintGlow", content);
            Stretch(hintRect, -18f, -18f, -18f, -18f);
            Image hintGlow = AddImage(hintRect, _glowSprite, WithAlpha(ItemRarityPalette.HintAvailable, 0f));
            hintGlow.type = Image.Type.Sliced;
            hintGlow.pixelsPerUnitMultiplier = 2f;

            RectTransform backgroundRect = CreateRect("Background", content);
            Stretch(backgroundRect, 0f, 0f, 0f, 0f);
            Image background = AddImage(backgroundRect, _panelSprite, ItemRarityPalette.SlotEmpty);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            RectTransform frameRect = CreateRect("Frame", content);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, ItemRarityPalette.SlotFrameIdle);
            frame.type = Image.Type.Sliced;

            TMP_Text placeholderText = CreateText("Txt_Placeholder", content, placeholder, placeholderSize, DraftPalette.Muted, TextAlignmentOptions.Center, false);
            Stretch(placeholderText.rectTransform, 0f, 0f, 0f, 0f);

            RectTransform itemRect = CreateRect("Item", content);
            Stretch(itemRect, 0f, 0f, 0f, 0f);
            CanvasGroup itemGroup = itemRect.gameObject.AddComponent<CanvasGroup>();
            itemGroup.alpha = 0f;
            itemGroup.blocksRaycasts = false;
            itemGroup.interactable = false;

            RectTransform bodyRect = CreateRect("Body", itemRect);
            Stretch(bodyRect, 0f, 0f, 0f, 0f);

            // Прозрачная зона попадания: перетаскивание начинается с любой точки ячейки
            Image hit = AddImage(bodyRect, null, new Color(0f, 0f, 0f, 0f));
            hit.raycastTarget = true;

            RectTransform glowRect = CreateRect("Glow", bodyRect);
            SetAnchors(glowRect, new Vector2(0.02f, 0.2f), new Vector2(0.98f, 1.02f));
            Image glow = AddImage(glowRect, _radialSprite, WithAlpha(ItemRarityPalette.CommonGlow, 0.35f));

            RectTransform iconRect = CreateRect("Icon", bodyRect);
            SetAnchors(iconRect, new Vector2(0.16f, 0.3f), new Vector2(0.84f, 0.94f));
            Image icon = AddImage(iconRect, _swordSprite, ItemRarityPalette.CommonGlow);
            icon.preserveAspect = true;

            TMP_Text bonusText = CreateText("Txt_Bonus", bodyRect, "+0", bonusMaxSize, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            SetAnchors(bonusText.rectTransform, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.3f));
            bonusText.enableAutoSizing = true;
            bonusText.fontSizeMin = 8f;
            bonusText.fontSizeMax = bonusMaxSize;

            EquipItemView itemView = itemRect.gameObject.AddComponent<EquipItemView>();
            SerializedObject itemSo = new SerializedObject(itemView);
            SetRef(itemSo, "canvasGroup", itemGroup);
            SetRef(itemSo, "body", bodyRect);
            SetRef(itemSo, "glow", glow);
            SetRef(itemSo, "icon", icon);
            SetRef(itemSo, "bonusText", bonusText);
            itemSo.ApplyModifiedPropertiesWithoutUndo();

            EquipSlotView slot = root.gameObject.AddComponent<EquipSlotView>();
            SerializedObject slotSo = new SerializedObject(slot);
            SetRef(slotSo, "content", content);
            SetRef(slotSo, "background", background);
            SetRef(slotSo, "frame", frame);
            SetRef(slotSo, "hintGlow", hintGlow);
            SetRef(slotSo, "placeholderText", placeholderText);
            SetRef(slotSo, "itemView", itemView);
            slotSo.FindProperty("placeholder").stringValue = placeholder;
            slotSo.ApplyModifiedPropertiesWithoutUndo();

            return slot;
        }

        private static EquipSlotView[] BuildStash(RectTransform parent)
        {
            Vector2 bottomLeft = Vector2.zero;
            Vector2 topLeft = new Vector2(0f, 1f);
            float panelWidth = HeroPanelStep * (HeroCount - 1) + HeroPanelWidth;

            RectTransform panel = CreateRect("StashPanel", parent);
            Place(panel, bottomLeft, bottomLeft, new Vector2(HeroAreaLeft, 196f), new Vector2(panelWidth, 206f), bottomLeft);

            Image background = AddImage(panel, _panelSprite, WithAlpha(DraftPalette.PanelLight, 0.92f));
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;

            RectTransform frameRect = CreateRect("Frame", panel);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.Sapphire);
            frame.type = Image.Type.Sliced;

            TMP_Text title = CreateText("Txt_StashTitle", panel, "ХРАНИЛИЩЕ  ·  СТАРТОВАЯ ЭКИПИРОВКА", 20f, DraftPalette.TextLight, TextAlignmentOptions.MidlineLeft, true);
            Place(title.rectTransform, topLeft, topLeft, new Vector2(24f, -12f), new Vector2(800f, 32f));
            title.characterSpacing = 3f;

            TMP_Text legend = CreateText("Txt_RarityLegend", panel, BuildRarityLegend(), 16f, DraftPalette.Muted, TextAlignmentOptions.MidlineRight, false);
            Place(legend.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -12f), new Vector2(620f, 32f));
            legend.richText = true;

            RectTransform row = CreateRect("StashCells", panel);
            float rowWidth = StashCellStep * (StashCellCount - 1) + StashCellWidth;
            Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(rowWidth, StashCellHeight), new Vector2(0.5f, 0f));

            EquipSlotView[] cells = new EquipSlotView[StashCellCount];

            for (int i = 0; i < cells.Length; i++)
            {
                RectTransform cellRect = CreateRect($"StashCell_{i + 1}", row);
                Place(cellRect, topLeft, topLeft, new Vector2(i * StashCellStep, 0f), new Vector2(StashCellWidth, StashCellHeight));
                cells[i] = BuildSlot(cellRect, "·", 30f, 17f);
            }

            return cells;
        }

        private static string BuildRarityLegend()
        {
            return $"<color=#{ItemRarityPalette.ToHex(ItemRarityPalette.Common)}>• Обычный</color>   " +
                   $"<color=#{ItemRarityPalette.ToHex(ItemRarityPalette.Rare)}>• Редкий</color>   " +
                   $"<color=#{ItemRarityPalette.ToHex(ItemRarityPalette.Epic)}>• Эпический</color>   " +
                   $"<color=#{ItemRarityPalette.ToHex(ItemRarityPalette.Legendary)}>• Легендарный</color>";
        }

        #endregion

        #region AI Panel

        private static void BuildAiPanel(RectTransform parent, out Image[] portraits, out TMP_Text[] names, out TMP_Text statusText)
        {
            Vector2 topRight = new Vector2(1f, 1f);
            Vector2 topLeft = new Vector2(0f, 1f);
            const float panelWidth = 330f;
            const float panelHeight = 696f;

            TMP_Text teamTitle = CreateText("Txt_AiTeam", parent, "КОМАНДА ИИ", 24f, AiAccent, TextAlignmentOptions.MidlineRight, true);
            Place(teamTitle.rectTransform, topRight, topRight, new Vector2(-60f, -122f), new Vector2(panelWidth, 34f));
            teamTitle.characterSpacing = 4f;

            // Линия затухает влево: зеркалим спрайт относительно центра
            RectTransform underline = CreateRect("Underline_AiTeam", parent);
            Place(underline, topRight, topRight, new Vector2(-60f - panelWidth * 0.5f, -157.5f), new Vector2(panelWidth, 3f), new Vector2(0.5f, 0.5f));
            underline.localScale = new Vector3(-1f, 1f, 1f);
            AddImage(underline, _lineFadeRightSprite, WithAlpha(AiAccent, 0.9f));

            RectTransform panel = CreateRect("AiPanel", parent);
            Place(panel, topRight, topRight, new Vector2(-60f, HeroAreaTop), new Vector2(panelWidth, panelHeight));

            Image background = AddImage(panel, _panelSprite, WithAlpha(DraftPalette.Panel, 0.92f));
            background.type = Image.Type.Sliced;

            RectTransform frameRect = CreateRect("Frame", panel);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.Crimson);
            frame.type = Image.Type.Sliced;

            portraits = new Image[AiHeroCount];
            names = new TMP_Text[AiHeroCount];
            const float rowStep = 112f;

            for (int i = 0; i < AiHeroCount; i++)
            {
                RectTransform row = CreateRect($"AiHero_{i + 1}", panel);
                Place(row, topLeft, topLeft, new Vector2(16f, -16f - i * rowStep), new Vector2(panelWidth - 32f, 100f));

                RectTransform holder = CreateRect("PortraitHolder", row);
                Place(holder, topLeft, topLeft, Vector2.zero, new Vector2(84f, 100f));

                Image holderBackground = AddImage(holder, _panelSprite, DraftPalette.PanelLight);
                holderBackground.type = Image.Type.Sliced;

                RectTransform portraitRect = CreateRect("Portrait", holder);
                Stretch(portraitRect, 6f, 6f, 6f, 6f);
                portraits[i] = AddImage(portraitRect, _silhouetteSprite, DraftPalette.RoleCarry);
                portraits[i].preserveAspect = true;

                RectTransform holderFrame = CreateRect("Frame", holder);
                Stretch(holderFrame, 0f, 0f, 0f, 0f);
                Image holderFrameImage = AddImage(holderFrame, _outlineSprite, DraftPalette.Crimson);
                holderFrameImage.type = Image.Type.Sliced;

                names[i] = CreateText("Txt_Name", row, "Hero", 18f, DraftPalette.TextBright, TextAlignmentOptions.MidlineLeft, true);
                Place(names[i].rectTransform, topLeft, topLeft, new Vector2(98f, -6f), new Vector2(200f, 28f));
                names[i].enableAutoSizing = true;
                names[i].fontSizeMin = 12f;
                names[i].fontSizeMax = 18f;

                // Четыре «закрытых» слота: экипировка ИИ скрыта до начала боя
                for (int s = 0; s < EquipmentLoadout.SlotsPerHero; s++)
                {
                    RectTransform hidden = CreateRect($"HiddenSlot_{s + 1}", row);
                    Place(hidden, topLeft, topLeft, new Vector2(98f + s * 48f, -44f), new Vector2(42f, 48f));

                    Image hiddenBackground = AddImage(hidden, _panelSprite, ItemRarityPalette.SlotEmpty);
                    hiddenBackground.type = Image.Type.Sliced;

                    RectTransform hiddenFrame = CreateRect("Frame", hidden);
                    Stretch(hiddenFrame, 0f, 0f, 0f, 0f);
                    Image hiddenFrameImage = AddImage(hiddenFrame, _outlineSprite, WithAlpha(DraftPalette.Crimson, 0.6f));
                    hiddenFrameImage.type = Image.Type.Sliced;

                    TMP_Text mark = CreateText("Txt_Hidden", hidden, "?", 22f, WithAlpha(AiAccent, 0.75f), TextAlignmentOptions.Center, true);
                    Stretch(mark.rectTransform, 0f, 0f, 0f, 0f);
                }
            }

            statusText = CreateText("Txt_AiStatus", panel, "Экипировка противника скрыта до начала боя", 16f, DraftPalette.Muted, TextAlignmentOptions.Center, false);
            Place(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(panelWidth - 30f, 54f), new Vector2(0.5f, 0f));
            statusText.textWrappingMode = TextWrappingModes.Normal;
            statusText.fontStyle = FontStyles.Italic;
        }

        #endregion

        #region Bottom

        private static void BuildBottom(RectTransform parent, out TMP_Text footerHint, out Button battleButton, out TMP_Text battleLabel,
            out Button resetButton, out Button leaveButton)
        {
            Vector2 bottomCenter = new Vector2(0.5f, 0f);
            Vector2 bottomRight = new Vector2(1f, 0f);
            Vector2 bottomLeft = Vector2.zero;

            footerHint = CreateText("Txt_FooterHint", parent, "Подсказка", 18f, DraftPalette.Muted, TextAlignmentOptions.Center, false);
            Place(footerHint.rectTransform, bottomCenter, bottomCenter, new Vector2(-120f, 140f), new Vector2(1100f, 34f), bottomCenter);

            RectTransform battleGlowRect = CreateRect("BattleGlow", parent);
            Place(battleGlowRect, bottomRight, bottomRight, new Vector2(-24f, 24f), new Vector2(372f, 132f), bottomRight);
            Image battleGlow = AddImage(battleGlowRect, _glowSprite, WithAlpha(DraftPalette.SapphireGlow, 0.3f));
            battleGlow.type = Image.Type.Sliced;

            battleButton = CreateButton("Btn_Battle", parent, bottomRight, new Vector2(-60f, 50f), new Vector2(300f, 80f),
                DraftPalette.Sapphire, DraftPalette.SapphireGlow, "В БОЙ", 32f, out battleLabel);
            battleLabel.characterSpacing = 6f;

            resetButton = CreateButton("Btn_Reset", parent, bottomRight, new Vector2(-390f, 58f), new Vector2(230f, 64f),
                DraftPalette.PanelLight, DraftPalette.FrameIdle, "СБРОСИТЬ", 20f, out TMP_Text resetLabel);
            resetLabel.characterSpacing = 3f;

            leaveButton = CreateButton("Btn_Leave", parent, bottomLeft, new Vector2(60f, 58f), new Vector2(250f, 56f),
                DraftPalette.PanelLight, DraftPalette.FrameIdle, "ПОКИНУТЬ", 18f, out TMP_Text leaveLabel);
            leaveLabel.characterSpacing = 3f;
        }

        private static Button CreateButton(string objectName, RectTransform parent, Vector2 corner, Vector2 position, Vector2 size,
            Color fill, Color frameColor, string text, float fontSize, out TMP_Text label)
        {
            RectTransform buttonRect = CreateRect(objectName, parent);
            Place(buttonRect, corner, corner, position, size, corner);

            Image image = AddImage(buttonRect, _panelSprite, fill);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            RectTransform frameRect = CreateRect("Frame", buttonRect);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, frameColor);
            frame.type = Image.Type.Sliced;

            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CreateButtonColors();

            label = CreateText("Txt_Label", buttonRect, text, fontSize, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            Stretch(label.rectTransform, 0f, 0f, 0f, 0f);

            return button;
        }

        private static ColorBlock CreateButtonColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.85f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.75f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            colors.fadeDuration = 0.1f;
            return colors;
        }

        #endregion

        #region Overlay

        private static EquipTooltip BuildTooltip(RectTransform overlay)
        {
            RectTransform root = CreateRect("Tooltip", overlay);
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 196f), new Vector2(0f, 1f));
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            Image background = AddImage(root, _panelSprite, WithAlpha(DraftPalette.Obsidian, 0.97f));
            background.type = Image.Type.Sliced;

            RectTransform stripeRect = CreateRect("RarityStripe", root);
            Place(stripeRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), new Vector2(-24f, 3f), new Vector2(0.5f, 1f));
            Image rarityStripe = AddImage(stripeRect, _lineSprite, ItemRarityPalette.Common);

            RectTransform frameRect = CreateRect("Frame", root);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, ItemRarityPalette.Common);
            frame.type = Image.Type.Sliced;

            Vector2 topLeft = new Vector2(0f, 1f);

            TMP_Text titleText = CreateText("Txt_Title", root, "Item", 24f, ItemRarityPalette.CommonGlow, TextAlignmentOptions.MidlineLeft, true);
            Place(titleText.rectTransform, topLeft, topLeft, new Vector2(18f, -12f), new Vector2(324f, 32f));
            titleText.enableAutoSizing = true;
            titleText.fontSizeMin = 16f;
            titleText.fontSizeMax = 24f;

            TMP_Text rarityText = CreateText("Txt_Rarity", root, "Обычный · Стартовый", 16f, DraftPalette.Muted, TextAlignmentOptions.MidlineLeft, false);
            Place(rarityText.rectTransform, topLeft, topLeft, new Vector2(18f, -44f), new Vector2(324f, 24f));

            TMP_Text bonusText = CreateText("Txt_Bonus", root, "+0  Атака", 20f, DraftPalette.TextLight, TextAlignmentOptions.TopLeft, false);
            Place(bonusText.rectTransform, topLeft, topLeft, new Vector2(18f, -74f), new Vector2(324f, 80f));
            bonusText.richText = true;
            bonusText.textWrappingMode = TextWrappingModes.Normal;
            bonusText.lineSpacing = -6f;

            TMP_Text hintText = CreateText("Txt_Hint", root, "Подсказка", 13f, DraftPalette.Muted, TextAlignmentOptions.BottomLeft, false);
            Place(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 10f), new Vector2(324f, 34f), new Vector2(0f, 0f));
            hintText.textWrappingMode = TextWrappingModes.Normal;

            EquipTooltip tooltip = root.gameObject.AddComponent<EquipTooltip>();
            SerializedObject so = new SerializedObject(tooltip);
            SetRef(so, "canvasGroup", group);
            SetRef(so, "frame", frame);
            SetRef(so, "rarityStripe", rarityStripe);
            SetRef(so, "titleText", titleText);
            SetRef(so, "rarityText", rarityText);
            SetRef(so, "bonusText", bonusText);
            SetRef(so, "hintText", hintText);
            so.ApplyModifiedPropertiesWithoutUndo();

            return tooltip;
        }

        private static EquipDragGhost BuildDragGhost(RectTransform overlay)
        {
            RectTransform root = CreateRect("DragGhost", overlay);
            Place(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(StashCellWidth, StashCellHeight));
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform bodyRect = CreateRect("Body", root);
            Stretch(bodyRect, 0f, 0f, 0f, 0f);

            RectTransform glowRect = CreateRect("Glow", bodyRect);
            Stretch(glowRect, -22f, -22f, -22f, -22f);
            Image glow = AddImage(glowRect, _glowSprite, WithAlpha(ItemRarityPalette.CommonGlow, 0.9f));
            glow.type = Image.Type.Sliced;
            glow.pixelsPerUnitMultiplier = 2f;

            RectTransform backgroundRect = CreateRect("Background", bodyRect);
            Stretch(backgroundRect, 0f, 0f, 0f, 0f);
            Image background = AddImage(backgroundRect, _panelSprite, ItemRarityPalette.SlotEmpty);
            background.type = Image.Type.Sliced;

            RectTransform iconRect = CreateRect("Icon", bodyRect);
            SetAnchors(iconRect, new Vector2(0.14f, 0.14f), new Vector2(0.86f, 0.86f));
            Image icon = AddImage(iconRect, _swordSprite, ItemRarityPalette.CommonGlow);
            icon.preserveAspect = true;

            RectTransform frameRect = CreateRect("Frame", bodyRect);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, ItemRarityPalette.Common);
            frame.type = Image.Type.Sliced;

            EquipDragGhost ghost = root.gameObject.AddComponent<EquipDragGhost>();
            SerializedObject so = new SerializedObject(ghost);
            SetRef(so, "canvasGroup", group);
            SetRef(so, "body", bodyRect);
            SetRef(so, "glow", glow);
            SetRef(so, "background", background);
            SetRef(so, "frame", frame);
            SetRef(so, "icon", icon);
            so.ApplyModifiedPropertiesWithoutUndo();

            return ghost;
        }

        #endregion

        #region Helpers

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
            gameObject.layer = _uiLayer;
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            Place(rect, anchorMin, anchorMax, anchoredPosition, sizeDelta, anchorMin == anchorMax ? anchorMin : new Vector2(0.5f, 0.5f));
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image AddImage(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text CreateText(string objectName, RectTransform parent, string text, float size, Color color, TextAlignmentOptions alignment, bool bold)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void SetRef(SerializedObject so, string propertyName, Object value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[EquipCanvasBuilder] Поле «{propertyName}» не найдено у {so.targetObject.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetArray<T>(SerializedObject so, string propertyName, T[] values) where T : Object
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[EquipCanvasBuilder] Массив «{propertyName}» не найден у {so.targetObject.GetType().Name}.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        #endregion
    }
}
