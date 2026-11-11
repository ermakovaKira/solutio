using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace Solutio.EditorTools
{
    /// <summary>
    /// Применяет шрифты оформления ко всем текстам TextMeshPro проекта:
    /// Underdog — весь интерфейс (кириллица + латиница), New Rocker — логотип «SOLUTIO».
    /// 1) Создает TMP-ассеты шрифтов (SDF, атлас 2048, заранее запеченный набор символов, затем Static),
    ///    LiberationSans SDF — запасной шрифт для редких символов.
    /// 2) Перезаписывает шрифт во всех префабах Assets/_Project/Prefabs (без вложенных экземпляров).
    /// 3) Обрабатывает открытые сцены: объекты вне префабов меняет напрямую, у экземпляров префабов снимает override шрифта.
    /// 4) Делает Underdog шрифтом по умолчанию в TMP Settings — его подхватывают новые тексты и DraftCanvasBuilder.
    /// Повторный запуск безопасен: готовые ассеты переиспользуются, совпадающие тексты не трогаются.
    /// </summary>
    public static class SolutioFontApplier
    {
        private const string FontFolder = "Assets/_Project/Fonts";
        private const string BodyTtfPath = FontFolder + "/Underdog-Regular.ttf";
        private const string LogoTtfPath = FontFolder + "/NewRocker-Regular.ttf";
        private const string BodyAssetPath = FontFolder + "/Underdog SDF.asset";
        private const string LogoAssetPath = FontFolder + "/NewRocker SDF.asset";
        private const string FallbackAssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const string PrefabFolder = "Assets/_Project/Prefabs";

        private const string LogoText = "SOLUTIO";

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 2048;

        private const string CyrillicCharacters =
            "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        private const string TypographyCharacters = "—–«»…№·’‘“”„•×°";

        // Отладочные тексты остаются на LiberationSans: моноширинные по смыслу цифры счетчика FPS читаются лучше
        private static readonly HashSet<string> SkippedObjectNames = new HashSet<string> { "Txt_Fps" };

        private static readonly Regex RichTextTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);

        [MenuItem("Solutio/Применить шрифты (Underdog + New Rocker)")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Solutio", "Остановите режим Play — изменения в Play не сохраняются.", "OK");
                return;
            }

            TMP_FontAsset fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackAssetPath);
            if (fallback == null)
            {
                Debug.LogWarning($"[SolutioFontApplier] Не найден запасной шрифт {FallbackAssetPath} — редкие символы могут не отображаться.");
            }

            TMP_FontAsset bodyFont = GetOrCreateFontAsset(BodyTtfPath, BodyAssetPath, BuildBodyCharacters(), fallback);
            TMP_FontAsset logoFont = GetOrCreateFontAsset(LogoTtfPath, LogoAssetPath, BuildAsciiCharacters(), fallback);

            if (bodyFont == null || logoFont == null)
            {
                EditorUtility.DisplayDialog("Solutio", "Не удалось подготовить шрифты — подробности в Console.", "OK");
                return;
            }

            int prefabTexts = ApplyToPrefabs(bodyFont, logoFont, out int prefabCount);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int sceneTexts = ApplyToOpenScenes(bodyFont, logoFont);
            bool settingsUpdated = SetDefaultFont(bodyFont);

            AssetDatabase.SaveAssets();

            Debug.Log("<color=#307082><b>[SolutioFontApplier]</b></color> Шрифты применены. " +
                      $"Префабов обновлено: {prefabCount} (текстов: {prefabTexts}); текстов в сцене: {sceneTexts}; " +
                      $"TMP Settings: {(settingsUpdated ? "шрифт по умолчанию — Underdog SDF" : "без изменений")}. Сохраните сцену (Ctrl+S).");
        }

        #region Font Assets

        private static TMP_FontAsset GetOrCreateFontAsset(string ttfPath, string assetPath, string characters, TMP_FontAsset fallback)
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                EnsureFallback(existing, fallback);
                return existing;
            }

            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (sourceFont == null)
            {
                Debug.LogError($"[SolutioFontApplier] Не найден файл шрифта {ttfPath}. Положите .ttf в {FontFolder}.");
                return null;
            }

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                SamplingPointSize,
                AtlasPadding,
                GlyphRenderMode.SDFAA,
                AtlasSize,
                AtlasSize,
                AtlasPopulationMode.Dynamic,
                false);

            if (fontAsset == null)
            {
                Debug.LogError($"[SolutioFontApplier] TextMeshPro не смог создать ассет из {ttfPath}.");
                return null;
            }

            string assetName = Path.GetFileNameWithoutExtension(assetPath);
            fontAsset.name = assetName;
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            Texture2D atlas = fontAsset.atlasTextures[0];
            atlas.name = assetName + " Atlas";
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);

            Material material = fontAsset.material;
            material.name = assetName + " Material";
            AssetDatabase.AddObjectToAsset(material, fontAsset);

            if (!fontAsset.TryAddCharacters(characters, out string missingCharacters))
            {
                string visibleMissing = missingCharacters != null ? missingCharacters.Trim() : string.Empty;
                if (visibleMissing.Length > 0)
                {
                    Debug.LogWarning($"[SolutioFontApplier] {assetName}: в шрифте нет символов «{visibleMissing}» — они возьмутся из запасного шрифта.");
                }
            }

            // Набор символов запечен в атлас заранее: в Static атлас не меняется в рантайме и в билде
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            EnsureFallback(fontAsset, fallback);

            EditorUtility.SetDirty(atlas);
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SolutioFontApplier] Создан {assetPath}: символов в атласе {fontAsset.characterTable.Count}.");
            return fontAsset;
        }

        private static void EnsureFallback(TMP_FontAsset fontAsset, TMP_FontAsset fallback)
        {
            if (fallback == null || fontAsset == fallback)
            {
                return;
            }

            if (fontAsset.fallbackFontAssetTable == null)
            {
                fontAsset.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }

            if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
            {
                fontAsset.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(fontAsset);
            }
        }

        private static string BuildAsciiCharacters()
        {
            StringBuilder builder = new StringBuilder(95);
            for (char c = (char)32; c <= (char)126; c++)
            {
                builder.Append(c);
            }

            return builder.ToString();
        }

        private static string BuildBodyCharacters()
        {
            return BuildAsciiCharacters() + CyrillicCharacters + TypographyCharacters;
        }

        #endregion

        #region Prefabs

        private static int ApplyToPrefabs(TMP_FontAsset bodyFont, TMP_FontAsset logoFont, out int changedPrefabs)
        {
            changedPrefabs = 0;
            int changedTexts = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });

            List<TMP_Text> texts = new List<TMP_Text>();
            List<TMP_InputField> inputs = new List<TMP_InputField>();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);

                try
                {
                    int changedHere = 0;

                    texts.Clear();
                    root.GetComponentsInChildren(true, texts);
                    foreach (TMP_Text text in texts)
                    {
                        // Вложенные префабы обрабатываются в своих ассетах
                        if (PrefabUtility.IsPartOfPrefabInstance(text))
                        {
                            continue;
                        }

                        if (ApplyToText(text, bodyFont, logoFont))
                        {
                            changedHere++;
                        }
                    }

                    inputs.Clear();
                    root.GetComponentsInChildren(true, inputs);
                    foreach (TMP_InputField input in inputs)
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(input) && ApplyToInputField(input, bodyFont))
                        {
                            changedHere++;
                        }
                    }

                    if (changedHere > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                        if (saved)
                        {
                            changedPrefabs++;
                            changedTexts += changedHere;
                        }
                        else
                        {
                            Debug.LogError($"[SolutioFontApplier] Не удалось сохранить {path}.");
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return changedTexts;
        }

        #endregion

        #region Scenes

        private static int ApplyToOpenScenes(TMP_FontAsset bodyFont, TMP_FontAsset logoFont)
        {
            int changedTexts = 0;
            List<TMP_Text> texts = new List<TMP_Text>();
            List<TMP_InputField> inputs = new List<TMP_InputField>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                bool sceneChanged = false;

                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                {
                    texts.Clear();
                    sceneRoot.GetComponentsInChildren(true, texts);

                    foreach (TMP_Text text in texts)
                    {
                        if (SkippedObjectNames.Contains(text.name))
                        {
                            continue;
                        }

                        TMP_FontAsset target = ResolveTarget(text, bodyFont, logoFont);

                        if (PrefabUtility.IsPartOfPrefabInstance(text) && text.font != target)
                        {
                            RevertFontOverride(text);
                        }

                        if (ApplyToText(text, bodyFont, logoFont))
                        {
                            changedTexts++;
                            sceneChanged = true;
                        }
                    }

                    inputs.Clear();
                    sceneRoot.GetComponentsInChildren(true, inputs);
                    foreach (TMP_InputField input in inputs)
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(input) && ApplyToInputField(input, bodyFont))
                        {
                            sceneChanged = true;
                        }
                    }
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            return changedTexts;
        }

        private static void RevertFontOverride(TMP_Text text)
        {
            SerializedObject serializedText = new SerializedObject(text);
            RevertIfOverridden(serializedText.FindProperty("m_fontAsset"));
            RevertIfOverridden(serializedText.FindProperty("m_sharedMaterial"));
        }

        private static void RevertIfOverridden(SerializedProperty property)
        {
            if (property != null && property.prefabOverride)
            {
                PrefabUtility.RevertPropertyOverride(property, InteractionMode.AutomatedAction);
            }
        }

        #endregion

        #region Text Helpers

        private static TMP_FontAsset ResolveTarget(TMP_Text text, TMP_FontAsset bodyFont, TMP_FontAsset logoFont)
        {
            string plain = RichTextTagRegex.Replace(text.text ?? string.Empty, string.Empty).Trim();
            return string.Equals(plain, LogoText, StringComparison.OrdinalIgnoreCase) ? logoFont : bodyFont;
        }

        private static bool ApplyToText(TMP_Text text, TMP_FontAsset bodyFont, TMP_FontAsset logoFont)
        {
            if (SkippedObjectNames.Contains(text.name))
            {
                return false;
            }

            TMP_FontAsset target = ResolveTarget(text, bodyFont, logoFont);
            if (text.font == target && text.fontSharedMaterial == target.material)
            {
                return false;
            }

            text.font = target;
            text.fontSharedMaterial = target.material;
            EditorUtility.SetDirty(text);
            return true;
        }

        /// <summary>
        /// Обновляет только сериализованное поле Font Asset поля ввода (то, что показывает Инспектор).
        /// Сами тексты поля уже переключены как обычные TMP_Text.
        /// </summary>
        private static bool ApplyToInputField(TMP_InputField input, TMP_FontAsset bodyFont)
        {
            SerializedObject serializedInput = new SerializedObject(input);
            SerializedProperty fontProperty = serializedInput.FindProperty("m_GlobalFontAsset");

            if (fontProperty == null || fontProperty.objectReferenceValue == bodyFont)
            {
                return false;
            }

            fontProperty.objectReferenceValue = bodyFont;
            serializedInput.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool SetDefaultFont(TMP_FontAsset bodyFont)
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
            {
                Debug.LogWarning($"[SolutioFontApplier] Не найден {TmpSettingsPath} — шрифт по умолчанию не изменен.");
                return false;
            }

            SerializedObject serializedSettings = new SerializedObject(settings);
            SerializedProperty defaultFont = serializedSettings.FindProperty("m_defaultFontAsset");

            if (defaultFont == null || defaultFont.objectReferenceValue == bodyFont)
            {
                return false;
            }

            defaultFont.objectReferenceValue = bodyFont;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return true;
        }

        #endregion
    }
}
