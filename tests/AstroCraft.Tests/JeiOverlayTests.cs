using AstroCraft.Client.Game;
using AstroCraft.Client.Input;
using AstroCraft.Client.UI;
using Silk.NET.Input;

namespace AstroCraft.Tests;

public class JeiOverlayTests
{
    [Fact]
    public void JeiOverlayState_LoadsAllEmbeddedRecipes()
    {
        JeiOverlayState jei = new();
        Assert.True(jei.RecipeCount >= 15);
    }

    [Fact]
    public void JeiOverlayState_ScrollsRecipes()
    {
        JeiOverlayState jei = new();
        jei.SetOpen(true);
        int start = jei.SelectedIndex;

        jei.HandleKeyDown(Key.Down);
        Assert.NotEqual(start, jei.SelectedIndex);

        jei.HandleKeyDown(Key.Up);
        Assert.Equal(start, jei.SelectedIndex);
    }

    [Fact]
    public void JeiOverlayState_BuildStatusLine_ShowsIngredientsAndResult()
    {
        JeiOverlayState jei = new();
        jei.SetOpen(true);

        while (jei.SelectedRecipe?.Id != "planks_from_wood")
        {
            jei.HandleKeyDown(Key.Down);
        }

        string status = jei.BuildStatusLine();
        Assert.Contains("planks_from_wood", status);
        Assert.Contains("Wood", status);
        Assert.Contains("Planks", status);
    }

    [Fact]
    public void GameHud_JeiOpen_SetsFlagBit1024()
    {
        GameHud hud = new();
        hud.IsJeiOpen = true;
        Assert.True(((int)hud.BuildHudFlags() & 1024) != 0);
    }

    [Fact]
    public void JeiOpenClose_DoesNotSoftLockHudState()
    {
        GameHud hud = new();
        hud.IsJeiOpen = true;
        hud.IsInventoryOpen = false;
        Assert.True(hud.IsJeiOpen);

        hud.IsJeiOpen = false;
        Assert.False(hud.IsJeiOpen);
        Assert.False(hud.IsPaused);
    }
}
