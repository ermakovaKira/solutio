using System.Collections.Generic;
using Solutio.Auth;
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
    /// Достраивает панель сброса пароля в AuthCanvas.prefab:
    /// создает поля «Новый пароль» / «Повторите пароль» (клоны Input_ResetEmail),
    /// чинит внутренние связи поля кода, раскладывает элементы панели,
    /// назначает ссылки ResetPasswordController и AuthViewController прямо в ассете префаба,
    /// затем снимает устаревшие override-ы этих компонентов в открытых сценах.
    /// Повторный запуск безопасен: существующие объекты переиспользуются и перенастраиваются.
    /// </summary>
    public static class AuthResetPanelBuilder
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/UI/AuthCanvas.prefab";

        private const string PanelName = "ResetPasswordPanel";
        private const string EmailFieldName = "Input_ResetEmail";
        private const string CodeFieldName = "Input_ResetCode";
        private const string NewPasswordFieldName = "Input_ResetNewPassword";
        private const string ConfirmPasswordFieldName = "Input_ResetConfirmPassword";
        private const string StatusTextName = "Txt_ResetStatus";
        private const string SubmitButtonName = "Btn_SubmitResetCode";
        private const string CancelButtonName = "Btn_CancelReset";
        private const string ForgotButtonName = "Btn_ForgotPassword";
        private const string TextAreaName = "Text Area";
        private const string TextName = "Text";
        private const string PlaceholderName = "Placeholder";

        private const int PasswordCharacterLimit = 64;

        // Раскладка панели 440×480 (центр в 0,0). Поля этапов 1–2 стоят в Y = 5, поля этапа 3 — ниже.
        private static readonly Vector2 FieldSize = new Vector2(340f, 50f);
        private static readonly Vector2 NewPasswordPosition = new Vector2(0f, 15f);
        private static readonly Vector2 ConfirmPasswordPosition = new Vector2(0f, -42f);
        private static readonly Vector2 StatusPosition = new Vector2(0f, -84f);
        private static readonly Vector2 StatusSize = new Vector2(380f, 28f);
        private static readonly Vector2 SubmitPosition = new Vector2(0f, -125f);
        private static readonly Vector2 CancelPosition = new Vector2(0f, -182f);

        private static readonly Color CaretColor = new Color32(0xE8, 0xF4, 0xF8, 0xFF);

        [MenuItem("Solutio/Достроить панель сброса пароля")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Solutio", "Остановите режим Play — изменения в Play не сохраняются.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                Debug.LogError($"[AuthResetPanelBuilder] Префаб не найден: {PrefabPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            bool saved = false;

            try
            {
                if (!BuildInsidePrefab(root))
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out saved);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            if (!saved)
            {
                Debug.LogError("[AuthResetPanelBuilder] Не удалось сохранить AuthCanvas.prefab.");
                return;
            }

            int revertedCount = RevertStaleSceneOverrides();

            Debug.Log("<color=#307082><b>[AuthResetPanelBuilder]</b></color> AuthCanvas.prefab обновлен: поля нового пароля, связи поля кода, раскладка и ссылки контроллеров. " +
                      $"Снято устаревших override-ов в сцене: {revertedCount}. Сохраните сцену (Ctrl+S).");
        }

        private static bool BuildInsidePrefab(GameObject root)
        {
            Transform panel = FindDeep(root.transform, PanelName);
            if (panel == null)
            {
                Debug.LogError($"[AuthResetPanelBuilder] В префабе не найден объект '{PanelName}'.");
                return false;
            }

            TMP_InputField emailField = FindComponent<TMP_InputField>(panel, EmailFieldName);
            TMP_InputField codeField = FindComponent<TMP_InputField>(panel, CodeFieldName);
            ResetPasswordController controller = panel.GetComponent<ResetPasswordController>();
            AuthViewController authView = root.GetComponentInChildren<AuthViewController>(true);

            if (emailField == null || codeField == null || controller == null || authView == null)
            {
                Debug.LogError("[AuthResetPanelBuilder] Не найдены Input_ResetEmail / Input_ResetCode / ResetPasswordController / AuthViewController.");
                return false;
            }

            // 1. Поле кода: связи только на собственных детей, текст включен, видимая каретка.
            if (!WireOwnChildren(codeField))
            {
                return false;
            }

            ApplyCaret(codeField);
            ApplyCaret(emailField);

            // 2. Поля нового пароля — клоны поля почты (Instantiate переносит внутренние связи на клон).
            TMP_InputField newPasswordField = GetOrCloneField(emailField, panel, NewPasswordFieldName, codeField.transform.GetSiblingIndex() + 1);
            TMP_InputField confirmPasswordField = GetOrCloneField(emailField, panel, ConfirmPasswordFieldName, newPasswordField.transform.GetSiblingIndex() + 1);

            if (!ConfigurePasswordField(newPasswordField, NewPasswordPosition, "Новый пароль...") ||
                !ConfigurePasswordField(confirmPasswordField, ConfirmPasswordPosition, "Повторите пароль..."))
            {
                return false;
            }

            // 3. Раскладка статуса и кнопок под поля этапа 3.
            SetRect(panel, StatusTextName, StatusPosition, StatusSize);
            SetRect(panel, SubmitButtonName, SubmitPosition, null);
            SetRect(panel, CancelButtonName, CancelPosition, null);

            // 4. Ссылки ResetPasswordController.
            TextMeshProUGUI submitCaption = null;
            Transform submitButton = panel.Find(SubmitButtonName);
            if (submitButton != null)
            {
                submitCaption = submitButton.GetComponentInChildren<TextMeshProUGUI>(true);
            }

            SerializedObject controllerSo = new SerializedObject(controller);
            bool ok = SetReference(controllerSo, "codeInputField", codeField)
                      & SetReference(controllerSo, "newPasswordInputField", newPasswordField)
                      & SetReference(controllerSo, "confirmPasswordInputField", confirmPasswordField)
                      & SetReference(controllerSo, "submitButtonText", submitCaption);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            // 5. AuthViewController прячет «Забыли пароль?» вместе с остальными полями входа.
            Button forgotButton = FindComponent<Button>(root.transform, ForgotButtonName);
            SerializedObject authViewSo = new SerializedObject(authView);
            ok &= SetReference(authViewSo, "forgotPasswordButton", forgotButton);
            authViewSo.ApplyModifiedPropertiesWithoutUndo();

            if (!ok)
            {
                Debug.LogError("[AuthResetPanelBuilder] Часть ссылок не назначена — проверьте, что скрипты обновлены и скомпилированы.");
                return false;
            }

            return true;
        }

        #region Fields

        private static TMP_InputField GetOrCloneField(TMP_InputField template, Transform parent, string fieldName, int siblingIndex)
        {
            Transform existing = parent.Find(fieldName);
            TMP_InputField field = existing != null ? existing.GetComponent<TMP_InputField>() : null;

            if (field == null)
            {
                GameObject clone = Object.Instantiate(template.gameObject, parent, false);
                clone.name = fieldName;
                field = clone.GetComponent<TMP_InputField>();
            }

            field.transform.SetSiblingIndex(Mathf.Min(siblingIndex, parent.childCount - 1));
            return field;
        }

        private static bool ConfigurePasswordField(TMP_InputField field, Vector2 position, string placeholderText)
        {
            if (!WireOwnChildren(field))
            {
                return false;
            }

            RectTransform rect = (RectTransform)field.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = FieldSize;

            field.contentType = TMP_InputField.ContentType.Password;
            field.characterLimit = PasswordCharacterLimit;
            field.text = string.Empty;
            field.interactable = true;
            ApplyCaret(field);

            if (field.placeholder is TMP_Text placeholder)
            {
                placeholder.text = placeholderText;
            }

            field.gameObject.SetActive(false);
            EditorUtility.SetDirty(field);
            return true;
        }

        /// <summary>
        /// Ставит Text Viewport / Text Component / Placeholder / Target Graphic на собственных детей поля
        /// и включает дочерний Text.
        /// </summary>
        private static bool WireOwnChildren(TMP_InputField field)
        {
            RectTransform textArea = field.transform.Find(TextAreaName) as RectTransform;
            Transform textTransform = textArea != null ? textArea.Find(TextName) : null;
            Transform placeholderTransform = textArea != null ? textArea.Find(PlaceholderName) : null;
            TMP_Text text = textTransform != null ? textTransform.GetComponent<TMP_Text>() : null;

            if (textArea == null || text == null)
            {
                Debug.LogError($"[AuthResetPanelBuilder] У поля '{field.name}' нет '{TextAreaName}/{TextName}'.");
                return false;
            }

            field.textViewport = textArea;
            field.textComponent = text;

            Graphic placeholder = placeholderTransform != null ? placeholderTransform.GetComponent<Graphic>() : null;
            if (placeholder != null)
            {
                field.placeholder = placeholder;
            }

            Graphic background = field.GetComponent<Graphic>();
            if (background != null)
            {
                field.targetGraphic = background;
            }

            text.gameObject.SetActive(true);
            EditorUtility.SetDirty(field);
            return true;
        }

        private static void ApplyCaret(TMP_InputField field)
        {
            field.customCaretColor = true;
            field.caretColor = CaretColor;
            EditorUtility.SetDirty(field);
        }

        #endregion

        #region Scene Overrides

        /// <summary>
        /// В открытых сценах снимает override-ы с ResetPasswordController и TMP_InputField поля кода
        /// у экземпляров AuthCanvas: теперь правильные значения лежат в самом префабе,
        /// а старые override-ы указывали в том числе на объекты RegistrationCanvas.
        /// </summary>
        private static int RevertStaleSceneOverrides()
        {
            int reverted = 0;
            List<ResetPasswordController> controllers = new List<ResetPasswordController>();

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
                    controllers.Clear();
                    sceneRoot.GetComponentsInChildren(true, controllers);

                    foreach (ResetPasswordController controller in controllers)
                    {
                        if (!PrefabUtility.IsPartOfPrefabInstance(controller))
                        {
                            continue;
                        }

                        if (RevertIfOverridden(controller))
                        {
                            reverted++;
                            sceneChanged = true;
                        }

                        TMP_InputField codeField = FindComponent<TMP_InputField>(controller.transform, CodeFieldName);
                        if (codeField != null && RevertIfOverridden(codeField))
                        {
                            reverted++;
                            sceneChanged = true;
                        }
                    }
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            return reverted;
        }

        private static bool RevertIfOverridden(Object target)
        {
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(target);
            if (modifications == null)
            {
                return false;
            }

            Object source = PrefabUtility.GetCorrespondingObjectFromSource(target);
            bool hasOwnOverrides = false;

            foreach (PropertyModification modification in modifications)
            {
                if (modification.target == source)
                {
                    hasOwnOverrides = true;
                    break;
                }
            }

            if (!hasOwnOverrides)
            {
                return false;
            }

            PrefabUtility.RevertObjectOverride(target, InteractionMode.AutomatedAction);
            return true;
        }

        #endregion

        #region Utilities

        private static void SetRect(Transform panel, string childName, Vector2 position, Vector2? size)
        {
            RectTransform rect = panel.Find(childName) as RectTransform;
            if (rect == null)
            {
                Debug.LogWarning($"[AuthResetPanelBuilder] Не найден '{childName}' — раскладка для него пропущена.");
                return;
            }

            rect.anchoredPosition = position;
            if (size.HasValue)
            {
                rect.sizeDelta = size.Value;
            }

            EditorUtility.SetDirty(rect);
        }

        private static bool SetReference(SerializedObject serializedObject, string propertyName, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"[AuthResetPanelBuilder] У {serializedObject.targetObject.GetType().Name} нет поля '{propertyName}'.");
                return false;
            }

            if (value == null)
            {
                Debug.LogError($"[AuthResetPanelBuilder] Не найден объект для поля '{propertyName}'.");
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static T FindComponent<T>(Transform root, string objectName) where T : Component
        {
            Transform found = FindDeep(root, objectName);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindDeep(Transform root, string objectName)
        {
            if (root.name == objectName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), objectName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        #endregion
    }
}
