using System.IO;
using Solutio.Draft;
using Solutio.Session;
using Solutio.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.EditorTools
{
    /// <summary>
    /// Сборщик интерфейса стадии драфта. Пункт меню «Solutio → Собрать DraftCanvas»:
    /// 1) генерирует процедурные спрайты (мягкое свечение, скруглённая панель, рамка, силуэт-заглушка портрета);
    /// 2) строит иерархию DraftCanvas в открытой сцене с ручной раскладкой без Layout Group
    ///    (нет пересчёта разметки во время игры — важно для 360+ FPS);
    /// 3) связывает все поля компонентов через SerializedObject и сохраняет префаб DraftCanvas.prefab;
    /// 4) назначает экземпляру в сцене ссылки на SessionConfigurator и MainMenuController.
    /// </summary>
    public static class DraftCanvasBuilder
    {
        private const string SpriteFolder = "Assets/_Project/Sprites/UI/Draft";
        private const string PrefabFolder = "Assets/_Project/Prefabs/UI";
        private const string PrefabPath = PrefabFolder + "/DraftCanvas.prefab";
        private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string VideoTexturePath = "Assets/_Project/Materials/RT_VideoBg.renderTexture";
        private const string CanvasName = "DraftCanvas";

        private const int CardsPerRow = 7;
        private const float CardWidth = 140f;
        private const float CardHeight = 164f;
        private const float CardSpacing = 14f;
        private const float RowSpacing = 14f;
        private const float RowLabelWidth = 160f;

        private const float TeamPanelWidth = 740f;
        private const float PickSlotWidth = 128f;
        private const float PickSlotHeight = 150f;
        private const float PickSlotStep = 143f;
        private const float BanSlotSize = 56f;
        private const float BanSlotStep = 66f;

        private static TMP_FontAsset _font;
        private static Sprite _glowSprite;
        private static Sprite _panelSprite;
        private static Sprite _outlineSprite;
        private static Sprite _silhouetteSprite;
        private static Sprite _radialSprite;
        private static Sprite _gradientSprite;
        private static Sprite _shineSprite;
        private static Sprite _lineSprite;
        private static Sprite _lineFadeRightSprite;
        private static Sprite _vignetteSprite;
        private static int _uiLayer;

        [MenuItem("Solutio/Собрать DraftCanvas")]
        public static void Build()
        {
            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "DraftCanvas уже есть в сцене",
                    "Пересобрать DraftCanvas? Старый объект в сцене будет удалён, префаб перезаписан.",
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

            GameObject canvasObject = BuildCanvas(out DraftViewController view);

            Directory.CreateDirectory(PrefabFolder);
            GameObject prefabInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(canvasObject, PrefabPath, InteractionMode.UserAction);

            // Ссылки на объекты сцены назначаются уже экземпляру (в префаб-ассет их записать нельзя)
            SessionConfigurator configurator = Object.FindFirstObjectByType<SessionConfigurator>();
            MainMenuController mainMenu = Object.FindFirstObjectByType<MainMenuController>();

            DraftViewController instanceView = prefabInstance.GetComponent<DraftViewController>();
            SerializedObject viewSo = new SerializedObject(instanceView);
            viewSo.FindProperty("sessionConfigurator").objectReferenceValue = configurator;
            viewSo.FindProperty("mainMenu").objectReferenceValue = mainMenu;
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(instanceView);

            EditorSceneManager.MarkSceneDirty(prefabInstance.scene);
            Selection.activeGameObject = prefabInstance;

            Debug.Log($"<color=#307082>[DraftCanvasBuilder]</color> DraftCanvas собран и сохранён: {PrefabPath}. " +
                      $"SessionConfigurator: {(configurator != null ? "найден" : "НЕ найден")}, MainMenuController: {(mainMenu != null ? "найден" : "НЕ найден")}.");
        }

        #region Assets

        private static bool PrepareAssets()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (_font == null)
            {
                _font = TMP_Settings.defaultFontAsset;
            }

            if (_font == null)
            {
                EditorUtility.DisplayDialog("Шрифт не найден", "Не найден LiberationSans SDF. Импортируйте TMP Essential Resources.", "OK");
                return false;
            }

            Directory.CreateDirectory(SpriteFolder);

            _glowSprite = CreateSprite("Draft_Glow.png", 128, 128, 48, GlowPixel);
            _panelSprite = CreateSprite("Draft_Panel.png", 64, 64, 16, PanelPixel);
            _outlineSprite = CreateSprite("Draft_Outline.png", 64, 64, 16, OutlinePixel);
            _silhouetteSprite = CreateSprite("Draft_Silhouette.png", 256, 320, 0, SilhouettePixel);
            _radialSprite = CreateSprite("Draft_Radial.png", 128, 128, 0, RadialPixel);
            _gradientSprite = CreateSprite("Draft_GradientV.png", 16, 128, 0, GradientPixel);
            _shineSprite = CreateSprite("Draft_Shine.png", 64, 256, 0, ShinePixel);
            _lineSprite = CreateSprite("Draft_Line.png", 256, 8, 0, LinePixel);
            _lineFadeRightSprite = CreateSprite("Draft_LineFadeRight.png", 256, 8, 0, LineFadeRightPixel);
            _vignetteSprite = CreateSprite("Draft_Vignette.png", 256, 256, 0, VignettePixel);

            return _glowSprite != null && _panelSprite != null && _outlineSprite != null && _silhouetteSprite != null
                   && _radialSprite != null && _gradientSprite != null && _shineSprite != null
                   && _lineSprite != null && _lineFadeRightSprite != null && _vignetteSprite != null;
        }

        private delegate float PixelAlpha(float x, float y, int width, int height);

        private static Sprite CreateSprite(string fileName, int width, int height, int border, PixelAlpha alphaFunction)
        {
            string assetPath = SpriteFolder + "/" + fileName;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float alpha = Mathf.Clamp01(alphaFunction(x + 0.5f, y + 0.5f, width, height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
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
            settings.spriteBorder = new Vector4(border, border, border, border);
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static float RoundedRectDistance(float x, float y, float centerX, float centerY, float halfWidth, float halfHeight, float radius)
        {
            float qx = Mathf.Abs(x - centerX) - (halfWidth - radius);
            float qy = Mathf.Abs(y - centerY) - (halfHeight - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - radius;
        }

        private static float GlowPixel(float x, float y, int width, int height)
        {
            float distance = RoundedRectDistance(x, y, width * 0.5f, height * 0.5f, 18f, 18f, 12f);
            if (distance <= 0f)
            {
                return 1f;
            }

            float falloff = 1f - Mathf.Clamp01(distance / 44f);
            return falloff * falloff;
        }

        private static float PanelPixel(float x, float y, int width, int height)
        {
            float distance = RoundedRectDistance(x, y, width * 0.5f, height * 0.5f, width * 0.5f - 1f, height * 0.5f - 1f, 12f);
            return 0.5f - distance;
        }

        private static float OutlinePixel(float x, float y, int width, int height)
        {
            const float thickness = 2.5f;
            float distance = RoundedRectDistance(x, y, width * 0.5f, height * 0.5f, width * 0.5f - 1f, height * 0.5f - 1f, 12f);
            return Mathf.Clamp01(0.5f - distance) * Mathf.Clamp01(0.5f + distance + thickness);
        }

        private static float SilhouettePixel(float x, float y, int width, int height)
        {
            Vector2 point = new Vector2(x, y);
            float centerX = width * 0.5f;

            float head = Vector2.Distance(point, new Vector2(centerX, 200f)) - 56f;

            Vector2 shoulderOffset = new Vector2((x - centerX) / 112f, (y - 20f) / 100f);
            float shoulders = (shoulderOffset.magnitude - 1f) * 100f;

            float neck = RoundedRectDistance(x, y, centerX, 128f, 26f, 30f, 8f);
            float shape = Mathf.Min(head, Mathf.Min(shoulders, neck));

            float alpha = Mathf.Clamp01(0.5f - shape / 1.5f);
            float verticalFade = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(y / (height * 0.75f)));
            return alpha * verticalFade;
        }

        private static float RadialPixel(float x, float y, int width, int height)
        {
            float radius = Vector2.Distance(new Vector2(x, y), new Vector2(width * 0.5f, height * 0.5f)) / (width * 0.5f);
            float falloff = 1f - Mathf.Clamp01(radius);
            return falloff * falloff;
        }

        private static float GradientPixel(float x, float y, int width, int height)
        {
            float t = 1f - y / height;
            return t * t;
        }

        private static float ShinePixel(float x, float y, int width, int height)
        {
            float band = (x - width * 0.5f) / (width * 0.16f);
            float vertical = Mathf.Sin(Mathf.PI * y / height);
            return Mathf.Exp(-band * band) * vertical;
        }

        private static float LinePixel(float x, float y, int width, int height)
        {
            float horizontal = Mathf.Pow(1f - Mathf.Abs(x - width * 0.5f) / (width * 0.5f), 1.5f);
            float vertical = 1f - Mathf.Abs(y - height * 0.5f) / (height * 0.5f);
            return horizontal * vertical;
        }

        private static float LineFadeRightPixel(float x, float y, int width, int height)
        {
            float horizontal = 1f - x / width;
            float vertical = 1f - Mathf.Abs(y - height * 0.5f) / (height * 0.5f);
            return horizontal * vertical;
        }

        private static float VignettePixel(float x, float y, int width, int height)
        {
            float nx = (x / width - 0.5f) * 2f;
            float ny = (y / height - 0.5f) * 2f;
            float radius = Mathf.Sqrt(nx * nx + ny * ny) / 1.4142f;
            return Mathf.SmoothStep(0f, 0.92f, Mathf.InverseLerp(0.3f, 1f, radius));
        }

        #endregion

        #region Canvas

        private static GameObject BuildCanvas(out DraftViewController view)
        {
            GameObject root = new GameObject(CanvasName, typeof(RectTransform));
            root.layer = _uiLayer;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;

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

            view = root.AddComponent<DraftViewController>();
            RectTransform rootRect = (RectTransform)root.transform;

            // Фон: видеофон главного меню (RT_VideoBg) + затемнение, перехватывающее клики по меню
            RenderTexture videoTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(VideoTexturePath);
            if (videoTexture != null)
            {
                RectTransform videoRect = CreateRect("Bg_Video", rootRect);
                Stretch(videoRect, 0f, 0f, 0f, 0f);
                RawImage video = videoRect.gameObject.AddComponent<RawImage>();
                video.texture = videoTexture;
                video.raycastTarget = false;
            }

            RectTransform dimRect = CreateRect("Bg_Dim", rootRect);
            Stretch(dimRect, 0f, 0f, 0f, 0f);
            Image dim = AddImage(dimRect, null, WithAlpha(DraftPalette.Obsidian, 0.62f));
            dim.raycastTarget = true;

            RectTransform vignetteRect = CreateRect("Bg_Vignette", rootRect);
            Stretch(vignetteRect, 0f, 0f, 0f, 0f);
            AddImage(vignetteRect, _vignetteSprite, Color.black);

            RectTransform topShade = CreateRect("Bg_TopShade", rootRect);
            Place(topShade, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -210f), new Vector2(0f, 420f), new Vector2(0.5f, 0.5f));
            topShade.localRotation = Quaternion.Euler(0f, 0f, 180f);
            AddImage(topShade, _gradientSprite, WithAlpha(DraftPalette.Obsidian, 0.95f));

            RectTransform bottomShade = CreateRect("Bg_BottomShade", rootRect);
            Place(bottomShade, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 260f), new Vector2(0.5f, 0f));
            AddImage(bottomShade, _gradientSprite, WithAlpha(DraftPalette.Obsidian, 0.95f));

            // Верхняя полоса: команды по краям, заголовок фазы по центру
            BuildTeamPanel(rootRect, DraftSide.User, out TMP_Text userTitle, out DraftSlotView[] userPicks, out DraftSlotView[] userBans, out DraftTimerBar userTimer);
            BuildTeamPanel(rootRect, DraftSide.Ai, out TMP_Text aiTitle, out DraftSlotView[] aiPicks, out DraftSlotView[] aiBans, out DraftTimerBar aiTimer);
            BuildHeader(rootRect, out TMP_Text phaseText, out TMP_Text turnText, out Image turnGlow, out TMP_Text stepText, out TMP_Text matchInfoText);

            // Сетка героев по ролям
            RectTransform gridRect = CreateRect("HeroGrid", rootRect);
            float gridWidth = RowLabelWidth + CardsPerRow * CardWidth + (CardsPerRow - 1) * CardSpacing;
            float gridHeight = 3f * CardHeight + 2f * RowSpacing;
            Place(gridRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(gridWidth, gridHeight));

            DraftHeroCard[] tankCards = BuildRow(gridRect, 0, "ТАНКИ", "Tank", DraftPalette.RoleTank);
            DraftHeroCard[] carryCards = BuildRow(gridRect, 1, "КЕРРИ", "Carry", DraftPalette.RoleCarry);
            DraftHeroCard[] supportCards = BuildRow(gridRect, 2, "ПОДДЕРЖКА", "Support", DraftPalette.RoleSupport);

            DraftSelectionPanel selectionPanel = BuildSelectionPanel(rootRect);
            Button leaveButton = BuildLeaveButton(rootRect);

            SerializedObject so = new SerializedObject(view);
            SetRef(so, "rootCanvasGroup", rootGroup);
            SetRef(so, "phaseText", phaseText);
            SetRef(so, "turnText", turnText);
            SetRef(so, "turnGlow", turnGlow);
            SetRef(so, "stepCounterText", stepText);
            SetRef(so, "matchInfoText", matchInfoText);
            SetRef(so, "userTeamTitle", userTitle);
            SetRef(so, "aiTeamTitle", aiTitle);
            SetArray(so, "tankCards", tankCards);
            SetArray(so, "carryCards", carryCards);
            SetArray(so, "supportCards", supportCards);
            SetArray(so, "userPickSlots", userPicks);
            SetArray(so, "aiPickSlots", aiPicks);
            SetArray(so, "userBanSlots", userBans);
            SetArray(so, "aiBanSlots", aiBans);
            SetRef(so, "userTimer", userTimer);
            SetRef(so, "aiTimer", aiTimer);
            SetRef(so, "selectionPanel", selectionPanel);
            SetRef(so, "leaveButton", leaveButton);
            SetRef(so, "placeholderPortrait", _silhouetteSprite);
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static void BuildHeader(RectTransform parent, out TMP_Text phaseText, out TMP_Text turnText, out Image turnGlow, out TMP_Text stepText, out TMP_Text matchInfoText)
        {
            RectTransform header = CreateRect("Header", parent);
            Place(header, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(360f, 210f));

            phaseText = CreateText("Txt_Phase", header, "ФАЗА БАНОВ I", 20f, DraftPalette.TextLight, TextAlignmentOptions.Center, true);
            Place(phaseText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(360f, 30f));
            phaseText.characterSpacing = 6f;

            RectTransform turnGlowRect = CreateRect("TurnGlow", header);
            Place(turnGlowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(480f, 100f));
            turnGlow = AddImage(turnGlowRect, _radialSprite, WithAlpha(DraftPalette.SapphireGlow, 0.35f));

            turnText = CreateText("Txt_Turn", header, "ВАШ БАН", 40f, DraftPalette.SapphireGlow, TextAlignmentOptions.Center, true);
            Place(turnText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -34f), new Vector2(360f, 56f));
            turnText.enableAutoSizing = true;
            turnText.fontSizeMin = 22f;
            turnText.fontSizeMax = 40f;
            turnText.characterSpacing = 3f;

            RectTransform ornament = CreateRect("Ornament_Line", header);
            Place(ornament, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(340f, 3f));
            AddImage(ornament, _lineSprite, WithAlpha(DraftPalette.SapphireGlow, 0.8f));

            RectTransform diamond = CreateRect("Ornament_Diamond", header);
            Place(diamond, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -95f), new Vector2(10f, 10f));
            diamond.pivot = new Vector2(0.5f, 0.5f);
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            AddImage(diamond, null, DraftPalette.SapphireGlow);

            stepText = CreateText("Txt_StepCounter", header, "Ход 1 из 16", 18f, DraftPalette.Muted, TextAlignmentOptions.Center, false);
            Place(stepText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -106f), new Vector2(360f, 28f));

            matchInfoText = CreateText("Txt_MatchInfo", header, "Страж  ·  Руины Собора", 18f, DraftPalette.TextLight, TextAlignmentOptions.Center, false);
            Place(matchInfoText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(360f, 28f));
        }

        #endregion

        #region Team Panels

        private static void BuildTeamPanel(RectTransform parent, DraftSide side, out TMP_Text title, out DraftSlotView[] picks, out DraftSlotView[] bans, out DraftTimerBar timer)
        {
            bool isAi = side == DraftSide.Ai;
            float sign = isAi ? -1f : 1f;
            Vector2 corner = isAi ? new Vector2(1f, 1f) : new Vector2(0f, 1f);

            RectTransform panel = CreateRect(isAi ? "Panel_AiTeam" : "Panel_UserTeam", parent);
            Place(panel, corner, corner, new Vector2(40f * sign, -24f), new Vector2(TeamPanelWidth, 320f));

            Color sideColor = DraftPalette.GetSideGlowColor(side);
            TextAlignmentOptions alignment = isAi ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;

            title = CreateText(isAi ? "Txt_AiTeam" : "Txt_UserTeam", panel, isAi ? "КОМАНДА ИИ" : "ВАША КОМАНДА", 24f, sideColor, alignment, true);
            Place(title.rectTransform, corner, corner, Vector2.zero, new Vector2(TeamPanelWidth, 34f));
            title.characterSpacing = 4f;

            RectTransform underline = CreateRect("Underline", panel);
            Place(underline, corner, corner, new Vector2(260f * sign, -35.5f), new Vector2(520f, 3f), new Vector2(0.5f, 0.5f));
            underline.localScale = new Vector3(sign, 1f, 1f);

            AddImage(underline, _lineFadeRightSprite, WithAlpha(sideColor, 0.9f));

            RectTransform pickRow = CreateRect("PickSlots", panel);
            Place(pickRow, corner, corner, new Vector2(0f, -42f), new Vector2(TeamPanelWidth, PickSlotHeight));

            picks = new DraftSlotView[DraftSequence.TeamSize];
            float pickOffset = (TeamPanelWidth - ((DraftSequence.TeamSize - 1) * PickSlotStep + PickSlotWidth)) * 0.5f;

            for (int i = 0; i < picks.Length; i++)
            {
                RectTransform slotRect = CreateRect($"PickSlot_{i + 1}", pickRow);
                Place(slotRect, corner, corner, new Vector2((pickOffset + i * PickSlotStep) * sign, 0f), new Vector2(PickSlotWidth, PickSlotHeight));
                picks[i] = BuildSlot(slotRect, DraftActionType.Pick, side);
            }

            timer = BuildTimer(panel, side, corner, sign);

            TMP_Text banLabel = CreateText("Txt_Bans", panel, "БАНЫ", 16f, DraftPalette.Muted, alignment, true);
            Place(banLabel.rectTransform, corner, corner, new Vector2(0f, -258f), new Vector2(80f, BanSlotSize));
            banLabel.characterSpacing = 4f;

            RectTransform banRow = CreateRect("BanSlots", panel);
            Place(banRow, corner, corner, new Vector2(90f * sign, -258f), new Vector2(DraftSequence.BansPerSide * BanSlotStep, BanSlotSize));

            bans = new DraftSlotView[DraftSequence.BansPerSide];
            for (int i = 0; i < bans.Length; i++)
            {
                RectTransform slotRect = CreateRect($"BanSlot_{i + 1}", banRow);
                Place(slotRect, corner, corner, new Vector2(i * BanSlotStep * sign, 0f), new Vector2(BanSlotSize, BanSlotSize));
                bans[i] = BuildSlot(slotRect, DraftActionType.Ban, side);
            }
        }

        private static DraftSlotView BuildSlot(RectTransform root, DraftActionType slotType, DraftSide side)
        {
            bool isBan = slotType == DraftActionType.Ban;

            RectTransform content = CreateRect("Content", root);
            Stretch(content, 0f, 0f, 0f, 0f);

            RectTransform glowRect = CreateRect("Glow", content);
            float slotGlow = isBan ? -26f : -38f;
            Stretch(glowRect, slotGlow, slotGlow, slotGlow, slotGlow);
            Image glow = AddImage(glowRect, _glowSprite, WithAlpha(DraftPalette.GetSideGlowColor(side), 0f));
            glow.type = Image.Type.Sliced;

            RectTransform bodyRect = CreateRect("Body", content);
            Stretch(bodyRect, 0f, 0f, 0f, 0f);
            Image body = AddImage(bodyRect, _panelSprite, DraftPalette.PanelLight);
            body.type = Image.Type.Sliced;
            body.raycastTarget = false;

            RectTransform portraitGroupRect = CreateRect("PortraitGroup", content);
            Stretch(portraitGroupRect, 4f, 4f, 4f, isBan ? 4f : 30f);
            CanvasGroup portraitGroup = portraitGroupRect.gameObject.AddComponent<CanvasGroup>();
            portraitGroup.alpha = 0f;

            RectTransform slotHaloRect = CreateRect("Halo", portraitGroupRect);
            Stretch(slotHaloRect, -10f, -10f, -10f, -10f);
            Image slotHalo = AddImage(slotHaloRect, _radialSprite, WithAlpha(DraftPalette.Muted, 0f));

            RectTransform portraitRect = CreateRect("Portrait", portraitGroupRect);
            Stretch(portraitRect, 0f, 0f, 0f, 0f);
            Image portrait = AddImage(portraitRect, _silhouetteSprite, Color.white);
            portrait.preserveAspect = true;

            if (!isBan)
            {
                RectTransform plateRect = CreateRect("NamePlate", content);
                Place(plateRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f), new Vector2(-4f, 40f), new Vector2(0.5f, 0f));
                AddImage(plateRect, _gradientSprite, WithAlpha(Color.black, 0.85f));
            }

            TMP_Text placeholder = CreateText("Txt_Placeholder", content, isBan ? "БАН" : "1", isBan ? 12f : 40f, DraftPalette.Muted, TextAlignmentOptions.Center, true);
            Stretch(placeholder.rectTransform, 0f, 0f, 0f, isBan ? 0f : 30f);

            TMP_Text nameText = null;
            if (!isBan)
            {
                nameText = CreateText("Txt_Name", content, string.Empty, 14f, DraftPalette.TextBright, TextAlignmentOptions.Center, false);
                Place(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f), new Vector2(-8f, 26f), new Vector2(0.5f, 0f));
                nameText.enableAutoSizing = true;
                nameText.fontSizeMin = 10f;
                nameText.fontSizeMax = 14f;
            }

            Image strike = CreateStrike(content, isBan ? 70f : 170f, isBan ? 4f : 6f);

            RectTransform frameRect = CreateRect("Frame", content);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.FrameIdle);
            frame.type = Image.Type.Sliced;

            DraftSlotView slot = root.gameObject.AddComponent<DraftSlotView>();
            SerializedObject so = new SerializedObject(slot);
            so.FindProperty("slotType").enumValueIndex = (int)slotType;
            SetRef(so, "content", content);
            SetRef(so, "glow", glow);
            SetRef(so, "frame", frame);
            SetRef(so, "portraitGroup", portraitGroup);
            SetRef(so, "halo", slotHalo);
            SetRef(so, "portrait", portrait);
            SetRef(so, "placeholderText", placeholder);
            SetRef(so, "nameText", nameText);
            SetRef(so, "banStrike", strike);
            so.ApplyModifiedPropertiesWithoutUndo();

            return slot;
        }

        private static DraftTimerBar BuildTimer(RectTransform panel, DraftSide side, Vector2 corner, float sign)
        {
            bool isAi = side == DraftSide.Ai;

            RectTransform root = CreateRect(isAi ? "TimerBar_Ai" : "TimerBar_User", panel);
            Place(root, corner, corner, new Vector2(0f, -204f), new Vector2(TeamPanelWidth, 46f));
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();

            TMP_Text seconds = CreateText("Txt_Seconds", root, "30", 34f, DraftPalette.SapphireGlow, TextAlignmentOptions.Center, true);
            Place(seconds.rectTransform, corner, corner, Vector2.zero, new Vector2(64f, 46f));

            RectTransform turnTrack = CreateRect("TurnTrack", root);
            Place(turnTrack, corner, corner, new Vector2(76f * sign, -8f), new Vector2(520f, 16f));
            Image turnTrackImage = AddImage(turnTrack, _panelSprite, DraftPalette.Obsidian);
            turnTrackImage.type = Image.Type.Sliced;
            turnTrackImage.raycastTarget = false;

            RectTransform glowRect = CreateRect("TurnFillGlow", turnTrack);
            Stretch(glowRect, -3f, -3f, -3f, -3f);
            Image turnFillGlowImage = AddImage(glowRect, null, WithAlpha(DraftPalette.SapphireGlow, 0.3f));
            ConfigureFill(turnFillGlowImage, isAi);

            RectTransform fillRect = CreateRect("TurnFill", turnTrack);
            Stretch(fillRect, 2f, 2f, 2f, 2f);
            Image turnFill = AddImage(fillRect, null, DraftPalette.SapphireGlow);
            ConfigureFill(turnFill, isAi);

            RectTransform reserveTrack = CreateRect("ReserveTrack", root);
            Place(reserveTrack, corner, corner, new Vector2(76f * sign, -32f), new Vector2(520f, 6f));
            Image reserveTrackImage = AddImage(reserveTrack, null, DraftPalette.Obsidian);
            reserveTrackImage.raycastTarget = false;

            RectTransform reserveRect = CreateRect("ReserveFill", reserveTrack);
            Stretch(reserveRect, 0f, 0f, 0f, 0f);
            Image reserveFill = AddImage(reserveRect, null, DraftPalette.Reserve);
            ConfigureFill(reserveFill, isAi);

            TMP_Text reserveText = CreateText("Txt_Reserve", root, "ЗАПАС 1:00", 16f, DraftPalette.Reserve, isAi ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight, false);
            Place(reserveText.rectTransform, corner, corner, new Vector2(606f * sign, 0f), new Vector2(134f, 46f));

            DraftTimerBar timer = root.gameObject.AddComponent<DraftTimerBar>();
            SerializedObject so = new SerializedObject(timer);
            SetRef(so, "canvasGroup", group);
            SetRef(so, "turnFill", turnFill);
            SetRef(so, "turnFillGlow", turnFillGlowImage);
            SetRef(so, "reserveFill", reserveFill);
            SetRef(so, "secondsText", seconds);
            SetRef(so, "reserveText", reserveText);
            SetRef(so, "pulseTarget", seconds.rectTransform);
            so.ApplyModifiedPropertiesWithoutUndo();

            return timer;
        }

        private static void ConfigureFill(Image image, bool fromRight)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = fromRight ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.raycastTarget = false;
        }

        #endregion

        #region Hero Grid

        private static DraftHeroCard[] BuildRow(RectTransform grid, int rowIndex, string label, string roleKey, Color roleColor)
        {
            Vector2 topLeft = new Vector2(0f, 1f);
            float rowY = -rowIndex * (CardHeight + RowSpacing);

            RectTransform row = CreateRect($"Row_{roleKey}", grid);
            Place(row, topLeft, topLeft, new Vector2(0f, rowY), new Vector2(grid.sizeDelta.x, CardHeight));

            TMP_Text rowLabel = CreateText("Txt_RowLabel", row, label, 20f, roleColor, TextAlignmentOptions.MidlineLeft, true);
            Place(rowLabel.rectTransform, topLeft, topLeft, Vector2.zero, new Vector2(RowLabelWidth - 10f, CardHeight));
            rowLabel.characterSpacing = 4f;

            DraftHeroCard[] cards = new DraftHeroCard[CardsPerRow];
            for (int i = 0; i < CardsPerRow; i++)
            {
                RectTransform cardRect = CreateRect($"Card_{roleKey}_{i + 1}", row);
                Place(cardRect, topLeft, topLeft, new Vector2(RowLabelWidth + i * (CardWidth + CardSpacing), 0f), new Vector2(CardWidth, CardHeight));
                cards[i] = BuildCard(cardRect, roleColor);
            }

            return cards;
        }

        private static DraftHeroCard BuildCard(RectTransform root, Color roleColor)
        {
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();

            RectTransform content = CreateRect("Content", root);
            Stretch(content, 0f, 0f, 0f, 0f);

            RectTransform glowRect = CreateRect("Glow", content);
            Stretch(glowRect, -40f, -40f, -40f, -40f);
            Image glow = AddImage(glowRect, _glowSprite, WithAlpha(DraftPalette.SapphireGlow, 0f));
            glow.type = Image.Type.Sliced;

            RectTransform bodyRect = CreateRect("Body", content);
            Stretch(bodyRect, 0f, 0f, 0f, 0f);
            Image body = AddImage(bodyRect, _panelSprite, DraftPalette.Panel);
            body.type = Image.Type.Sliced;
            body.raycastTarget = true;

            // Всё, что внутри карточки, обрезается по её границам (RectMask2D — без стенсил-буфера)
            RectTransform clip = CreateRect("Clip", content);
            Stretch(clip, 2f, 2f, 2f, 2f);
            clip.gameObject.AddComponent<RectMask2D>();

            RectTransform haloRect = CreateRect("Halo", clip);
            Place(haloRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 10f), new Vector2(190f, 170f), new Vector2(0.5f, 1f));
            Image halo = AddImage(haloRect, _radialSprite, WithAlpha(roleColor, 0.35f));

            RectTransform portraitRect = CreateRect("Portrait", clip);
            Stretch(portraitRect, 6f, 6f, 8f, 30f);
            Image portrait = AddImage(portraitRect, _silhouetteSprite, roleColor);
            portrait.preserveAspect = true;

            RectTransform plateRect = CreateRect("NamePlate", clip);
            Place(plateRect, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 64f), new Vector2(0.5f, 0f));
            AddImage(plateRect, _gradientSprite, WithAlpha(Color.black, 0.9f));

            RectTransform shadeRect = CreateRect("Shade", clip);
            Stretch(shadeRect, 0f, 0f, 0f, 0f);
            Image shade = AddImage(shadeRect, null, WithAlpha(Color.black, 0f));

            RectTransform shineRect = CreateRect("Shine", clip);
            Place(shineRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-CardWidth, 0f), new Vector2(46f, 260f));
            shineRect.localRotation = Quaternion.Euler(0f, 0f, -20f);
            Image shineImage = AddImage(shineRect, _shineSprite, WithAlpha(Color.white, 0f));

            RectTransform stripeRect = CreateRect("RoleStripe", content);
            Place(stripeRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -3f), new Vector2(-28f, 3f), new Vector2(0.5f, 1f));
            AddImage(stripeRect, _lineSprite, roleColor);

            Image strike = CreateStrike(content, 200f, 5f);

            RectTransform overlayRect = CreateRect("StateOverlay", content);
            Place(overlayRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 38f), new Vector2(-24f, 24f), new Vector2(0.5f, 0f));
            CanvasGroup overlay = overlayRect.gameObject.AddComponent<CanvasGroup>();
            overlay.alpha = 0f;
            Image overlayBackground = AddImage(overlayRect, _panelSprite, WithAlpha(DraftPalette.Sapphire, 0.92f));
            overlayBackground.type = Image.Type.Sliced;

            TMP_Text stateText = CreateText("Txt_State", overlayRect, "ВЫ", 14f, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            Stretch(stateText.rectTransform, 0f, 0f, 0f, 0f);
            stateText.characterSpacing = 8f;

            TMP_Text nameText = CreateText("Txt_Name", content, "Hero", 14f, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            Place(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(-12f, 26f), new Vector2(0.5f, 0f));
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 9f;
            nameText.fontSizeMax = 14f;

            RectTransform frameRect = CreateRect("Frame", content);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.FrameIdle);
            frame.type = Image.Type.Sliced;

            DraftHeroCard card = root.gameObject.AddComponent<DraftHeroCard>();
            SerializedObject so = new SerializedObject(card);
            SetRef(so, "canvasGroup", group);
            SetRef(so, "content", content);
            SetRef(so, "glow", glow);
            SetRef(so, "frame", frame);
            SetRef(so, "halo", halo);
            SetRef(so, "portrait", portrait);
            SetRef(so, "roleStripe", stripeRect.GetComponent<Image>());
            SetRef(so, "shade", shade);
            SetRef(so, "shine", shineRect);
            SetRef(so, "shineImage", shineImage);
            SetRef(so, "nameText", nameText);
            SetRef(so, "stateOverlay", overlay);
            SetRef(so, "stateOverlayBackground", overlayBackground);
            SetRef(so, "stateText", stateText);
            SetRef(so, "banStrike", strike);
            so.ApplyModifiedPropertiesWithoutUndo();

            return card;
        }

        #endregion

        #region Bottom

        private static DraftSelectionPanel BuildSelectionPanel(RectTransform parent)
        {
            RectTransform root = CreateRect("SelectionPanel", parent);
            Place(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(980f, 150f), new Vector2(0.5f, 0f));
            CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            Image background = AddImage(root, _panelSprite, WithAlpha(DraftPalette.PanelLight, 0.95f));
            background.type = Image.Type.Sliced;

            RectTransform borderRect = CreateRect("Frame", root);
            Stretch(borderRect, 0f, 0f, 0f, 0f);
            Image border = AddImage(borderRect, _outlineSprite, DraftPalette.Sapphire);
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;

            Vector2 leftCenter = new Vector2(0f, 0.5f);
            RectTransform holder = CreateRect("PortraitHolder", root);
            Place(holder, leftCenter, leftCenter, new Vector2(20f, 0f), new Vector2(104f, 126f), leftCenter);

            RectTransform selHaloRect = CreateRect("Halo", holder);
            Stretch(selHaloRect, -20f, -20f, -20f, -20f);
            Image selHalo = AddImage(selHaloRect, _radialSprite, WithAlpha(DraftPalette.RoleTank, 0.55f));

            RectTransform portraitRect = CreateRect("Portrait", holder);
            Stretch(portraitRect, 6f, 6f, 6f, 6f);
            Image portrait = AddImage(portraitRect, _silhouetteSprite, Color.white);
            portrait.preserveAspect = true;

            RectTransform portraitFrameRect = CreateRect("PortraitFrame", holder);
            Stretch(portraitFrameRect, 0f, 0f, 0f, 0f);
            Image portraitFrame = AddImage(portraitFrameRect, _outlineSprite, DraftPalette.SapphireGlow);
            portraitFrame.type = Image.Type.Sliced;

            Vector2 topLeft = new Vector2(0f, 1f);
            TMP_Text nameText = CreateText("Txt_SelName", root, "Hero", 30f, DraftPalette.TextBright, TextAlignmentOptions.MidlineLeft, true);
            Place(nameText.rectTransform, topLeft, topLeft, new Vector2(146f, -16f), new Vector2(520f, 40f));

            TMP_Text roleText = CreateText("Txt_SelRole", root, "Танк", 20f, DraftPalette.RoleTank, TextAlignmentOptions.MidlineLeft, true);
            Place(roleText.rectTransform, topLeft, topLeft, new Vector2(146f, -58f), new Vector2(520f, 30f));

            TMP_Text statsText = CreateText("Txt_SelStats", root, "Атака 0   ·   Броня 0   ·   Здоровье 0", 20f, DraftPalette.TextLight, TextAlignmentOptions.MidlineLeft, false);
            Place(statsText.rectTransform, topLeft, topLeft, new Vector2(146f, -96f), new Vector2(560f, 30f));

            Vector2 rightCenter = new Vector2(1f, 0.5f);
            RectTransform confirmGlowRect = CreateRect("ConfirmGlow", root);
            Place(confirmGlowRect, rightCenter, rightCenter, new Vector2(4f, 0f), new Vector2(316f, 132f), rightCenter);
            Image confirmGlow = AddImage(confirmGlowRect, _glowSprite, WithAlpha(DraftPalette.SapphireGlow, 0.25f));
            confirmGlow.type = Image.Type.Sliced;

            RectTransform buttonRect = CreateRect("Btn_Confirm", root);
            Place(buttonRect, rightCenter, rightCenter, new Vector2(-24f, 0f), new Vector2(260f, 76f), rightCenter);
            Image buttonImage = AddImage(buttonRect, _panelSprite, DraftPalette.Sapphire);
            buttonImage.type = Image.Type.Sliced;
            buttonImage.raycastTarget = true;
            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.colors = CreateButtonColors();

            TMP_Text confirmLabel = CreateText("Txt_Confirm", buttonRect, "ВЫБРАТЬ", 28f, DraftPalette.TextBright, TextAlignmentOptions.Center, true);
            Stretch(confirmLabel.rectTransform, 0f, 0f, 0f, 0f);
            confirmLabel.characterSpacing = 4f;

            DraftSelectionPanel panel = root.gameObject.AddComponent<DraftSelectionPanel>();
            SerializedObject so = new SerializedObject(panel);
            SetRef(so, "canvasGroup", group);
            SetRef(so, "halo", selHalo);
            SetRef(so, "portrait", portrait);
            SetRef(so, "portraitFrame", portraitFrame);
            SetRef(so, "nameText", nameText);
            SetRef(so, "roleText", roleText);
            SetRef(so, "statsText", statsText);
            SetRef(so, "confirmButton", button);
            SetRef(so, "confirmButtonImage", buttonImage);
            SetRef(so, "confirmLabel", confirmLabel);
            SetRef(so, "confirmGlow", confirmGlow);
            so.ApplyModifiedPropertiesWithoutUndo();

            return panel;
        }

        private static Button BuildLeaveButton(RectTransform parent)
        {
            Vector2 bottomLeft = Vector2.zero;
            RectTransform buttonRect = CreateRect("Btn_Leave", parent);
            Place(buttonRect, bottomLeft, bottomLeft, new Vector2(40f, 120f), new Vector2(250f, 56f), bottomLeft);

            Image image = AddImage(buttonRect, _panelSprite, DraftPalette.PanelLight);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            RectTransform frameRect = CreateRect("Frame", buttonRect);
            Stretch(frameRect, 0f, 0f, 0f, 0f);
            Image frame = AddImage(frameRect, _outlineSprite, DraftPalette.FrameIdle);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;

            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = CreateButtonColors();

            TMP_Text label = CreateText("Txt_Leave", buttonRect, "ПОКИНУТЬ ДРАФТ", 18f, DraftPalette.TextLight, TextAlignmentOptions.Center, true);
            Stretch(label.rectTransform, 0f, 0f, 0f, 0f);
            label.characterSpacing = 3f;

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

        private static Image AddImage(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateStrike(RectTransform parent, float length, float thickness)
        {
            RectTransform strikeRect = CreateRect("BanStrike", parent);
            Place(strikeRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(length, thickness));
            strikeRect.localRotation = Quaternion.Euler(0f, 0f, 50f);
            Image strike = AddImage(strikeRect, null, DraftPalette.Critical);
            strike.enabled = false;
            return strike;
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
                Debug.LogError($"[DraftCanvasBuilder] Поле «{propertyName}» не найдено у {so.targetObject.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void SetArray<T>(SerializedObject so, string propertyName, T[] values) where T : Object
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[DraftCanvasBuilder] Массив «{propertyName}» не найден у {so.targetObject.GetType().Name}.");
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
