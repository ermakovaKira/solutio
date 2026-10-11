using System;
using System.Text.RegularExpressions;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.UI
{
    /// <summary>
    /// Контроллер формы регистрации новых пользователей.
    /// Реализует комплексную валидацию логина, email, длины пароля и проверки совпадения паролей.
    /// </summary>
    public sealed class RegistrationViewController : MonoBehaviour
    {
        [Header("UI Architecture & Containers")]
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private RectTransform modalContainer;
        [SerializeField] private UIBlurEffect backgroundBlur;

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
        [SerializeField] private Color successColor = new Color(0.2f, 0.95f, 0.5f, 1f);
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        // Регулярные выражения для строгой верификации
        private static readonly Regex LoginRegex = new Regex(@"^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);
        private static readonly Regex EmailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

        private bool _isPasswordVisible;
        private Tween _statusFadeTween;
        private Tween _shakeTween;

        /// <summary>
        /// Событие отправки проверенных регистрационных данных (Login, Email, Password).
        /// </summary>
        public event Action<string, string, string> OnRegistrationSubmitted;

        /// <summary>
        /// Событие перехода обратно к окну авторизации.
        /// </summary>
        public event Action OnNavigateToLoginRequested;

        private void Awake()
        {
            ValidateSerializedFields();
            RegisterEventHandlers();
            ResetFormState();
            if (submitRegisterButton != null)
                submitRegisterButton.GetComponentInChildren<TMP_Text>().text = "СОЗДАТЬ АККАУНТ";

            if (switchToLoginButton != null)
                switchToLoginButton.GetComponentInChildren<TMP_Text>().text = "Уже есть аккаунт? Войти";
        }

        private void OnDestroy()
        {
            UnregisterEventHandlers();
            _statusFadeTween?.Kill();
            _shakeTween?.Kill();
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
        /// Центральная логика верификации данных регистрации.
        /// </summary>
        private void HandleSubmit()
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

            // 2. Проверка Email
            if (string.IsNullOrEmpty(rawEmail))
            {
                ShowValidationError(emailFieldBorder, "Поле Email не может быть пустым!");
                return;
            }

            if (!EmailRegex.IsMatch(rawEmail))
            {
                ShowValidationError(emailFieldBorder, "Некорректный формат адреса электронной почты!");
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

            // 5. Успешная валидация
            DisplayStatus("Данные корректны. Создание профиля...", successColor);
            SetInteractive(false);

            OnRegistrationSubmitted?.Invoke(rawLogin, rawEmail, rawPassword);
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

        private void ResetFormState()
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
        }
    }
}