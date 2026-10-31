using System;
using System.Collections.Generic;
using DG.Tweening;
using Solutio.Auth;
using Solutio.Database;
using Solutio.Heroes;
using Solutio.Session;
using Solutio.Settings;
using Solutio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Draft
{
    /// <summary>Портрет конкретного героя (заменяет заглушку, когда появятся арты).</summary>
    [Serializable]
    public struct HeroPortraitEntry
    {
        public int heroId;
        public Sprite portrait;
    }

    /// <summary>
    /// Представление стадии драфта 5 на 5 с банами (по мотивам Captains Mode из Dota 2).
    /// Отвечает только за отображение: сетка героев по ролям, слоты пиков и банов обеих сторон,
    /// шкалы таймера ходов, панель выбранного героя, заголовок фазы и хода.
    /// Показ и скрытие через CanvasGroup и DOTween с SetUpdate(true), без SetActive.
    /// Логика ходов и таймер на временных метках — День 11, автопик из SQLite и решения ИИ — День 12.
    /// Режим предпросмотра вёрстки позволяет пройти драфт вручную для проверки визуальных состояний.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class DraftViewController : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private SessionConfigurator sessionConfigurator;
        [SerializeField] private MainMenuController mainMenu;

        [Header("Root")]
        [SerializeField] private CanvasGroup rootCanvasGroup;
        [SerializeField, Range(0.1f, 1.5f)] private float fadeDuration = 0.4f;

        [Header("Header")]
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private Image turnGlow;
        [SerializeField] private TMP_Text stepCounterText;
        [SerializeField] private TMP_Text matchInfoText;
        [SerializeField] private TMP_Text userTeamTitle;
        [SerializeField] private TMP_Text aiTeamTitle;

        [Header("Hero Pool (по ролям)")]
        [SerializeField] private DraftHeroCard[] tankCards;
        [SerializeField] private DraftHeroCard[] carryCards;
        [SerializeField] private DraftHeroCard[] supportCards;

        [Header("Teams")]
        [SerializeField] private DraftSlotView[] userPickSlots;
        [SerializeField] private DraftSlotView[] aiPickSlots;
        [SerializeField] private DraftSlotView[] userBanSlots;
        [SerializeField] private DraftSlotView[] aiBanSlots;

        [Header("Timers")]
        [SerializeField] private DraftTimerBar userTimer;
        [SerializeField] private DraftTimerBar aiTimer;
        [SerializeField, Range(5f, 120f)] private float turnDurationSeconds = 30f;
        [SerializeField, Range(0f, 300f)] private float reserveDurationSeconds = 60f;

        [Header("Selection")]
        [SerializeField] private DraftSelectionPanel selectionPanel;
        [SerializeField] private Button leaveButton;

        [Header("Portraits")]
        [SerializeField] private Sprite placeholderPortrait;
        [SerializeField] private HeroPortraitEntry[] portraits = Array.Empty<HeroPortraitEntry>();

        [Header("Layout Preview (проверка вёрстки)")]
        [SerializeField] private bool enableLayoutPreview = true;
        [SerializeField, Range(0.2f, 5f)] private float previewAiThinkSeconds = 1.2f;

        [Header("Debug")]
        [SerializeField] private bool enableDetailedLogging = true;

        /// <summary>Пользователь подтвердил бан или пик героя (ID героя). Обрабатывается контроллером драфта (День 11).</summary>
        public event Action<int> OnActionConfirmed;

        /// <summary>true, если экран драфта показан.</summary>
        public bool IsVisible { get; private set; }

        /// <summary>Конфигурация матча, для которой открыт драфт.</summary>
        public MatchSessionConfig Session { get; private set; }

        /// <summary>Номер текущего хода (с нуля) или TotalSteps, если драфт завершён.</summary>
        public int CurrentStepIndex { get; private set; }

        /// <summary>Пул героев, загруженный из таблицы heroes.</summary>
        public IReadOnlyList<HeroRecord> HeroPool => _heroPool;

        private readonly Dictionary<int, DraftHeroCard> _cardsByHeroId = new Dictionary<int, DraftHeroCard>(32);
        private readonly Dictionary<int, Sprite> _portraitsByHeroId = new Dictionary<int, Sprite>(32);
        private readonly List<DraftHeroCard> _allCards = new List<DraftHeroCard>(32);
        private readonly List<DraftHeroCard> _availableBuffer = new List<DraftHeroCard>(32);

        private IReadOnlyList<HeroRecord> _heroPool = Array.Empty<HeroRecord>();
        private DraftHeroCard _selectedCard;
        private System.Random _previewRandom;
        private Tween _fadeTween;
        private Tween _turnPunchTween;
        private Tween _previewAiTween;
        private bool _isSubscribed;
        private bool _isValid;

        #region Unity Lifecycle

        private void Awake()
        {
            if (rootCanvasGroup == null)
            {
                rootCanvasGroup = GetComponent<CanvasGroup>();
            }

            _isValid = ValidateReferences();
            ApplyHiddenState();

            if (!_isValid)
            {
                return;
            }

            BuildPortraitLookup();
            CollectCards();
            SetupSlots();

            userTeamTitle.SetText("ВАША КОМАНДА");
            aiTeamTitle.SetText("КОМАНДА ИИ");
            userTeamTitle.color = DraftPalette.SapphireGlow;
            aiTeamTitle.color = DraftPalette.CrimsonGlow;

            for (int i = 0; i < _allCards.Count; i++)
            {
                _allCards[i].OnClicked += HandleCardClicked;
            }

            selectionPanel.OnConfirmClicked += HandleConfirmClicked;
            leaveButton.onClick.AddListener(HandleLeaveClicked);
        }

        private void OnEnable()
        {
            if (sessionConfigurator == null || _isSubscribed)
            {
                return;
            }

            sessionConfigurator.OnSessionConfigured += HandleSessionConfigured;
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_isSubscribed && sessionConfigurator != null)
            {
                sessionConfigurator.OnSessionConfigured -= HandleSessionConfigured;
            }

            _isSubscribed = false;
            StopPreview();
        }

        private void OnDestroy()
        {
            _fadeTween?.Kill();
            _turnPunchTween?.Kill();
            StopPreview();

            for (int i = 0; i < _allCards.Count; i++)
            {
                if (_allCards[i] != null)
                {
                    _allCards[i].OnClicked -= HandleCardClicked;
                }
            }

            if (selectionPanel != null)
            {
                selectionPanel.OnConfirmClicked -= HandleConfirmClicked;
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.RemoveListener(HandleLeaveClicked);
            }
        }

        #endregion

        #region Show / Hide

        /// <summary>
        /// Открывает экран драфта для сформированной конфигурации матча.
        /// </summary>
        public void Show(MatchSessionConfig config)
        {
            if (!_isValid)
            {
                return;
            }

            Session = config ?? throw new ArgumentNullException(nameof(config));
            _previewRandom = config.CreateMatchRandom();

            LoadHeroPool();
            BindCards();
            ResetTeams();

            matchInfoText.SetText($"{MatchCatalog.GetDifficultyDisplayName(config.Difficulty)}  ·  {config.Map.DisplayName}");

            SetStep(0);

            IsVisible = true;
            rootCanvasGroup.interactable = true;
            rootCanvasGroup.blocksRaycasts = true;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(1f, fadeDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[DraftView]</color> Экран драфта открыт: {config.UserLogin}, героев в пуле: {_heroPool.Count}, ходов: {DraftSequence.TotalSteps}.");
            }

            if (enableLayoutPreview)
            {
                StartPreviewStep();
            }
        }

        /// <summary>
        /// Плавно скрывает экран драфта.
        /// </summary>
        public void Hide(Action onComplete = null)
        {
            StopPreview();
            ClearSelection();

            IsVisible = false;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;

            _fadeTween?.Kill();
            _fadeTween = rootCanvasGroup
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad)
                .Owned(gameObject)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region View API (День 11)

        /// <summary>
        /// Переключает экран на указанный ход: заголовок фазы, чья очередь, активные шкала и слот.
        /// Значение TotalSteps переводит экран в состояние «драфт завершён».
        /// </summary>
        public void SetStep(int stepIndex)
        {
            if (!_isValid)
            {
                return;
            }

            CurrentStepIndex = Mathf.Clamp(stepIndex, 0, DraftSequence.TotalSteps);
            ClearSelection();
            DeactivateAllSlots();

            if (CurrentStepIndex >= DraftSequence.TotalSteps)
            {
                ApplyFinishedState();
                return;
            }

            DraftStep step = DraftSequence.GetStep(CurrentStepIndex);
            bool isUserTurn = step.Side == DraftSide.User;
            bool isBan = step.Action == DraftActionType.Ban;

            phaseText.SetText(DraftSequence.GetPhaseDisplayName(step.PhaseIndex));
            phaseText.color = isBan ? (Color)DraftPalette.Critical : DraftPalette.TextLight;

            if (isUserTurn)
            {
                turnText.SetText(isBan ? "ВАШ БАН" : "ВАШ ВЫБОР");
            }
            else
            {
                turnText.SetText(isBan ? "ИИ БАНИТ" : "ИИ ВЫБИРАЕТ");
            }

            turnText.color = DraftPalette.GetSideGlowColor(step.Side);
            SetTurnGlow(isBan ? (Color)DraftPalette.Critical : DraftPalette.GetSideGlowColor(step.Side), 0.35f);
            PunchTurnText();
            stepCounterText.SetText("Ход {0:0} из {1:0}", CurrentStepIndex + 1, DraftSequence.TotalSteps);

            userTimer.SetActive(isUserTurn);
            aiTimer.SetActive(!isUserTurn);
            userTimer.ResetBar(turnDurationSeconds, 1f, reserveDurationSeconds);
            aiTimer.ResetBar(turnDurationSeconds, 1f, reserveDurationSeconds);

            GetSlots(step.Side, step.Action)[step.SlotIndex].SetActive(true);

            for (int i = 0; i < _allCards.Count; i++)
            {
                DraftHeroCard card = _allCards[i];
                if (!card.IsBound)
                {
                    continue;
                }

                card.SetActionHint(step.Action);
                card.SetInteractable(isUserTurn);
            }
        }

        /// <summary>
        /// Обновляет шкалу таймера стороны (вызывается контроллером драфта каждый кадр).
        /// </summary>
        public void SetTimer(DraftSide side, float turnNormalized, float turnSecondsLeft, float reserveNormalized, float reserveSecondsLeft)
        {
            GetTimer(side).SetProgress(turnNormalized, turnSecondsLeft, reserveNormalized, reserveSecondsLeft);
        }

        /// <summary>
        /// Визуально применяет бан или пик героя для указанного хода.
        /// </summary>
        /// <returns>false, если герой не найден или уже занят.</returns>
        public bool ApplyAction(int stepIndex, int heroId)
        {
            if (!_isValid || !DraftSequence.IsValidStepIndex(stepIndex))
            {
                return false;
            }

            if (!_cardsByHeroId.TryGetValue(heroId, out DraftHeroCard card) || !card.IsAvailable)
            {
                return false;
            }

            DraftStep step = DraftSequence.GetStep(stepIndex);
            HeroRecord hero = card.Hero;

            if (card == _selectedCard)
            {
                ClearSelection();
            }

            DraftCardState newState;
            if (step.Action == DraftActionType.Ban)
            {
                newState = DraftCardState.Banned;
            }
            else
            {
                newState = step.Side == DraftSide.User ? DraftCardState.PickedByUser : DraftCardState.PickedByAi;
            }

            card.SetState(newState);

            Sprite sprite = GetPortrait(hero, out Color tint);
            GetSlots(step.Side, step.Action)[step.SlotIndex].Fill(hero, sprite, tint);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[DraftView]</color> {step} → {hero.Name}");
            }

            return true;
        }

        #endregion

        #region Event Handlers

        private void HandleSessionConfigured(MatchSessionConfig config)
        {
            if (IsVisible || config == null)
            {
                return;
            }

            if (mainMenu != null && mainMenu.IsVisible)
            {
                mainMenu.Hide();
            }

            Show(config);
        }

        private void HandleCardClicked(DraftHeroCard card)
        {
            if (!IsVisible || CurrentStepIndex >= DraftSequence.TotalSteps)
            {
                return;
            }

            DraftStep step = DraftSequence.GetStep(CurrentStepIndex);
            if (step.Side != DraftSide.User || !card.IsAvailable)
            {
                return;
            }

            if (_selectedCard == card)
            {
                return;
            }

            if (_selectedCard != null && _selectedCard.State == DraftCardState.Selected)
            {
                _selectedCard.SetState(DraftCardState.Available);
            }

            _selectedCard = card;
            card.SetState(DraftCardState.Selected);

            Sprite sprite = GetPortrait(card.Hero, out Color tint);
            selectionPanel.Show(card.Hero, sprite, tint, step.Action);
        }

        private void HandleConfirmClicked()
        {
            if (_selectedCard == null || !_selectedCard.IsBound)
            {
                return;
            }

            int heroId = _selectedCard.Hero.Id;
            OnActionConfirmed?.Invoke(heroId);

            if (enableLayoutPreview)
            {
                CompletePreviewStep(heroId);
            }
        }

        private void HandleLeaveClicked()
        {
            if (!IsVisible)
            {
                return;
            }

            if (mainMenu != null && AuthController.CurrentUserId > 0)
            {
                mainMenu.Show(AuthController.CurrentUserId, AuthController.CurrentUserLogin);
            }

            Hide();
        }

        #endregion

        #region Layout Preview

        private void StartPreviewStep()
        {
            StopPreview();

            if (!IsVisible || CurrentStepIndex >= DraftSequence.TotalSteps)
            {
                return;
            }

            DraftStep step = DraftSequence.GetStep(CurrentStepIndex);
            GetTimer(step.Side).PlayPreview(turnDurationSeconds, 1f, reserveDurationSeconds);

            if (step.Side == DraftSide.Ai)
            {
                _previewAiTween = DOVirtual
                    .DelayedCall(previewAiThinkSeconds, PreviewAiAction, true)
                    .Owned(gameObject);
            }
        }

        private void PreviewAiAction()
        {
            _availableBuffer.Clear();

            for (int i = 0; i < _allCards.Count; i++)
            {
                if (_allCards[i].IsAvailable)
                {
                    _availableBuffer.Add(_allCards[i]);
                }
            }

            if (_availableBuffer.Count == 0)
            {
                return;
            }

            DraftHeroCard choice = _availableBuffer[_previewRandom.Next(0, _availableBuffer.Count)];
            CompletePreviewStep(choice.Hero.Id);
        }

        private void CompletePreviewStep(int heroId)
        {
            if (!ApplyAction(CurrentStepIndex, heroId))
            {
                return;
            }

            SetStep(CurrentStepIndex + 1);
            StartPreviewStep();
        }

        private void StopPreview()
        {
            _previewAiTween?.Kill();
            _previewAiTween = null;

            if (userTimer != null)
            {
                userTimer.StopPreview();
            }

            if (aiTimer != null)
            {
                aiTimer.StopPreview();
            }
        }

        #endregion

        #region Data Binding

        private void LoadHeroPool()
        {
            if (DatabaseManager.Instance == null)
            {
                Debug.LogError("[DraftView] DatabaseManager не найден в сцене — пул героев не загружен.");
                _heroPool = Array.Empty<HeroRecord>();
                return;
            }

            _heroPool = new HeroRepository(DatabaseManager.Instance).LoadAll();

            if (_heroPool.Count < DraftSequence.RequiredHeroCount)
            {
                Debug.LogWarning($"[DraftView] В пуле {_heroPool.Count} героев, для драфта с банами нужно не меньше {DraftSequence.RequiredHeroCount}.");
            }
        }

        private void BindCards()
        {
            _cardsByHeroId.Clear();

            int tankIndex = 0;
            int carryIndex = 0;
            int supportIndex = 0;

            for (int i = 0; i < _heroPool.Count; i++)
            {
                HeroRecord hero = _heroPool[i];
                DraftHeroCard card;

                switch (hero.Role)
                {
                    case HeroRole.Tank:
                        card = tankIndex < tankCards.Length ? tankCards[tankIndex++] : null;
                        break;
                    case HeroRole.Carry:
                        card = carryIndex < carryCards.Length ? carryCards[carryIndex++] : null;
                        break;
                    case HeroRole.Support:
                        card = supportIndex < supportCards.Length ? supportCards[supportIndex++] : null;
                        break;
                    default:
                        card = null;
                        break;
                }

                if (card == null)
                {
                    Debug.LogWarning($"[DraftView] Для героя «{hero.Name}» не хватило карточки в ряду «{HeroRoleInfo.GetDisplayName(hero.Role)}».");
                    continue;
                }

                Sprite sprite = GetPortrait(hero, out Color tint);
                card.Bind(hero, sprite, tint);
                _cardsByHeroId[hero.Id] = card;
            }

            UnbindRemaining(tankCards, tankIndex);
            UnbindRemaining(carryCards, carryIndex);
            UnbindRemaining(supportCards, supportIndex);
        }

        private static void UnbindRemaining(DraftHeroCard[] cards, int firstFreeIndex)
        {
            for (int i = firstFreeIndex; i < cards.Length; i++)
            {
                cards[i].Unbind();
            }
        }

        private void ResetTeams()
        {
            for (int i = 0; i < userPickSlots.Length; i++)
            {
                userPickSlots[i].SetEmpty();
            }

            for (int i = 0; i < aiPickSlots.Length; i++)
            {
                aiPickSlots[i].SetEmpty();
            }

            for (int i = 0; i < userBanSlots.Length; i++)
            {
                userBanSlots[i].SetEmpty();
            }

            for (int i = 0; i < aiBanSlots.Length; i++)
            {
                aiBanSlots[i].SetEmpty();
            }
        }

        private Sprite GetPortrait(HeroRecord hero, out Color tint)
        {
            if (_portraitsByHeroId.TryGetValue(hero.Id, out Sprite sprite) && sprite != null)
            {
                tint = Color.white;
                return sprite;
            }

            tint = DraftPalette.GetRoleColor(hero.Role);
            return placeholderPortrait;
        }

        #endregion

        #region Helpers

        private void ApplyFinishedState()
        {
            phaseText.SetText("ДРАФТ ЗАВЕРШЁН");
            phaseText.color = DraftPalette.TextLight;
            turnText.SetText("КОМАНДЫ СОБРАНЫ");
            turnText.color = DraftPalette.TextBright;
            SetTurnGlow(DraftPalette.SapphireGlow, 0.2f);
            PunchTurnText();
            stepCounterText.SetText("Ход {0:0} из {1:0}", DraftSequence.TotalSteps, DraftSequence.TotalSteps);

            userTimer.SetActive(false);
            aiTimer.SetActive(false);

            for (int i = 0; i < _allCards.Count; i++)
            {
                if (_allCards[i].IsBound)
                {
                    _allCards[i].SetInteractable(false);
                }
            }

            if (enableDetailedLogging)
            {
                Debug.Log("<color=#307082>[DraftView]</color> Драфт завершён: команды собраны.");
            }
        }

        private void SetTurnGlow(Color color, float alpha)
        {
            if (turnGlow == null)
            {
                return;
            }

            color.a = alpha;
            turnGlow.color = color;
        }

        private void PunchTurnText()
        {
            _turnPunchTween?.Kill();
            RectTransform turnRect = turnText.rectTransform;
            turnRect.localScale = Vector3.one;
            _turnPunchTween = turnRect
                .DOPunchScale(Vector3.one * 0.12f, 0.35f, 5, 0.5f)
                .Owned(gameObject);
        }

        private void ClearSelection()
        {
            if (_selectedCard != null && _selectedCard.State == DraftCardState.Selected)
            {
                _selectedCard.SetState(DraftCardState.Available);
            }

            _selectedCard = null;

            if (selectionPanel != null && selectionPanel.IsVisible)
            {
                selectionPanel.Hide();
            }
        }

        private void DeactivateAllSlots()
        {
            DeactivateSlots(userPickSlots);
            DeactivateSlots(aiPickSlots);
            DeactivateSlots(userBanSlots);
            DeactivateSlots(aiBanSlots);
        }

        private static void DeactivateSlots(DraftSlotView[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].SetActive(false);
            }
        }

        private DraftSlotView[] GetSlots(DraftSide side, DraftActionType action)
        {
            if (action == DraftActionType.Ban)
            {
                return side == DraftSide.User ? userBanSlots : aiBanSlots;
            }

            return side == DraftSide.User ? userPickSlots : aiPickSlots;
        }

        private DraftTimerBar GetTimer(DraftSide side)
        {
            return side == DraftSide.User ? userTimer : aiTimer;
        }

        private void BuildPortraitLookup()
        {
            _portraitsByHeroId.Clear();

            if (portraits == null)
            {
                return;
            }

            for (int i = 0; i < portraits.Length; i++)
            {
                if (portraits[i].heroId > 0 && portraits[i].portrait != null)
                {
                    _portraitsByHeroId[portraits[i].heroId] = portraits[i].portrait;
                }
            }
        }

        private void CollectCards()
        {
            _allCards.Clear();
            _allCards.AddRange(tankCards);
            _allCards.AddRange(carryCards);
            _allCards.AddRange(supportCards);
        }

        private void SetupSlots()
        {
            for (int i = 0; i < userPickSlots.Length; i++)
            {
                userPickSlots[i].Setup(DraftSide.User, i);
            }

            for (int i = 0; i < aiPickSlots.Length; i++)
            {
                aiPickSlots[i].Setup(DraftSide.Ai, i);
            }

            for (int i = 0; i < userBanSlots.Length; i++)
            {
                userBanSlots[i].Setup(DraftSide.User, i);
            }

            for (int i = 0; i < aiBanSlots.Length; i++)
            {
                aiBanSlots[i].Setup(DraftSide.Ai, i);
            }
        }

        private void ApplyHiddenState()
        {
            IsVisible = false;
            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;
        }

        private bool ValidateReferences()
        {
            bool isValid = true;

            isValid &= Check(rootCanvasGroup, nameof(rootCanvasGroup));
            isValid &= Check(phaseText, nameof(phaseText));
            isValid &= Check(turnText, nameof(turnText));
            isValid &= Check(stepCounterText, nameof(stepCounterText));
            isValid &= Check(matchInfoText, nameof(matchInfoText));
            isValid &= Check(userTeamTitle, nameof(userTeamTitle));
            isValid &= Check(aiTeamTitle, nameof(aiTeamTitle));
            isValid &= Check(userTimer, nameof(userTimer));
            isValid &= Check(aiTimer, nameof(aiTimer));
            isValid &= Check(selectionPanel, nameof(selectionPanel));
            isValid &= Check(leaveButton, nameof(leaveButton));
            isValid &= Check(placeholderPortrait, nameof(placeholderPortrait));

            isValid &= CheckArray(tankCards, nameof(tankCards), 1);
            isValid &= CheckArray(carryCards, nameof(carryCards), 1);
            isValid &= CheckArray(supportCards, nameof(supportCards), 1);
            isValid &= CheckArray(userPickSlots, nameof(userPickSlots), DraftSequence.TeamSize);
            isValid &= CheckArray(aiPickSlots, nameof(aiPickSlots), DraftSequence.TeamSize);
            isValid &= CheckArray(userBanSlots, nameof(userBanSlots), DraftSequence.BansPerSide);
            isValid &= CheckArray(aiBanSlots, nameof(aiBanSlots), DraftSequence.BansPerSide);

            if (sessionConfigurator == null)
            {
                Debug.LogWarning("[DraftView] Поле «sessionConfigurator» не назначено — экран не откроется по кнопке «НАЧАТЬ БИТВУ».", this);
            }

            return isValid;
        }

        private bool Check(UnityEngine.Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"[DraftView] Не назначено поле «{fieldName}» в Инспекторе.", this);
            return false;
        }

        private bool CheckArray<T>(T[] array, string fieldName, int minLength) where T : UnityEngine.Object
        {
            if (array == null || array.Length < minLength)
            {
                Debug.LogError($"[DraftView] Массив «{fieldName}» должен содержать не меньше {minLength} элементов.", this);
                return false;
            }

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null)
                {
                    Debug.LogError($"[DraftView] Элемент {i} массива «{fieldName}» не назначен.", this);
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
