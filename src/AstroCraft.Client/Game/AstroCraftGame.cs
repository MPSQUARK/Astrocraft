using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Math;
using AstroCraft.Core.Simulation;
using AstroCraft.Client.Input;
using AstroCraft.Client.Networking;
using AstroCraft.Client.Rendering;
using AstroCraft.Client.UI;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace AstroCraft.Client.Game;

public sealed class AstroCraftGame : IDisposable
{
    private readonly ClientLaunchOptions _options;
    private readonly GameHud _hud = new();
    private readonly ClientInputState _input = new();
    private IWindow? _window;
    private GameClientSession? _session;
    private VulkanRenderer? _renderer;
    private ChunkMeshCache? _meshCache;
    private double _networkAccumulator;
    private double _fpsAccumulator;
    private int _frameCount;
    private bool _initialMeshBuilt;

    public AstroCraftGame(ClientLaunchOptions options)
    {
        _options = options;
    }

    public void Run()
    {
        WindowOptions windowOptions = WindowOptions.DefaultVulkan with
        {
            Title = "AstroCraft",
            Size = new Vector2D<int>(1280, 720),
            VSync = true,
            FramesPerSecond = 240,
            UpdatesPerSecond = 240,
        };

        _window = Window.Create(windowOptions);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.Run();
    }

    public void Dispose()
    {
        _meshCache?.Dispose();
        _renderer?.Dispose();
        _session?.Dispose();
        _input.Dispose();
        _window?.Dispose();
    }

    private async void OnLoad()
    {
        if (_window is null)
        {
            return;
        }

        _input.Attach(_window);
        _renderer = new VulkanRenderer(_window);
        _session = await ConnectAsync();
        _meshCache = new ChunkMeshCache(_session.World, _renderer);
        _hud.StatusText = _session.IsConnected ? "Connected" : "Connecting...";
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_window is null || _session is null || _renderer is null || _meshCache is null)
        {
            return;
        }

        if (_input.IsPaused != _hud.IsPaused)
        {
            _hud.IsPaused = _input.IsPaused;
            _input.SetPaused(_hud.IsPaused, _window);
        }

        _input.BeginFrame(_window);
        PlayerInput playerInput = _input.BuildInput();

        _networkAccumulator += deltaSeconds;
        while (_networkAccumulator >= GameConstants.TickDurationSeconds)
        {
            _session.Poll();
            if (_session.IsConnected)
            {
                _session.SendInput(playerInput);
            }

            _networkAccumulator -= GameConstants.TickDurationSeconds;
        }

        _session.Poll();
        _hud.UpdateFromPlayer(_session.LocalPlayer);
        _hud.IsConnected = _session.IsConnected;
        _hud.StatusText = _session.IsConnected ? $"Tick {_session.ServerTick}" : "Connecting...";

        _fpsAccumulator += deltaSeconds;
        _frameCount++;
        if (_fpsAccumulator >= 1d)
        {
            _hud.Fps = _frameCount / (float)_fpsAccumulator;
            _fpsAccumulator = 0d;
            _frameCount = 0;
        }

        _window.Title = _hud.BuildTitle();
    }

    private void OnRender(double deltaSeconds)
    {
        if (_window is null || _session is null || _renderer is null || _meshCache is null)
        {
            return;
        }

        if (!_initialMeshBuilt && _session.IsConnected && _session.World.LoadedChunkPositions.Any())
        {
            _meshCache.SyncAllLoaded(_renderer);
            _initialMeshBuilt = true;
        }

        if (_session.IsConnected)
        {
            _session.StreamChunksAroundPlayer();
        }

        List<ChunkPosition> dirtyChunks = _session.DirtyChunks.ToList();
        _meshCache.Sync(_renderer, dirtyChunks);
        _session.ClearDirtyChunks();

        if (!_renderer.BeginFrame())
        {
            return;
        }

        float aspect = _window.Size.X / (float)Math.Max(1, _window.Size.Y);
        Matrix4x4 mvp = _renderer.BuildViewProjection(_session.LocalPlayer, aspect);
        _renderer.DrawChunks(_meshCache.Meshes, mvp, _session.LocalPlayer.EyePosition);
        _renderer.EndFrame();
    }

    private async Task<GameClientSession> ConnectAsync()
    {
        string address = _options.ServerAddress;
        int port = _options.Port;

        if (_options.Discover)
        {
            using LanDiscoveryClient discovery = new();
            IReadOnlyList<DiscoveredServer> servers = await discovery.DiscoverAsync();
            DiscoveredServer? selected = servers.FirstOrDefault();
            if (selected is not null)
            {
                address = selected.Value.Address;
                port = selected.Value.Port;
                _hud.StatusText = $"Found {selected.Value.Name}";
            }
        }

        GameClientSession session = new(address, port, _options.FlatWorld);
        session.SendHello(_options.PlayerName);
        return session;
    }
}

public sealed record ClientLaunchOptions(
    string ServerAddress,
    int Port,
    string PlayerName,
    bool Discover,
    bool FlatWorld);
