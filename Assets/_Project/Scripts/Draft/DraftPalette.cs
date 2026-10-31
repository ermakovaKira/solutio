using Solutio.Heroes;
using UnityEngine;

namespace Solutio.Draft
{
    /// <summary>
    /// Единая палитра стадии драфта в UI-каноне проекта (сапфир, обсидиан, светлый текст).
    /// Цвет пользователя — сапфировый, цвет ИИ — багровый, цвета ролей — для заглушек портретов.
    /// </summary>
    public static class DraftPalette
    {
        public static readonly Color32 Obsidian = new Color32(0x0C, 0x0F, 0x17, 0xFF);
        public static readonly Color32 Panel = new Color32(0x12, 0x18, 0x24, 0xF0);
        public static readonly Color32 PanelLight = new Color32(0x1A, 0x22, 0x31, 0xFF);
        public static readonly Color32 FrameIdle = new Color32(0x2A, 0x34, 0x45, 0xFF);
        public static readonly Color32 Muted = new Color32(0x5A, 0x62, 0x70, 0xFF);
        public static readonly Color32 TextLight = new Color32(0xC8, 0xCC, 0xD4, 0xFF);
        public static readonly Color32 TextBright = new Color32(0xE8, 0xF4, 0xF8, 0xFF);

        public static readonly Color32 Sapphire = new Color32(0x30, 0x70, 0x82, 0xFF);
        public static readonly Color32 SapphireGlow = new Color32(0x4F, 0xC3, 0xE0, 0xFF);
        public static readonly Color32 Crimson = new Color32(0x8E, 0x24, 0x2E, 0xFF);
        public static readonly Color32 CrimsonGlow = new Color32(0xE0, 0x45, 0x50, 0xFF);

        public static readonly Color32 Warning = new Color32(0xE0, 0xB0, 0x40, 0xFF);
        public static readonly Color32 Critical = new Color32(0xD0, 0x40, 0x40, 0xFF);
        public static readonly Color32 Reserve = new Color32(0x8A, 0x7A, 0xC8, 0xFF);

        public static readonly Color32 RoleTank = new Color32(0x7A, 0x93, 0xAE, 0xFF);
        public static readonly Color32 RoleCarry = new Color32(0xD0, 0x7A, 0x45, 0xFF);
        public static readonly Color32 RoleSupport = new Color32(0x5D, 0xB0, 0x8C, 0xFF);

        /// <summary>Основной цвет стороны драфта.</summary>
        public static Color GetSideColor(DraftSide side)
        {
            return side == DraftSide.User ? (Color)Sapphire : Crimson;
        }

        /// <summary>Цвет свечения стороны драфта.</summary>
        public static Color GetSideGlowColor(DraftSide side)
        {
            return side == DraftSide.User ? (Color)SapphireGlow : CrimsonGlow;
        }

        /// <summary>Цвет роли героя (бейдж и подкраска заглушки портрета).</summary>
        public static Color GetRoleColor(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.Tank:
                    return RoleTank;
                case HeroRole.Carry:
                    return RoleCarry;
                case HeroRole.Support:
                    return RoleSupport;
                default:
                    return Muted;
            }
        }
    }
}
