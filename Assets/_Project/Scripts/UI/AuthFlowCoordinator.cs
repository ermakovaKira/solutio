using UnityEngine;

namespace Solutio.UI
{
    /// <summary>
    /// Координатор бесшовного переключения между Авторизацией и Регистрацией.
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

            // На старте показываем авторизацию
            registrationView.gameObject.SetActive(false);
            authView.ShowModal();
        }

        private void OnDestroy()
        {
            if (authView != null)
                authView.OnNavigateToRegisterRequested -= SwitchToRegistration;

            if (registrationView != null)
                registrationView.OnNavigateToLoginRequested -= SwitchToLogin;
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
    }
}