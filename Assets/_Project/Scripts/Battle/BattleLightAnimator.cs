using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Solutio.Battle
{
    /// <summary>
    /// Мерцание всех источников света карты в одном Update (без отдельного компонента на каждый свет):
    /// интенсивность = база × (1 ± амплитуда × шум Перлина). Время — unscaledTime, пауза боя не замораживает огонь.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleLightAnimator : MonoBehaviour
    {
        private struct Entry
        {
            public Light2D Light;
            public float BaseIntensity;
            public float Amplitude;
            public float Speed;
            public float Seed;
        }

        [SerializeField, Range(0.1f, 10f)] private float defaultSpeed = 2.2f;

        private readonly List<Entry> _entries = new List<Entry>(48);

        public int Count => _entries.Count;

        /// <summary>Регистрирует свет. Базовой считается текущая интенсивность света.</summary>
        public void Register(Light2D light, float amplitude, float speed = -1f)
        {
            if (light == null || amplitude <= 0f)
            {
                return;
            }

            _entries.Add(new Entry
            {
                Light = light,
                BaseIntensity = light.intensity,
                Amplitude = Mathf.Clamp01(amplitude),
                Speed = speed > 0f ? speed : defaultSpeed,
                Seed = 13.37f + _entries.Count * 7.31f
            });
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private void Update()
        {
            float time = Time.unscaledTime;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry.Light == null)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                float noise = Mathf.PerlinNoise(entry.Seed, time * entry.Speed) * 2f - 1f;
                entry.Light.intensity = entry.BaseIntensity * (1f + entry.Amplitude * noise);
            }
        }
    }
}
