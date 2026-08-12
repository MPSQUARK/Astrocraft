using System.Buffers.Binary;
using System.Runtime.InteropServices;
using AstroCraft.Core.Blocks;
using Silk.NET.OpenAL;

namespace AstroCraft.Client.Audio;

public enum BlockMaterialGroup
{
    Stone,
    Wood,
    Sand,
    Gravel,
    Glass,
}

public enum GameSoundEffect
{
    FootstepGrass,
    FootstepStone,
    FootstepSand,
    FootstepGravel,
    FootstepWood,
    FootstepGlass,
    BlockBreakStone,
    BlockBreakWood,
    BlockBreakSand,
    BlockBreakGravel,
    BlockBreakGlass,
    BlockPlaceStone,
    BlockPlaceWood,
    BlockPlaceSand,
    BlockPlaceGravel,
    BlockPlaceGlass,
    Jump,
    ItemPickup,
}

public sealed class GameSound : IDisposable
{
    private const int SampleRate = 22050;
    private const int SourcePoolSize = 4;

    private readonly ALContext? _alc;
    private readonly AL? _al;
    private readonly unsafe Device* _device;
    private readonly unsafe Context* _context;
    private readonly bool _available;
    private readonly Dictionary<GameSoundEffect, uint> _buffers = new();
    private readonly uint[] _sources = new uint[SourcePoolSize];
    private int _nextSource;

    public GameSound()
    {
        try
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();
        }
        catch (Exception)
        {
            return;
        }

        unsafe
        {
            _device = _alc.OpenDevice(null);
            if (_device == null)
            {
                return;
            }

            _context = _alc.CreateContext(_device, null);
            if (_context == null)
            {
                _alc.CloseDevice(_device);
                return;
            }

            if (!_alc.MakeContextCurrent(_context))
            {
                _alc.DestroyContext(_context);
                _alc.CloseDevice(_device);
                return;
            }
        }

        _available = true;
        LoadEffect(GameSoundEffect.FootstepGrass, ProceduralWav.FootstepGrass());
        LoadEffect(GameSoundEffect.FootstepStone, ProceduralWav.FootstepStone());
        LoadEffect(GameSoundEffect.FootstepSand, ProceduralWav.FootstepSand());
        LoadEffect(GameSoundEffect.FootstepGravel, ProceduralWav.FootstepGravel());
        LoadEffect(GameSoundEffect.FootstepWood, ProceduralWav.FootstepWood());
        LoadEffect(GameSoundEffect.FootstepGlass, ProceduralWav.FootstepGlass());
        LoadEffect(GameSoundEffect.BlockBreakStone, ProceduralWav.BlockBreakStone());
        LoadEffect(GameSoundEffect.BlockBreakWood, ProceduralWav.BlockBreakWood());
        LoadEffect(GameSoundEffect.BlockBreakSand, ProceduralWav.BlockBreakSand());
        LoadEffect(GameSoundEffect.BlockBreakGravel, ProceduralWav.BlockBreakGravel());
        LoadEffect(GameSoundEffect.BlockBreakGlass, ProceduralWav.BlockBreakGlass());
        LoadEffect(GameSoundEffect.BlockPlaceStone, ProceduralWav.BlockPlaceStone());
        LoadEffect(GameSoundEffect.BlockPlaceWood, ProceduralWav.BlockPlaceWood());
        LoadEffect(GameSoundEffect.BlockPlaceSand, ProceduralWav.BlockPlaceSand());
        LoadEffect(GameSoundEffect.BlockPlaceGravel, ProceduralWav.BlockPlaceGravel());
        LoadEffect(GameSoundEffect.BlockPlaceGlass, ProceduralWav.BlockPlaceGlass());
        LoadEffect(GameSoundEffect.Jump, ProceduralWav.Jump());
        LoadEffect(GameSoundEffect.ItemPickup, ProceduralWav.ItemPickup());

