using DG.Tweening;
using Solutio.Auth;
using UnityEngine;
using UnityEngine.Video;

namespace Solutio.UI
{
    /// <summary>
    /// Координатор переходов между экранами: Авторизация ↔ Регистрация ↔ Главное меню.
    /// Отвечает за порядок анимаций, завершение сессии пользователя и бесшовную
    /// передачу видеофона (один активный видеодекодер в каждый момент времени).
    /// </summary>
    public sealed class AuthFlowCoordinator : MonoBehaviour
    {
        [Header("Views")]
        [SerializeField] private AuthViewController authView;
        [SerializeField] private RegistrationViewController registrationView;
        [SerializeField] private MainMenuController mainMenu;

        [Header("Video Background Handoff")]
        [SerializeField] private VideoPlayer authVideoPlayer;
        [SerializeField] private VideoPlayer menuVideoPlayer;

        [Header("Timings (seconds)")]
        [SerializeField] private float loginGreetingDelay = 0.6f;
        [SerializeField] private float registrationSuccessDelay = 2.0f;

        private Tween _pendingTransition;
        private bool _isTransitioning;
        private bool _isInitialized;

        private void Start()
        {
            if (authView == null || registrationView == null || mainMenu == null)
            {
                Debug.LogWarning("[AuthFlowCoordinator] Не назначены ссылки на экраны (Auth / Registration / MainMenu) в Инспекторе!");
                return;
            }

            authView.OnNavigateToRegisterRequested += SwitchToRegistration;
            authView.OnLoginSubmitted += HandleLoginSubmitted;
            registrationView.OnNavigateToLoginRequested += SwitchToLogin;
            registrationView.OnRegistrationCompleted += HandleRegistrationCompleted;
            mainMenu.OnLogoutRequested += HandleLogoutRequested;
            _isInitialized = true;

            PrepareMenuVideo();

            registrationView.gameObject.SetActive(false);
            authView.ShowModal();
        }

        private void OnDestroy()
        {
            _pendingTransition?.Kill();

            if (!_isInitialized)
            {
                return;
            }

            if (authView != null)
            {
                authView.OnNavigateToRegisterRequested -= SwitchToRegistration;
                authView.OnLoginSubmitted -= HandleLoginSubmitted;
            }

            if (registrationView != null)
            {
                registrationView.OnNavigateToLoginRequested -= SwitchToLogin;
                registrationView.OnRegistrationCompleted -= HandleRegistrationCompleted;
            }

            if (mainMenu != null)
            {
                mainMenu.OnLogoutRequested -= HandleLogoutRequested;
            }
        }

        #region Auth ↔ Registration

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
            // Кинематографичная пауза, чтобы пользователь увидел статус успеха в панели верификации
            _pendingTransition?.Kill();
            _pendingTransition = DOVirtual.DelayedCall(registrationSuccessDelay, () =>
            {
                registrationView.HideModal(() =>
                {
                    authView.ShowModal();
                    authView.SetLogin(registeredLogin);
                });
            }).SetUpdate(true);
        }

        #endregion

        #region Auth → Main Menu

        private void HandleLoginSubmitted(string login, string password)
        {
            if (_isTransitioning)
            {
                return;
            }

            int userId = AuthController.CurrentUserId;
            if (userId <= 0)
            {
                Debug.LogError("[AuthFlowCoordinator] Получен сигнал входа, но активная сессия пользователя отсутствует.");
                return;
            }

            string userLogin = AuthController.CurrentUserLogin;
            _isTransitioning = true;

            // Даём пользователю увидеть приветствие в форме авторизации перед переходом
            _pendingTransition?.Kill();
            _pendingTransition = DOVirtual
                .DelayedCall(loginGreetingDelay, () => EnterMainMenu(userId, userLogin))
                .SetUpdate(true);
        }

        private void EnterMainMenu(int userId, string userLogin)
        {
            HandOffVideoToMenu();
            mainMenu.Show(userId, userLogin);
            authView.HideModal(() => _isTransitioning = false);
        }

        #endregion

        #region Main Menu → Auth (Logout)

        private void HandleLogoutRequested()
        {
            if (_isTransitioning)
            {
                return;
            }

            _isTransitioning = true;
            string lastLogin = AuthController.CurrentUserLogin;

            if (AuthController.Instance != null)
            {
                AuthController.Instance.SignOut();
            }

            // Останавливаем видео меню сразу: RenderTexture удержит последний кадр,
            // пока видеоплеер формы авторизации не выдаст свой первый кадр.
            PauseMenuVideo();

            // Форма авторизации проявляется ПОД меню (Sort Order ниже), меню растворяется сверху
            authView.ShowModal();
            authView.ResetForNewSession(lastLogin);
            mainMenu.Hide(() => _isTransitioning = false);
        }

        #endregion

        #region Video Handoff

        private void PrepareMenuVideo()
        {
            if (menuVideoPlayer == null)
            {
                return;
            }

            menuVideoPlayer.playOnAwake = false;
            menuVideoPlayer.isLooping = true;
            menuVideoPlayer.Prepare();
        }

        private void HandOffVideoToMenu()
        {
            if (menuVideoPlayer == null)
            {
                return;
            }

            double syncTime = 0d;

            if (authVideoPlayer != null && authVideoPlayer.isPlaying)
            {
                syncTime = authVideoPlayer.time;
                authVideoPlayer.Pause();
            }

            menuVideoPlayer.time = syncTime;
            menuVideoPlayer.Play();
        }

        private void PauseMenuVideo()
        {
            if (menuVideoPlayer != null && menuVideoPlayer.isPlaying)
            {
                menuVideoPlayer.Pause();
            }
        }

        #endregion
    }
}
