using AVAMule.Engine;

namespace AVAMule.Tests;

internal static class Ext
{
    public static void Idle(this GameEngine g, int ticks)
    {
        for (int i = 0; i < ticks; i++) g.Tick(default);
    }
}