        unsafe
        {
            fixed (uint* sourcePtr = _sources)
            {
                _al.GenSources(SourcePoolSize, sourcePtr);
            }
        }
    }

    public bool IsAvailable => _available;

    public void Play(GameSoundEffect effect)
    {
        if (!_available || _al is null || !_buffers.TryGetValue(effect, out uint buffer))
        {
            return;
        }

        uint source = _sources[_nextSource];
        _nextSource = (_nextSource + 1) % SourcePoolSize;

        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        _al.SetSourceProperty(source, SourceFloat.Gain, 0.55f);
        _al.SourcePlay(source);
    }

    public void PlayFootstep(BlockId blockUnderFeet) => Play(ResolveFootstepEffect(blockUnderFeet));

    public void PlayBlockBreak(BlockId blockId) => Play(ResolveBreakEffect(blockId));

    public void PlayBlockPlace(BlockId blockId) => Play(ResolvePlaceEffect(blockId));

    public static BlockMaterialGroup ResolveMaterialGroup(BlockId blockId) => blockId switch
    {
        BlockId.Glass => BlockMaterialGroup.Glass,
        BlockId.Gravel => BlockMaterialGroup.Gravel,
        BlockId.Sand or BlockId.RedSand or BlockId.Sandstone or BlockId.Snow or BlockId.SnowLayer
            or BlockId.Clay or BlockId.Cactus => BlockMaterialGroup.Sand,
        BlockId.Wood or BlockId.BirchLog or BlockId.SpruceLog or BlockId.JungleLog
            or BlockId.Leaves or BlockId.BirchLeaves or BlockId.SpruceLeaves or BlockId.JungleLeaves => BlockMaterialGroup.Wood,
        _ => BlockMaterialGroup.Stone,
    };

    private static GameSoundEffect ResolveFootstepEffect(BlockId blockId)
    {
        if (blockId is BlockId.Grass or BlockId.Moss or BlockId.Dirt or BlockId.JungleGrass or BlockId.Podzol or BlockId.Mycelium)
        {
            return GameSoundEffect.FootstepGrass;
        }

        return ResolveMaterialGroup(blockId) switch
        {
            BlockMaterialGroup.Sand => GameSoundEffect.FootstepSand,
            BlockMaterialGroup.Gravel => GameSoundEffect.FootstepGravel,
            BlockMaterialGroup.Wood => GameSoundEffect.FootstepWood,
            BlockMaterialGroup.Glass => GameSoundEffect.FootstepGlass,
            _ => GameSoundEffect.FootstepStone,
        };
    }

    private static GameSoundEffect ResolveBreakEffect(BlockId blockId) =>
        ResolveMaterialGroup(blockId) switch
        {
            BlockMaterialGroup.Wood => GameSoundEffect.BlockBreakWood,
            BlockMaterialGroup.Sand => GameSoundEffect.BlockBreakSand,
            BlockMaterialGroup.Gravel => GameSoundEffect.BlockBreakGravel,
            BlockMaterialGroup.Glass => GameSoundEffect.BlockBreakGlass,
            _ => GameSoundEffect.BlockBreakStone,
        };

    private static GameSoundEffect ResolvePlaceEffect(BlockId blockId) =>
        ResolveMaterialGroup(blockId) switch
        {
            BlockMaterialGroup.Wood => GameSoundEffect.BlockPlaceWood,
            BlockMaterialGroup.Sand => GameSoundEffect.BlockPlaceSand,
            BlockMaterialGroup.Gravel => GameSoundEffect.BlockPlaceGravel,
            BlockMaterialGroup.Glass => GameSoundEffect.BlockPlaceGlass,
            _ => GameSoundEffect.BlockPlaceStone,
        };

    public void Dispose()
    {
        if (!_available || _alc is null || _al is null)
        {
            return;
        }

        unsafe
        {
            fixed (uint* sourcePtr = _sources)
            {
                _al.DeleteSources(SourcePoolSize, sourcePtr);
            }
        }

        foreach (uint buffer in _buffers.Values)
        {
            _al.DeleteBuffer(buffer);
        }

        _buffers.Clear();

        unsafe
        {
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _alc.CloseDevice(_device);
        }
    }

    private void LoadEffect(GameSoundEffect effect, byte[] wavBytes)
    {
        if (_al is null)
        {
            return;
        }

        ReadOnlySpan<short> pcm = ProceduralWav.ExtractPcm16(wavBytes);
        uint buffer = _al.GenBuffer();
        unsafe
        {
            fixed (short* pcmPtr = pcm)
            {
                _al.BufferData(buffer, BufferFormat.Mono16, pcmPtr, pcm.Length * sizeof(short), SampleRate);
            }
        }

        _buffers[effect] = buffer;
    }
}

internal static class ProceduralWav
{
    private const int SampleRate = 22050;

    public static byte[] FootstepGrass() =>
        Build(GenerateThump(frequency: 95f, durationSeconds: 0.07f, volume: 0.35f));

    public static byte[] FootstepStone() =>
        Build(GenerateThump(frequency: 180f, durationSeconds: 0.05f, volume: 0.32f));

    public static byte[] FootstepSand() =>
        Build(GenerateNoise(durationSeconds: 0.08f, volume: 0.28f, decay: 22f));

    public static byte[] FootstepGravel() =>
        Build(GenerateNoise(durationSeconds: 0.09f, volume: 0.34f, decay: 18f));

    public static byte[] FootstepWood() =>
        Build(GenerateTone(frequency: 240f, durationSeconds: 0.06f, volume: 0.3f, decay: 32f));

