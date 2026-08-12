using System;
using System.Numerics;
using System.Runtime.Versioning;
using AstroCraft.Client.UI;
using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Math;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Core.World.Generation;

namespace AstroCraft.Client.Game;

public sealed class CriticModeController
{
    private const double WalkDurationSeconds = 0.0;
    private const float ScenicPitchRadians = PlayerState.ScenicSpawnPitchRadians;
    private const double ShotHoldSeconds = 0.5;
    private const double WarmupSeconds = 2.5;
    private const int MinSettleFrames = 18;
    private const int MinFramesAfterShotChange = 16;
    private const int MaxCaptureRetries = 6;

    private readonly CriticCaptureSequence _sequence;
    private readonly string? _screenshotDirectory;
    private readonly string? _primaryScreenshotPath;

    private bool _started;
    private bool _baseCameraLocked;
    private float _baseYaw;
    private float _basePitch = PlayerState.DefaultSpawnPitchRadians;
    private bool _warmupCaptureDone;
    private int _shotsCaptured;
    private CriticCaptureSequence.CameraShot? _pendingShot;
    private int _pendingShotFrames;
    private int _pendingShotRetries;
    private bool _pendingWarmupCaptureDone;
    private string? _pendingCapturePath;
    private bool _captureScheduled;
    private int _shotChangeGeneration;
    private int _framesSinceShotChange;
    private readonly List<ulong> _captureFingerprints = new();

    public CriticModeController(
        int criticSeconds,
        string? screenshotPath,
        string? screenshotDirectory)
    {
        CriticSeconds = criticSeconds > 0 ? criticSeconds : 20;
        _primaryScreenshotPath = screenshotPath;
        _screenshotDirectory = screenshotDirectory;
        _sequence = new CriticCaptureSequence();
    }

    public int CriticSeconds { get; }

    public double ElapsedSeconds { get; private set; }

    public bool IsActive => CriticSeconds > 0;

    public bool ShouldWalkForward => IsActive && _started && !_baseCameraLocked && ElapsedSeconds < WalkDurationSeconds;

    public void MarkStarted()
    {
        _started = true;
        ElapsedSeconds = 0d;
        _baseCameraLocked = false;
        _shotsCaptured = 0;
        _warmupCaptureDone = false;
        ClearPendingCapture();
        _captureFingerprints.Clear();
    }

    public void Reset()
    {
        _started = false;
        ElapsedSeconds = 0d;
        _baseCameraLocked = false;
        _shotsCaptured = 0;
        _warmupCaptureDone = false;
        ClearPendingCapture();
        _captureFingerprints.Clear();
        _sequence.ResetCaptured();
    }

    public void Advance(double deltaSeconds) => ElapsedSeconds += deltaSeconds;

    public PlayerInput ApplyCamera(PlayerInput input, PlayerState player, GameWorld? world = null)
    {
        if (!IsActive || !_started)
        {
            return input;
        }

        if (!_baseCameraLocked && ElapsedSeconds >= WalkDurationSeconds)
        {
            _baseCameraLocked = true;
            _baseYaw = player.YawRadians;
            _basePitch = MathF.Abs(player.PitchRadians - ScenicPitchRadians) < 0.2f
                ? player.PitchRadians
                : ScenicPitchRadians;
            player.YawRadians = _baseYaw;
            player.PitchRadians = _basePitch;
        }

        if (_baseCameraLocked && !_warmupCaptureDone && ElapsedSeconds >= WarmupSeconds)
        {
            // Warmup handled by TryWarmupCapture from game loop.
        }

        if (!_baseCameraLocked)
        {
            return input;
        }

        if (!_sequence.TryGetActiveShot(ElapsedSeconds, out CriticCaptureSequence.CameraShot shot))
        {
            return ZeroMovement(input, _baseYaw, _basePitch);
        }

        ApplyShotCamera(shot, player, world);
        float yaw = player.YawRadians;
        float pitch = player.PitchRadians;
        return ZeroMovement(input, yaw, pitch);
    }

