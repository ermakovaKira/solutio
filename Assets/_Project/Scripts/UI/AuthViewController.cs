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
    /// Контроллер формы экранной авторизации с комплексной валидацией,
    /// криптографической проверкой хэша пароля в базе данных и Dark Fantasy микро-анимациями.
    /// </summary>
    public sealed class AuthViewController : MonoBehaviour
    {
        [Header("UI Architecture & Containers")]
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField] private RectTransform modalContainer;
        [SerializeField] private UIBlurEffect backgroundBlur;
        [SerializeField] private TextMeshProUGUI subtitleText;

        [Header("Input Fields")]
        [SerializeField] private TMP_InputField loginInputField;
        [SerializeField] private TMP_InputField passwordInputField;
        [SerializeField] private Image loginFieldBorder;
        [SerializeField] private Image passwordFieldBorder;

        [Header("Buttons")]
        [SerializeField] private Button submitLoginButton;
        [SerializeField] private Button switchToRegisterButton;
        [SerializeField] private Button togglePasswordButton;
        [SerializeField] private Image togglePasswordIcon;

        [Header("Feedback Elements")]
        [SerializeField] private TextMeshProUGUI statusFeedbackText;
        [SerializeField] private Sprite passwordHiddenSprite;
        [SerializeField] private Sprite passwordVisibleSprite;

        [Header("Visual Theme Settings")]
        [SerializeField] private Color normalBorderColor = new Color(0.2f, 0.6f, 0.8f, 0.4f);
        [SerializeField] private Color errorBorderColor = new Color(0.95f, 0.15f, 0.25f, 0.9f);
        [SerializeField] private Color successColor = new Color(0.19f, 0.44f, 0.51f, 1f);
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        // Регулярное выражение: логин от 3 до 20 символов (только латиница, цифры и _)
        private static readonly Regex LoginRegex = new Regex(@"^[a-zA-Z0-9_]{3,20}$", RegexOptions.Compiled);

        private bool _isPasswordVisible;
        private Tween _statusFadeTween;
        private Tween _shakeTween;

        public event Action<string, string> OnLoginSubmitted;
        public event Action OnNavigateToRegisterRequested;

        private void Awake()
        {
            ValidateSerializedFields();
            RegisterEventHandlers();
            ResetFormState();
        }

        private void Start()
        {
            ShowModal();
        }

        private void OnDestroy()
        {
            UnregisterEventHandlers();
            _statusFadeTween?.Kill();
            _shakeTween?.Kill();
        }

        private void RegisterEventHandlers()
        {
            submitLoginButton.onClick.AddListener(HandleSubmit);
            switchToRegisterButton.onClick.AddListener(() => OnNavigateToRegisterRequested?.Invoke());
            togglePasswordButton.onClick.AddListener(TogglePasswordVisibility);

            loginInputField.onValueChanged.AddListener(_ => ClearFieldStatus(loginFieldBorder));
            passwordInputField.onValueChanged.AddListener(_ => ClearFieldStatus(passwordFieldBorder));
        }

        private void UnregisterEventHandlers()
        {
            submitLoginButton.onClick.RemoveAllListeners();
            switchToRegisterButton.onClick.RemoveAllListeners();
            togglePasswordButton.onClick.RemoveAllListeners();

            loginInputField.onValueChanged.RemoveAllListeners();
            passwordInputField.onValueChanged.RemoveAllListeners();
        }

        public void ShowModal()
        {
            gameObject.SetActive(true);
            SetFieldsActive(true);
            SetInteractive(true);

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
        /// Управляет видимостью полей формы (для бесконфликтного открытия ResetPasswordPanel).
        /// </summary>
        public void SetFieldsActive(bool isActive)
        {
            if (loginInputField != null) loginInputField.gameObject.SetActive(isActive);
            if (passwordInputField != null) passwordInputField.gameObject.SetActive(isActive);
            if (submitLoginButton != null) submitLoginButton.gameObject.SetActive(isActive);
            if (switchToRegisterButton != null) switchToRegisterButton.gameObject.SetActive(isActive);
            if (togglePasswordButton != null) togglePasswordButton.gameObject.SetActive(isActive);
            if (subtitleText != null) subtitleText.gameObject.SetActive(isActive);
        }

        /// <summary>
        /// Автоматически подставляет логин в поле ввода и переводит фокус на пароль.
        /// </summary>
        public void SetLogin(string login)
        {
            if (loginInputField != null)
            {
                loginInputField.text = login;
                if (passwordInputField != null)
                {
                    passwordInputField.ActivateInputField();
                }
            }
        }
        /// <summary>
        /// Полная очистка формы после выхода пользователя: пароль не должен оставаться в поле ввода.
        /// Логин подставляется обратно для удобства повторного входа.
        /// </summary>
        public void ResetForNewSession(string lastLogin)
        {
            ResetFormState();

            if (togglePasswordIcon != null && passwordHiddenSprite != null)
            {
                togglePasswordIcon.sprite = passwordHiddenSprite;
            }

            passwordInputField.ForceLabelUpdate();

            if (!string.IsNullOrEmpty(lastLogin))
            {
                SetLogin(lastLogin);
            }
        }
        /// <summary>
        /// Обработчик кнопки входа: клиентская валидация и сверка хэша SHA-256 в базе SQLite.
        /// </summary>
        private void HandleSubmit()
        {
            string rawLogin = loginInputField.text?.Trim() ?? string.Empty;
            string rawPassword = passwordInputField.text ?? string.Empty;

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

            if (string.IsNullOrEmpty(rawPassword))
            {
                ShowValidationError(passwordFieldBorder, "Поле пароля не может быть пустым!");
                return;
            }

            if (rawPassword.Length < 6)
            {
                ShowValidationError(passwordFieldBorder, "Пароль не может быть короче 6 символов!");
                return;
            }

            DisplayStatus("Проверка печати доступа...", successColor);

            // Боевая проверка учетных данных в SQLite через AuthController
            if (AuthController.Instance != null)
            {
                if (AuthController.Instance.TryAuthenticate(rawLogin, rawPassword, out int userId, out string authError))
                {
                    DisplayStatus($"Добро пожаловать, {rawLogin}!", successColor);
                    SetInteractive(false);
                    OnLoginSubmitted?.Invoke(rawLogin, rawPassword);
                }
                else
                {
                    Image targetBorder = authError.Contains("найден") ? loginFieldBorder : passwordFieldBorder;
                    ShowValidationError(targetBorder, authError);
                }
            }
            else
            {
                ShowValidationError(null, "Ошибка: AuthController не обнаружен на сцене!");
            }
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

            passwordInputField.contentType = _isPasswordVisible
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;

            if (togglePasswordIcon != null && passwordHiddenSprite != null && passwordVisibleSprite != null)
            {
                togglePasswordIcon.sprite = _isPasswordVisible ? passwordVisibleSprite : passwordHiddenSprite;
            }

            passwordInputField.ForceLabelUpdate();
        }

        public void SetInteractive(bool isInteractive)
        {
            loginInputField.interactable = isInteractive;
            passwordInputField.interactable = isInteractive;
            submitLoginButton.interactable = isInteractive;
            switchToRegisterButton.interactable = isInteractive;
            togglePasswordButton.interactable = isInteractive;
        }

        private void ResetFormState()
        {
            loginInputField.text = string.Empty;
            passwordInputField.text = string.Empty;
            _isPasswordVisible = false;
            passwordInputField.contentType = TMP_InputField.ContentType.Password;
            statusFeedbackText.text = string.Empty;

            if (loginFieldBorder != null) loginFieldBorder.color = normalBorderColor;
            if (passwordFieldBorder != null) passwordFieldBorder.color = normalBorderColor;
        }

        private void ValidateSerializedFields()
        {
            if (rootCanvasGroup == null) rootCanvasGroup = GetComponent<CanvasGroup>();
            if (modalContainer == null) modalContainer = GetComponent<RectTransform>();
        }
    }
}