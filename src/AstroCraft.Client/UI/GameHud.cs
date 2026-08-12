using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;

namespace AstroCraft.Client.UI;

public sealed class GameHud
{
    private static readonly BlockRegistry s_blockRegistry = BlockRegistry.CreateDefault();
    /// <summary>Minecraft GUI hotbar width in texture pixels (ice_and_lake_trees_gui.jpg reference).</summary>
    public const float HotbarWidthGuiUnits = 182f;

    /// <summary>Minecraft GUI hotbar height in texture pixels.</summary>
    public const float HotbarHeightGuiUnits = 22f;

    /// <summary>Minecraft GUI scale at 1080p for HUD proportion matching.</summary>
    public const float GuiScaleAt1080p = 2f;

    /// <summary>Survival icon step in GUI texture pixels (9px at GUI scale 1).</summary>
    public const float SurvivalIconStepGuiUnits = 9f;

    /// <summary>Survival icon texture size in GUI pixels (9px at GUI scale 1).</summary>
    public const float SurvivalIconTextureGuiUnits = 9f;

    /// <summary>Hearts/hunger icons per row above hotbar.</summary>
    public const int SurvivalIconsPerRow = 10;

    public const int InventorySlotCount = GameConstants.HotbarSize + GameConstants.InventorySize;

    private readonly int[] _inventorySlots = new int[InventorySlotCount];

    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float Hunger { get; set; }
    public float BreakProgress { get; set; }
    public int SelectedHotbarIndex { get; set; }
    public float Fps { get; set; }
    public int ChunkCount { get; set; }
    public int LoadedChunkCount { get; set; }
    public int PendingChunkCount { get; set; }
    public uint VertexCount { get; set; }
    public bool IsPaused { get; set; }
    public bool IsPauseSettingsOpen { get; set; }
    public int PauseMenuSelectedIndex { get; set; }
    public float FovSetting { get; set; } = 0.5f;
    public float MouseSensitivitySetting { get; set; } = 0.5f;
    public bool InvertMouseY { get; set; }
    public bool IsInventoryOpen { get; set; }
    public bool IsJeiOpen { get; set; }
    public int JeiSelectedIndex { get; set; }
    public int JeiRecipeCount { get; set; }
    public string JeiStatusText { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool IsDead { get; set; }
    public bool IsMainMenuActive { get; set; }
    public int MainMenuSelectedIndex { get; set; }
    public bool IsLanBrowserActive { get; set; }
    public int LanServerCount { get; set; }
    public string LanStatusText { get; set; } = string.Empty;
    public int RespawnTicksRemaining { get; set; }
    public string StatusText { get; set; } = "Connecting...";
    public float InventoryFullHintTimer { get; set; }
    public float PickupFlashTimer { get; set; }

    public void TriggerInventoryFullHint() => InventoryFullHintTimer = 2.5f;

    public void TriggerPickupFlash() => PickupFlashTimer = 0.4f;

    public void TickHints(float deltaSeconds)
    {
        if (InventoryFullHintTimer > 0f)
        {
            InventoryFullHintTimer = Math.Max(0f, InventoryFullHintTimer - deltaSeconds);
        }

        if (PickupFlashTimer > 0f)
        {
            PickupFlashTimer = Math.Max(0f, PickupFlashTimer - deltaSeconds);
        }
    }

    public void UpdateFromPlayer(PlayerState player)
    {
        Health = player.Survival.Health;
        Oxygen = player.Survival.Oxygen;
        Hunger = player.Survival.Hunger;
        IsDead = player.Survival.IsDead;
        RespawnTicksRemaining = player.Survival.RespawnTicksRemaining;
        SelectedHotbarIndex = player.Inventory.SelectedHotbarIndex;
        BreakProgress = player.BreakProgress;
    }

    public float BuildHudFlags()
    {
        float flags = 0f;
        if (IsPaused)
        {
            flags += 1f;
        }

        if (IsInventoryOpen)
        {
            flags += 2f;
        }

        if (Oxygen < GameConstants.MaxOxygen - 0.5f)
        {
            flags += 4f;
        }

        if (Oxygen < GameConstants.OxygenLowThreshold)
        {
            flags += 32f;
        }

        if (IsDead)
        {
            flags += 8f;
        }

        if (IsMainMenuActive)
        {
            flags += 16f;
        }

        if (IsPauseSettingsOpen)
        {
            flags += 64f;
        }

        if (InvertMouseY)
        {
            flags += 128f;
        }

        if (InventoryFullHintTimer > 0f)
        {
            flags += 256f;
        }

        if (PickupFlashTimer > 0f)
        {
            flags += 512f;
        }

        if (IsJeiOpen)
        {
            flags += 1024f;
        }

        return flags;
    }

    public float BuildOverlayProgress()
    {
        if (!IsDead)
        {
            return BreakProgress;
        }

        return RespawnTicksRemaining / (float)GameConstants.TickRate;
    }

    /// <summary>Packs atlas texture index (low 16 bits) and stack count (high 16 bits) per slot.</summary>
    public static int PackSlotForHud(InventorySlot slot)
    {
        if (slot.Count <= 0 || slot.BlockId == BlockId.Air || slot.ItemId != ItemId.None)
        {
            return 0;
        }

        int textureIndex = s_blockRegistry.Get(slot.BlockId).TextureSide;
        return (slot.Count << 16) | textureIndex;
    }

    public ReadOnlySpan<int> PackInventorySlots(PlayerInventory inventory)
    {
        for (int i = 0; i < GameConstants.HotbarSize; i++)
        {
            _inventorySlots[i] = PackSlotForHud(inventory.Hotbar[i]);
        }

        for (int i = 0; i < GameConstants.InventorySize; i++)
        {
            _inventorySlots[GameConstants.HotbarSize + i] = PackSlotForHud(inventory.Storage[i]);
        }

        return _inventorySlots;
    }

    public string BuildTitle()
    {
        if (IsMainMenuActive)
        {
            return "AstroCraft | Main Menu";
        }

        if (IsPaused)
        {
            return IsPauseSettingsOpen
                ? "AstroCraft | Settings"
                : "AstroCraft | Game Menu";
        }

        if (IsInventoryOpen)
        {
            return "AstroCraft | INVENTORY (E or Esc to close)";
        }

        if (IsJeiOpen)
        {
            return $"AstroCraft | RECIPES {JeiSelectedIndex + 1}/{Math.Max(1, JeiRecipeCount)} | {JeiStatusText} | Up/Down J/Esc close";
        }

        string breakText = BreakProgress > 0f ? $" | Break {BreakProgress * 100f:0}%" : string.Empty;
        string oxygenText = Oxygen < GameConstants.MaxOxygen - 0.5f ? $" O2 {Oxygen:0}" : string.Empty;
        string pendingText = PendingChunkCount > 0 ? $" (+{PendingChunkCount})" : string.Empty;
        return $"AstroCraft | HP {Health:0}{oxygenText} Food {Hunger:0}{breakText} | Slot {SelectedHotbarIndex + 1} | {Fps:0} FPS | Chunks {ChunkCount}/{LoadedChunkCount}{pendingText} Verts {VertexCount} | {StatusText}";
    }
}
