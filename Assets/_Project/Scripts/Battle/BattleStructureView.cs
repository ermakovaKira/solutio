using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Solutio.Battle
{
    /// <summary>
    /// Представление постройки на поле боя (башня или база): спрайт и собственный Light2D.
    /// Только визуал; прочность и стрельба — модель симуляции (Дни 16–17).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleStructureView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer body;
        [Tooltip("Каменный цоколь под башней: закрывает платформу карты, если её перспектива круглее основания спрайта.")]
        [SerializeField] private SpriteRenderer plinth;
        [SerializeField] private Light2D glow;
        [Tooltip("Доля внешнего радиуса, внутри которой свет горит на полную яркость.")]
        [SerializeField, Range(0f, 0.9f)] private float innerRadiusRatio = 0.15f;

        public BattleStructureKind Kind { get; private set; }
        public BattleSide Side { get; private set; }
        public LaneId Lane { get; private set; }
        public int Tier { get; private set; }
        public Light2D Glow => glow;
        public SpriteRenderer Body => body;

        public void Setup(
            BattleStructureKind kind,
            BattleSide side,
            LaneId lane,
            int tier,
            Sprite sprite,
            Sprite plinthSprite,
            float plinthScaleY,
            Color lightColor,
            float lightRadius,
            float lightIntensity,
            float scale)
        {
            Kind = kind;
            Side = side;
            Lane = lane;
            Tier = tier;

            transform.localScale = new Vector3(scale, scale, 1f);

            int sortingOrder = BattleMapMath.GetDepthSortingOrder(transform.position.y);

            if (body != null)
            {
                body.sprite = sprite;
                body.sortingOrder = sortingOrder;
            }
            else
            {
                Debug.LogError($"[Battle] У постройки «{name}» не назначен SpriteRenderer body.", this);
            }

            if (plinth != null)
            {
                bool hasPlinth = plinthSprite != null && plinthScaleY > 0f;
                plinth.enabled = hasPlinth;
                if (hasPlinth)
                {
                    plinth.sprite = plinthSprite;
                    plinth.sortingOrder = sortingOrder - 1;
                    plinth.transform.localScale = new Vector3(1f, plinthScaleY, 1f);
                }
            }

            if (glow != null)
            {
                float radius = lightRadius * scale;
                glow.color = lightColor;
                glow.intensity = lightIntensity;
                glow.pointLightOuterRadius = radius;
                glow.pointLightInnerRadius = radius * innerRadiusRatio;
            }
            else
            {
                Debug.LogError($"[Battle] У постройки «{name}» не назначен Light2D glow.", this);
            }
        }

#if UNITY_EDITOR
        public void EditorAssign(SpriteRenderer bodyRenderer, SpriteRenderer plinthRenderer, Light2D glowLight)
        {
            body = bodyRenderer;
            plinth = plinthRenderer;
            glow = glowLight;
        }
#endif
    }
}
