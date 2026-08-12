using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;

namespace AstroCraft.Core.Players;

public sealed class InventorySlot
{
    public BlockId BlockId { get; set; } = BlockId.Air;
    public ItemId ItemId { get; set; } = ItemId.None;
    public int Count { get; set; }
    public int Durability { get; set; }

    public bool IsEmpty => Count <= 0 || (BlockId == BlockId.Air && ItemId == ItemId.None);

    public void Clear()
    {
        BlockId = BlockId.Air;
        ItemId = ItemId.None;
        Count = 0;
        Durability = 0;
    }

    public StackKey AsStackKey()
    {
        if (ItemId != ItemId.None)
        {
            return StackKey.Item(ItemId);
        }

        return BlockId != BlockId.Air ? StackKey.Block(BlockId) : StackKey.Empty;
    }
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
            if (slot.BlockId == blockId && slot.ItemId == ItemId.None && slot.Count < 64)
            {
                slot.Count += count;
                return true;
            }
        }

        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.IsEmpty)
            {
                slot.BlockId = blockId;
                slot.ItemId = ItemId.None;
                slot.Count = count;
                return true;
            }
        }

        return false;
    }

    public bool TryAddItem(ItemId itemId, int count = 1)
    {
        if (itemId == ItemId.None)
        {
            return false;
        }

        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.ItemId == itemId && slot.BlockId == BlockId.Air && slot.Count < 64)
            {
                slot.Count += count;
                EnsureToolDurability(slot);
                return true;
            }
        }

        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.IsEmpty)
            {
                slot.BlockId = BlockId.Air;
                slot.ItemId = itemId;
                slot.Count = count;
                EnsureToolDurability(slot);
                return true;
            }
        }

        return false;
    }

    public bool TryDamageSelectedTool()
    {
        InventorySlot slot = SelectedHotbarSlot;
        if (slot.ItemId == ItemId.None || slot.BlockId != BlockId.Air || !ToolBreakSpeed.IsTool(slot.ItemId))
        {
            return false;
        }

        int maxDurability = ToolBreakSpeed.GetMaxDurability(slot.ItemId);
        if (slot.Durability <= 0)
        {
            slot.Durability = maxDurability;
        }

        slot.Durability--;
        if (slot.Durability > 0)
        {
            return true;
        }

        slot.Count--;
        if (slot.Count <= 0)
        {
            slot.Clear();
            return true;
        }

        slot.Durability = maxDurability;
        return true;
    }

    public bool TryCraft(string recipeId, RecipeRegistry registry)
    {
        if (!registry.TryGetById(recipeId, out RecipeDefinition? recipe) || recipe is null)
        {
            return false;
        }

        return CraftingSystem.TryCraft(this, recipe);
    }

    public bool TryCraftShaped(ReadOnlySpan<StackKey> grid, RecipeRegistry registry, out string? recipeId)
    {
        if (!registry.TryMatchShaped(grid, out RecipeDefinition? recipe) || recipe is null)
        {
            recipeId = null;
            return false;
        }

        if (!CraftingSystem.TryCraft(this, recipe))
        {
            recipeId = null;
            return false;
        }

        recipeId = recipe.Id;
        return true;
    }

    public bool TryConsumeSelected(BlockId blockId)
    {
        InventorySlot slot = SelectedHotbarSlot;
        if (slot.BlockId != blockId || slot.ItemId != ItemId.None || slot.Count <= 0)
        {
            return false;
        }

        slot.Count--;
        if (slot.Count == 0)
        {
            slot.Clear();
        }

        return true;
    }

    public bool TryRemoveBlock(BlockId blockId, int count = 1)
    {
        if (blockId == BlockId.Air || count <= 0)
        {
            return false;
        }

        int remaining = count;
        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.BlockId != blockId || slot.ItemId != ItemId.None || slot.Count <= 0)
            {
                continue;
            }

            int removed = System.Math.Min(slot.Count, remaining);
            slot.Count -= removed;
            remaining -= removed;
            if (slot.Count == 0)
            {
                slot.Clear();
            }

            if (remaining == 0)
            {
                return true;
            }
        }

        return remaining == 0;
    }

    public bool TryRemoveItem(ItemId itemId, int count = 1)
    {
        if (itemId == ItemId.None || count <= 0)
        {
            return false;
        }

        int remaining = count;
        foreach (InventorySlot slot in Hotbar.Concat(Storage))
        {
            if (slot.ItemId != itemId || slot.BlockId != BlockId.Air || slot.Count <= 0)
            {
                continue;
            }

            int removed = System.Math.Min(slot.Count, remaining);
            slot.Count -= removed;
            remaining -= removed;
            if (slot.Count == 0)
            {
                slot.Clear();
            }

            if (remaining == 0)
            {
                return true;
            }
        }

        return remaining == 0;
    }

    private static void EnsureToolDurability(InventorySlot slot)
    {
        if (!ToolBreakSpeed.IsTool(slot.ItemId) || slot.Durability > 0)
        {
            return;
        }

        slot.Durability = ToolBreakSpeed.GetMaxDurability(slot.ItemId);
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
