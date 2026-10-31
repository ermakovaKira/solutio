using UnityEngine;
using DG.Tweening;

namespace Solutio.Core.Graphics
{
    [DisallowMultipleComponent]
    public sealed class GraphicsInitializer : MonoBehaviour
    {
        [Header("DOTween Pool Configuration")]
        [SerializeField] private int _maxTweenCapacity = 500;
        [SerializeField] private int _maxSequenceCapacity = 100;
        [SerializeField] private bool _recycleAllByDefault = true;
        [SerializeField] private bool _useSafeMode = true;

        [Header("Debug")]
        [SerializeField] private bool _enableDetailedLogging = true;

        private static bool _isInitialized;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoadRuntimeMethod()
        {
            if (_isInitialized)
            {
                return;
            }

            GameObject initializerObject = new GameObject("[GRAPHICS_INITIALIZER]");
            GraphicsInitializer instance = initializerObject.AddComponent<GraphicsInitializer>();
            DontDestroyOnLoad(initializerObject);

            instance.InitializeAnimationEngine();
        }

        private void Awake()
        {
            if (_isInitialized && transform.parent == null)
            {
                if (this != null && gameObject != null && gameObject.name == "[GRAPHICS_INITIALIZER]")
                {
                    return;
                }

                Destroy(gameObject);
                return;
            }

            InitializeAnimationEngine();
        }

        public void InitializeAnimationEngine()
        {
            if (_isInitialized)
            {
                return;
            }

            DOTween.Init(
    recycleAllByDefault: _recycleAllByDefault,
    useSafeMode: _useSafeMode,
    logBehaviour: _enableDetailedLogging ? LogBehaviour.ErrorsOnly : LogBehaviour.Default
);


            DOTween.SetTweensCapacity(_maxTweenCapacity, _maxSequenceCapacity);

            _isInitialized = true;

            if (_enableDetailedLogging)
            {
                Debug.Log($"<color=#D4AF37>[Solutio Engine]</color> Графический конвейер и DOTween успешно инициализированы. " +
                          $"Зарезервированная емкость: Tweens = {_maxTweenCapacity}, Sequences = {_maxSequenceCapacity}.");
            }
        }

        private void OnDestroy()
        {
            if (gameObject.name == "[GRAPHICS_INITIALIZER]")
            {
                DOTween.Clear(true);
                _isInitialized = false;
            }
        }
    }
}
