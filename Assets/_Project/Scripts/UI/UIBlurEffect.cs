using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Solutio.UI
{
    /// <summary>
    /// Контроллер процедурного размытия заднего плана (UI Blur / Frosted Glass) для модальных окон.
    /// Анимация построена на плавной интерполяции альфа-канала и свойств шейдера без вызова SetActive.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class UIBlurEffect : MonoBehaviour
    {
        [Header("Shader Properties")]
        [SerializeField] private Material blurMaterialTemplate;
        [SerializeField][Range(0f, 10f)] private float targetBlurSize = 3.5f;
        [SerializeField][Range(0.1f, 1.5f)] private float animationDuration = 0.35f;

        private Image _backgroundImage;
        private Material _instancedMaterial;
        private Tween _blurTween;
        private Tween _fadeTween;

        private static readonly int BlurSizePropertyId = Shader.PropertyToID("_BlurSize");

        private void Awake()
        {
            _backgroundImage = GetComponent<Image>();

            if (blurMaterialTemplate != null)
            {
                // Создаем независимый экземпляр материала
                _instancedMaterial = new Material(blurMaterialTemplate);
                _backgroundImage.material = _instancedMaterial;
            }
            else
            {
                Debug.LogWarning("[UIBlurEffect] Шаблон материала не назначен. Используется базовый цвет.");
            }

            // Инициализация в скрытом состоянии (без вызова SetActive для сохранения плавности Canvas)
            SetBlurIntensity(0f);
            SetAlpha(0f);
            _backgroundImage.raycastTarget = false;
        }

        /// <summary>
        /// Плавно активирует эффект размытия заднего плана.
        /// </summary>
        public void EnableBlur(Action onComplete = null)
        {
            _backgroundImage.raycastTarget = true;
            _blurTween?.Kill();
            _fadeTween?.Kill();

            float currentBlur = _instancedMaterial != null ? _instancedMaterial.GetFloat(BlurSizePropertyId) : 0f;

            _blurTween = DOTween.To(() => currentBlur, SetBlurIntensity, targetBlurSize, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true);

            _fadeTween = _backgroundImage.DOFade(1f, animationDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Плавно отключает эффект размытия заднего плана.
        /// </summary>
        public void DisableBlur(Action onComplete = null)
        {
            _backgroundImage.raycastTarget = false;
            _blurTween?.Kill();
            _fadeTween?.Kill();

            float currentBlur = _instancedMaterial != null ? _instancedMaterial.GetFloat(BlurSizePropertyId) : 0f;

            _blurTween = DOTween.To(() => currentBlur, SetBlurIntensity, 0f, animationDuration * 0.75f)
                .SetEase(Ease.InQuad)
                .SetUpdate(true);

            _fadeTween = _backgroundImage.DOFade(0f, animationDuration * 0.75f)
                .SetEase(Ease.InQuad)
                .SetUpdate(true)
                .OnComplete(() => onComplete?.Invoke());
        }

        private void SetBlurIntensity(float value)
        {
            if (_instancedMaterial != null)
            {
                _instancedMaterial.SetFloat(BlurSizePropertyId, value);
            }
        }

        private void SetAlpha(float alpha)
        {
            if (_backgroundImage != null)
            {
                Color c = _backgroundImage.color;
                c.a = alpha;
                _backgroundImage.color = c;
            }
        }

        private void OnDestroy()
        {
            _blurTween?.Kill();
            _fadeTween?.Kill();

            if (_instancedMaterial != null)
            {
                Destroy(_instancedMaterial);
            }
        }
    }
}