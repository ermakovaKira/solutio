using DG.Tweening;
using UnityEngine;

namespace Solutio.UI
{
    /// <summary>
    /// Координатор бесшовного переключения между Авторизацией и Регистрацией.
    /// Управляет анимацией переходов и автоподстановкой созданного аккаунта.
    /// </summary>
    public sealed class AuthFlowCoordinator : MonoBehaviour
    {
        [Header("Views")]
        [SerializeField] private AuthViewController authView;
        [SerializeField] private RegistrationViewController registrationView;

        private void Start()
        {
            if (authView == null || registrationView == null)
            {
                Debug.LogWarning("[AuthFlowCoordinator] Не назначены ссылки на экраны в Инспекторе!");
                return;
            }

            authView.OnNavigateToRegisterRequested += SwitchToRegistration;
            registrationView.OnNavigateToLoginRequested += SwitchToLogin;
            registrationView.OnRegistrationCompleted += HandleRegistrationCompleted;

            registrationView.gameObject.SetActive(false);
            authView.ShowModal();
        }

        private void OnDestroy()
        {
            if (authView != null)
            {
                authView.OnNavigateToRegisterRequested -= SwitchToRegistration;
            }

            if (registrationView != null)
            {
                registrationView.OnNavigateToLoginRequested -= SwitchToLogin;
                registrationView.OnRegistrationCompleted -= HandleRegistrationCompleted;
            }
        }

        private void SwitchToRegistration()
        {
            authView.HideModal();
            registrationView.ShowModal();
        }

        private void SwitchToLogin()
        {
            registrationView.HideModal();
            authView.ShowModal();
        }

        private void HandleRegistrationCompleted(string registeredLogin)
        {
            // Небольшая кинематографичная пауза, чтобы игрок увидел статус успеха в панели верификации
            DOVirtual.DelayedCall(2.0f, () =>
            {
                registrationView.HideModal(() =>
                {
                    authView.ShowModal();
                    authView.SetLogin(registeredLogin);
                });
            }).SetUpdate(true);
        }
    }
}