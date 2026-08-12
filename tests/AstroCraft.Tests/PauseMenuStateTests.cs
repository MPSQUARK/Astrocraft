using System.Numerics;
using AstroCraft.Client.Game;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Players;
using AstroCraft.Core.Simulation;
using AstroCraft.Core.World;
using AstroCraft.Tests.TestFixtures;
using Silk.NET.Input;

namespace AstroCraft.Tests;

public class PauseMenuStateTests : IClassFixture<FlatWorldFixture>
{
    private readonly FlatWorldFixture _flat;

    public PauseMenuStateTests(FlatWorldFixture flat) => _flat = flat;

    [Fact]
    public void Escape_OnMainMenu_Resumes()
    {
        PauseMenuState menu = new();
        ClientSettings settings = new();
        menu.OnOpened();

        menu.HandleKeyDown(Key.Escape, settings);

        Assert.Equal(PauseMenuAction.Resume, menu.PendingAction);
    }

    [Fact]
    public void Escape_OnSettings_ReturnsToMainMenu()
    {
        PauseMenuState menu = new();
        ClientSettings settings = new();
        menu.OnOpened();
        menu.Screen = PauseMenuScreen.Settings;

        menu.HandleKeyDown(Key.Escape, settings);

        Assert.Equal(PauseMenuAction.Back, menu.PendingAction);
    }

    [Fact]
    public void MainMenu_ActivatesResumeSettingsDisconnect()
    {
        PauseMenuState menu = new();
        ClientSettings settings = new();
        menu.OnOpened();

        menu.SelectedIndex = 0;
        menu.HandleKeyDown(Key.Enter, settings);
        Assert.Equal(PauseMenuAction.Resume, menu.PendingAction);

        menu.ResetPendingAction();
        menu.SelectedIndex = 1;
        menu.HandleKeyDown(Key.Enter, settings);
        Assert.Equal(PauseMenuAction.OpenSettings, menu.PendingAction);

        menu.ResetPendingAction();
        menu.SelectedIndex = 2;
        menu.HandleKeyDown(Key.Enter, settings);
        Assert.Equal(PauseMenuAction.Disconnect, menu.PendingAction);
    }

    [Fact]
    public void Settings_BackActivatesReturn()
    {
        PauseMenuState menu = new();
        ClientSettings settings = new();
        menu.OnOpened();
        menu.Screen = PauseMenuScreen.Settings;
        menu.SelectedIndex = 3;

        menu.HandleKeyDown(Key.Enter, settings);

        Assert.Equal(PauseMenuAction.Back, menu.PendingAction);
    }

    [Fact]
    public void Settings_HasFourAdjustableOptions()
    {
        PauseMenuState menu = new();
        menu.OnOpened();
        menu.Screen = PauseMenuScreen.Settings;

        Assert.Equal(4, menu.CurrentOptionCount);
    }

    [Fact]
    public void Settings_LeftRight_AdjustsFov()
    {
        ClientSettings settings = new() { FieldOfViewDegrees = 70f };
        settings.AdjustFov(5f);

        Assert.Equal(75f, settings.FieldOfViewDegrees);
    }

    [Fact]
    public void TryRotateTargetBlock_CyclesAxisOnLog()
    {
        GameWorld world = _flat.CreateWorld(2);
        world.TrySetBlock(0, 25, 0, BlockId.Wood, BlockAxis.Y);
        BlockInteractionSystem interaction = new(_flat.Registry);
        PlayerState player = new();
        player.ResetToSpawn(new Vector3(0.5f, 27f, 0.5f));
        player.PitchRadians = -MathF.PI / 2f + 0.05f;

        bool rotated = interaction.TryRotateTargetBlock(player, world);

        Assert.True(rotated);
        Assert.Equal(BlockAxis.X, world.GetBlockAxis(0, 25, 0));
    }
}
