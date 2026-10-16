using System;
using System.Text.RegularExpressions;
using DG.Tweening;
using Solutio.Auth;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.UI
{
    /// <summary>
    /// Контроллер формы регистрации новых пользователей.
    /// Реализует валидацию, предварительную проверку базы данных SQLite,
    /// вызов SMTP-верификации и переключение на панель ввода кода.
    /// </summary>
    public sealed class RegistrationViewController : MonoBehaviour
    {
        [Header("UI Architecture & Containers")]
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private RectTransform modalContainer;
        [SerializeField] private UIBlurEffect backgroundBlur;

        [Header("Verification Panel Reference")]
        [SerializeField] private VerificationPanelController verificationPanel;

        [Header("Input Fields")]
        [SerializeField] private TMP_InputField loginInputField;
        [SerializeField] private TMP_InputField emailInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private TMP_InputField confirmPasswordInputField;

        [Header("Field Borders / Highlights")]
        [SerializeField] private Image loginFieldBorder;
        [SerializeField] private Image emailFieldBorder;
        [SerializeField] private Image passwordFieldBorder;
        [SerializeField] private Image confirmPasswordFieldBorder;

        [Header("Buttons")]
        [SerializeField] private Button submitRegisterButton;
        [SerializeField] private Button switchToLoginButton;
        [SerializeField] private Button togglePasswordButton;
        [SerializeField] private Image togglePasswordIcon;

        [Header("Feedback Elements")]
        [SerializeField] private TextMeshProUGUI statusFeedbackText;
        [SerializeField] private Sprite passwordHiddenSprite;
        [SerializeField] private Sprite passwordVisibleSprite;

        [Header("Visual Theme Settings")]
        [SerializeField] private Color normalBorderColor = new Color(0.05f, 0.07f, 0.11f, 0.9f);
        [SerializeField] private Color errorBorderColor = new Color(0.95f, 0.15f, 0.25f, 0.95f);
        [SerializeField] private Color successColor = new Color(0.19f, 0.44f, 0.51f, 1f);
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        // Регулярные выражения
        private static readonly Regex LoginRegex = new Regex(@"^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);

        private bool _isPasswordVisible;
        private Tween _statusFadeTween;
        private Tween _shakeTween;

        public event Action<string, string, string> OnRegistrationSubmitted;
        public event Action OnNavigateToLoginRequested;
        public event Action<string> OnRegistrationCompleted;

        private void Awake()
        {
            ValidateSerializedFields();
            RegisterEventHandlers();
            ResetFormState();

            if (submitRegisterButton != null)
            {
                TMP_Text btnText = submitRegisterButton.GetComponentInChildren<TMP_Text>();
                if (btnText != null) btnText.text = "СОЗДАТЬ АККАУНТ";
            }

            if (switchToLoginButton != null)
            {
                TMP_Text switchText = switchToLoginButton.GetComponentInChildren<TMP_Text>();
                if (switchText != null) switchText.text = "Уже есть аккаунт? Войти";
            }
        }

        private void Start()
        {
            // Подписываемся на системные события контроллера авторизации
            if (AuthController.Instance != null)
            {
                AuthController.Instance.OnCodeSentSuccessfully += HandleCodeSentSuccessfully;
                AuthController.Instance.OnCodeSendFailed += HandleCodeSendFailed;
                AuthController.Instance.OnUserRegisteredSuccessfully += HandleUserRegisteredInDatabase;
            }
        }

        private void OnDestroy()
        {
            UnregisterEventHandlers();
            _statusFadeTween?.Kill();
            _shakeTween?.Kill();

            if (AuthController.Instance != null)
            {
                AuthController.Instance.OnCodeSentSuccessfully -= HandleCodeSentSuccessfully;
                AuthController.Instance.OnCodeSendFailed -= HandleCodeSendFailed;
                AuthController.Instance.OnUserRegisteredSuccessfully -= HandleUserRegisteredInDatabase;
            }
        }

        private void RegisterEventHandlers()
        {
            submitRegisterButton.onClick.AddListener(HandleSubmit);
            switchToLoginButton.onClick.AddListener(() => OnNavigateToLoginRequested?.Invoke());
            togglePasswordButton.onClick.AddListener(TogglePasswordVisibility);

            loginInputField.onValueChanged.AddListener(_ => ClearFieldStatus(loginFieldBorder));
            emailInputField.onValueChanged.AddListener(_ => ClearFieldStatus(emailFieldBorder));
            passwordInputField.onValueChanged.AddListener(_ => ClearFieldStatus(passwordFieldBorder));
            confirmPasswordInputField.onValueChanged.AddListener(_ => ClearFieldStatus(confirmPasswordFieldBorder));
        }

        private void UnregisterEventHandlers()
        {
            submitRegisterButton.onClick.RemoveAllListeners();
            switchToLoginButton.onClick.RemoveAllListeners();
            togglePasswordButton.onClick.RemoveAllListeners();

            loginInputField.onValueChanged.RemoveAllListeners();
            emailInputField.onValueChanged.RemoveAllListeners();
            passwordInputField.onValueChanged.RemoveAllListeners();
            confirmPasswordInputField.onValueChanged.RemoveAllListeners();
        }

        public void ShowModal()
        {
            gameObject.SetActive(true);
            SetFieldsActive(true);
            SetInteractive(true);

            if (verificationPanel != null)
            {
                verificationPanel.gameObject.SetActive(false);
            }

            rootCanvasGroup.alpha = 0f;
            modalContainer.localScale = new Vector3(0.85f, 0.85f, 1f);

            if (backgroundBlur != null)
            {
                backgroundBlur.EnableBlur();
            }

            rootCanvasGroup.DOFade(1f, 0.3f).SetUpdate(true);
            modalContainer.DOScale(1f, 0.4f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void HideModal(Action onComplete = null)
        {
            if (backgroundBlur != null)
            {
                backgroundBlur.DisableBlur();
            }

            rootCanvasGroup.DOFade(0f, 0.25f).SetUpdate(true);
            modalContainer.DOScale(0.85f, 0.25f).SetEase(Ease.InBack).SetUpdate(true).OnComplete(() =>
            {
                gameObject.SetActive(false);
                onComplete?.Invoke();
            });
        }

        /// <summary>
        /// Управляет видимостью полей формы для предотвращения визуального наложения на VerificationPanel.
        /// </summary>
        public void SetFieldsActive(bool isActive)
        {
            if (loginInputField != null) loginInputField.gameObject.SetActive(isActive);
            if (emailInputField != null) emailInputField.gameObject.SetActive(isActive);
            if (passwordInputField != null) passwordInputField.gameObject.SetActive(isActive);
            if (confirmPasswordInputField != null) confirmPasswordInputField.gameObject.SetActive(isActive);
            if (submitRegisterButton != null) submitRegisterButton.gameObject.SetActive(isActive);
            if (switchToLoginButton != null) switchToLoginButton.gameObject.SetActive(isActive);
        }

        /// <summary>
        /// Обработчик кнопки регистрации: валидация, проверка уникальности в SQLite и запуск процесса.
        /// </summary>
        private async void HandleSubmit()
        {
            string rawLogin = loginInputField.text?.Trim() ?? string.Empty;
            string rawEmail = emailInputField.text?.Trim() ?? string.Empty;
            string rawPassword = passwordInputField.text ?? string.Empty;
            string rawConfirmPassword = confirmPasswordInputField.text ?? string.Empty;

            // 1. Проверка логина
            if (string.IsNullOrEmpty(rawLogin))
            {
                ShowValidationError(loginFieldBorder, "Поле логина не может быть пустым!");
                return;
            }

            if (!LoginRegex.IsMatch(rawLogin))
            {
                ShowValidationError(loginFieldBorder, "Логин: 3-20 символов (только латиница, цифры и _).");
                return;
            }

            // 2. Проверка Email по RFC 5322
            if (!EmailValidator.ValidateEmail(rawEmail, out string sanitizedEmail, out string emailError))
            {
                ShowValidationError(emailFieldBorder, emailError);
                return;
            }

            // 3. Проверка пароля
            if (string.IsNullOrEmpty(rawPassword))
            {
                ShowValidationError(passwordFieldBorder, "Введите пароль!");
                return;
            }

            if (rawPassword.Length < 6)
            {
                ShowValidationError(passwordFieldBorder, "Пароль должен содержать не менее 6 символов!");
                return;
            }

            // 4. Проверка совпадения паролей
            if (string.IsNullOrEmpty(rawConfirmPassword))
            {
                ShowValidationError(confirmPasswordFieldBorder, "Подтвердите введенный пароль!");
                return;
            }

            if (!string.Equals(rawPassword, rawConfirmPassword, StringComparison.Ordinal))
            {
                ShowValidationError(confirmPasswordFieldBorder, "Введенные пароли не совпадают!");
                return;
            }

            // 5. Проверка базы данных на доступность учетных данных перед отправкой
            if (AuthController.Instance != null)
            {
                if (!AuthController.Instance.ValidateCredentialsAvailability(rawLogin, sanitizedEmail, out string conflictError))
                {
                    if (conflictError.Contains("Email"))
                    {
                        ShowValidationError(emailFieldBorder, conflictError);
                    }
                    else
                    {
                        ShowValidationError(loginFieldBorder, conflictError);
                    }
                    return;
                }
            }

            // 6. Запуск асинхронной регистрации
            SetInteractive(false);
            DisplayStatus("Проверка данных и создание печати...", successColor);

            OnRegistrationSubmitted?.Invoke(rawLogin, sanitizedEmail, rawPassword);

            if (AuthController.Instance != null)
            {
                await AuthController.Instance.StartRegistrationAsync(rawLogin, sanitizedEmail, rawPassword);
            }
            else
            {
                ShowValidationError(null, "Ошибка: AuthController не обнаружен на сцене!");
                SetInteractive(true);
            }
        }

        private void HandleCodeSentSuccessfully(string email)
        {
            DisplayStatus("Печать отправлена на почту!", successColor);

            if (verificationPanel != null)
            {
                SetFieldsActive(false);
                verificationPanel.ShowPanel(email);
            }
            else
            {
                Debug.LogWarning("[RegistrationViewController] Ссылка на VerificationPanel не назначена!");
            }
        }

        private void HandleCodeSendFailed(string errorMessage)
        {
            SetInteractive(true);
            ShowValidationError(emailFieldBorder, errorMessage);
        }

        private void HandleUserRegisteredInDatabase(int userId, string registeredLogin)
        {
            DisplayStatus($"Профиль {registeredLogin} успешно создан!", successColor);
            OnRegistrationCompleted?.Invoke(registeredLogin);
        }

        private void ShowValidationError(Image fieldBorder, string errorMessage)
        {
            if (fieldBorder != null)
            {
                fieldBorder.color = errorBorderColor;
            }

            DisplayStatus(errorMessage, failureColor);
            PlayErrorShakeAnimation();
        }

        private void ClearFieldStatus(Image fieldBorder)
        {
            if (fieldBorder != null)
            {
                fieldBorder.color = normalBorderColor;
            }

            statusFeedbackText.text = string.Empty;
        }

        private void DisplayStatus(string message, Color color)
        {
            _statusFadeTween?.Kill();
            statusFeedbackText.text = message;
            statusFeedbackText.color = color;
            statusFeedbackText.alpha = 0f;
            _statusFadeTween = statusFeedbackText.DOFade(1f, 0.2f).SetUpdate(true);
        }

        private void PlayErrorShakeAnimation()
        {
            _shakeTween?.Kill();
            modalContainer.anchoredPosition = Vector2.zero;
            _shakeTween = modalContainer.DOShakeAnchorPos(0.35f, new Vector2(15f, 0f), 20, 90, false, true)
                .SetUpdate(true);
        }

        private void TogglePasswordVisibility()
        {
            _isPasswordVisible = !_isPasswordVisible;

            TMP_InputField.ContentType targetType = _isPasswordVisible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;

            passwordInputField.contentType = targetType;
            confirmPasswordInputField.contentType = targetType;

            if (togglePasswordIcon != null && passwordHiddenSprite != null && passwordVisibleSprite != null)
            {
                togglePasswordIcon.sprite = _isPasswordVisible ? passwordVisibleSprite : passwordHiddenSprite;
            }

            passwordInputField.ForceLabelUpdate();
            confirmPasswordInputField.ForceLabelUpdate();
        }

        public void SetInteractive(bool isInteractive)
        {
            loginInputField.interactable = isInteractive;
            emailInputField.interactable = isInteractive;
            passwordInputField.interactable = isInteractive;
            confirmPasswordInputField.interactable = isInteractive;
            submitRegisterButton.interactable = isInteractive;
            switchToLoginButton.interactable = isInteractive;
            togglePasswordButton.interactable = isInteractive;
        }

        public void ResetFormState()
        {
            loginInputField.text = string.Empty;
            emailInputField.text = string.Empty;
            passwordInputField.text = string.Empty;
            confirmPasswordInputField.text = string.Empty;

            _isPasswordVisible = false;
            passwordInputField.contentType = TMP_InputField.ContentType.Password;
            confirmPasswordInputField.contentType = TMP_InputField.ContentType.Password;

            statusFeedbackText.text = string.Empty;

            if (loginFieldBorder != null) loginFieldBorder.color = normalBorderColor;
            if (emailFieldBorder != null) emailFieldBorder.color = normalBorderColor;
            if (passwordFieldBorder != null) passwordFieldBorder.color = normalBorderColor;
            if (confirmPasswordFieldBorder != null) confirmPasswordFieldBorder.color = normalBorderColor;
        }

        private void ValidateSerializedFields()
        {
            if (rootCanvasGroup == null) rootCanvasGroup = GetComponent<CanvasGroup>();
            if (modalContainer == null) modalContainer = GetComponent<RectTransform>();
            if (verificationPanel == null) verificationPanel = GetComponentInChildren<VerificationPanelController>(true);
        }
    }
}