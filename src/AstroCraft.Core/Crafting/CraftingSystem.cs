using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;

namespace AstroCraft.Core.Crafting;

public static class CraftingSystem
{
    public static bool TryCraft(PlayerInventory inventory, RecipeDefinition recipe)
    {
        if (!HasIngredients(inventory, recipe))
        {
            return false;
        }

        ConsumeIngredients(inventory, recipe);
        GiveResult(inventory, recipe);
        return true;
    }

    public static bool MatchesShaped(ReadOnlySpan<StackKey> grid, RecipeDefinition recipe)
    {
        if (recipe.Kind != RecipeKind.Shaped)
        {
            return false;
        }

        if (grid.Length != 9)
        {
            return false;
        }

        for (int offsetY = 0; offsetY <= 3 - recipe.Height; offsetY++)
        {
            for (int offsetX = 0; offsetX <= 3 - recipe.Width; offsetX++)
            {
                if (PatternFitsAt(grid, recipe, offsetX, offsetY))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool MatchesShapeless(IReadOnlyList<StackKey> provided, RecipeDefinition recipe)
    {
        if (recipe.Kind != RecipeKind.Shapeless)
        {
            return false;
        }

        IReadOnlyDictionary<StackKey, int> required = recipe.RequiredIngredients;
        Dictionary<StackKey, int> available = CountIngredients(provided);
        foreach ((StackKey key, int count) in required)
        {
            if (!available.TryGetValue(key, out int have) || have < count)
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasIngredients(PlayerInventory inventory, RecipeDefinition recipe)
    {
        foreach ((StackKey key, int count) in recipe.RequiredIngredients)
        {
            if (CountInInventory(inventory, key) < count)
            {
                return false;
            }
        }

        return true;
    }

    private static void ConsumeIngredients(PlayerInventory inventory, RecipeDefinition recipe)
    {
        foreach ((StackKey key, int count) in recipe.RequiredIngredients)
        {
            RemoveFromInventory(inventory, key, count);
        }
    }

    private static void GiveResult(PlayerInventory inventory, RecipeDefinition recipe)
    {
        if (!recipe.Result.IsEmpty && recipe.Result.BlockId != BlockId.Air)
        {
            inventory.TryAddBlock(recipe.Result.BlockId, recipe.ResultCount);
            return;
        }

        if (recipe.Result.ItemId != ItemId.None)
        {
            inventory.TryAddItem(recipe.Result.ItemId, recipe.ResultCount);
        }
    }

    private static int CountInInventory(PlayerInventory inventory, StackKey key)
    {
        int total = 0;
        foreach (InventorySlot slot in inventory.Hotbar.Concat(inventory.Storage))
        {
            if (SlotMatches(slot, key))
            {
                total += slot.Count;
            }
        }

        return total;
    }

    private static void RemoveFromInventory(PlayerInventory inventory, StackKey key, int count)
    {
        int remaining = count;
        foreach (InventorySlot slot in inventory.Hotbar.Concat(inventory.Storage))
        {
            if (!SlotMatches(slot, key) || slot.Count <= 0)
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
                return;
            }
        }
    }

    private static bool SlotMatches(InventorySlot slot, StackKey key)
    {
        if (key.BlockId != BlockId.Air)
        {
            return slot.BlockId == key.BlockId && slot.ItemId == ItemId.None;
        }

        return slot.ItemId == key.ItemId && slot.BlockId == BlockId.Air;
    }

    private static bool PatternFitsAt(ReadOnlySpan<StackKey> grid, RecipeDefinition recipe, int offsetX, int offsetY)
    {
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++)
            {
                StackKey gridKey = grid[y * 3 + x];
                int patternX = x - offsetX;
                int patternY = y - offsetY;
                StackKey patternKey = StackKey.Empty;
                if (patternX >= 0 && patternX < recipe.Width && patternY >= 0 && patternY < recipe.Height)
                {
                    patternKey = recipe.Pattern[patternY * recipe.Width + patternX];
                }

                if (gridKey != patternKey)
                {
                    return false;
                }
            }
        }

        return true;
    }

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
