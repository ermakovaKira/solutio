using System;
using System.Collections.Generic;
using Solutio.Auth;
using Solutio.Database;
using Solutio.Draft;
using Solutio.Items;
using Solutio.Session;
using Solutio.UI;
using UnityEngine;

namespace Solutio.Equipment
{
    /// <summary>
    /// Контроллер стадии предматчевой подготовки (День 13).
    /// После драфта (DraftController.OnDraftCompleted) выдерживает паузу на метках времени,
    /// загружает стартовые предметы (items.item_type = 'Free') из SQLite, создаёт раскладки обеих команд,
    /// раздаёт предметы ИИ по профилю сложности (скрыто от пользователя) и открывает экран экипировки.
    /// Применяет запросы представления к EquipmentLoadout; кнопка «В БОЙ» фиксирует экипировку
    /// и публикует EquipmentResult (статическое LastResult + событие) для боя (Дни 14–17).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentController : MonoBehaviour
    {
        /// <summary>«Соль» seed матча для раздачи ИИ: не пересекается с последовательностью драфта.</summary>
        private const int EquipSeedSalt = 0x45515550;

        [Header("References")]
        [SerializeField] private DraftController draftController;
        [SerializeField] private DraftViewController draftView;
        [SerializeField] private EquipViewController view;
        [SerializeField] private MainMenuController mainMenu;

        [Header("Flow")]
        [Tooltip("Пауза после завершения драфта, чтобы пользователь увидел итоговые составы, секунды реального времени.")]
        [SerializeField, Range(0f, 5f)] private float transitionDelaySeconds = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool enableDetailedLogging = true;

        /// <summary>Экипировка зафиксирована кнопкой «В БОЙ» (используется боем, День 14).</summary>
        public event Action<EquipmentResult> OnEquipmentCompleted;

        /// <summary>Итог последней подготовки (null, если экипировка не зафиксирована).</summary>
        public static EquipmentResult LastResult { get; private set; }

        /// <summary>Раскладка пользователя (null вне стадии подготовки).</summary>
        public EquipmentLoadout UserLoadout => _userLoadout;

        /// <summary>true, пока открыт экран подготовки.</summary>
        public bool IsActive { get; private set; }

        private readonly List<ItemRecord> _itemBuffer = new List<ItemRecord>(16);
        private readonly List<EquipAiDecision> _aiDecisions = new List<EquipAiDecision>(16);

        private EquipmentLoadout _userLoadout;
        private EquipmentLoadout _aiLoadout;
        private MatchSessionConfig _session;
        private DraftResult _pendingDraft;
        private double _transitionAt;
        private int _aiOptimal;
        private int _aiTotal;
        private bool _isSubscribed;

        #region Unity Lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LastResult = null;
        }

        private void Awake()
        {
            if (draftController == null)
            {
                Debug.LogError("[EquipmentController] Не назначено поле «draftController» в Инспекторе.", this);
            }

            if (draftView == null)
            {
                Debug.LogError("[EquipmentController] Не назначено поле «draftView» (DraftCanvas) в Инспекторе.", this);
            }

            if (view == null)
            {
                Debug.LogError("[EquipmentController] Не назначено поле «view» (EquipMenu) в Инспекторе.", this);
            }

            // Ссылка на префаб-ассет вместо объекта сцены: у ассета не вызывается Awake, экран не откроется.
            draftController = RequireSceneObject(draftController, "draftController");
            draftView = RequireSceneObject(draftView, "draftView");
            view = RequireSceneObject(view, "view");
            mainMenu = RequireSceneObject(mainMenu, "mainMenu");
        }

        private T RequireSceneObject<T>(T component, string fieldName) where T : Component
        {
            if (component == null || component.gameObject.scene.IsValid())
            {
                return component;
            }

            Debug.LogError($"[EquipmentController] Поле «{fieldName}» указывает на префаб-ассет «{component.name}», а не на объект сцены. " +
                           "Перетащите объект из Hierarchy в поле Инспектора.", this);
            return null;
        }

        private void OnEnable()
        {
            if (_isSubscribed || draftController == null || draftView == null || view == null)
            {
                return;
            }

            draftController.OnDraftCompleted += HandleDraftCompleted;
            draftView.OnDraftHidden += HandleDraftHidden;
            view.OnMoveRequested += HandleMoveRequested;
            view.OnQuickEquipRequested += HandleQuickEquipRequested;
            view.OnUnequipRequested += HandleUnequipRequested;
            view.OnResetRequested += HandleResetRequested;
            view.OnBattleRequested += HandleBattleRequested;
            view.OnLeaveRequested += HandleLeaveRequested;
            _isSubscribed = true;
        }

