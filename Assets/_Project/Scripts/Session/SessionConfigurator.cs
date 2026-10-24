using System;
using System.Security.Cryptography;
using Solutio.Auth;
using Solutio.Settings;
using Solutio.UI;
using UnityEngine;

namespace Solutio.Session
{
    /// <summary>
    /// Управление параметрами игровой сессии.
    ///   — применяет лимит частоты кадров (QualitySettings.vSyncCount / Application.targetFrameRate)
    ///     сразу при входе пользователя и при каждом изменении настроек в главном меню;
    ///   — при старте матча формирует неизменяемый MatchSessionConfig: стратегический профиль ИИ,
    ///     конкретную карту (алгоритм случайного подбора для «Random») и зерно генератора матча;
    ///   — хранит активную конфигурацию в статическом свойстве, доступном из следующих сцен.
    /// Вся расчётная логика вынесена в чистые C#-классы (FrameRatePolicy, AiStrategyTable, MapRandomizer).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionConfigurator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MainMenuController mainMenu;

        [Header("Debug")]
        [SerializeField] private bool enableDetailedLogging = true;

        private static MapRandomizer _mapRandomizer;
        private static int _randomizerOwnerUserId = -1;

        /// <summary>Конфигурация текущего матча или null, если матч не запущен.</summary>
        public static MatchSessionConfig ActiveSession { get; private set; }

        /// <summary>Параметры частоты кадров, применённые последними.</summary>
        public static FrameRateSettings AppliedFrameRate { get; private set; }

        /// <summary>Вызывается после формирования конфигурации матча.</summary>
        public event Action<MatchSessionConfig> OnSessionConfigured;

        private bool _hasAppliedFrameRate;
        private bool _isSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // Защита от «протухшей» статики при отключённой перезагрузке домена (Enter Play Mode Options)
            _mapRandomizer = null;
            _randomizerOwnerUserId = -1;
            ActiveSession = null;
            AppliedFrameRate = default;
        }

        private void Awake()
        {
            // До входа пользователя действует лимит по умолчанию — экран авторизации не рендерит лишние кадры
            ApplyFrameRate(MatchCatalog.DefaultFpsLimit);
        }

        private void OnEnable()
        {
            if (mainMenu == null)
            {
                Debug.LogError("[SessionConfigurator] Не назначено поле «mainMenu» в Инспекторе.");
                return;
            }

            mainMenu.OnSettingsChanged += HandleSettingsChanged;
            mainMenu.OnStartMatchRequested += HandleStartMatchRequested;
            mainMenu.OnLogoutRequested += HandleLogoutRequested;
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (!_isSubscribed || mainMenu == null)
            {
                return;
            }

            mainMenu.OnSettingsChanged -= HandleSettingsChanged;
            mainMenu.OnStartMatchRequested -= HandleStartMatchRequested;
            mainMenu.OnLogoutRequested -= HandleLogoutRequested;
            _isSubscribed = false;
        }

        #region Event Handlers

        private void HandleSettingsChanged(UserSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            ApplyFrameRate(settings.FpsLimit);
        }

        private void HandleStartMatchRequested(UserSettings settings)
        {
            if (settings == null)
            {
                Debug.LogError("[SessionConfigurator] Запрос старта матча без настроек пользователя.");
                return;
            }

            int sessionUserId = AuthController.CurrentUserId;
            if (sessionUserId <= 0 || sessionUserId != settings.UserId)
            {
                Debug.LogError($"[SessionConfigurator] Настройки пользователя ID {settings.UserId} не совпадают с активной сессией (ID {sessionUserId}). Старт матча отменён.");
                return;
            }

            MatchSessionConfig config;

            try
            {
                config = BuildSessionConfig(settings, AuthController.CurrentUserLogin);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[SessionConfigurator] Не удалось сформировать конфигурацию матча: {exception.Message}");
                return;
            }

            ApplyFrameRate(config.FrameRate.FpsLimit);
            ActiveSession = config;

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[SessionConfigurator]</color> Матч сконфигурирован: {config}");
                Debug.Log($"<color=#307082>[SessionConfigurator]</color> Профиль ИИ — {config.AiProfile}");
            }

            OnSessionConfigured?.Invoke(config);
        }

        private void HandleLogoutRequested()
        {
            ActiveSession = null;

            if (enableDetailedLogging)
            {
                Debug.Log("<color=#307082>[SessionConfigurator]</color> Сессия пользователя завершена, конфигурация матча очищена.");
            }
        }

        #endregion

        #region Session Building

        private static MatchSessionConfig BuildSessionConfig(UserSettings settings, string userLogin)
        {
            settings.Sanitize();

            AiStrategyProfile aiProfile = AiStrategyTable.Get(settings.Difficulty);
            MapRandomizer randomizer = GetRandomizerForUser(settings.UserId);
            MapDefinition map = randomizer.Resolve(settings.MapKey, out bool wasRandomized);
            FrameRateSettings frameRate = FrameRatePolicy.Resolve(settings.FpsLimit);

            return new MatchSessionConfig(
                settings.UserId,
                userLogin,
                settings.Difficulty,
                aiProfile,
                settings.MapKey,
                map,
                wasRandomized,
                frameRate,
                GenerateMatchSeed(),
                DateTime.UtcNow);
        }

        private static MapRandomizer GetRandomizerForUser(int userId)
        {
            if (_mapRandomizer == null)
            {
                _mapRandomizer = new MapRandomizer();
            }
            else if (_randomizerOwnerUserId != userId)
            {
                // История сыгранных карт одного пользователя не должна влиять на другого
                _mapRandomizer.Reset();
            }

            _randomizerOwnerUserId = userId;
            return _mapRandomizer;
        }

        private static int GenerateMatchSeed()
        {
            byte[] buffer = new byte[sizeof(int)];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(buffer);
            }

            return BitConverter.ToInt32(buffer, 0);
        }

        #endregion

        #region Frame Rate

        private void ApplyFrameRate(int fpsLimit)
        {
            FrameRateSettings target = FrameRatePolicy.Resolve(fpsLimit);

            if (_hasAppliedFrameRate
                && AppliedFrameRate == target
                && QualitySettings.vSyncCount == target.VSyncCount
                && Application.targetFrameRate == target.TargetFrameRate)
            {
                return;
            }

            // Порядок важен: при включённом VSync targetFrameRate на ПК игнорируется
            QualitySettings.vSyncCount = target.VSyncCount;
            Application.targetFrameRate = target.TargetFrameRate;

            AppliedFrameRate = target;
            _hasAppliedFrameRate = true;

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[SessionConfigurator]</color> Применён лимит кадров: {target}");
            }
        }

        #endregion
    }
}
