using System;
using DG.Tweening;
using Mono.Data.Sqlite;
using Solutio.Database;
using Solutio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Auth
{
    /// <summary>
    /// Контроллер двухэтапного сброса пароля:
    /// Этап 1: ввод Email и отправка письма.
    /// Этап 2: ввод 6-значного кода и подтверждение.
    /// </summary>
    public sealed class ResetPasswordController : MonoBehaviour
    {
        [Header("UI References - Stage 1 (Email)")]
        [SerializeField] private TMP_InputField emailInputField;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TextMeshProUGUI submitButtonText;
        [SerializeField] private TextMeshProUGUI statusFeedbackText;
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Header("UI References - Stage 2 (Code)")]
        [SerializeField] private TMP_InputField codeInputField;

        [Header("Auth View Reference")]
        [SerializeField] private AuthViewController authViewController;

        [Header("Database Verification Settings")]
        [SerializeField] private bool checkDatabase = false;

        [Header("Visual Theme Settings")]
        [SerializeField] private Color successColor = new Color(0.19f, 0.44f, 0.51f, 1f); // #307082
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        private CanvasGroup _canvasGroup;
        private RectTransform _rectTransform;
        private Tween _shakeTween;
        private bool _isAwaitingCode = false;

        private void Awake()
        {
            EnsureComponents();

            if (submitButton != null)
                submitButton.onClick.AddListener(HandleSubmit);

            if (cancelButton != null)
                cancelButton.onClick.AddListener(HidePanel);
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
        }

        public void ShowPanel()
        {
            EnsureComponents();

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

        public void HidePanel()
        {
            EnsureComponents();

            _canvasGroup.DOFade(0f, 0.2f).SetUpdate(true);
            _rectTransform.DOScale(0.85f, 0.2f).SetUpdate(true).OnComplete(() =>
            {
                gameObject.SetActive(false);

                if (authViewController != null)
                {
                    authViewController.SetFieldsActive(true);
                }
            });
        }

        private void ResetToStageOne()
        {
            _isAwaitingCode = false;

            if (emailInputField != null)
            {
                emailInputField.gameObject.SetActive(true);
                emailInputField.text = string.Empty;
                emailInputField.interactable = true;
            }

            if (codeInputField != null)
            {
                codeInputField.gameObject.SetActive(false);
                codeInputField.text = string.Empty;
            }

            if (descriptionText != null)
            {
                descriptionText.text = "Введите Email, привязанный к вашему профилю. Мы вышлем код восстановления:";
            }

            if (submitButtonText != null)
            {
                submitButtonText.text = "ВЫСЛАТЬ КОД";
            }

            if (statusFeedbackText != null) statusFeedbackText.text = string.Empty;
            if (submitButton != null) submitButton.interactable = true;
        }

        private void SwitchToStageTwo(string email)
        {
            _isAwaitingCode = true;

            // Прячем поле Email, показываем ввод 6 цифр кода
            if (emailInputField != null) emailInputField.gameObject.SetActive(false);
            if (codeInputField != null)
            {
                codeInputField.gameObject.SetActive(true);
                codeInputField.interactable = true;
                codeInputField.text = string.Empty;
            }

            if (descriptionText != null)
            {
                descriptionText.text = $"Код выслан на: <color=#307082><b>{email}</b></color>\nВведите 6 цифр для подтверждения:";
            }

            if (submitButtonText != null)
            {
                submitButtonText.text = "ПОДТВЕРДИТЬ КОД";
            }

            if (statusFeedbackText != null) statusFeedbackText.text = string.Empty;
            if (submitButton != null) submitButton.interactable = true;
        }

        private async void HandleSubmit()
        {
            // ЭТАП 1: Отправка письма
            if (!_isAwaitingCode)
            {
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

                if (checkDatabase && DatabaseManager.Instance != null)
                {
                    try
                    {
                        string checkQuery = "SELECT COUNT(*) FROM users WHERE email = @email;";
                        object countResult = DatabaseManager.Instance.ExecuteScalar(checkQuery, new SqliteParameter("@email", sanitizedEmail));
                        int usersFound = countResult != null ? Convert.ToInt32(countResult) : 0;

                        if (usersFound == 0)
                        {
                            ShowError("Аккаунт с таким Email не зарегистрирован!");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ResetPasswordController] Ошибка БД: {ex.Message}");
                    }
                }

                if (statusFeedbackText != null)
                {
                    statusFeedbackText.text = "Отправка тайного кода...";
                    statusFeedbackText.color = successColor;
                }

                if (submitButton != null) submitButton.interactable = false;

                if (AuthController.Instance != null)
                {
                    bool isSent = await AuthController.Instance.SendVerificationCodeAsync(sanitizedEmail, isPasswordReset: true);

                    if (isSent)
                    {
                        // ПЕРЕКЛЮЧАЕМСЯ НА ЭТАП ВВОДА КОДА!
                        SwitchToStageTwo(sanitizedEmail);
                    }
                    else
                    {
                        ShowError("Не удалось отправить код. Проверьте соединение.");
                        if (submitButton != null) submitButton.interactable = true;
                    }
                }
                else
                {
                    ShowError("Ошибка: AuthController не найден!");
                    if (submitButton != null) submitButton.interactable = true;
                }
            }
            // ЭТАП 2: Проверка введенного 6-значного кода
            else
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

                if (AuthController.Instance.VerifyCode(enteredCode, out string errorMsg))
                {
                    if (statusFeedbackText != null)
                    {
                        statusFeedbackText.text = "Email подтвержден! Задайте новый пароль.";
                        statusFeedbackText.color = successColor;
                    }

                    if (submitButton != null) submitButton.interactable = false;
                    if (codeInputField != null) codeInputField.interactable = false;

                    Debug.Log("<color=#307082>[PASSWORD RESET SUCCESS] Код подтвержден, личность верифицирована!</color>");
                }
                else
                {
                    ShowError(errorMsg);
                }
            }
        }

        private void ShowError(string message)
        {
            if (statusFeedbackText != null)
            {
                statusFeedbackText.text = message;
                statusFeedbackText.color = failureColor;
            }

            _shakeTween?.Kill();
            _rectTransform.anchoredPosition = new Vector2(0, -15);
            _shakeTween = _rectTransform.DOShakeAnchorPos(0.35f, new Vector2(15f, 0f), 20, 90, false, true).SetUpdate(true);
        }
    }
}