using DG.Tweening;
using UnityEngine;

namespace Solutio.Draft
{
    /// <summary>
    /// Настройка твинов, ссылки на которые хранятся в полях компонентов.
    /// В проекте DOTween инициализирован с recycleAllByDefault = true (GraphicsInitializer):
    /// завершённый твин возвращается в пул и переиспользуется другим объектом. Если компонент
    /// держит ссылку на такой твин и вызывает Kill(), он убивает чужую анимацию.
    /// Поэтому «собственные» твины помечаются как непереиспользуемые.
    /// </summary>
    public static class DraftTweenExtensions
    {
        /// <summary>
        /// Делает твин непереиспользуемым, независимым от timeScale и привязанным к жизни объекта.
        /// </summary>
        public static T Owned<T>(this T tween, GameObject link) where T : Tween
        {
            return tween
                .SetRecyclable(false)
                .SetUpdate(true)
                .SetLink(link);
        }
    }
}
