using System;
using System.Collections.Generic;
using DG.Tweening;
using Solutio.Database;
using Solutio.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.UI
{
    /// <summary>
    /// Контроллер главного меню: выбор сложности ИИ, карты и лимита FPS
    /// с привязкой к профилю активного пользователя (таблица user_settings).
    /// Кнопки вариантов генерируются из MatchCatalog — справочник является единственным источником правды.
    /// Показ и скрытие выполняются через CanvasGroup без SetActive (без перестроения Canvas).
    /// Запись в SQLite отложенная: только при старте матча, выходе, сворачивании и закрытии игры.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private sealed class OptionView
        {
            public readonly Button Button;
            public readonly Image Background;
            public readonly TMP_Text Label;

            public OptionView(Button button, Image background, TMP_Text label)
            {
                Button = button;
                Background = background;
                Label = label;
            }
        }

        [Header("Root")]
        [SerializeField] private CanvasGroup rootCanvasGroup;

        [Header("Header")]
        [SerializeField] private TMP_Text welcomeText;
        [SerializeField] private Button logoutButton;

        [Header("Difficulty")]
        [SerializeField] private RectTransform difficultyOptionsRoot;

        [Header("Map")]
        [SerializeField] private RectTransform mapOptionsRoot;

        [Header("FPS Limit")]
        [SerializeField] private Slider fpsSlider;
        [SerializeField] private TMP_Text fpsValueText;

        [Header("Summary & Start")]
        [SerializeField] private TMP_Text matchSummaryText;
        [SerializeField] private Button startMatchButton;

        [Header("Option Prefab")]
        [SerializeField] private Button optionButtonPrefab;

        [Header("Option Colors")]
        [SerializeField] private Color optionNormalColor = new Color32(0x0C, 0x0F, 0x17, 0xFF);
        [SerializeField] private Color optionSelectedColor = new Color32(0x30, 0x70, 0x82, 0xFF);
        [SerializeField] private Color optionTextNormalColor = new Color32(0xC8, 0xCC, 0xD4, 0xFF);
        [SerializeField] private Color optionTextSelectedColor = new Color32(0xE8, 0xF4, 0xF8, 0xFF);

        [Header("Animation")]
        [SerializeField] private float fadeDuration = 0.4f;

        public event Action<UserSettings> OnSettingsChanged;
        public event Action<UserSettings> OnStartMatchRequested;
        public event Action OnLogoutRequested;

        public bool IsVisible { get; private set; }
        public UserSettings CurrentSettings => _currentSettings;

        private readonly List<OptionView> _difficultyOptions = new List<OptionView>(4);
        private readonly List<OptionView> _mapOptions = new List<OptionView>(6);
        private readonly List<string> _mapKeys = new List<string>(6);

        private UserSettingsRepository _repository;
        private UserSettings _currentSettings;
        private bool _isDirty;
        private Tween _fadeTween;

        private void Awake()
        {
            if (rootCanvasGroup == null)
            {
                rootCanvasGroup = GetComponent<CanvasGroup>();
            }

            if (!ValidateReferences())
            {
                enabled = false;
                return;
            }

            BuildDifficultyOptions();
            BuildMapOptions();
            ConfigureFpsSlider();

            logoutButton.onClick.AddListener(HandleLogoutClicked);
            startMatchButton.onClick.AddListener(HandleStartMatchClicked);

            ApplyHiddenStateImmediate();
        }

        private void OnDestroy()
        {
            _fadeTween?.Kill();

            if (logoutButton != null)
            {
                logoutButton.onClick.RemoveListener(HandleLogoutClicked);
            }

            if (startMatchButton != null)
            {
                startMatchButton.onClick.RemoveListener(HandleStartMatchClicked);
            }

            if (fpsSlider != null)
            {
                fpsSlider.onValueChanged.RemoveListener(HandleFpsSliderChanged);
            }

            for (int i = 0; i < _difficultyOptions.Count; i++)
            {
                _difficultyOptions[i].Button.onClick.RemoveAllListeners();
            }

            for (int i = 0; i < _mapOptions.Count; i++)
            {
                _mapOptions[i].Button.onClick.RemoveAllListeners();
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                FlushSettings();
            }
        }

        private void OnApplicationQuit()
        {
            FlushSettings();
        }

        #region Public API

        /// <summary>
        /// Загружает настройки пользователя из SQLite и плавно показывает меню.
        /// </summary>
        public void Show(int userId, string userLogin)
        {
            if (!enabled)
            {
                Debug.LogError("[MainMenuController] Контроллер отключён из-за неназначенных ссылок в Инспекторе.");
                return;
            }

            if (userId <= 0)
            {
                Debug.LogError($"[MainMenuController] Попытка открыть меню без авторизованной сессии (ID: {userId}).");
                return;
            }

            if (DatabaseManager.Instance == null)
            {
                Debug.LogError("[MainMenuController] DatabaseManager не найден на сцене.");
                return;
            }

            if (_repository == null)
            {
                _repository = new UserSettingsRepository(DatabaseManager.Instance);
            }

            _currentSettings = _repository.LoadOrCreate(userId);
            _isDirty = false;

            welcomeText.text = $"Добро пожаловать, {userLogin}!";
            RefreshAllViews();

            IsVisible = true;
            rootCanvasGroup.interactable = true;
            rootCanvasGroup.blocksRaycasts = true;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(1f, fadeDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);

            OnSettingsChanged?.Invoke(_currentSettings);
        }

        /// <summary>
        /// Сохраняет несохранённые изменения и плавно скрывает меню.
        /// </summary>
        public void Hide(Action onComplete = null)
        {
            FlushSettings();

            IsVisible = false;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Сбрасывает накопленные изменения настроек в SQLite одной параметризированной командой.
        /// </summary>
        public void FlushSettings()
        {
            if (!_isDirty || _currentSettings == null || _repository == null)
            {
                return;
            }

            if (_repository.Save(_currentSettings))
            {
                _isDirty = false;
                Debug.Log($"<color=#307082>[MainMenuController]</color> Настройки пользователя ID {_currentSettings.UserId} сохранены.");
            }
            else
            {
                Debug.LogWarning($"[MainMenuController] Не удалось сохранить настройки пользователя ID {_currentSettings.UserId}.");
            }
        }

        #endregion

        #region Building

        private void BuildDifficultyOptions()
        {
            IReadOnlyList<AiDifficulty> difficulties = MatchCatalog.Difficulties;

            for (int i = 0; i < difficulties.Count; i++)
            {
                AiDifficulty difficulty = difficulties[i];
                OptionView view = CreateOption(
                    difficultyOptionsRoot,
                    $"Btn_Difficulty_{difficulty}",
                    MatchCatalog.GetDifficultyDisplayName(difficulty));

                view.Button.onClick.AddListener(() => SelectDifficulty(difficulty));
                _difficultyOptions.Add(view);
            }
        }

        private void BuildMapOptions()
        {
            AddMapOption(MatchCatalog.RandomMapKey, MatchCatalog.RandomMapDisplayName);

            IReadOnlyList<MapDefinition> maps = MatchCatalog.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                AddMapOption(maps[i].Key, maps[i].DisplayName);
            }
        }

        private void AddMapOption(string mapKey, string displayName)
        {
            OptionView view = CreateOption(mapOptionsRoot, $"Btn_Map_{mapKey}", displayName);
            view.Button.onClick.AddListener(() => SelectMap(mapKey));

            _mapOptions.Add(view);
            _mapKeys.Add(mapKey);
        }

        private OptionView CreateOption(RectTransform parent, string objectName, string displayName)
        {
            Button button = Instantiate(optionButtonPrefab, parent, false);
            button.name = objectName;

            Image background = button.targetGraphic as Image;
            if (background == null)
            {
                background = button.GetComponent<Image>();
            }

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = displayName;
            }

            return new OptionView(button, background, label);
        }

        private void ConfigureFpsSlider()
        {
            fpsSlider.wholeNumbers = true;
            fpsSlider.minValue = 0f;
            fpsSlider.maxValue = MatchCatalog.FpsOptions.Count - 1;
            fpsSlider.onValueChanged.AddListener(HandleFpsSliderChanged);
        }

        #endregion

        #region Selection Handlers

        private void SelectDifficulty(AiDifficulty difficulty)
        {
            if (_currentSettings == null || _currentSettings.Difficulty == difficulty)
            {
                return;
            }

            _currentSettings.Difficulty = difficulty;
            RefreshDifficultyView();
            MarkDirty();
        }

        private void SelectMap(string mapKey)
        {
            if (_currentSettings == null || string.Equals(_currentSettings.MapKey, mapKey, StringComparison.Ordinal))
            {
                return;
            }

            _currentSettings.MapKey = mapKey;
            RefreshMapView();
            MarkDirty();
        }

        private void HandleFpsSliderChanged(float rawValue)
        {
            if (_currentSettings == null)
            {
                return;
            }

            int index = Mathf.Clamp(Mathf.RoundToInt(rawValue), 0, MatchCatalog.FpsOptions.Count - 1);
            int fpsLimit = MatchCatalog.FpsOptions[index];

            if (_currentSettings.FpsLimit == fpsLimit)
            {
                return;
            }

            _currentSettings.FpsLimit = fpsLimit;
            fpsValueText.text = MatchCatalog.GetFpsDisplayName(fpsLimit);
            MarkDirty();
        }

        private void HandleStartMatchClicked()
        {
            if (_currentSettings == null)
            {
                return;
            }

            FlushSettings();
            OnStartMatchRequested?.Invoke(_currentSettings);
        }

        private void HandleLogoutClicked()
        {
            FlushSettings();
            OnLogoutRequested?.Invoke();
        }

        private void MarkDirty()
        {
            _isDirty = true;
            RefreshSummary();
            OnSettingsChanged?.Invoke(_currentSettings);
        }

        #endregion

        #region View Refresh

        private void RefreshAllViews()
        {
            RefreshDifficultyView();
            RefreshMapView();

            int fpsIndex = MatchCatalog.IndexOfFps(_currentSettings.FpsLimit);
            if (fpsIndex < 0)
            {
                fpsIndex = MatchCatalog.IndexOfFps(MatchCatalog.DefaultFpsLimit);
            }

            fpsSlider.SetValueWithoutNotify(fpsIndex);
            fpsValueText.text = MatchCatalog.GetFpsDisplayName(_currentSettings.FpsLimit);

            RefreshSummary();
        }

        private void RefreshDifficultyView()
        {
            IReadOnlyList<AiDifficulty> difficulties = MatchCatalog.Difficulties;

            for (int i = 0; i < _difficultyOptions.Count; i++)
            {
                ApplyOptionVisual(_difficultyOptions[i], difficulties[i] == _currentSettings.Difficulty);
            }
        }

        private void RefreshMapView()
        {
            for (int i = 0; i < _mapOptions.Count; i++)
            {
                bool isSelected = string.Equals(_mapKeys[i], _currentSettings.MapKey, StringComparison.Ordinal);
                ApplyOptionVisual(_mapOptions[i], isSelected);
            }
        }

        private void ApplyOptionVisual(OptionView view, bool isSelected)
        {
            if (view.Background != null)
            {
                view.Background.color = isSelected ? optionSelectedColor : optionNormalColor;
            }

            if (view.Label != null)
            {
                view.Label.color = isSelected ? optionTextSelectedColor : optionTextNormalColor;
            }
        }

        private void RefreshSummary()
        {
            matchSummaryText.text =
                $"{MatchCatalog.GetDifficultyDisplayName(_currentSettings.Difficulty)}  ·  " +
                $"{MatchCatalog.GetMapDisplayName(_currentSettings.MapKey)}\n" +
                $"<size=80%>{MatchCatalog.GetFpsDisplayName(_currentSettings.FpsLimit)}</size>";
        }

        #endregion

        #region Helpers

        private void ApplyHiddenStateImmediate()
        {
            IsVisible = false;
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;
        }

        private bool ValidateReferences()
        {
            bool isValid = true;

            isValid &= CheckReference(rootCanvasGroup, nameof(rootCanvasGroup));
            isValid &= CheckReference(welcomeText, nameof(welcomeText));
            isValid &= CheckReference(logoutButton, nameof(logoutButton));
            isValid &= CheckReference(difficultyOptionsRoot, nameof(difficultyOptionsRoot));
            isValid &= CheckReference(mapOptionsRoot, nameof(mapOptionsRoot));
            isValid &= CheckReference(fpsSlider, nameof(fpsSlider));
            isValid &= CheckReference(fpsValueText, nameof(fpsValueText));
            isValid &= CheckReference(matchSummaryText, nameof(matchSummaryText));
            isValid &= CheckReference(startMatchButton, nameof(startMatchButton));
            isValid &= CheckReference(optionButtonPrefab, nameof(optionButtonPrefab));

            return isValid;
        }

        private static bool CheckReference(UnityEngine.Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"[MainMenuController] Не назначено поле «{fieldName}» в Инспекторе.");
            return false;
        }

        #endregion
    }
}
