namespace Solutio.Battle
{
    /// <summary>Сторона боя: пользователь (база слева внизу) или ИИ (база справа вверху).</summary>
    public enum BattleSide
    {
        User = 0,
        Ai = 1
    }

    /// <summary>Линия карты (как в Dota 2). MidAlt — вторая ветка мида, есть только на картах с развилкой.</summary>
    public enum LaneId
    {
        Top = 0,
        Mid = 1,
        Bot = 2,
        MidAlt = 3
    }

    /// <summary>Тип особой зоны карты. Зоны читают движение (День 16) и симуляция (День 17).</summary>
    public enum MapZoneType
    {
        FireBridge = 0,
        Altar = 1,
        Ice = 2,
        Bog = 3
    }

    /// <summary>Особенность карты: правило боя, одинаковое для обеих сторон (сложность ИИ на него не влияет).</summary>
    public enum MapFeatureType
    {
        None = 0,
        RiverFlare = 1,
        PerunStrike = 2,
        Slaughter = 3,
        Frost = 4,
        Fog = 5
    }

    /// <summary>Тип постройки на поле боя.</summary>
    public enum BattleStructureKind
    {
        Tower = 0,
        Base = 1
    }
}
