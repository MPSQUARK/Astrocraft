using AstroCraft.Core.Crafting;

namespace AstroCraft.Core.Furnaces;

public sealed class SmeltingRecipeDefinition
{
    public required string Id { get; init; }
    public required StackKey Input { get; init; }
    public required StackKey Output { get; init; }
    public int CookTicks { get; init; } = 200;
    public int OutputCount { get; init; } = 1;
}
