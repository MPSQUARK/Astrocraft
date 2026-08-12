using AstroCraft.Client.Game;
using AstroCraft.Client.Input;
using AstroCraft.Client.UI;

namespace AstroCraft.Tests;

public class ClientSettingsTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsSettings()
    {
        string path = ClientSettings.SettingsPath;
        string? backup = File.Exists(path) ? File.ReadAllText(path) : null;

        try
        {
            ClientSettings settings = new()
            {
                FieldOfViewDegrees = 95f,
                MouseSensitivity = 1.6f,
                InvertMouseY = true,
            };
            settings.Save();

            ClientSettings loaded = ClientSettings.Load();

            Assert.Equal(95f, loaded.FieldOfViewDegrees);
            Assert.Equal(1.6f, loaded.MouseSensitivity, 3);
            Assert.True(loaded.InvertMouseY);
        }
        finally
        {
            if (backup is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                File.WriteAllText(path, backup);
            }
        }
    }

    [Fact]
    public void ApplyTo_UpdatesInputAndHud()
    {
        ClientSettings settings = new()
        {
            FieldOfViewDegrees = 55f,
            MouseSensitivity = 0.75f,
            InvertMouseY = true,
        };
        ClientInputState input = new();
        GameHud hud = new();

        settings.ApplyTo(input, hud);

        Assert.Equal(55f, input.FieldOfViewDegrees);
        Assert.Equal(0.75f, input.MouseSensitivity);
        Assert.True(input.InvertMouseY);
        Assert.True(hud.InvertMouseY);
        Assert.Equal(settings.FovNormalized, hud.FovSetting, 3);
    }

    [Fact]
    public void AdjustFov_ClampsToSupportedRange()
    {
        ClientSettings settings = new();

        settings.AdjustFov(500f);
        Assert.Equal(ClientInputState.MaxFieldOfViewDegrees, settings.FieldOfViewDegrees);

        settings.AdjustFov(-500f);
        Assert.Equal(ClientInputState.MinFieldOfViewDegrees, settings.FieldOfViewDegrees);
    }
}