        private void OnDisable()
        {
            if (_isSubscribed)
            {
                if (draftController != null)
                {
                    draftController.OnDraftCompleted -= HandleDraftCompleted;
                }

                if (draftView != null)
                {
                    draftView.OnDraftHidden -= HandleDraftHidden;
                }

                if (view != null)
                {
                    view.OnMoveRequested -= HandleMoveRequested;
                    view.OnQuickEquipRequested -= HandleQuickEquipRequested;
                    view.OnUnequipRequested -= HandleUnequipRequested;
                    view.OnResetRequested -= HandleResetRequested;
                    view.OnBattleRequested -= HandleBattleRequested;
                    view.OnLeaveRequested -= HandleLeaveRequested;
                }
            }

            _isSubscribed = false;
            _pendingDraft = null;
        }

        private void Update()
        {
            // Переход драфт → подготовка по метке реального времени (без WaitForSeconds и корутин).
            if (_pendingDraft != null && Time.realtimeSinceStartupAsDouble >= _transitionAt)
            {
                DraftResult draft = _pendingDraft;
                _pendingDraft = null;
                BeginPreparation(draft);
            }
        }

        #endregion

        #region Flow

        private void HandleDraftCompleted(DraftResult result)
        {
            if (result == null)
            {
                return;
            }

            _pendingDraft = result;
            _transitionAt = Time.realtimeSinceStartupAsDouble + transitionDelaySeconds;
            LastResult = null;

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> Драфт завершён — экран подготовки откроется через {transitionDelaySeconds:0.0} с.");
            }
        }

        private void HandleDraftHidden()
        {
            // Пользователь вышел из драфта в меню до перехода — переход отменяется.
            if (_pendingDraft != null)
            {
                _pendingDraft = null;

                if (enableDetailedLogging)
                {
                    Debug.Log("<color=#307082>[Equipment]</color> Переход к подготовке отменён: экран драфта закрыт.");
                }
            }
        }

        private void BeginPreparation(DraftResult draft)
        {
            MatchSessionConfig session = draftView.Session ?? SessionConfigurator.ActiveSession;

            if (session == null)
            {
                Debug.LogError("[EquipmentController] Нет конфигурации матча — экран подготовки не открыт.", this);
                return;
            }

            if (!TryLoadStartingItems(_itemBuffer))
            {
                Debug.LogError("[EquipmentController] Не удалось загрузить стартовые предметы (items.item_type = 'Free') — хранилище будет пустым.", this);
            }

            try
            {
                _userLoadout = new EquipmentLoadout(draft.UserTeam, _itemBuffer);
                _aiLoadout = new EquipmentLoadout(draft.AiTeam, _itemBuffer);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"[EquipmentController] Не удалось создать раскладку: {exception.Message}", this);
                _userLoadout = null;
                _aiLoadout = null;
                return;
            }

            _session = session;
            DistributeAiItems(session);

            IsActive = true;
            draftView.Hide();
            view.Show(session, _userLoadout, draft.AiTeam);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> Подготовка: стартовых предметов {_itemBuffer.Count} (SQLite items, item_type = 'Free'), слотов у героя {EquipmentLoadout.SlotsPerHero}.");
            }
        }

        private bool TryLoadStartingItems(List<ItemRecord> buffer)
        {
            buffer.Clear();
            DatabaseManager database = DatabaseManager.Instance;

            if (database == null)
            {
                Debug.LogError("[EquipmentController] DatabaseManager.Instance не найден.", this);
                return false;
            }

            ItemRepository repository = new ItemRepository(database);
            return repository.TryLoadByType(ItemType.Free, buffer);
        }

        private void DistributeAiItems(MatchSessionConfig session)
        {
            EquipmentAiPlanner planner = new EquipmentAiPlanner(session.AiProfile);
            System.Random random = new System.Random(unchecked(session.MatchSeed ^ EquipSeedSalt));

            _aiOptimal = planner.Distribute(_aiLoadout, random, _aiDecisions);
            _aiTotal = _aiDecisions.Count;

            if (!enableDetailedLogging)
            {
                return;
            }

            Debug.Log($"<color=#8E242E>[AiEquip]</color> Раздача ИИ ({session.AiProfile.Difficulty}, синергия {session.AiProfile.ItemSynergyAwareness:0.00}): оптимальных {_aiOptimal} из {_aiTotal}. Пользователю не показывается.");

            for (int i = 0; i < _aiDecisions.Count; i++)
            {
                EquipAiDecision decision = _aiDecisions[i];
                string color = decision.Mode == EquipAiDecisionMode.Optimal ? "#E0B040" : "#8A8F98";
                Debug.Log($"<color={color}>{decision}</color>");
            }
        }

        #endregion

        #region View Requests

        private void HandleMoveRequested(EquipLocation from, EquipLocation to)
        {
            if (!IsActive || _userLoadout == null)
            {
                return;
            }

            string itemName = _userLoadout.GetItem(from)?.Name ?? "—";
            EquipMoveResult result = _userLoadout.Move(from, to);

            if (result.IsChange())
            {
                view.Refresh();
            }

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> {itemName}: {from} → {to} — {result}. Распределено {_userLoadout.EquippedItems}/{_userLoadout.TotalItems}.");
            }
        }

        private void HandleQuickEquipRequested(int stashIndex, int heroIndex)
        {
            if (!IsActive || _userLoadout == null)
            {
                return;
            }

            string itemName = stashIndex >= 0 && stashIndex < _userLoadout.Stash.Count ? _userLoadout.Stash[stashIndex].Name : "—";
            EquipMoveResult result = _userLoadout.EquipFromStash(stashIndex, heroIndex, out int slotIndex);

            if (result.IsChange())
            {
                view.Refresh();
                view.PunchSlot(heroIndex, slotIndex);
            }
            else if (result == EquipMoveResult.NoFreeSlot)
            {
                view.ShowStatus($"У героя {_userLoadout.Heroes[heroIndex].Name} нет свободных слотов", true);
            }

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> Быстрая экипировка {itemName} → герой {heroIndex}, слот {slotIndex} — {result}.");
            }
        }

        private void HandleUnequipRequested(int heroIndex, int slotIndex)
        {
            if (!IsActive || _userLoadout == null)
            {
                return;
            }

            EquipMoveResult result = _userLoadout.Unequip(heroIndex, slotIndex);

            if (result.IsChange())
            {
                view.Refresh();
            }

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> Снятие: герой {heroIndex}, слот {slotIndex} — {result}.");
            }
        }

        private void HandleResetRequested()
        {
            if (!IsActive || _userLoadout == null)
            {
                return;
            }

            int removed = _userLoadout.UnequipAll();

            if (removed > 0)
            {
                view.Refresh();
                view.ShowStatus($"Все предметы возвращены в хранилище ({removed})", false);
            }

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> Сброс экипировки: снято {removed}.");
            }
        }

        private void HandleBattleRequested()
        {
            if (!IsActive || _userLoadout == null || _aiLoadout == null || _userLoadout.IsLocked)
            {
                return;
            }

            _userLoadout.Lock();
            _aiLoadout.Lock();

            EquipmentResult result = new EquipmentResult(
                _session.MatchSeed,
                _userLoadout.BuildSnapshot(),
                _aiLoadout.BuildSnapshot(),
                CopyStash(_userLoadout),
                _aiOptimal,
                _aiTotal);

            LastResult = result;

            string message = _userLoadout.Stash.Count > 0
                ? $"Экипировка зафиксирована, в хранилище осталось предметов: {_userLoadout.Stash.Count}. Автобой — следующий этап"
                : "Экипировка зафиксирована. Автобой — следующий этап";

            view.SetLocked(message);

            if (enableDetailedLogging)
            {
                Debug.Log($"<color=#307082>[Equipment]</color> «В БОЙ»: {result}.");

                for (int i = 0; i < result.UserTeam.Count; i++)
                {
                    Debug.Log($"<color=#307082>[Equipment]</color> Пользователь — {result.UserTeam[i]}");
                }

                for (int i = 0; i < result.AiTeam.Count; i++)
                {
                    Debug.Log($"<color=#8E242E>[AiEquip]</color> ИИ — {result.AiTeam[i]}");
                }
            }

            OnEquipmentCompleted?.Invoke(result);
        }

        private void HandleLeaveRequested()
        {
            if (!IsActive)
            {
                return;
            }

            StopPreparation();

            if (mainMenu != null && AuthController.CurrentUserId > 0)
            {
                mainMenu.Show(AuthController.CurrentUserId, AuthController.CurrentUserLogin);
            }

            view.Hide();

            if (enableDetailedLogging)
            {
                Debug.Log("<color=#307082>[Equipment]</color> Выход в главное меню со стадии подготовки.");
            }
        }

        #endregion

        private void StopPreparation()
        {
            IsActive = false;
            _pendingDraft = null;
            _userLoadout = null;
            _aiLoadout = null;
            _session = null;
            _aiDecisions.Clear();
            _aiOptimal = 0;
            _aiTotal = 0;
        }

        private static IReadOnlyList<ItemRecord> CopyStash(EquipmentLoadout loadout)
        {
            ItemRecord[] copy = new ItemRecord[loadout.Stash.Count];

            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = loadout.Stash[i];
            }

            return copy;
        }
    }
}
