using AstroCraft.Client.Game;
using AstroCraft.Core.Discovery;
using Silk.NET.Input;

namespace AstroCraft.Tests;

public class MainMenuStateTests
{
    [Fact]
    public void BrowseLan_OpensLanBrowserScreen()
    {
        MainMenuState menu = new() { IsActive = true };
        menu.HandleKeyDown(Key.Down);
        menu.HandleKeyDown(Key.Enter);

        Assert.Equal(MainMenuAction.BrowseLan, menu.PendingAction);
        menu.ResetPendingAction();
        menu.BeginLanDiscovery();

        Assert.Equal(MainMenuScreen.LanBrowser, menu.Screen);
        Assert.True(menu.IsDiscovering);
    }

    [Fact]
    public void LanBrowser_ShowsDiscoveredServers_AndSelectsForConnect()
    {
        MainMenuState menu = new() { IsActive = true };
        menu.BeginLanDiscovery();
        menu.SetDiscoveredServers(
        [
            new DiscoveredServer("Alpha", "192.168.0.10", 27000, 2),
            new DiscoveredServer("Beta", "192.168.0.11", 27001, 1),
        ]);

        Assert.Equal(3, menu.CurrentOptionCount);
        Assert.Equal("Alpha (192.168.0.10:27000) [2]", menu.GetSelectedLabel());

        menu.SelectedIndex = 1;
        menu.HandleKeyDown(Key.Enter);

        Assert.Equal(MainMenuAction.ConnectToServer, menu.PendingAction);
        Assert.Equal("Beta", menu.PendingServer?.Name);
    }

    [Fact]
    public void LanBrowser_BackReturnsToRoot()
    {
        MainMenuState menu = new() { IsActive = true };
        menu.BeginLanDiscovery();
        menu.SetDiscoveredServers([new DiscoveredServer("Alpha", "127.0.0.1", 27000, 1)]);
        menu.SelectedIndex = 1;
        menu.HandleKeyDown(Key.Enter);

        Assert.Equal(MainMenuAction.BackToRoot, menu.PendingAction);
    }
}
