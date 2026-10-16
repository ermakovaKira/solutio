using DG.Tweening;
using Solutio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Auth
{
    /// <summary>
    /// Контроллер модальной панели ввода 6-значного кода верификации.
    /// Синхронизирован с AuthController для коммита учетной записи в базу данных SQLite.
    /// </summary>
    public sealed class VerificationPanelController : MonoBehaviour
    {
        [Header("Parent Form Reference")]
        [SerializeField] private RegistrationViewController registrationViewController;

        [Header("UI References")]
        [SerializeField] private TMP_InputField codeInputField;
        [SerializeField] private Button submitCodeButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TextMeshProUGUI statusFeedbackText;
        [SerializeField] private TextMeshProUGUI descriptionText;

        [Header("Visual Theme Settings")]
        [SerializeField] private Color successColor = new Color(0.19f, 0.44f, 0.51f, 1f);
        [SerializeField] private Color failureColor = new Color(0.95f, 0.25f, 0.25f, 1f);

        private CanvasGroup _panelCanvasGroup;
        private RectTransform _panelRectTransform;
        private Tween _shakeTween;

        private void Awake()
        {
            EnsureComponents();

            if (submitCodeButton != null)
                submitCodeButton.onClick.AddListener(HandleCodeSubmit);

            if (backButton != null)
                backButton.onClick.AddListener(HandleBackClick);
        }

        private void EnsureComponents()
        {
            if (_panelCanvasGroup == null)
            {
                _panelCanvasGroup = GetComponent<CanvasGroup>();
                if (_panelCanvasGroup == null)
                    _panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (_panelRectTransform == null)
            {
                _panelRectTransform = GetComponent<RectTransform>();
            }

            if (registrationViewController == null)
            {
                registrationViewController = GetComponentInParent<RegistrationViewController>();
            }
        }

        private void OnDestroy()
        {
            if (submitCodeButton != null) submitCodeButton.onClick.RemoveAllListeners();
            if (backButton != null) backButton.onClick.RemoveAllListeners();
            _shakeTween?.Kill();
        }

        /// <summary>
        /// Открывает панель подтверждения кода с привязкой к указанному email.
        /// </summary>
        public void ShowPanel(string email)
        {
            EnsureComponents();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (descriptionText != null)
                descriptionText.text = $"Код безопасности отправлен на:\n<color=#307082><b>{email}</b></color>";

            if (codeInputField != null)
            {
                codeInputField.text = string.Empty;
                codeInputField.interactable = true;
            }

            if (statusFeedbackText != null) statusFeedbackText.text = string.Empty;
            if (submitCodeButton != null) submitCodeButton.interactable = true;

            _panelCanvasGroup.alpha = 1f;
            _panelCanvasGroup.blocksRaycasts = true;
            _panelCanvasGroup.interactable = true;

            _panelRectTransform.localScale = new Vector3(0.85f, 0.85f, 1f);
            _panelRectTransform.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        /// <summary>
        /// Скрывает панель верификации с плавной анимацией затухания.
        /// </summary>
        public void HidePanel()
        {
            EnsureComponents();

            _panelCanvasGroup.DOFade(0f, 0.2f).SetUpdate(true);
            _panelRectTransform.DOScale(0.85f, 0.2f).SetEase(Ease.InBack).SetUpdate(true).OnComplete(() =>
            {
                gameObject.SetActive(false);
            });
        }

        private void HandleBackClick()
        {
            HidePanel();

            // Возвращаем видимость и кликабельность полям регистрации
            if (registrationViewController != null)
            {
                registrationViewController.SetFieldsActive(true);
                registrationViewController.SetInteractive(true);
            }
        }

        private void HandleCodeSubmit()
        {
            string enteredCode = codeInputField != null ? codeInputField.text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(enteredCode) || enteredCode.Length < 6)
            {
                ShowError("Введите все 6 цифр кода!");
                return;
            }

            if (AuthController.Instance == null)
            {
                ShowError("Ошибка: AuthController не найден!");
                return;
            }

            // Верифицируем код и триггерим внесение в базу данных
            if (AuthController.Instance.VerifyCode(enteredCode, out string errorMsg))
            {
                if (statusFeedbackText != null)
                {
                    statusFeedbackText.text = "Печать скреплена! Профиль сохранен в базе данных.";
                    statusFeedbackText.color = successColor;
                }

                if (submitCodeButton != null) submitCodeButton.interactable = false;
                if (codeInputField != null) codeInputField.interactable = false;

                Debug.Log("<color=#00FF66><b>[VerificationPanel]</b> Верификация пройдена, запись в users выполнена успешно!</color>");

                // Кинематичная задержка перед закрытием панели
                DOVirtual.DelayedCall(1.8f, () =>
                {
                    HidePanel();
                }).SetUpdate(true);
            }
            else
            {
                ShowError(errorMsg);
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
            _panelRectTransform.anchoredPosition = new Vector2(0, -20);
            _shakeTween = _panelRectTransform.DOShakeAnchorPos(0.35f, new Vector2(15f, 0f), 20, 90, false, true).SetUpdate(true);
        }
    }
}