using System;
using DG.Tweening;
using Solutio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Auth
{
    /// <summary>
    /// Контроллер трехэтапного сброса пароля:
    /// Этап 1: ввод Email, проверка по таблице users и отправка письма с кодом.
    /// Этап 2: ввод 6-значного кода и подтверждение.
    /// Этап 3: ввод и повтор нового пароля, запись хэша в users через AuthController.
    /// </summary>
    public sealed class ResetPasswordController : MonoBehaviour
    {
        private enum ResetStage
        {
            Email,
            Code,
            NewPassword,
            Completed
        }

        [Header("UI References - Stage 1 (Email)")]
        [SerializeField] private TMP_InputField emailInputField;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TextMeshProUGUI submitButtonText;
        [SerializeField] private TextMeshProUGUI statusFeedbackText;
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Header("UI References - Stage 2 (Code)")]
        [SerializeField] private TMP_InputField codeInputField;

        [Header("UI References - Stage 3 (New Password)")]
        [SerializeField] private TMP_InputField newPasswordInputField;
        [SerializeField] private TMP_InputField confirmPasswordInputField;

        [Header("Auth View Reference")]
        [SerializeField] private AuthViewController authViewController;

        [Header("Timing")]
        [Tooltip("Пауза между сообщением об успешной смене пароля и закрытием панели.")]
        [SerializeField] private float successCloseDelaySeconds = 1.5f;

        [Header("Visual Theme Settings")]
        [SerializeField] private Color successColor = new Color(0.19f, 0.44f, 0.51f, 1f); // #307082
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        private const string CodeTextAreaName = "Text Area";
        private const string CodeTextName = "Text";
        private const string CodePlaceholderName = "Placeholder";

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private Tween _shakeTween;
        private Tween _closeDelayTween;
        private ResetStage _stage = ResetStage.Email;
        private bool _isCodeFieldWired = false;
        private bool _isSending = false;

        private void Awake()
        {
            EnsureComponents();

            if (submitButton != null)
                submitButton.onClick.AddListener(HandleSubmit);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(HandleCancel);
        }

        private void EnsureComponents()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
                if (_canvasGroup == null)
                    _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }
        }

        private void OnDestroy()
        {
            if (submitButton != null) submitButton.onClick.RemoveAllListeners();
            if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
            _shakeTween?.Kill();
            _closeDelayTween?.Kill();
        }

        /// <summary>
        /// Открывает панель на первом этапе. Вызывается кнопкой «Забыли пароль?» (persistent listener).
        /// </summary>
        public void ShowPanel()
        {
            EnsureComponents();
            _closeDelayTween?.Kill();

            if (authViewController != null)
            {
                authViewController.SetFieldsActive(false);
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            ResetToStageOne();

            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
            _canvasGroup.interactable = true;

            _rectTransform.localScale = new Vector3(0.85f, 0.85f, 1f);
            _rectTransform.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        /// <summary>
        /// Закрывает панель и возвращает форму входа. onHidden вызывается после того, как поля входа снова видны.
        /// </summary>
        public void HidePanel(Action onHidden = null)
        {
            EnsureComponents();
            _closeDelayTween?.Kill();

            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            _canvasGroup.DOFade(0f, 0.2f).SetUpdate(true);
            _rectTransform.DOScale(0.85f, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                gameObject.SetActive(false);

                if (authViewController != null)
                {
                    authViewController.SetFieldsActive(true);
                }

                onHidden?.Invoke();
            });
        }

        private void HandleCancel()
        {
            if (AuthController.Instance != null)
            {
                AuthController.Instance.CancelPasswordReset();
            }

            HidePanel();
        }

        #region Stages

        private void ResetToStageOne()
        {
            _stage = ResetStage.Email;
            _isSending = false;

            SetFieldVisible(emailInputField, true);
            SetFieldVisible(codeInputField, false);
            SetFieldVisible(newPasswordInputField, false);
            SetFieldVisible(confirmPasswordInputField, false);

            SetDescription("Введите Email, привязанный к вашему профилю. Мы вышлем код восстановления:");
            SetSubmitCaption("ВЫСЛАТЬ КОД");
            ClearStatus();

            if (submitButton != null) submitButton.interactable = true;
            if (cancelButton != null) cancelButton.interactable = true;
        }

        private void SwitchToStageTwo(string email)
        {
            _stage = ResetStage.Code;

            SetFieldVisible(emailInputField, false);

            if (codeInputField != null)
            {
                // Связи поля перестраиваются, пока оно выключено: в OnEnable TMP_InputField
                // создает каретку внутри Text Viewport и подписывается на свой Text Component.
                if (codeInputField.gameObject.activeSelf) codeInputField.gameObject.SetActive(false);
                EnsureCodeFieldWiring();
            }

            SetFieldVisible(codeInputField, true);

            SetDescription($"Код выслан на: <color=#307082><b>{email}</b></color>\nВведите 6 цифр для подтверждения:");
            SetSubmitCaption("ПОДТВЕРДИТЬ КОД");
            ClearStatus();

            if (submitButton != null) submitButton.interactable = true;
            FocusField(codeInputField);
        }

        private void SwitchToStageThree()
        {
            _stage = ResetStage.NewPassword;

            SetFieldVisible(codeInputField, false);
            SetFieldVisible(newPasswordInputField, true);
            SetFieldVisible(confirmPasswordInputField, true);

            SetDescription($"Код подтвержден. Придумайте новый пароль (не менее {AuthController.MinPasswordLength} символов):");
            SetSubmitCaption("СОХРАНИТЬ ПАРОЛЬ");
            ShowStatus("Email подтвержден!", successColor);

            if (submitButton != null) submitButton.interactable = true;
            FocusField(newPasswordInputField);
        }

        #endregion

        #region Submit

        private void HandleSubmit()
        {
            switch (_stage)
            {
                case ResetStage.Email:
                    SubmitEmailAsync();
                    break;
                case ResetStage.Code:
                    SubmitCode();
                    break;
                case ResetStage.NewPassword:
                    SubmitNewPassword();
                    break;
            }
        }

        private async void SubmitEmailAsync()
        {
            if (_isSending)
            {
                return;
            }

            string rawEmail = emailInputField != null ? emailInputField.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(rawEmail))
            {
                ShowError("Введите адрес электронной почты!");
                return;
            }

            if (!EmailValidator.ValidateEmail(rawEmail, out string sanitizedEmail, out string validationError))
            {
                ShowError(validationError);
                return;
            }

            AuthController auth = AuthController.Instance;

            if (auth == null)
            {
                ShowError("Ошибка: AuthController не найден!");
                return;
            }

            if (!auth.IsEmailRegistered(sanitizedEmail, out string lookupError))
            {
                ShowError(lookupError);
                return;
            }

            ShowStatus("Отправка тайного кода...", successColor);
            _isSending = true;
            if (submitButton != null) submitButton.interactable = false;

            bool isSent = await auth.SendVerificationCodeAsync(sanitizedEmail, isPasswordReset: true);

            _isSending = false;

            // Панель могли закрыть или уничтожить, пока письмо уходило
            if (this == null || !gameObject.activeInHierarchy || _stage != ResetStage.Email)
            {
                return;
            }

            if (isSent)
            {
                SwitchToStageTwo(sanitizedEmail);
            }
            else
            {
                ShowError("Не удалось отправить код. Проверьте соединение.");
                if (submitButton != null) submitButton.interactable = true;
            }
        }

        private void SubmitCode()
        {
            string enteredCode = codeInputField != null ? codeInputField.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(enteredCode) || enteredCode.Length < 6)
            {
                ShowError("Введите все 6 цифр кода!");
                return;
            }

            if (AuthController.Instance == null)
            {
                ShowError("Ошибка: AuthController не найден на сцене!");
                return;
            }

            if (AuthController.Instance.VerifyPasswordResetCode(enteredCode, out string errorMsg))
            {
                Debug.Log("<color=#307082>[PASSWORD RESET] Код подтвержден, личность верифицирована — этап нового пароля.</color>");
                SwitchToStageThree();
            }
            else
            {
                ShowError(errorMsg);
            }
        }

        private void SubmitNewPassword()
        {
            string newPassword = newPasswordInputField != null ? newPasswordInputField.text : string.Empty;
            string confirmPassword = confirmPasswordInputField != null ? confirmPasswordInputField.text : string.Empty;

            if (string.IsNullOrEmpty(newPassword))
            {
                ShowError("Введите новый пароль!");
                return;
            }

            if (newPassword.Length < AuthController.MinPasswordLength)
            {
                ShowError($"Пароль должен содержать не менее {AuthController.MinPasswordLength} символов!");
                return;
            }

            if (string.IsNullOrEmpty(confirmPassword))
            {
                ShowError("Повторите новый пароль!");
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                ShowError("Введенные пароли не совпадают!");
                return;
            }

            if (AuthController.Instance == null)
            {
                ShowError("Ошибка: AuthController не найден на сцене!");
                return;
            }

            if (!AuthController.Instance.TryResetPassword(newPassword, out string userLogin, out string resetError))
            {
                ShowError(resetError);

                // Окно смены пароля истекло или почта пропала из базы — начинаем заново
                if (!AuthController.Instance.HasVerifiedPasswordReset)
                {
                    if (submitButton != null) submitButton.interactable = false;
                    _closeDelayTween?.Kill();
                    _closeDelayTween = DOVirtual.DelayedCall(successCloseDelaySeconds, ResetToStageOne)
                        .SetRecyclable(false)
                        .SetUpdate(true)
                        .SetLink(gameObject);
                }

                return;
            }

            _stage = ResetStage.Completed;
            ClearPasswordFields();

            if (submitButton != null) submitButton.interactable = false;
            if (cancelButton != null) cancelButton.interactable = false;
            if (newPasswordInputField != null) newPasswordInputField.interactable = false;
            if (confirmPasswordInputField != null) confirmPasswordInputField.interactable = false;

            ShowStatus("Пароль обновлен! Войдите с новым паролем.", successColor);

            _closeDelayTween?.Kill();
            _closeDelayTween = DOVirtual.DelayedCall(successCloseDelaySeconds, () =>
                {
                    HidePanel(() =>
                    {
                        if (authViewController != null)
                        {
                            authViewController.SetLogin(userLogin);
                        }
                    });
                })
                .SetRecyclable(false)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        #endregion

        #region UI Helpers

        private static void SetFieldVisible(TMP_InputField field, bool isVisible)
        {
            if (field == null)
            {
                return;
            }

            field.text = string.Empty;
            field.interactable = isVisible;
            field.gameObject.SetActive(isVisible);
        }

        private static void FocusField(TMP_InputField field)
        {
            if (field != null && field.gameObject.activeInHierarchy)
            {
                field.Select();
                field.ActivateInputField();
            }
        }

        private void ClearPasswordFields()
        {
            if (newPasswordInputField != null) newPasswordInputField.text = string.Empty;
            if (confirmPasswordInputField != null) confirmPasswordInputField.text = string.Empty;
        }

        private void SetDescription(string text)
        {
            if (descriptionText != null) descriptionText.text = text;
        }

        private void SetSubmitCaption(string text)
        {
            if (submitButtonText != null) submitButtonText.text = text;
        }

        private void ShowStatus(string message, Color color)
        {
            if (statusFeedbackText == null)
            {
                return;
            }

            statusFeedbackText.text = message;
            statusFeedbackText.color = color;
        }

        private void ClearStatus()
        {
            if (statusFeedbackText != null) statusFeedbackText.text = string.Empty;
        }

        private void ShowError(string message)
        {
            ShowStatus(message, failureColor);

            _shakeTween?.Kill();
            _rectTransform.anchoredPosition = new Vector2(0, -15);
            _shakeTween = _rectTransform.DOShakeAnchorPos(0.35f, new Vector2(15f, 0f), 20, 90, false, true)
                .SetRecyclable(false)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        #endregion

        #region Code Field Self-Check

        /// <summary>
        /// Привязывает поле кода к его собственным дочерним объектам (Text Area / Text / Placeholder)
        /// и к своему фону. Защищает от ссылок на одноименные объекты другого канваса
        /// (например, на поле кода RegistrationCanvas), из-за которых ввод уходил в скрытый текст.
        /// Вызывается при выключенном поле, один раз за жизнь компонента.
        /// </summary>
        private void EnsureCodeFieldWiring()
        {
            if (_isCodeFieldWired || codeInputField == null)
            {
                return;
            }

            Transform fieldRoot = codeInputField.transform;
            RectTransform textArea = fieldRoot.Find(CodeTextAreaName) as RectTransform;

            if (textArea == null)
            {
                Debug.LogError($"[ResetPasswordController] У поля '{codeInputField.name}' не найден дочерний объект '{CodeTextAreaName}'. Привязка не выполнена.");
                return;
            }

            Transform textTransform = textArea.Find(CodeTextName);
            Transform placeholderTransform = textArea.Find(CodePlaceholderName);
            TMP_Text ownText = textTransform != null ? textTransform.GetComponent<TMP_Text>() : null;
            Graphic ownPlaceholder = placeholderTransform != null ? placeholderTransform.GetComponent<Graphic>() : null;
            Graphic ownBackground = codeInputField.GetComponent<Graphic>();

            if (ownText == null)
            {
                Debug.LogError($"[ResetPasswordController] У поля '{codeInputField.name}' не найден '{CodeTextAreaName}/{CodeTextName}' с компонентом TMP_Text. Привязка не выполнена.");
                return;
            }

            int fixedCount = 0;

            if (codeInputField.textViewport != textArea)
            {
                LogRewire("Text Viewport", codeInputField.textViewport, textArea);
                codeInputField.textViewport = textArea;
                fixedCount++;
            }

            if (codeInputField.textComponent != ownText)
            {
                LogRewire("Text Component", codeInputField.textComponent, ownText);
                codeInputField.textComponent = ownText;
                fixedCount++;
            }

            if (ownPlaceholder != null && codeInputField.placeholder != ownPlaceholder)
            {
                LogRewire("Placeholder", codeInputField.placeholder, ownPlaceholder);
                codeInputField.placeholder = ownPlaceholder;
                fixedCount++;
            }

            if (ownBackground != null && codeInputField.targetGraphic != ownBackground)
            {
                LogRewire("Target Graphic", codeInputField.targetGraphic, ownBackground);
                codeInputField.targetGraphic = ownBackground;
                fixedCount++;
            }

            if (!ownText.gameObject.activeSelf)
            {
                Debug.LogWarning($"[ResetPasswordController] Объект '{CodeTextAreaName}/{CodeTextName}' поля '{codeInputField.name}' был выключен — включаю, иначе введенные цифры не видны.");
                ownText.gameObject.SetActive(true);
                fixedCount++;
            }

            _isCodeFieldWired = true;

            if (fixedCount > 0)
            {
                Debug.LogWarning($"[ResetPasswordController] Поле кода '{codeInputField.name}': исправлено связей — {fixedCount}. Запустите «Solutio → Достроить панель сброса пароля», чтобы исправить префаб.");
            }
            else
            {
                Debug.Log($"[ResetPasswordController] Поле кода '{codeInputField.name}': все связи корректны.");
            }
        }

        private static void LogRewire(string fieldLabel, UnityEngine.Object previous, UnityEngine.Object current)
        {
            string previousPath = previous != null ? GetHierarchyPath(previous) : "None";
            string currentPath = GetHierarchyPath(current);
            Debug.LogWarning($"[ResetPasswordController] {fieldLabel}: '{previousPath}' → '{currentPath}'.");
        }

        private static string GetHierarchyPath(UnityEngine.Object target)
        {
            Transform node = null;

            if (target is Component component)
            {
                node = component.transform;
            }
            else if (target is GameObject gameObjectTarget)
            {
                node = gameObjectTarget.transform;
            }

            if (node == null)
            {
                return target.name;
            }

            string path = node.name;
            Transform parent = node.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        #endregion
    }
}