    public static byte[] FootstepGlass() =>
        Build(GenerateTone(frequency: 680f, durationSeconds: 0.04f, volume: 0.22f, decay: 38f));

    public static byte[] BlockBreakStone() =>
        Build(GenerateNoise(durationSeconds: 0.12f, volume: 0.42f, decay: 14f));

    public static byte[] BlockBreakWood() =>
        Build(GenerateNoise(durationSeconds: 0.10f, volume: 0.36f, decay: 20f, seed: 0xB10C));

    public static byte[] BlockBreakSand() =>
        Build(GenerateNoise(durationSeconds: 0.11f, volume: 0.34f, decay: 24f, seed: 0x5A4D));

    public static byte[] BlockBreakGravel() =>
        Build(GenerateNoise(durationSeconds: 0.13f, volume: 0.40f, decay: 16f, seed: 0x6BA1));

    public static byte[] BlockBreakGlass() =>
        Build(GenerateSweep(startFrequency: 920f, endFrequency: 260f, durationSeconds: 0.08f, volume: 0.30f));

    public static byte[] BlockPlaceStone() =>
        Build(GenerateTone(frequency: 520f, durationSeconds: 0.05f, volume: 0.3f, decay: 28f));

    public static byte[] BlockPlaceWood() =>
        Build(GenerateTone(frequency: 310f, durationSeconds: 0.06f, volume: 0.28f, decay: 26f));

    public static byte[] BlockPlaceSand() =>
        Build(GenerateNoise(durationSeconds: 0.06f, volume: 0.24f, decay: 30f, seed: 0x51A2));

    public static byte[] BlockPlaceGravel() =>
        Build(GenerateNoise(durationSeconds: 0.07f, volume: 0.30f, decay: 22f, seed: 0x6A1E));

    public static byte[] BlockPlaceGlass() =>
        Build(GenerateTone(frequency: 760f, durationSeconds: 0.04f, volume: 0.24f, decay: 34f));

    public static byte[] Jump() =>
        Build(GenerateSweep(startFrequency: 180f, endFrequency: 420f, durationSeconds: 0.1f, volume: 0.28f));

    public static byte[] ItemPickup() =>
        Build(GenerateSweep(startFrequency: 520f, endFrequency: 880f, durationSeconds: 0.08f, volume: 0.24f));

    public static ReadOnlySpan<short> ExtractPcm16(byte[] wavBytes) =>
        MemoryMarshal.Cast<byte, short>(wavBytes.AsSpan(44));

    public static byte[] Build(ReadOnlySpan<short> samples)
    {
        int dataSize = samples.Length * sizeof(short);
        byte[] wav = new byte[44 + dataSize];

        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(0), 0x46464952);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataSize);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(8), 0x45564157);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(12), 0x20746D66);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(36), 0x61746164);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataSize);

        MemoryMarshal.Cast<short, byte>(samples).CopyTo(wav.AsSpan(44));
        return wav;
    }

    private static short[] GenerateTone(float frequency, float durationSeconds, float volume, float decay)
    {
        int count = Math.Max(1, (int)(durationSeconds * SampleRate));
        short[] samples = new short[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = MathF.Exp(-t * decay);
            float sample = MathF.Sin(MathF.Tau * frequency * t) * envelope * volume;
            samples[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return samples;
    }

    private static short[] GenerateThump(float frequency, float durationSeconds, float volume)
    {
        int count = Math.Max(1, (int)(durationSeconds * SampleRate));
        short[] samples = new short[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = MathF.Exp(-t * 42f);
            float sample = MathF.Sin(MathF.Tau * frequency * t) * envelope * volume;
            samples[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return samples;
    }

    private static short[] GenerateSweep(float startFrequency, float endFrequency, float durationSeconds, float volume)
    {
        int count = Math.Max(1, (int)(durationSeconds * SampleRate));
        short[] samples = new short[count];

        for (int i = 0; i < count; i++)
        {
            float progress = i / (float)Math.Max(1, count - 1);
            float t = i / (float)SampleRate;
            float frequency = float.Lerp(startFrequency, endFrequency, progress);
            float envelope = 1f - progress;
            float sample = MathF.Sin(MathF.Tau * frequency * t) * envelope * volume;
            samples[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return samples;
    }

    private static short[] GenerateNoise(float durationSeconds, float volume, float decay, int seed = 0xA57A0)
    {
        int count = Math.Max(1, (int)(durationSeconds * SampleRate));
        short[] samples = new short[count];
        Random random = new(seed);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = MathF.Exp(-t * decay);
            float sample = ((random.NextSingle() * 2f) - 1f) * envelope * volume;
            samples[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return samples;
    }
}
