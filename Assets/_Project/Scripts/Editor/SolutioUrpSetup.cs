using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Solutio.EditorTools
{
    /// <summary>
    /// День 14. Однократная настройка Universal Render Pipeline с 2D Renderer:
    /// создает URP_2D и URP_2D_Renderer в Assets/_Project/Settings и назначает их
    /// в Graphics и во всех уровнях Quality. Повторный запуск безопасен.
    /// </summary>
    public static class SolutioUrpSetup
    {
        private const string ParentFolder = "Assets/_Project";
        private const string SettingsFolderName = "Settings";
        private const string SettingsFolder = ParentFolder + "/" + SettingsFolderName;
        private const string RendererPath = SettingsFolder + "/URP_2D_Renderer.asset";
        private const string PipelinePath = SettingsFolder + "/URP_2D.asset";

        [MenuItem("Solutio/Настроить URP 2D")]
        public static void Setup()
        {
            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder(ParentFolder, SettingsFolderName);
            }

            Renderer2DData rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            EditorUtility.SetDirty(pipeline);
            EditorUtility.SetDirty(rendererData);

            GraphicsSettings.defaultRenderPipeline = pipeline;

            int currentLevel = QualitySettings.GetQualityLevel();
            int levelCount = QualitySettings.names.Length;
            for (int level = 0; level < levelCount; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(currentLevel, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SolutioUrpSetup] URP 2D назначен: {PipelinePath} (рендерер {RendererPath}), уровней Quality: {levelCount}.");
        }
    }
}
