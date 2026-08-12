using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;

namespace AstroCraft.Core.Furnaces;

public readonly record struct FurnaceStateChange(
    int X,
    int Y,
    int Z,
    BlockId OutputBlockId,
    ItemId OutputItemId,
    int OutputCount);
