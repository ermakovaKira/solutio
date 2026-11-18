using System;
using UnityEngine;

namespace Solutio.Battle
{
    /// <summary>Набор профилей всех боевых карт с поиском по ключу MatchCatalog.</summary>
    [CreateAssetMenu(fileName = "MapProfileLibrary", menuName = "Solutio/Battle/Map Profile Library")]
    public sealed class MapProfileLibrary : ScriptableObject
    {
        [SerializeField] private MapProfile[] profiles = Array.Empty<MapProfile>();

        public int Count => profiles.Length;

        public MapProfile GetAt(int index)
        {
            return profiles[index];
        }

        public bool TryGet(string mapKey, out MapProfile profile)
        {
            if (!string.IsNullOrEmpty(mapKey))
            {
                for (int i = 0; i < profiles.Length; i++)
                {
                    MapProfile candidate = profiles[i];
                    if (candidate != null && string.Equals(candidate.MapKey, mapKey, StringComparison.Ordinal))
                    {
                        profile = candidate;
                        return true;
                    }
                }
            }

            profile = null;
            return false;
        }

        public int IndexOf(MapProfile profile)
        {
            return Array.IndexOf(profiles, profile);
        }

#if UNITY_EDITOR
        public void EditorSetProfiles(MapProfile[] value)
        {
            profiles = value ?? Array.Empty<MapProfile>();
        }
#endif
    }
}
