namespace AstroCraft.Core.Crafting;

public enum RecipeKind
{
    Shaped,
    Shapeless,
}

public sealed class RecipeDefinition
{
    public required string Id { get; init; }
    public RecipeKind Kind { get; init; }
    public int Width { get; init; } = 3;
    public int Height { get; init; } = 3;
    public StackKey[] Pattern { get; init; } = [];
    public StackKey[] Ingredients { get; init; } = [];
    public required StackKey Result { get; init; }
    public int ResultCount { get; init; } = 1;

    public IReadOnlyDictionary<StackKey, int> RequiredIngredients =>
        Kind == RecipeKind.Shapeless ? CountIngredients(Ingredients) : CountIngredients(Pattern);

    private static Dictionary<StackKey, int> CountIngredients(IEnumerable<StackKey> keys)
    {
        Dictionary<StackKey, int> counts = new();
        foreach (StackKey key in keys)
        {
            if (key.IsEmpty)
            {
                continue;
            }

            counts.TryGetValue(key, out int existing);
            counts[key] = existing + 1;
        }

        return counts;
    }
}
