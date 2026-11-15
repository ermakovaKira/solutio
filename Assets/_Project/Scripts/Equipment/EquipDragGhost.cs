using System;
using DG.Tweening;
using Solutio.Draft;
using Solutio.Items;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Solutio.Equipment
{
    /// <summary>
    /// Призрак перетаскиваемого предмета на верхнем слое канваса (DragLayer).
    /// Не перехватывает лучи (CanvasGroup.blocksRaycasts = false), поэтому ячейки под курсором
    /// получают OnDrop и подсветку. При неудачном сбросе возвращается к исходной ячейке твином,
    /// при удачном — «садится» в целевую. Все твины .Owned(gameObject) — независимы от timeScale.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class EquipDragGhost : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform body;
        [SerializeField] private Image glow;
        [SerializeField] private Image background;
        [SerializeField] private Image frame;
        [SerializeField] private Image icon;

        [Header("Animation")]
        [SerializeField, Range(1f, 1.4f)] private float liftScale = 1.15f;
        [SerializeField, Range(0.05f, 0.6f)] private float liftDuration = 0.12f;
        [SerializeField, Range(0.05f, 0.6f)] private float returnDuration = 0.22f;
        [SerializeField, Range(0.05f, 0.6f)] private float landDuration = 0.1f;

        private RectTransform _rect;
        private RectTransform _parentRect;
        private Sequence _sequence;
        private bool _isInitialized;

        /// <summary>true, пока призрак виден.</summary>
        public bool IsVisible { get; private set; }

        private void Awake()
        {
            Initialize();
            HideInstant();
        }

        private void OnDisable()
        {
            KillSequence();
        }

        private void OnDestroy()
        {
            KillSequence();
        }

        /// <summary>Поднимает призрак над исходной ячейкой и привязывает к курсору.</summary>
        public void Show(ItemRecord item, Sprite sprite, RectTransform source, PointerEventData eventData)
        {
            if (!Initialize() || item == null || source == null)
            {
                return;
            }

            KillSequence();

            Color rarity = ItemRarityPalette.GetRarityColor(item.Rarity);
            Color rarityGlow = ItemRarityPalette.GetRarityGlowColor(item.Rarity);

            frame.color = rarity;
            background.color = ItemRarityPalette.GetRarityBackground(item.Rarity);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.color = rarityGlow;
            rarityGlow.a = 0.9f;
            glow.color = rarityGlow;

            _rect.sizeDelta = source.rect.size;
            _rect.position = source.position;
            body.localScale = Vector3.one;
            Follow(eventData);

            IsVisible = true;
            canvasGroup.alpha = 0.95f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;

            _sequence = DOTween.Sequence()
                .Append(body.DOScale(liftScale, liftDuration).SetEase(Ease.OutBack))
                .Owned(gameObject);
        }

        /// <summary>Перемещает призрак за курсором (без аллокаций).</summary>
        public void Follow(PointerEventData eventData)
        {
            if (!_isInitialized || eventData == null)
            {
                return;
            }

            // Overlay-канвас: камера события равна null, точка переводится в локальные координаты слоя.
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _parentRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                _rect.localPosition = new Vector3(localPoint.x, localPoint.y, 0f);
            }
        }

        /// <summary>Неудачный сброс: призрак возвращается к исходной ячейке и гаснет.</summary>
        public void ReturnTo(RectTransform target, Action onComplete)
        {
            AnimateTo(target, returnDuration, Ease.OutCubic, onComplete);
        }

        /// <summary>Удачный сброс: призрак быстро «садится» в целевую ячейку.</summary>
        public void LandOn(RectTransform target, Action onComplete)
        {
            AnimateTo(target, landDuration, Ease.OutQuad, onComplete);
        }

        /// <summary>Мгновенно прячет призрак (закрытие экрана, отмена).</summary>
        public void HideInstant()
        {
            if (!Initialize())
            {
                return;
            }

            KillSequence();
            IsVisible = false;
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            body.localScale = Vector3.one;
        }

        private void AnimateTo(RectTransform target, float duration, Ease ease, Action onComplete)
        {
            if (!Initialize() || !IsVisible)
            {
                onComplete?.Invoke();
                return;
            }

            KillSequence();

            if (target == null)
            {
                HideInstant();
                onComplete?.Invoke();
                return;
            }

            _sequence = DOTween.Sequence()
                .Append(_rect.DOMove(target.position, duration).SetEase(ease))
                .Join(body.DOScale(1f, duration).SetEase(Ease.OutQuad))
                .Append(canvasGroup.DOFade(0f, 0.08f))
                .Owned(gameObject)
                .OnComplete(() =>
                {
                    IsVisible = false;
                    body.localScale = Vector3.one;
                    onComplete?.Invoke();
                });
        }

        private bool Initialize()
        {
            if (_isInitialized)
            {
                return true;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            _rect = (RectTransform)transform;
            _parentRect = _rect.parent as RectTransform;

            if (body == null)
            {
                body = _rect;
            }

            if (_parentRect == null || glow == null || background == null || frame == null || icon == null)
            {
                Debug.LogError($"[EquipDragGhost] Не назначены ссылки или нет родительского RectTransform у «{name}».", this);
                return false;
            }

            glow.raycastTarget = false;
            background.raycastTarget = false;
            frame.raycastTarget = false;
            icon.raycastTarget = false;
            _isInitialized = true;
            return true;
        }

        private void KillSequence()
        {
            _sequence?.Kill();
            _sequence = null;
        }
    }
}
