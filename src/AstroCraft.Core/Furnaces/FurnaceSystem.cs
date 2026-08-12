using AstroCraft.Core.Blocks;
using AstroCraft.Core.Crafting;
using AstroCraft.Core.Players;

namespace AstroCraft.Core.Furnaces;

public static class FurnaceSystem
{
    public static bool Tick(FurnaceState state, SmeltingRecipeRegistry recipes, FurnaceFuelRegistry fuels)
    {
        bool changed = false;

        if (state.FuelTicksRemaining <= 0 && CanSmelt(state, recipes))
        {
            if (TryConsumeFuel(state, fuels))
            {
                changed = true;
            }
        }

        if (state.FuelTicksRemaining <= 0 || !CanSmelt(state, recipes))
        {
            return changed;
        }

        state.FuelTicksRemaining--;
        state.ProgressTicks++;

        if (!recipes.TryGetForInput(state.Input, out SmeltingRecipeDefinition? recipe))
        {
            return changed;
        }

        if (state.ProgressTicks < recipe.CookTicks)
        {
            return changed;
        }

        CompleteSmelt(state, recipe);
        return true;
    }

    public static bool CanSmelt(FurnaceState state, SmeltingRecipeRegistry recipes)
    {
        if (!recipes.TryGetForInput(state.Input, out SmeltingRecipeDefinition? recipe))
        {
            return false;
        }

        if (state.Input.Count <= 0)
        {
            return false;
        }

        if (state.Output.IsEmpty)
        {
            return true;
        }

        if (recipe.Output.ItemId != ItemId.None)
        {
            return state.Output.ItemId == recipe.Output.ItemId
                && state.Output.BlockId == BlockId.Air
                && state.Output.Count < 64;
        }

        return state.Output.BlockId == recipe.Output.BlockId
            && state.Output.ItemId == ItemId.None
            && state.Output.Count < 64;
    }

    private static bool TryConsumeFuel(FurnaceState state, FurnaceFuelRegistry fuels)
    {
        if (!fuels.TryGetForSlot(state.Fuel, out FurnaceFuelDefinition? fuelDef))
        {
            return false;
        }

        state.Fuel.Count--;
        if (state.Fuel.Count <= 0)
        {
            state.Fuel.Clear();
        }

        state.FuelTicksRemaining = fuelDef.BurnTicks;
        return true;
    }

    private static void CompleteSmelt(FurnaceState state, SmeltingRecipeDefinition recipe)
    {
        state.Input.Count--;
        if (state.Input.Count <= 0)
        {
            state.Input.Clear();
        }

        if (state.Output.IsEmpty)
        {
            if (recipe.Output.ItemId != ItemId.None)
            {
                state.Output.ItemId = recipe.Output.ItemId;
                state.Output.BlockId = BlockId.Air;
            }
            else
            {
                state.Output.BlockId = recipe.Output.BlockId;
                state.Output.ItemId = ItemId.None;
            }

            state.Output.Count = recipe.OutputCount;
        }
        else
        {
            state.Output.Count += recipe.OutputCount;
        }

        state.ProgressTicks = 0;
    }
}
