using DG.Tweening;
using Solutio.Draft;
using Solutio.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.Equipment
{
    /// <summary>
    /// Подсказка предмета: название цветом редкости, редкость и тип, бонусы и строка управления.
    /// Появляется сбоку от ячейки и не выходит за край экрана. Не перехватывает лучи.
    /// Показ и скрытие через CanvasGroup и DOTween с SetUpdate(true).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class EquipTooltip : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image frame;
        [SerializeField] private Image rarityStripe;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private TMP_Text bonusText;
        [SerializeField] private TMP_Text hintText;

        [Header("Layout")]
        [SerializeField] private float gap = 14f;
        [SerializeField] private float screenMargin = 16f;
        [SerializeField, Range(0.05f, 0.5f)] private float fadeDuration = 0.12f;

        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _rect;
        private RectTransform _parentRect;
        private Tween _fadeTween;
        private bool _isInitialized;

        /// <summary>Предмет, для которого показана подсказка.</summary>
        public ItemRecord Item { get; private set; }

        private void Awake()
        {
            if (Initialize())
            {
                HideInstant();
            }
        }

        private void OnDisable()
        {
            _fadeTween?.Kill();
            _fadeTween = null;
        }

        /// <summary>Показывает подсказку для предмета рядом с ячейкой.</summary>
        public void Show(ItemRecord item, RectTransform anchor, string controlsHint)
        {
            if (!Initialize() || item == null || anchor == null)
            {
                return;
            }

            Item = item;
            Color rarityColor = ItemRarityPalette.GetRarityColor(item.Rarity);

            frame.color = rarityColor;
            rarityStripe.color = rarityColor;
            titleText.color = ItemRarityPalette.GetRarityGlowColor(item.Rarity);
            titleText.SetText(item.Name);
            rarityText.SetText($"{ItemCatalogInfo.GetRarityDisplayName(item.Rarity)} · {ItemCatalogInfo.GetTypeDisplayName(item.Type)}");
            bonusText.SetText(FormatBonusLines(item));
            hintText.SetText(controlsHint ?? string.Empty);

            PlaceNear(anchor);

            _fadeTween?.Kill();
            _fadeTween = canvasGroup
                .DOFade(1f, fadeDuration)
                .SetEase(Ease.OutQuad)
                .Owned(gameObject);
        }

        /// <summary>Плавно прячет подсказку.</summary>
        public void Hide()
        {
            if (!Initialize() || Item == null)
            {
                return;
            }

            Item = null;
            _fadeTween?.Kill();
            _fadeTween = canvasGroup
                .DOFade(0f, fadeDuration)
                .SetEase(Ease.InQuad)
                .Owned(gameObject);
        }

        /// <summary>Мгновенно прячет подсказку.</summary>
        public void HideInstant()
        {
            if (!Initialize())
            {
                return;
            }

            Item = null;
            _fadeTween?.Kill();
            canvasGroup.alpha = 0f;
        }

        /// <summary>Бонусы предмета в цветах характеристик, по строке на бонус.</summary>
        public static string FormatBonusLines(ItemRecord item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(128);
            AppendLine(builder, item.BonusAttack, "Атака", ItemRarityPalette.StatAttack);
            AppendLine(builder, item.BonusArmor, "Броня", ItemRarityPalette.StatArmor);
            AppendLine(builder, item.BonusHp, "Здоровье", ItemRarityPalette.StatHp);
            return builder.Length > 0 ? builder.ToString() : "Без бонусов";
        }

        private static void AppendLine(System.Text.StringBuilder builder, int value, string label, Color color)
        {
            if (value == 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append("<color=#").Append(ItemRarityPalette.ToHex(color)).Append(">+")
                .Append(value).Append("</color>  ").Append(label);
        }

        private void PlaceNear(RectTransform anchor)
        {
            anchor.GetWorldCorners(_corners);

            Vector2 bottomLeft = WorldToParent(_corners[0]);
            Vector2 topRight = WorldToParent(_corners[2]);
            Rect parent = _parentRect.rect;
            Vector2 size = _rect.rect.size;

            // Пивот подсказки (0, 1): позиция — её левый верхний угол.
            _rect.pivot = new Vector2(0f, 1f);

            float x = topRight.x + gap;
            if (x + size.x > parent.xMax - screenMargin)
            {
                x = bottomLeft.x - gap - size.x;
            }

            float y = topRight.y;
            y = Mathf.Min(y, parent.yMax - screenMargin);
            y = Mathf.Max(y, parent.yMin + screenMargin + size.y);
            x = Mathf.Clamp(x, parent.xMin + screenMargin, parent.xMax - screenMargin - size.x);

            _rect.localPosition = new Vector3(x, y, 0f);
        }

        private Vector2 WorldToParent(Vector3 world)
        {
            Vector3 local = _parentRect.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
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

            if (_parentRect == null || frame == null || rarityStripe == null || titleText == null
                || rarityText == null || bonusText == null || hintText == null)
            {
                Debug.LogError($"[EquipTooltip] Не назначены ссылки или нет родительского RectTransform у «{name}».", this);
                return false;
            }

            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            _isInitialized = true;
            return true;
        }
    }
}
