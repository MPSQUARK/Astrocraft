using AstroCraft.Core.Blocks;

namespace AstroCraft.Core.World;

public readonly record struct BlockChange(int X, int Y, int Z, BlockId BlockId, BlockAxis Axis = BlockAxis.Y);
