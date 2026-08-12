using AstroCraft.Core.Blocks;

namespace AstroCraft.Core.Crafting;

public readonly struct StackKey : IEquatable<StackKey>
{
    public BlockId BlockId { get; }
    public ItemId ItemId { get; }

    public StackKey(BlockId blockId, ItemId itemId)
    {
        BlockId = blockId;
        ItemId = itemId;
    }

    public bool IsEmpty => BlockId == BlockId.Air && ItemId == ItemId.None;

    public static StackKey Block(BlockId blockId) => new(blockId, ItemId.None);

    public static StackKey Item(ItemId itemId) => new(BlockId.Air, itemId);

    public static StackKey Empty => new(BlockId.Air, ItemId.None);

    public bool Equals(StackKey other) => BlockId == other.BlockId && ItemId == other.ItemId;

    public override bool Equals(object? obj) => obj is StackKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BlockId, ItemId);

    public static bool operator ==(StackKey left, StackKey right) => left.Equals(right);

    public static bool operator !=(StackKey left, StackKey right) => !left.Equals(right);
}