    public bool TryWarmupCapture(nint windowHandle)
    {
        if (!IsActive || !_started || !_baseCameraLocked || _warmupCaptureDone)
        {
            return false;
        }

        if (ElapsedSeconds < WarmupSeconds)
        {
            return false;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "astrocraft-critic-warmup.png");
        TryCaptureWithRetry((IntPtr)windowHandle, tempPath, null, null);
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }

        _warmupCaptureDone = true;
        return true;
    }

    public bool TryCaptureShot(
        nint windowHandle,
        Action? waitForPresentedFrame,
        Func<string, bool>? gpuCapture,
        out string? capturedPath)
    {
        capturedPath = null;
        if (!IsActive || !_started || !_baseCameraLocked || !_warmupCaptureDone)
        {
            return false;
        }

        if (_pendingShot is null)
        {
            if (!_sequence.ShouldCapture(ElapsedSeconds, ShotHoldSeconds, out CriticCaptureSequence.CameraShot readyShot))
            {
                return false;
            }

            _pendingShot = readyShot;
            _pendingShotFrames = 0;
            _pendingShotRetries = 0;
            _pendingWarmupCaptureDone = false;
            _shotChangeGeneration++;
            _framesSinceShotChange = 0;
            return false;
        }

        _pendingShotFrames++;
        _framesSinceShotChange++;
        int requiredSettleFrames = MinSettleFrames + (_pendingShotRetries * 3);
        if (_pendingShotFrames < requiredSettleFrames)
        {
            return false;
        }

        CriticCaptureSequence.CameraShot shot = _pendingShot.Value;
        string path = ResolveShotPath(shot.Name);
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        waitForPresentedFrame?.Invoke();
        Thread.Sleep(24);

        if (!_pendingWarmupCaptureDone)
        {
            _pendingWarmupCaptureDone = true;
            _pendingShotFrames = 0;
            return false;
        }

        if (_pendingCapturePath is null)
        {
            _pendingCapturePath = path;
            _captureScheduled = false;
            return false;
        }

        if (!_captureScheduled)
        {
            return false;
        }

        if (!File.Exists(path) || !TryMeetsMinBytes(path, 48_000))
        {
            TryGpuOrWindowCaptureFallback((IntPtr)windowHandle, path);
        }

        if (!File.Exists(path) || !TryMeetsMinBytes(path, 48_000))
        {
            return ResetCaptureAttempt(shot, path);
        }

        if (TryGetCaptureFingerprint(path, out ulong fingerprint)
            && _captureFingerprints.Contains(fingerprint))
        {
            TryDeleteCapture(path);
            return ResetCaptureAttempt(shot, path);
        }

        if (TryAcceptCapture(path, shot, out capturedPath))
        {
            return true;
        }

        return ResetCaptureAttempt(shot, path);
    }

    private bool ResetCaptureAttempt(CriticCaptureSequence.CameraShot shot, string path)
    {
        TryDeleteCapture(path);
        _pendingCapturePath = null;
        _captureScheduled = false;
        _pendingShotRetries++;
        _pendingShotFrames = 0;
        _pendingWarmupCaptureDone = false;
        _framesSinceShotChange = 0;
        if (_pendingShotRetries >= MaxCaptureRetries)
        {
            _sequence.ReleaseShot(shot.Name);
            ClearPendingCapture();
        }

        return false;
    }

    private bool TryAcceptCapture(
        string path,
        CriticCaptureSequence.CameraShot shot,
        out string? capturedPath)
    {
        capturedPath = null;
        if (!File.Exists(path) || !TryMeetsMinBytes(path, 48_000))
        {
            return false;
        }

        if (TryGetCaptureFingerprint(path, out ulong fingerprint)
            && _captureFingerprints.Contains(fingerprint))
        {
            return false;
        }

        if (TryGetCaptureFingerprint(path, out fingerprint))
        {
            _captureFingerprints.Add(fingerprint);
        }

        _sequence.MarkCaptured(shot.Name);
        capturedPath = path;
        _shotsCaptured++;
        ClearPendingCapture();

        if (!string.IsNullOrWhiteSpace(_screenshotDirectory)
            && shot.Name.Equals("center", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_primaryScreenshotPath))
        {
            try
            {
                File.Copy(path, _primaryScreenshotPath, overwrite: true);
            }
            catch (IOException)
            {
            }
        }

        return true;
    }

    private void ClearPendingCapture()
    {
        _pendingShot = null;
        _pendingShotFrames = 0;
        _pendingShotRetries = 0;
        _pendingWarmupCaptureDone = false;
        _pendingCapturePath = null;
        _captureScheduled = false;
        _framesSinceShotChange = 0;
    }

    public bool HasPendingGpuCapture => _pendingCapturePath is not null && !_captureScheduled;

    public bool TryScheduleFrameCapture(out string? capturePath)
    {
        capturePath = null;
        if (_pendingCapturePath is null || _captureScheduled)
        {
            return false;
        }

        if (_framesSinceShotChange < MinFramesAfterShotChange)
        {
            return false;
        }

        if (_pendingShotFrames < MinSettleFrames + 4)
        {
            return false;
        }

        capturePath = _pendingCapturePath;
        _captureScheduled = true;
        return true;
    }

    private static void TryDeleteCapture(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static bool TryGetCaptureFingerprint(string path, out ulong fingerprint)
    {
        fingerprint = 0;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return false;
            }

            ulong hash = (ulong)bytes.Length;
            int step = System.Math.Max(1, bytes.Length / 64);
            for (int i = 0; i < bytes.Length; i += step)
            {
                hash = unchecked(hash * 16777619UL ^ bytes[i]);
            }

            fingerprint = hash;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryCaptureWithRetry(
        IntPtr handle,
        string path,
        Action? waitForPresentedFrame,
        Func<string, bool>? gpuCapture)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        const int minBytes = 48_000;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            waitForPresentedFrame?.Invoke();
            if (gpuCapture is not null)
            {
                gpuCapture(path);
            }

            if (TryGpuOrWindowCaptureFallback(handle, path) && TryMeetsMinBytes(path, minBytes))
            {
                return true;
            }

            Thread.Sleep(40);
        }

        waitForPresentedFrame?.Invoke();
        if (gpuCapture is not null)
        {
            gpuCapture(path);
        }

        return TryGpuOrWindowCaptureFallback(handle, path);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGpuOrWindowCaptureFallback(IntPtr handle, string path)
    {
        if (File.Exists(path) && TryMeetsMinBytes(path, 48_000))
        {
            return true;
        }

        return CriticScreenshot.TryCaptureClientArea(handle, path)
            || CriticScreenshot.TryCapture("AstroCraft", path);
    }

    private static bool TryMeetsMinBytes(string path, int minBytes)
    {
        try
        {
            return new FileInfo(path).Length >= minBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool TryGetUndergroundLookDownPose(
        PlayerState player,
        GameWorld world,
        float baseYaw,
        out Vector3 eyePosition,
        out float pitchRadians)
    {
        eyePosition = default;
        pitchRadians = PlayerState.CriticLookDownPitchOffsetRadians;

        int pitX;
        int pitZ;
        if (world.Generator is ProceduralWorldGenerator proceduralGenerator)
        {
            // Ask the world generator for the exact pit center it carved rather than
            // re-deriving it here; a previous independent recomputation used a slightly
            // different rounding scheme (an extra +0.5 combined with Floor instead of Round)
            // that drifted the aim point a full block off-center about half the time, landing
            // the "look-down" pose against a wall/corridor instead of over the open lava floor.
            (pitX, pitZ) = proceduralGenerator.GetScenicShowcasePitCenter();
        }
        else
        {
            float cosPitch = MathF.Cos(ScenicPitchRadians + PlayerState.CriticLookDownPitchOffsetRadians);
            float lookDx = MathF.Sin(baseYaw) * cosPitch;
            float lookDz = MathF.Cos(baseYaw) * cosPitch;
            float slope = MathF.Tan(-(ScenicPitchRadians + PlayerState.CriticLookDownPitchOffsetRadians));
            float targetDistance = slope > 0.05f
                ? System.Math.Clamp((2f + GameConstants.PlayerEyeHeight) / slope, 2.2f, 4.8f)
                : 3.2f;

            pitX = (int)MathF.Round(player.Position.X) + (int)MathF.Round(lookDx * targetDistance);
            pitZ = (int)MathF.Round(player.Position.Z) + (int)MathF.Round(lookDz * targetDistance);
        }

        int surfaceY = FindSurfaceY(world, pitX, pitZ);
        if (surfaceY <= 0)
        {
            return false;
        }

        int floorY = System.Math.Max(6, surfaceY - 10);
        int standY = floorY + 2;
        if (world.GetBlock(pitX, standY, pitZ) != BlockId.Air)
        {
            standY = floorY + 1;
        }

        eyePosition = new Vector3(pitX + 0.5f, standY + GameConstants.PlayerEyeHeight, pitZ + 0.5f);
        pitchRadians = System.Math.Clamp(
            ScenicPitchRadians + PlayerState.CriticLookDownPitchOffsetRadians * 1.35f,
            -1.45f,
            -0.35f);
        return true;
    }

    private static int FindSurfaceY(GameWorld world, int worldX, int worldZ)
    {
        for (int y = GameConstants.WorldHeight - 2; y >= 0; y--)
        {
            BlockId block = world.GetBlock(worldX, y, worldZ);
            if (block != BlockId.Air && block is not BlockId.Water and not BlockId.Lava)
            {
                return y;
            }
        }

        return -1;
    }

    public bool ShouldClose()
    {
        if (!IsActive || !_started)
        {
            return false;
        }

        if (_shotsCaptured >= _sequence.Shots.Count
            && ElapsedSeconds >= _sequence.Shots[^1].StartSeconds + ShotHoldSeconds + 2d)
        {
            return true;
        }

        if (ElapsedSeconds < CriticSeconds)
        {
            return false;
        }

        // Allow extra time for angle captures when PrintWindow needs additional settled frames.
        if (_shotsCaptured < _sequence.Shots.Count && ElapsedSeconds < CriticSeconds + 25)
        {
            return false;
        }

        return true;
    }

    public bool IsCameraLocked => _baseCameraLocked;

    /// <summary>Force critic shot angles onto the player immediately before rendering (capture readback).</summary>
    public void SyncRenderCamera(PlayerState player, GameWorld? world)
    {
        if (!IsActive || !_started || !_baseCameraLocked)
        {
            return;
        }

        CriticCaptureSequence.CameraShot shot;
        if (_pendingShot.HasValue)
        {
            shot = _pendingShot.Value;
        }
        else if (!_sequence.TryGetActiveShot(ElapsedSeconds, out shot))
        {
            return;
        }

        ApplyShotCamera(shot, player, world);
    }

    private void ApplyShotCamera(CriticCaptureSequence.CameraShot shot, PlayerState player, GameWorld? world)
    {
        float yaw = _baseYaw + shot.YawOffset;
        float pitch = System.Math.Clamp(_basePitch + shot.PitchOffset, -1.45f, 1.45f);

        if (shot.Name.Equals("look-down", StringComparison.OrdinalIgnoreCase)
            && world is not null
            && TryGetUndergroundLookDownPose(player, world, _baseYaw, out Vector3 undergroundEye, out float undergroundPitch))
        {
            player.Position = undergroundEye - new Vector3(0f, player.EyeHeight, 0f);
            yaw = _baseYaw;
            pitch = undergroundPitch;
        }

        player.YawRadians = yaw;
        player.PitchRadians = pitch;
    }

    public int ShotsCaptured => _shotsCaptured;

    public bool TryGetTargetBlock(PlayerState player, GameWorld world, out BlockPosition target)
    {
        if (TryRaycastSolid(player, world, player.YawRadians, player.PitchRadians, out target))
        {
            return true;
        }

        ReadOnlySpan<float> pitchOffsets =
        [
            PlayerState.CriticCenterPitchOffsetRadians,
            -0.12f,
            -0.22f,
            PlayerState.CriticLookDownPitchOffsetRadians * 0.5f,
        ];
        foreach (float pitchOffset in pitchOffsets)
        {
            float pitch = Math.Clamp(player.PitchRadians + pitchOffset, -1.45f, 1.45f);
            if (TryRaycastSolid(player, world, player.YawRadians, pitch, out target))
            {
                return true;
            }
        }

        return TryFindGroundBlockAhead(player, world, out target);
    }

    private static bool TryRaycastSolid(
        PlayerState player,
        GameWorld world,
        float yaw,
        float pitch,
        out BlockPosition target)
    {
        target = default;
        Vector3 origin = player.EyePosition;
        Vector3 direction = GetLookDirection(yaw, pitch);
        const float step = 0.1f;
        BlockPosition previous = BlockPosition.FromWorld(origin.X, origin.Y, origin.Z);

        for (float distance = 0f; distance <= GameConstants.BlockReachDistance; distance += step)
        {
            Vector3 sample = origin + direction * distance;
            BlockPosition position = BlockPosition.FromWorld(sample.X, sample.Y, sample.Z);
            if (position == previous)
            {
                continue;
            }

            BlockId blockId = world.GetBlock(position.X, position.Y, position.Z);
            if (blockId != BlockId.Air && IsSolidTarget(blockId))
            {
                target = position;
                return true;
            }

            previous = position;
        }

        return false;
    }

    private static bool TryFindGroundBlockAhead(PlayerState player, GameWorld world, out BlockPosition target)
    {
        target = default;
        Vector3 origin = player.EyePosition;
        Vector3 direction = GetLookDirection(player.YawRadians, player.PitchRadians);
        Vector3 horizontal = new(direction.X, 0f, direction.Z);
        if (horizontal.LengthSquared() < 0.01f)
        {
            horizontal = new Vector3(MathF.Sin(player.YawRadians), 0f, MathF.Cos(player.YawRadians));
        }
        else
        {
            horizontal = Vector3.Normalize(horizontal);
        }

        for (float distance = 1.5f; distance <= 6f; distance += 0.5f)
        {
            int x = (int)MathF.Floor(origin.X + horizontal.X * distance);
            int z = (int)MathF.Floor(origin.Z + horizontal.Z * distance);
            int startY = (int)MathF.Floor(origin.Y);
            for (int y = startY; y >= startY - 8; y--)
            {
                BlockId blockId = world.GetBlock(x, y, z);
                if (blockId != BlockId.Air && IsSolidTarget(blockId))
                {
                    target = new BlockPosition(x, y, z);
                    return true;
                }
            }
        }

        return false;
    }

    private string ResolveShotPath(string shotName)
    {
        if (!string.IsNullOrWhiteSpace(_screenshotDirectory))
        {
            Directory.CreateDirectory(_screenshotDirectory);
            return Path.Combine(_screenshotDirectory, $"critic-{shotName}.png");
        }

        if (shotName.Equals("center", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_primaryScreenshotPath))
        {
            return _primaryScreenshotPath;
        }

        string? primaryDir = Path.GetDirectoryName(_primaryScreenshotPath);
        string primaryName = Path.GetFileNameWithoutExtension(_primaryScreenshotPath ?? "critic") ?? "critic";
        string dir = string.IsNullOrWhiteSpace(primaryDir)
            ? Path.Combine(Environment.CurrentDirectory, "docs", "critic-screenshots")
            : primaryDir;
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{primaryName}-{shotName}.png");
    }

    private static PlayerInput ZeroMovement(PlayerInput input, float yaw, float pitch) =>
        new(0f, 0f, 0f, 0f, false, false, false, false, false, -1, yaw, pitch);

    private static Vector3 GetLookDirection(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return Vector3.Normalize(new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            MathF.Cos(yaw) * cosPitch));
    }

    private static bool IsSolidTarget(BlockId blockId) =>
        blockId is not BlockId.Water and not BlockId.Lava;
}
