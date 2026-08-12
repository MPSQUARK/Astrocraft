using AstroCraft.Core;
using AstroCraft.Core.Server;

namespace AstroCraft.Tests;

public class GameTimeTests
{
    [Fact]
    public void GameServer_AdvancesTimeOfDayEachTick()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        float start = server.TimeOfDay;

        server.Tick();

        float expected = (start + (float)(GameConstants.TickDurationSeconds / GameConstants.DayCycleSeconds)) % 1f;
        Assert.Equal(expected, server.TimeOfDay, 5);
    }

    [Fact]
    public void GameServer_TimeOfDayEventuallyWraps()
    {
        GameServer server = new(seed: 1, flatWorld: true);
        float previous = server.TimeOfDay;
        bool wrapped = false;

        for (int i = 0; i < 50_000 && !wrapped; i++)
        {
            server.Tick();
            if (server.TimeOfDay < previous)
            {
                wrapped = true;
            }

            Assert.InRange(server.TimeOfDay, 0f, 1f);
            previous = server.TimeOfDay;
        }

        Assert.True(wrapped);
    }
}
