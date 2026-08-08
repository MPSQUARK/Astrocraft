using AstroCraft.Core.Blocks;

namespace AstroCraft.Core.Players;

public sealed class InventorySlot
{
    public BlockId BlockId { get; set; } = BlockId.Air;
    public int Count { get; set; }
}

public sealed class PlayerInventory
{
    public InventorySlot[] Hotbar { get; } = CreateSlots(GameConstants.HotbarSize);
    public InventorySlot[] Storage { get; } = CreateSlots(GameConstants.InventorySize);
    public int SelectedHotbarIndex { get; set; }

    public InventorySlot SelectedHotbarSlot => Hotbar[SelectedHotbarIndex];

    public bool TryAddBlock(BlockId blockId, int count = 1)
    {
        if (blockId == BlockId.Air)
        {
            return false;
        }

        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.BlockId == blockId && slot.Count < 64)
            {
                slot.Count += count;
                return true;
            }
        }

        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.BlockId == BlockId.Air)
            {
                slot.BlockId = blockId;
                slot.Count = count;
                return true;
            }
        }

        return false;
    }

    public bool TryConsumeSelected(BlockId blockId)
    {
        InventorySlot slot = SelectedHotbarSlot;
        if (slot.BlockId != blockId || slot.Count <= 0)
        {
            return false;
        }

        slot.Count--;
        if (slot.Count == 0)
        {
            slot.BlockId = BlockId.Air;
        }

        return true;
    }

    private static InventorySlot[] CreateSlots(int size)
    {
        InventorySlot[] slots = new InventorySlot[size];
        for (int i = 0; i < size; i++)
        {
            slots[i] = new InventorySlot();
        }

        return slots;
    }
}
