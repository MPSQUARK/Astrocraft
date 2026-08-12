using System.Text;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using Silk.NET.Input;

namespace AstroCraft.Client.Game;

/// <summary>Client-only read-only recipe browser stub (P53 / T135).</summary>
public sealed class JeiOverlayState
{
    private readonly RecipeRegistry _registry = RecipeRegistry.CreateDefault();
    private readonly List<RecipeDefinition> _recipes;

    public bool IsOpen { get; private set; }
    public int SelectedIndex { get; private set; }
    public int RecipeCount => _recipes.Count;

    public JeiOverlayState()
    {
        _recipes = _registry.All.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    public RecipeDefinition? SelectedRecipe =>
        _recipes.Count > 0 ? _recipes[SelectedIndex] : null;

    public void SetOpen(bool open)
    {
        IsOpen = open;
        if (open)
        {
            SelectedIndex = 0;
        }
    }

    public void HandleKeyDown(Key key)
    {
        if (!IsOpen || _recipes.Count == 0)
        {
            return;
        }

        if (key == Key.Up || key == Key.W)
        {
            SelectedIndex = (SelectedIndex - 1 + _recipes.Count) % _recipes.Count;
            return;
        }

        if (key == Key.Down || key == Key.S)
        {
            SelectedIndex = (SelectedIndex + 1) % _recipes.Count;
        }
    }

    public string BuildTitleLine()
    {
        if (!IsOpen || _recipes.Count == 0)
        {
            return "AstroCraft | RECIPES (empty)";
        }

        RecipeDefinition recipe = _recipes[SelectedIndex];
        string ingredients = FormatIngredients(recipe);
        string result = FormatStackKey(recipe.Result, recipe.ResultCount);
        return $"AstroCraft | RECIPES {SelectedIndex + 1}/{_recipes.Count} | {recipe.Id} | {ingredients} -> {result} | Up/Down J/Esc close";
    }

    public string BuildStatusLine()
    {
        RecipeDefinition? recipe = SelectedRecipe;
        if (recipe is null)
        {
            return "No recipes";
        }

        return $"{recipe.Id}: {FormatIngredients(recipe)} -> {FormatStackKey(recipe.Result, recipe.ResultCount)}";
    }

    private string FormatIngredients(RecipeDefinition recipe)
    {
        if (recipe.RequiredIngredients.Count == 0)
        {
            return "none";
        }

        StringBuilder builder = new();
        foreach (KeyValuePair<StackKey, int> entry in recipe.RequiredIngredients.OrderBy(e => FormatStackKey(e.Key, 1)))
        {
            if (builder.Length > 0)
            {
                builder.Append('+');
            }

            builder.Append(FormatStackKey(entry.Key, entry.Value));
        }

        return builder.ToString();
    }

    private static string FormatStackKey(StackKey key, int count)
    {
        if (key.IsEmpty)
        {
            return "empty";
        }

        string name = key.ItemId != ItemId.None
            ? key.ItemId.ToString()
            : BlockDisplayNames.GetDisplayName(key.BlockId);

        return count > 1 ? $"{name} x{count}" : name;
    }
}
