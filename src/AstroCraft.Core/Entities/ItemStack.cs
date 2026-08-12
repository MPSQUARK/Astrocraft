using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;

namespace AstroCraft.Core.Entities;

public sealed class ItemStack
{
    public BlockId BlockId { get; set; } = BlockId.Air;
    public ItemId ItemId { get; set; } = ItemId.None;
    public int Count { get; set; }

    public bool IsEmpty => (BlockId == BlockId.Air && ItemId == ItemId.None) || Count <= 0;
}
