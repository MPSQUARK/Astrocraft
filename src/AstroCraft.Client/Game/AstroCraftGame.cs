using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Numerics;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Discovery;
using AstroCraft.Core.Hosting;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Client.Input;
using AstroCraft.Client.Networking;
using AstroCraft.Client.Effects;
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
    private readonly MainMenuState _mainMenu = new();
    private readonly PauseMenuState _pauseMenu = new();
    private readonly JeiOverlayState _jei = new();
    private readonly ClientSettings _settings = ClientSettings.Load();
    private IWindow? _window;
    private GameClientSession? _session;
    private VulkanRenderer? _renderer;
    private ChunkMeshCache? _meshCache;
    private double _networkAccumulator;
    private double _fpsAccumulator;
    private int _frameCount;
    private bool _initialMeshBuilt;
    private bool _queuedInitialMesh;
    private bool _wasConnected;
    private bool _useEmbeddedServer;
    private bool _embeddedServerStartAttempted;
    private bool _embeddedServerBootFailed;
    private Task? _embeddedServerBootTask;
    private GameServerHost? _embeddedServer;
    private double _helloSendAccumulator;
    private double _unconnectedSeconds;
    private bool _lanDiscoveryInProgress;
    private LanDiscoveryResult? _pendingLanDiscovery;
    private string? _pendingSessionAddress;
    private int _pendingSessionPort;
    private bool _pendingSessionStart;
    private int _meshedWorldSeed = int.MinValue;
    private GameWorld? _meshedWorld;

    private bool _criticStarted;
    private float _criticPeakFps;
    private double _criticWorldWaitSeconds;
    private double _criticBootstrapSeconds;
    private bool _criticBootstrapTimedOut;
    private readonly List<string> _criticBootstrapGaps = new();
    private const int CriticBootstrapMinChunkCount = 20;
    private const int CriticBootstrapTimeoutMinChunkCount = 16;
    private const double CriticWorldSettleSeconds = 3.0;
    private const int CriticMinChunkCount = 24;
    private const uint CriticMinVertexCount = 100_000;
    private const int MaxChunkRebuildsPerFrame = 1;
    private const int MaxChunkRebuildsBootstrap = 2;
    private const int MeshProcessBudgetMilliseconds = 2;
    private const int MaxChunkRebuildsCriticWarmup = 2;
    private const double CriticStreamIntervalSeconds = 0.4;
    private const double CriticMeshWarmupSeconds = 8.0;
    private const double CriticStreamWarmupSeconds = 6.0;
    private readonly CriticModeController? _critic;
    private double _elapsedTime = 30.0;
    private double _criticStreamAccumulator;

    public AstroCraftGame(ClientLaunchOptions options)
    {
        _options = options;
        if (options.CriticSeconds > 0)
        {
            _critic = new CriticModeController(
                options.CriticSeconds,
                options.CriticScreenshotPath,
                options.CriticScreenshotDir);
        }
        _mainMenu.IsActive = options.ShowMainMenu;
        _hud.IsMainMenuActive = _mainMenu.IsActive;
        _hud.MainMenuSelectedIndex = _mainMenu.SelectedIndex;
        _settings.ApplyTo(_input, _hud);
    }

    public void Run()
    {
        Vector2D<int> windowSize = _critic is not null
            ? new Vector2D<int>(1600, 900)
            : new Vector2D<int>(1280, 720);
        WindowOptions windowOptions = WindowOptions.DefaultVulkan with
        {
            Title = "AstroCraft",
            Size = windowSize,
            VSync = true,
            FramesPerSecond = 60,
            UpdatesPerSecond = 60,
            ShouldSwapAutomatically = true,
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
        _embeddedServer?.Dispose();
        _input.Dispose();
        _window?.Dispose();
    }

    private void OnLoad()
    {
        if (_window is null)
        {
            return;
        }

        _input.Attach(_window);
        _settings.ApplyTo(_input, _hud);
        _input.MainMenuKeyDown += key => _mainMenu.HandleKeyDown(key);
        _input.MainMenuMouseClick += y => _mainMenu.HandleMouseClick(y, _window!.Size.Y);
        _input.PauseMenuKeyDown += key => _pauseMenu.HandleKeyDown(key, _settings);
        _input.PauseMenuMouseClick += y => _pauseMenu.HandleMouseClick(y, _window!.Size.Y, _settings);
        _input.JeiKeyDown += key => _jei.HandleKeyDown(key);

        if (_mainMenu.IsActive)
        {
            _input.IsMainMenuActive = true;
            _hud.StatusText = "Select an option";
            _renderer = new VulkanRenderer(_window);
            return;
        }

        bool localPlay = _options.ServerAddress is "127.0.0.1" or "localhost";
        if (localPlay && !IsUdpPortInUse(_options.Port))
        {
            _useEmbeddedServer = true;
            TryEnsureEmbeddedServer(_options.Port);
        }

        _renderer = new VulkanRenderer(_window);
        BeginConnection(_options.ServerAddress, _options.Port, _options.Discover, startEmbeddedServer: localPlay);
    }

    private void OnUpdate(double deltaSeconds)
    {
        if (_window is null || _renderer is null)
        {
            return;
        }

        _renderer.NotifyDrawableSizeChanged(_window.FramebufferSize);

        if (_mainMenu.IsActive)
        {
            UpdateMainMenu(deltaSeconds);
            ProcessPendingLanDiscovery();
            return;
        }

        ProcessPendingLanDiscovery();

        if (_pendingSessionStart)
        {
            TryStartPendingSession();
        }

        if (_session is null || _meshCache is null)
        {
            TickConnection(deltaSeconds);
            UpdateConnectingHud(deltaSeconds);
            return;
        }

        TickConnection(deltaSeconds);

        _input.BeginFrame(_window);
        _input.IsDead = _hud.IsDead;
        _hud.IsInventoryOpen = _input.IsInventoryOpen;
        SyncJeiOverlay();

        if (_input.IsPaused != _hud.IsPaused)
        {
            _hud.IsPaused = _input.IsPaused;
            _input.SetPaused(_hud.IsPaused, _window);
            if (_hud.IsPaused)
            {
                _pauseMenu.OnOpened();
            }
        }

        if (_hud.IsPaused)
        {
            UpdatePauseMenu();
            _settings.ApplyTo(_input, _hud);
            _fpsAccumulator += deltaSeconds;
            _frameCount++;
            if (_fpsAccumulator >= 1d)
            {
                _hud.Fps = _frameCount / (float)_fpsAccumulator;
                _fpsAccumulator = 0d;
                _frameCount = 0;
            }

            _window.Title = BuildWindowTitle();
            return;
        }

        PlayerInput playerInput = _input.BuildInput(_session.LocalPlayer.Inventory.SelectedHotbarSlot.BlockId);

        if (_critic is not null && _session.IsConnected && _criticStarted)
        {
            playerInput = _critic.ApplyCamera(playerInput, _session.LocalPlayer, _session.World);
        }

        if (_session.IsConnected || _session.WasEverConnected)
        {
            _session.ApplyLocalInput(playerInput, (float)deltaSeconds);
        }

        PlayerInput networkInput = playerInput with
        {
            YawRadians = _session.LocalPlayer.YawRadians,
            PitchRadians = _session.LocalPlayer.PitchRadians,
        };

        _networkAccumulator += deltaSeconds;
        int networkTicks = 0;
        while (_networkAccumulator >= GameConstants.TickDurationSeconds && networkTicks < 2)
        {
            _session.Poll();
            if (_session.IsConnected)
            {
                _session.SendInput(networkInput);
                _session.SimulateNetworkTick(networkInput);
            }

            _networkAccumulator -= GameConstants.TickDurationSeconds;
            networkTicks++;
        }

        _session.Poll();
        if (_session.IsConnected)
        {
            int chunkApplies = 2;
            if (_session.LoadedChunkCount < _session.ExpectedLoadedChunkCount)
            {
                chunkApplies = 4;
            }
            else if (_critic is not null && (!_criticStarted || _meshCache.Meshes.Count < CriticMinChunkCount))
            {
                chunkApplies = 16;
                if (_meshCache.Meshes.Count < CriticBootstrapMinChunkCount)
                {
                    _session.RequestChunkStreamFromServer(fullResync: true);
                }
            }

            _session.PumpNetwork(deltaSeconds, maxChunkApplies: chunkApplies);

            bool urgentStreaming = _session.LoadedChunkCount < _session.ExpectedLoadedChunkCount;
            _session.TickChunkStreaming(deltaSeconds, force: urgentStreaming);
        }

        ProcessWorldMeshes(deltaSeconds);
        EnsureMeshCacheMatchesSessionWorld();
        if (_session.IsConnected && !_wasConnected)
        {
            _wasConnected = true;
            _initialMeshBuilt = false;
            _queuedInitialMesh = false;
            _session.TickChunkStreaming(force: true);
            _session.RequestChunkStreamFromServer();
        }

        _elapsedTime += deltaSeconds;

        _hud.UpdateFromPlayer(_session.LocalPlayer);
        ItemPickupResult pickupResult = _session.UpdateEffects((float)deltaSeconds);
        if ((pickupResult & ItemPickupResult.PickedUp) != 0)
        {
            _hud.TriggerPickupFlash();
        }

        if ((pickupResult & ItemPickupResult.InventoryFull) != 0)
        {
            _hud.TriggerInventoryFullHint();
        }

        _hud.TickHints((float)deltaSeconds);
        _hud.IsConnected = _session.IsConnected;
        _hud.StatusText = _session.IsConnected
            ? $"Tick {_session.ServerTick}"
            : _useEmbeddedServer && _embeddedServer is null && !_embeddedServerStartAttempted
                ? "Starting local server..."
                : _session.IsDisconnected ? "Disconnected — retrying..." : "Connecting...";

        _fpsAccumulator += deltaSeconds;
        _frameCount++;
        if (_fpsAccumulator >= 1d)
        {
            _hud.Fps = _frameCount / (float)_fpsAccumulator;
            _fpsAccumulator = 0d;
            _frameCount = 0;
        }

        if (_critic is not null && _criticStarted)
        {
            _input.SetCriticMoveForward(_critic.ShouldWalkForward ? 1f : 0f);
        }
        else
        {
            _input.SetCriticMoveForward(0f);
        }

        if (_critic is not null && _criticStarted)
        {
            _critic.Advance(deltaSeconds);
            _criticPeakFps = MathF.Max(_criticPeakFps, _hud.Fps);
        }
        _hud.ChunkCount = _meshCache.Meshes.Count;
        _hud.LoadedChunkCount = _session.LoadedChunkCount;
        _hud.PendingChunkCount = _session.PendingChunkCount;
        _hud.VertexCount = (uint)Math.Max(0, _meshCache.TotalVertexCount);
        _window.Title = BuildWindowTitle();
    }

    private void ProcessWorldMeshes(double deltaSeconds)
    {
        if (_session is null || _meshCache is null || _renderer is null || !_session.IsConnected)
        {
            return;
        }

        if (_critic is not null
            && (!_criticStarted || _critic.ElapsedSeconds < CriticStreamWarmupSeconds))
        {
            if (!_criticStarted)
            {
                _session.RequestChunkStreamFromServer();
            }
            else
            {
                _criticStreamAccumulator += deltaSeconds;
                if (_criticStreamAccumulator >= CriticStreamIntervalSeconds * 2)
                {
                    _session.TickChunkStreaming(deltaSeconds, force: true);
                    _criticStreamAccumulator = 0d;
                }
            }
        }

        if (!_queuedInitialMesh && _session.World.LoadedChunkPositions.Any())
        {
            _meshCache.QueueChunksWithoutMesh();
            _queuedInitialMesh = true;
        }

        bool needsMeshWork = !_initialMeshBuilt
            || _session.HasDirtyChunks
            || _meshCache.HasPendingWork;
        if (!needsMeshWork)
        {
            return;
        }

        ChunkPosition playerChunk = ChunkPosition.FromBlock(
            (int)_session.LocalPlayer.Position.X,
            (int)_session.LocalPlayer.Position.Z);

        if (_session.HasDirtyChunks)
        {
            _meshCache.QueueDirty(_session.DirtyChunks);
            _session.ClearDirtyChunks();
        }

        int meshBudget = MaxChunkRebuildsPerFrame;
        if (!_initialMeshBuilt || _meshCache.Meshes.Count < _session.LoadedChunkCount)
        {
            meshBudget = MaxChunkRebuildsBootstrap;
        }
        else if (_critic is not null && !_criticStarted)
        {
            meshBudget = MaxChunkRebuildsBootstrap;
        }

        _meshCache.ProcessPending(_renderer, meshBudget, playerChunk, MeshProcessBudgetMilliseconds);

        if (!_initialMeshBuilt)
        {
            bool hasPlayerChunk = _meshCache.HasPlayerChunkMesh(playerChunk);
            bool enoughChunks = _critic is not null
                ? _meshCache.Meshes.Count >= CriticMinChunkCount
                : hasPlayerChunk;
            if (enoughChunks && hasPlayerChunk)
            {
                _initialMeshBuilt = true;
            }
        }
    }

    private void SyncJeiOverlay()
    {
        if (_input.IsJeiOpen != _jei.IsOpen)
        {
            _jei.SetOpen(_input.IsJeiOpen);
        }

        _hud.IsJeiOpen = _input.IsJeiOpen;
        _hud.JeiSelectedIndex = _jei.SelectedIndex;
        _hud.JeiRecipeCount = _jei.RecipeCount;
        _hud.JeiStatusText = _jei.IsOpen ? _jei.BuildStatusLine() : string.Empty;
    }

    private void UpdatePauseMenu()
    {
        _hud.PauseMenuSelectedIndex = _pauseMenu.SelectedIndex;
        _hud.IsPauseSettingsOpen = _pauseMenu.Screen == PauseMenuScreen.Settings;

        if (_pauseMenu.PendingAction == PauseMenuAction.None)
        {
            return;
        }

        PauseMenuAction action = _pauseMenu.PendingAction;
        _pauseMenu.ResetPendingAction();

        switch (action)
        {
            case PauseMenuAction.Resume:
                _input.SetPaused(false, _window!);
                _hud.IsPaused = false;
                break;
            case PauseMenuAction.OpenSettings:
                _pauseMenu.Screen = PauseMenuScreen.Settings;
                _pauseMenu.SelectedIndex = 0;
                _settings.ApplyTo(_input, _hud);
                break;
            case PauseMenuAction.Back:
                _pauseMenu.Screen = PauseMenuScreen.Main;
                _pauseMenu.SelectedIndex = 1;
                break;
            case PauseMenuAction.Disconnect:
                DisconnectToMainMenu();
                break;
        }
    }

    private void DisconnectToMainMenu()
    {
        if (_window is null)
        {
            return;
        }

        _pauseMenu.OnOpened();
        _input.SetPaused(false, _window);
        _hud.IsPaused = false;

        _session?.Dispose();
        _session = null;
        _meshCache?.Dispose();
        _meshCache = null;
        _embeddedServer?.Dispose();
        _embeddedServer = null;
        _embeddedServerBootTask = null;
        _embeddedServerBootFailed = false;
        _useEmbeddedServer = false;
        _embeddedServerStartAttempted = false;
        _initialMeshBuilt = false;
        _queuedInitialMesh = false;
        _wasConnected = false;
        _criticWorldWaitSeconds = 0d;
        _criticBootstrapSeconds = 0d;
        _criticBootstrapTimedOut = false;
        _criticBootstrapGaps.Clear();
        _criticStarted = false;
        _criticPeakFps = 0f;
        _criticStreamAccumulator = 0d;
        _meshedWorld = null;
        _meshedWorldSeed = int.MinValue;
        _lanDiscoveryInProgress = false;
        _pendingLanDiscovery = null;
        _pendingSessionAddress = null;
        _pendingSessionPort = 0;
        _pendingSessionStart = false;

        _mainMenu.IsActive = true;
        _mainMenu.SelectedIndex = 0;
        _hud.IsMainMenuActive = true;
        _input.IsMainMenuActive = true;
        _hud.StatusText = "Select an option";
    }

    private void UpdateMainMenu(double deltaSeconds)
    {
        _input.BeginFrame(_window!);
        _hud.MainMenuSelectedIndex = _mainMenu.SelectedIndex;
        _hud.IsLanBrowserActive = _mainMenu.Screen == MainMenuScreen.LanBrowser;
        _hud.LanServerCount = _mainMenu.DiscoveredServers.Count;
        _hud.LanStatusText = _mainMenu.IsDiscovering
            ? "Searching LAN..."
            : _mainMenu.Screen == MainMenuScreen.LanBrowser
                ? _mainMenu.GetSelectedLabel()
                : string.Empty;
        _hud.StatusText = _mainMenu.Screen == MainMenuScreen.LanBrowser
            ? _hud.LanStatusText
            : "Select an option";

        _fpsAccumulator += deltaSeconds;
        _frameCount++;
        if (_fpsAccumulator >= 1d)
        {
            _hud.Fps = _frameCount / (float)_fpsAccumulator;
            _fpsAccumulator = 0d;
            _frameCount = 0;
        }

        _window!.Title = BuildWindowTitle();

        if (_mainMenu.PendingAction == MainMenuAction.None)
        {
            if (_mainMenu.Screen == MainMenuScreen.LanBrowser && _mainMenu.IsDiscovering && !_lanDiscoveryInProgress)
            {
                StartLanDiscovery();
            }

            return;
        }

        MainMenuAction action = _mainMenu.PendingAction;
        _mainMenu.ResetPendingAction();

        if (action == MainMenuAction.Quit)
        {
            _window.Close();
            return;
        }

        if (action == MainMenuAction.BackToRoot)
        {
            _mainMenu.ReturnToRoot();
            _hud.StatusText = "Select an option";
            return;
        }

        if (action == MainMenuAction.BrowseLan)
        {
            _mainMenu.BeginLanDiscovery();
            _hud.StatusText = "Searching LAN...";
            StartLanDiscovery();
            return;
        }

        if (action == MainMenuAction.RefreshLan)
        {
            _mainMenu.BeginLanDiscovery();
            _hud.StatusText = "Searching LAN...";
            StartLanDiscovery();
            return;
        }

        if (action == MainMenuAction.ConnectToServer)
        {
            DiscoveredServer? server = _mainMenu.PendingServer ?? _mainMenu.GetSelectedServer();
            if (server is null)
            {
                return;
            }

            _mainMenu.IsActive = false;
            _hud.IsMainMenuActive = false;
            _input.IsMainMenuActive = false;
            _useEmbeddedServer = false;
            BeginConnection(server.Value.Address, server.Value.Port, discover: false, startEmbeddedServer: false);
            return;
        }

        _mainMenu.IsActive = false;
        _hud.IsMainMenuActive = false;
        _input.IsMainMenuActive = false;

        _useEmbeddedServer = action == MainMenuAction.PlayLocal;
        string address = _useEmbeddedServer ? "127.0.0.1" : _options.ServerAddress;
        int port = _options.Port;

        BeginConnection(address, port, discover: false, _useEmbeddedServer);
    }

    private void StartLanDiscovery()
    {
        if (_lanDiscoveryInProgress)
        {
            return;
        }

        _lanDiscoveryInProgress = true;
        _ = DiscoverLanAsync();
    }

    private void OnRender(double deltaSeconds)
    {
        if (_window is null || _renderer is null)
        {
            return;
        }

        if (_mainMenu.IsActive)
        {
            RenderMainMenu();
            TrackCritic(deltaSeconds);
            return;
        }

        if (_session is null || _meshCache is null)
        {
            RenderConnecting();
            TrackCritic(deltaSeconds);
            return;
        }

        if (_critic is not null && _criticStarted)
        {
            _critic.SyncRenderCamera(_session.LocalPlayer, _session.World);
        }

        if (!_renderer.BeginFrame())
        {
            return;
        }
        float timeOfDay = _session.IsConnected
            ? _session.ServerTimeOfDay
            : (float)((_elapsedTime % GameConstants.DayCycleSeconds) / GameConstants.DayCycleSeconds);
        PlayerState localPlayer = _session.LocalPlayer;
        Vector3 targetBlockMin = Vector3.Zero;
        float hasTarget = 0f;
        BlockPosition targetBlock = default;
        bool foundTarget = false;
        if (_critic?.IsCameraLocked == true
            && _critic.TryGetTargetBlock(localPlayer, _session.World, out BlockPosition criticTarget))
        {
            targetBlock = criticTarget;
            foundTarget = true;
        }
        else if (_session.TryGetTargetBlock(out BlockPosition sessionTarget, out _))
        {
            targetBlock = sessionTarget;
            foundTarget = true;
        }

        if (foundTarget)
        {
            targetBlockMin = new Vector3(targetBlock.X, targetBlock.Y, targetBlock.Z);
            hasTarget = 1f;
        }

        float breakBurstTimer = _session.BreakEffects.BreakBurstTimer;
        float breakingBlockTexture = ResolveBreakingBlockTexture(_session);
        if (breakingBlockTexture <= 0f && foundTarget && localPlayer.BreakProgress < 0.01f)
        {
            BlockId lookedAt = _session.World.GetBlock(targetBlock.X, targetBlock.Y, targetBlock.Z);
            if (lookedAt != BlockId.Air)
            {
                breakingBlockTexture = (float)(ushort)lookedAt;
            }
        }

        Vector3 ghostBlockMin = Vector3.Zero;
        float ghostActive = 0f;
        float ghostValid = 0f;
        float ghostTexture = 0f;
        BlockId heldBlock = localPlayer.Inventory.SelectedHotbarSlot.BlockId;
        float heldItemTexture = 0f;
        float hasHeldItem = 0f;
        if (!_hud.IsPaused && !_hud.IsInventoryOpen && !_hud.IsJeiOpen && heldBlock != BlockId.Air)
        {
            heldItemTexture = BlockRegistry.CreateDefault().Get(heldBlock).TextureSide;
            hasHeldItem = 1f;
            if (_session.TryResolvePlacement(out BlockPosition placement, out bool placementValid))
            {
                ghostBlockMin = new Vector3(placement.X, placement.Y, placement.Z);
                ghostActive = 1f;
                ghostValid = placementValid ? 1f : 0f;
                ghostTexture = heldItemTexture;
            }
        }

        float aspect = _renderer.Extent.Width / (float)Math.Max(1, _renderer.Extent.Height);
        Matrix4x4 mvp = _renderer.BuildViewProjection(localPlayer, aspect, _input.FieldOfViewDegrees);

        _renderer.DrawChunks(
            _meshCache.Meshes,
            mvp,
            _meshCache.MeshRevision,
            localPlayer.EyePosition,
            new Vector2(_renderer.Extent.Width, _renderer.Extent.Height),
            BuildSurvivalHudVector(),
            _hud.BuildHudFlags(),
            _hud.BuildOverlayProgress(),
            _hud.PackInventorySlots(localPlayer.Inventory),
            targetBlockMin,
            hasTarget,
            timeOfDay,
            breakBurstTimer,
            breakingBlockTexture,
            ghostBlockMin,
            ghostActive,
            ghostValid,
            ghostTexture,
            heldItemTexture,
            hasHeldItem,
            (float)_elapsedTime);
        _renderer.DrawBlockParticles(_session.BreakEffects.Particles, localPlayer.EyePosition);
        BlockParticle[] itemParticles = ItemEntityVisuals.BuildParticles(_session.ItemEntities.Entities, (float)_elapsedTime);
        _renderer.DrawItemEntities(itemParticles, localPlayer.EyePosition);
        if (_critic is not null
            && _criticStarted
            && _critic.TryScheduleFrameCapture(out string? capturePath)
            && capturePath is not null)
        {
            _critic.SyncRenderCamera(_session.LocalPlayer, _session.World);
            _renderer.ScheduleFrameCapture(capturePath);
        }

        _renderer.EndFrame();

        SyncHudFromMeshCache();
        TrackCritic(deltaSeconds);
    }

    private void SyncHudFromMeshCache()
    {
        if (_meshCache is null || _session is null)
        {
            return;
        }

        _hud.ChunkCount = _meshCache.Meshes.Count;
        _hud.LoadedChunkCount = _session.LoadedChunkCount;
        _hud.PendingChunkCount = _session.PendingChunkCount;
        _hud.VertexCount = (uint)Math.Max(0, _meshCache.TotalVertexCount);
    }

    private void RenderMainMenu()
    {
        if (!_renderer!.BeginFrame())
        {
            return;
        }

        float aspect = _renderer!.Extent.Width / (float)Math.Max(1, _renderer.Extent.Height);
        Matrix4x4 mvp = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            aspect,
            0.1f,
            500f);
        float timeOfDay = (float)((_elapsedTime % 120.0) / 120.0);

        _renderer.DrawChunks(
            new Dictionary<ChunkPosition, ChunkGpuMesh>(),
            mvp,
            meshRevision: 0,
            new Vector3(0f, GameConstants.SeaLevel + 8f, 0f),
            new Vector2(_renderer.Extent.Width, _renderer.Extent.Height),
            new Vector4(0f, 0f, 0f, _mainMenu.SelectedIndex / 2f),
            _hud.BuildHudFlags(),
            0f,
            ReadOnlySpan<int>.Empty,
            Vector3.Zero,
            0f,
            timeOfDay,
            time: (float)_elapsedTime);
        _renderer.EndFrame();
    }

    private void RenderConnecting()
    {
        if (!_renderer!.BeginFrame())
        {
            return;
        }

        float aspect = _renderer!.Extent.Width / (float)Math.Max(1, _renderer.Extent.Height);
        Matrix4x4 mvp = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4f,
            aspect,
            0.1f,
            500f);
        float timeOfDay = (float)((_elapsedTime % 120.0) / 120.0);

        _renderer.DrawChunks(
            new Dictionary<ChunkPosition, ChunkGpuMesh>(),
            mvp,
            meshRevision: 0,
            new Vector3(0f, GameConstants.SeaLevel + 8f, 0f),
            new Vector2(_renderer.Extent.Width, _renderer.Extent.Height),
            new Vector4(0f, 0f, 0f, 0f),
            0f,
            0f,
            ReadOnlySpan<int>.Empty,
            Vector3.Zero,
            0f,
            timeOfDay,
            time: (float)_elapsedTime);
        _renderer.EndFrame();
    }

    private static float ResolveBreakingBlockTexture(GameClientSession session)
    {
        BlockId blockId = session.BreakEffects.BurstBlock != BlockId.Air
            ? session.BreakEffects.BurstBlock
            : session.LocalPlayer.BreakingBlockId;
        if (blockId == BlockId.Air)
        {
            return 0f;
        }

        return BlockRegistry.CreateDefault().Get(blockId).TextureSide;
    }

    private Vector4 BuildSurvivalHudVector()
    {
        if (_hud.IsMainMenuActive)
        {
            float menuIndex = _hud.IsLanBrowserActive
                ? _hud.MainMenuSelectedIndex / Math.Max(1f, _hud.LanServerCount + 1f)
                : _hud.MainMenuSelectedIndex / 2f;
            return new Vector4(_hud.LanServerCount, 0f, 0f, menuIndex);
        }

        if (_hud.IsPaused)
        {
            if (_hud.IsPauseSettingsOpen)
            {
                return new Vector4(
                    _hud.FovSetting,
                    _hud.MouseSensitivitySetting,
                    _hud.InvertMouseY ? 1f : 0f,
                    _hud.PauseMenuSelectedIndex / 2f);
            }

            return new Vector4(0f, 0f, 0f, _hud.PauseMenuSelectedIndex / 2f);
        }

        if (_hud.IsJeiOpen)
        {
            float recipeIndex = _hud.JeiRecipeCount > 1
                ? _hud.JeiSelectedIndex / (float)(_hud.JeiRecipeCount - 1)
                : 0f;
            return new Vector4(0f, 0f, 0f, recipeIndex);
        }

        float healthNorm = _hud.Health / GameConstants.MaxHealth;
        float hungerNorm = _hud.Hunger / GameConstants.MaxHunger;
        if (_critic is not null && _criticStarted)
        {
            healthNorm = 1f;
            hungerNorm = 1f;
        }

        return new Vector4(
            healthNorm,
            hungerNorm,
            _hud.Oxygen / GameConstants.MaxOxygen,
            _hud.SelectedHotbarIndex / 8f);
    }

    private void TrackCritic(double deltaSeconds)
    {
        CriticModeController? critic = _critic;
        if (critic is null || _window is null)
        {
            return;
        }

        _session?.Poll();
        int packetBudget = _criticStarted ? 32 : 128;
        _session?.DrainPendingPackets(packetBudget);

        UpdateCriticWindowTitle();

        if (!_criticStarted)
        {
            if (_session is null || !_session.IsConnected || _meshCache is null)
            {
                return;
            }

            _criticBootstrapSeconds += deltaSeconds;

            int meshedChunks = _meshCache.Meshes.Count;
            uint vertexCount = (uint)Math.Max(0, _meshCache.TotalVertexCount);
            bool worldReady = meshedChunks >= CriticMinChunkCount
                && vertexCount >= CriticMinVertexCount;
            bool bootstrapTimedOut = _criticBootstrapSeconds >= _options.CriticMaxBootstrapSeconds;
            bool forceStart = worldReady;
            forceStart |= meshedChunks >= CriticBootstrapMinChunkCount && vertexCount >= 80_000;
            forceStart |= bootstrapTimedOut && meshedChunks >= CriticBootstrapTimeoutMinChunkCount;

            if (!worldReady && !forceStart)
            {
                _criticStreamAccumulator += deltaSeconds;
                if (_criticStreamAccumulator >= CriticStreamIntervalSeconds)
                {
                    _session.TickChunkStreaming(deltaSeconds, force: true);
                    _session.RequestChunkStreamFromServer(fullResync: true);
                    _criticStreamAccumulator = 0d;
                }

                return;
            }

            if (!worldReady)
            {
                _criticBootstrapTimedOut = bootstrapTimedOut && meshedChunks < CriticBootstrapTimeoutMinChunkCount;
                RecordBootstrapGaps(worldReady);
            }

            _criticWorldWaitSeconds += deltaSeconds;
            if (_criticWorldWaitSeconds < CriticWorldSettleSeconds && worldReady)
            {
                return;
            }

            _criticStarted = true;
            _criticPeakFps = 0f;
            critic.MarkStarted();
            UpdateCriticWindowTitle();
        }

        if (critic.TryWarmupCapture(GetNativeWindowHandle()))
        {
        }

        Action? waitForPresentedFrame = _renderer is not null
            ? _renderer.WaitForLastSubmittedFrame
            : null;
        Func<string, bool>? gpuCapture = OperatingSystem.IsWindows() && _renderer is not null
            ? _renderer.TryCapturePresentedFrame
            : null;
        if (critic.TryCaptureShot(GetNativeWindowHandle(), waitForPresentedFrame, gpuCapture, out _))
        {
        }

        if (!critic.ShouldClose())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.CriticScreenshotPath)
            && OperatingSystem.IsWindows()
            && !File.Exists(_options.CriticScreenshotPath))
        {
            CriticScreenshot.TryCaptureClientArea(GetNativeWindowHandle(), _options.CriticScreenshotPath);
        }

        if (!string.IsNullOrWhiteSpace(_options.CriticFpsReportPath))
        {
            bool worldReadyAtClose = _meshCache is not null
                && _meshCache.Meshes.Count >= CriticMinChunkCount
                && _meshCache.TotalVertexCount >= CriticMinVertexCount;
            bool bootstrapTimedOut = _criticBootstrapTimedOut && !worldReadyAtClose;
            var gaps = new List<string>();
            if (bootstrapTimedOut)
            {
                int meshedChunks = _meshCache?.Meshes.Count ?? _hud.ChunkCount;
                if (meshedChunks < CriticMinChunkCount)
                {
                    gaps.Add($"meshedChunks:{meshedChunks}/{CriticMinChunkCount}");
                }

                if (_meshCache is null || _meshCache.TotalVertexCount < CriticMinVertexCount)
                {
                    gaps.Add($"vertices:{_hud.VertexCount}/{CriticMinVertexCount}");
                }

                if (_session is not null && _session.LoadedChunkCount < CriticMinChunkCount)
                {
                    gaps.Add($"loadedChunks:{_session.LoadedChunkCount}/{CriticMinChunkCount}");
                }
            }

            WriteCriticFpsReport(
                _options.CriticFpsReportPath,
                MathF.Max(_hud.Fps, _criticPeakFps),
                _window.Title,
                critic.ShotsCaptured,
                bootstrapTimedOut,
                gaps);
        }

        _window.Close();
    }

    private void UpdateCriticWindowTitle()
    {
        if (_critic is null || _window is null)
        {
            return;
        }

        if (_criticStarted)
        {
            _window.Title = $"AstroCraft | Critic | {_hud.Fps:F0} FPS | chunks {_hud.ChunkCount} | verts {_hud.VertexCount}";
            return;
        }

        if (_session is null || !_session.IsConnected)
        {
            _window.Title = "AstroCraft | Connecting...";
            return;
        }

        _window.Title = "AstroCraft | Meshing...";
    }

    private string BuildWindowTitle()
    {
        if (_critic is not null)
        {
            if (_criticStarted)
            {
                return $"AstroCraft | Critic | {_hud.Fps:F0} FPS | chunks {_hud.ChunkCount} | verts {_hud.VertexCount}";
            }

            if (_session is null || !_session.IsConnected)
            {
                return "AstroCraft | Connecting...";
            }

            return "AstroCraft | Meshing...";
        }

        return _hud.BuildTitle();
    }

    private void RecordBootstrapGaps(bool worldReady)
    {
        _criticBootstrapGaps.Clear();
        if (worldReady || !_criticBootstrapTimedOut)
        {
            return;
        }

        if (_hud.ChunkCount < CriticMinChunkCount)
        {
            _criticBootstrapGaps.Add($"meshedChunks:{_hud.ChunkCount}/{CriticMinChunkCount}");
        }

        if (_hud.VertexCount < CriticMinVertexCount)
        {
            _criticBootstrapGaps.Add($"vertices:{_hud.VertexCount}/{CriticMinVertexCount}");
        }

        if (_session is not null && _session.LoadedChunkCount < CriticMinChunkCount)
        {
            _criticBootstrapGaps.Add($"loadedChunks:{_session.LoadedChunkCount}/{CriticMinChunkCount}");
        }
    }

    private nint GetNativeWindowHandle()
    {
        nint handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        return handle == 0 ? (nint)_window!.Handle : handle;
    }

    private void BeginConnection(string address, int port, bool discover, bool startEmbeddedServer)
    {
        ResetConnectionState();

        if (discover)
        {
            if (_lanDiscoveryInProgress)
            {
                return;
            }

            _lanDiscoveryInProgress = true;
            _hud.StatusText = "Searching LAN...";
            _ = DiscoverLanAsync();
            return;
        }

        _useEmbeddedServer = startEmbeddedServer;
        if (!startEmbeddedServer)
        {
            QueueSessionConnection(address, port);
            return;
        }

        address = "127.0.0.1";
        if (IsUdpPortInUse(port))
        {
            _embeddedServerStartAttempted = true;
            _hud.StatusText = "Connecting to existing server...";
            QueueSessionConnection(address, port);
            return;
        }

        _hud.StatusText = "Starting local server...";
        if (!TryEnsureEmbeddedServer(port))
        {
            _embeddedServerStartAttempted = true;
            _hud.StatusText = "Connecting to existing server...";
            QueueSessionConnection(address, port);
            return;
        }

        _embeddedServerStartAttempted = true;
        QueueSessionConnection(address, port);
    }

    private void ResetConnectionState()
    {
        _helloSendAccumulator = 0;
        _unconnectedSeconds = 0;
        _pendingSessionStart = false;
        _pendingSessionAddress = null;
        _pendingSessionPort = 0;
    }

    private async Task DiscoverLanAsync()
    {
        try
        {
            using LanDiscoveryClient discoveryClient = new();
            IReadOnlyList<DiscoveredServer> servers = await discoveryClient.DiscoverAsync().ConfigureAwait(false);
            _pendingLanDiscovery = LanDiscoveryResult.FoundServers(servers);
        }
        catch
        {
            _pendingLanDiscovery = LanDiscoveryResult.FoundServers(Array.Empty<DiscoveredServer>());
        }
    }

    private void ProcessPendingLanDiscovery()
    {
        LanDiscoveryResult? result = Interlocked.Exchange(ref _pendingLanDiscovery, null);
        if (result is null)
        {
            return;
        }

        _lanDiscoveryInProgress = false;

        if (_mainMenu.IsActive && _mainMenu.Screen == MainMenuScreen.LanBrowser)
        {
            _mainMenu.SetDiscoveredServers(result.Servers);
            _hud.StatusText = result.Servers.Count == 0
                ? "No LAN servers found"
                : $"Found {result.Servers.Count} server(s)";
            return;
        }

        if (result.Servers.Count == 0)
        {
            _mainMenu.IsActive = true;
            _mainMenu.ReturnToRoot();
            _hud.IsMainMenuActive = true;
            _input.IsMainMenuActive = true;
            _hud.StatusText = "No LAN servers found";
            return;
        }

        DiscoveredServer selected = result.Servers[0];
        QueueSessionConnection(selected.Address, selected.Port, $"Found {selected.Name}");
    }

    private void QueueSessionConnection(string address, int port, string? statusOverride = null)
    {
        _pendingSessionAddress = address;
        _pendingSessionPort = port;
        _pendingSessionStart = true;
        if (statusOverride is not null)
        {
            _hud.StatusText = statusOverride;
        }
    }

    private void TryStartPendingSession()
    {
        if (!_pendingSessionStart || _pendingSessionAddress is null)
        {
            return;
        }

        if (_useEmbeddedServer && _embeddedServer is null && !IsUdpPortInUse(_pendingSessionPort))
        {
            if (!TryEnsureEmbeddedServer(_pendingSessionPort))
            {
                return;
            }
        }

        if (_useEmbeddedServer
            && _embeddedServer is not null
            && !_embeddedServer.IsNetworkReady)
        {
            return;
        }

        string address = _pendingSessionAddress;
        int port = _pendingSessionPort;
        _pendingSessionStart = false;
        _pendingSessionAddress = null;
        _pendingSessionPort = 0;
        StartSessionConnection(address, port);
    }

    private void StartSessionConnection(string address, int port, string? statusOverride = null)
    {
        _session?.Dispose();
        _meshCache?.Dispose();

        _helloSendAccumulator = 0;
        _unconnectedSeconds = 0;
        _wasConnected = false;
        _initialMeshBuilt = false;
        _queuedInitialMesh = false;
        _criticWorldWaitSeconds = 0d;
        _criticBootstrapSeconds = 0d;
        _criticBootstrapTimedOut = false;
        _criticBootstrapGaps.Clear();
        _criticStarted = false;
        _criticPeakFps = 0f;
        _criticStreamAccumulator = 0d;
        _meshedWorld = null;
        _meshedWorldSeed = int.MinValue;

        _session = new GameClientSession(address, port, _options.FlatWorld);
        _session.ChunkUnloaded += OnSessionChunkUnloaded;
        _meshCache = new ChunkMeshCache(_session.World, _renderer!);
        _meshedWorld = _session.World;

        if (statusOverride is not null)
        {
            _hud.StatusText = statusOverride;
        }

        _session.SendHello(_options.PlayerName);
    }

    private void UpdateConnectingHud(double deltaSeconds)
    {
        _elapsedTime += deltaSeconds;
        _fpsAccumulator += deltaSeconds;
        _frameCount++;
        if (_fpsAccumulator >= 1d)
        {
            _hud.Fps = _frameCount / (float)_fpsAccumulator;
            _fpsAccumulator = 0d;
            _frameCount = 0;
        }

        if (_lanDiscoveryInProgress)
        {
            _hud.StatusText = "Searching LAN...";
        }
        else if (_pendingSessionStart)
        {
            _hud.StatusText = _useEmbeddedServer && _embeddedServer is null
                ? "Starting local server..."
                : "Connecting...";
        }
        else if (_session is not null)
        {
            _hud.StatusText = _session.IsDisconnected ? "Disconnected — retrying..." : "Connecting...";
        }

        _window!.Title = BuildWindowTitle();
    }

    private void TickConnection(double deltaSeconds)
    {
        if (_session is null)
        {
            if (_useEmbeddedServer
                && _embeddedServer is null
                && !_embeddedServerStartAttempted
                && _unconnectedSeconds >= 1.5d)
            {
                _embeddedServerStartAttempted = true;
                if (!IsUdpPortInUse(_options.Port))
                {
                    TryEnsureEmbeddedServer(_options.Port);
                    _hud.StatusText = "Starting local server...";
                }
                else
                {
                    _hud.StatusText = "Connecting to existing server...";
                }
            }

            _unconnectedSeconds += deltaSeconds;
            return;
        }

        if (_session.IsConnected)
        {
            return;
        }

        _unconnectedSeconds += deltaSeconds;
        _helloSendAccumulator += deltaSeconds;

        _session.Poll();
        _session.DrainPendingPackets(64);

        if (_helloSendAccumulator >= 0.4d && _session.IsReadyToReconnect)
        {
            _session.AttemptReconnect();
            _helloSendAccumulator = 0;
        }

        if (!_useEmbeddedServer || _embeddedServer is not null || _embeddedServerStartAttempted)
        {
            return;
        }

        if (_unconnectedSeconds < 2.0d)
        {
            return;
        }

        _embeddedServerStartAttempted = true;
        if (IsUdpPortInUse(_options.Port))
        {
            _hud.StatusText = "Connecting to existing server...";
            return;
        }

        if (TryEnsureEmbeddedServer(_options.Port))
        {
            _hud.StatusText = "Starting local server...";
        }
    }

    private static bool IsUdpPortInUse(int port)
    {
        if (IsUdpPortBound(port, IPAddress.Loopback))
        {
            return true;
        }

        if (IsUdpPortBound(port, IPAddress.Any))
        {
            return true;
        }

        return IsUdpPortListening(port);
    }

    private static bool IsUdpPortBound(int port, IPAddress address)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(address, port));
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    private static bool IsUdpPortListening(int port)
    {
        return IPGlobalProperties.GetIPGlobalProperties()
            .GetActiveUdpListeners()
            .Any(endPoint => endPoint.Port == port);
    }

    private bool TryEnsureEmbeddedServer(int port)
    {
        if (_embeddedServer is not null)
        {
            return _embeddedServer.IsNetworkReady;
        }

        if (_embeddedServerBootFailed)
        {
            return false;
        }

        if (_embeddedServerBootTask is null)
        {
            StartEmbeddedServerBoot(port);
            return false;
        }

        if (_embeddedServerBootTask.IsCompleted)
        {
            _embeddedServerBootTask = null;
            return _embeddedServer is not null && _embeddedServer.IsNetworkReady;
        }

        return false;
    }

    private void StartEmbeddedServerBoot(int port)
    {
        _embeddedServerBootTask = Task.Run(() =>
        {
            try
            {
                GameServerHost host = new("Local World", port, 42, _options.FlatWorld, enableDiscovery: false);
                host.Start();
                if (!host.WaitUntilReady(TimeSpan.FromSeconds(30)))
                {
                    host.Dispose();
                    _embeddedServerBootFailed = true;
                    return;
                }

                _embeddedServer = host;
            }
            catch (SocketException)
            {
                _embeddedServerBootFailed = true;
            }
        });
    }

    private void EnsureMeshCacheMatchesSessionWorld()
    {
        if (_session is null || _renderer is null || !_session.IsConnected)
        {
            return;
        }

        if (ReferenceEquals(_session.World, _meshedWorld) && _meshCache is not null)
        {
            return;
        }

        bool preserveCritic = _criticStarted && _critic is not null && _critic.ShotsCaptured > 0;

        _meshCache?.Dispose();
        _meshCache = new ChunkMeshCache(_session.World, _renderer);
        _meshedWorld = _session.World;
        _meshedWorldSeed = _session.WorldSeed;
        _initialMeshBuilt = false;
        _queuedInitialMesh = false;
        if (!preserveCritic)
        {
            _criticStarted = false;
            _criticPeakFps = 0f;
            _criticWorldWaitSeconds = 0d;
            _criticBootstrapSeconds = 0d;
            _criticBootstrapTimedOut = false;
            _criticBootstrapGaps.Clear();
            _criticStreamAccumulator = 0d;
            _critic?.Reset();
        }

        _session.TickChunkStreaming(force: true);
        _meshCache.QueueChunksWithoutMesh();
        _session.RequestChunkStreamFromServer();
    }

    private void OnSessionChunkUnloaded(ChunkPosition position) =>
        _meshCache?.RemoveChunkMesh(position);

    private sealed class LanDiscoveryResult
    {
        public IReadOnlyList<DiscoveredServer> Servers { get; init; } = Array.Empty<DiscoveredServer>();

        public static LanDiscoveryResult FoundServers(IReadOnlyList<DiscoveredServer> servers) =>
            new() { Servers = servers };
    }

    private static void WriteCriticFpsReport(
        string path,
        float fps,
        string windowTitle,
        int shotsCaptured,
        bool bootstrapTimedOut,
        IReadOnlyList<string> gaps)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string escapedTitle = windowTitle.Replace("\\", "\\\\").Replace("\"", "\\\"");
        string gapsJson = gaps.Count == 0
            ? "[]"
            : "[" + string.Join(",", gaps.Select(g => $"\"{g.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"")) + "]";
        string json = $"{{\"fps\":{fps:F1},\"shotsCaptured\":{shotsCaptured},\"windowTitle\":\"{escapedTitle}\",\"bootstrapTimedOut\":{(bootstrapTimedOut ? "true" : "false")},\"gaps\":{gapsJson}}}";
        File.WriteAllText(path, json);
    }
}

public sealed record ClientLaunchOptions(
    string ServerAddress,
    int Port,
    string PlayerName,
    bool Discover,
    bool FlatWorld,
    bool ShowMainMenu,
    int CriticSeconds = 0,
    string? CriticScreenshotPath = null,
    string? CriticFpsReportPath = null,
    string? CriticScreenshotDir = null,
    int CriticMaxBootstrapSeconds = 90);
