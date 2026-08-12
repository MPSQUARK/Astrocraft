using AstroCraft.Core.Discovery;
using Silk.NET.Input;

namespace AstroCraft.Client.Game;

public enum MainMenuAction
{
    None,
    PlayLocal,
    BrowseLan,
    Quit,
    ConnectToServer,
    RefreshLan,
    BackToRoot,
}

public enum MainMenuScreen
{
    Root,
    LanBrowser,
}

public sealed class MainMenuState
{
    public const int RootOptionCount = 3;

    public bool IsActive { get; set; }
    public MainMenuScreen Screen { get; set; } = MainMenuScreen.Root;
    public int SelectedIndex { get; set; }
    public bool IsDiscovering { get; set; }
    public IReadOnlyList<DiscoveredServer> DiscoveredServers => _discoveredServers;
    public MainMenuAction PendingAction { get; private set; } = MainMenuAction.None;
    public DiscoveredServer? PendingServer { get; private set; }

    private readonly List<DiscoveredServer> _discoveredServers = new();

    public int CurrentOptionCount => Screen switch
    {
        MainMenuScreen.Root => RootOptionCount,
        MainMenuScreen.LanBrowser => Math.Max(1, _discoveredServers.Count + 1),
        _ => 0,
    };

    public void ResetPendingAction()
    {
        PendingAction = MainMenuAction.None;
        PendingServer = null;
    }

    public void BeginLanDiscovery()
    {
        Screen = MainMenuScreen.LanBrowser;
        SelectedIndex = 0;
        IsDiscovering = true;
        _discoveredServers.Clear();
    }

    public void SetDiscoveredServers(IReadOnlyList<DiscoveredServer> servers)
    {
        _discoveredServers.Clear();
        _discoveredServers.AddRange(servers);
        IsDiscovering = false;
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, CurrentOptionCount - 1));
    }

    public void ReturnToRoot()
    {
        Screen = MainMenuScreen.Root;
        SelectedIndex = 0;
        IsDiscovering = false;
        _discoveredServers.Clear();
    }

    public DiscoveredServer? GetSelectedServer()
    {
        if (Screen != MainMenuScreen.LanBrowser || SelectedIndex >= _discoveredServers.Count)
        {
            return null;
        }

        return _discoveredServers[SelectedIndex];
    }

    public string GetSelectedLabel()
    {
        if (Screen == MainMenuScreen.Root)
        {
            return SelectedIndex switch
            {
                0 => "Play Local",
                1 => "Browse LAN",
                2 => "Quit",
                _ => string.Empty,
            };
        }

        if (IsDiscovering)
        {
            return "Searching...";
        }

        if (SelectedIndex >= _discoveredServers.Count)
        {
            return "Back";
        }

        DiscoveredServer server = _discoveredServers[SelectedIndex];
        return $"{server.Name} ({server.Address}:{server.Port}) [{server.PlayerCount}]";
    }

    public void HandleKeyDown(Key key)
    {
        if (!IsActive)
        {
            return;
        }

        if (key == Key.Up || key == Key.W)
        {
            int count = CurrentOptionCount;
            if (count > 0)
            {
                SelectedIndex = (SelectedIndex - 1 + count) % count;
            }

            return;
        }

        if (key == Key.Down || key == Key.S)
        {
            int count = CurrentOptionCount;
            if (count > 0)
            {
                SelectedIndex = (SelectedIndex + 1) % count;
            }

            return;
        }

        if (key == Key.Escape)
        {
            if (Screen == MainMenuScreen.LanBrowser)
            {
                PendingAction = MainMenuAction.BackToRoot;
            }

            return;
        }

        if (key == Key.Enter || key == Key.Space)
        {
            ActivateSelected();
        }

        if (key == Key.R && Screen == MainMenuScreen.LanBrowser)
        {
            PendingAction = MainMenuAction.RefreshLan;
        }
    }

    public void HandleMouseClick(double screenY, double viewportHeight)
    {
        if (!IsActive)
        {
            return;
        }

        float centerY = (float)viewportHeight * 0.5f;
        int optionCount = CurrentOptionCount;
        for (int i = 0; i < optionCount; i++)
        {
            float optionY = centerY - 20f + i * 44f;
            if (Math.Abs((float)screenY - optionY) < 22f)
            {
                SelectedIndex = i;
                ActivateSelected();
                return;
            }
        }
    }

    private void ActivateSelected()
    {
        if (Screen == MainMenuScreen.Root)
        {
            PendingAction = SelectedIndex switch
            {
                0 => MainMenuAction.PlayLocal,
                1 => MainMenuAction.BrowseLan,
                2 => MainMenuAction.Quit,
                _ => MainMenuAction.None,
            };
            return;
        }

        if (IsDiscovering)
        {
            return;
        }

        if (SelectedIndex >= _discoveredServers.Count)
        {
            PendingAction = MainMenuAction.BackToRoot;
            return;
        }

        PendingServer = _discoveredServers[SelectedIndex];
        PendingAction = MainMenuAction.ConnectToServer;
    }
}
