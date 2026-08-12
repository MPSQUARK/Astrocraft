using System.Text.Json;
using System.Text.Json.Serialization;
using AstroCraft.Client.Input;
using AstroCraft.Client.UI;

namespace AstroCraft.Client.Game;

public sealed class ClientSettings
{
    public const float MinMouseSensitivity = 0.25f;
    public const float MaxMouseSensitivity = 2.5f;
    public const float DefaultMouseSensitivity = 1f;

    public float FieldOfViewDegrees { get; set; } = ClientInputState.DefaultFieldOfViewDegrees;
    public float MouseSensitivity { get; set; } = DefaultMouseSensitivity;
    public bool InvertMouseY { get; set; }

    public float FovNormalized =>
        (FieldOfViewDegrees - ClientInputState.MinFieldOfViewDegrees)
        / (ClientInputState.MaxFieldOfViewDegrees - ClientInputState.MinFieldOfViewDegrees);

    public float MouseSensitivityNormalized =>
        (MouseSensitivity - MinMouseSensitivity) / (MaxMouseSensitivity - MinMouseSensitivity);

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AstroCraft");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static ClientSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new ClientSettings();
            }

            string json = File.ReadAllText(SettingsPath);
            ClientSettings? loaded = JsonSerializer.Deserialize<ClientSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return new ClientSettings();
            }

            loaded.Clamp();
            return loaded;
        }
        catch
        {
            return new ClientSettings();
        }
    }

    public void Save()
    {
        Clamp();
        Directory.CreateDirectory(SettingsDirectory);
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public void ApplyTo(ClientInputState input, GameHud hud)
    {
        Clamp();
        input.FieldOfViewDegrees = FieldOfViewDegrees;
        input.MouseSensitivity = MouseSensitivity;
        input.InvertMouseY = InvertMouseY;
        hud.FovSetting = FovNormalized;
        hud.MouseSensitivitySetting = MouseSensitivityNormalized;
        hud.InvertMouseY = InvertMouseY;
    }

    public void AdjustFov(float deltaDegrees)
    {
        FieldOfViewDegrees = Math.Clamp(
            FieldOfViewDegrees + deltaDegrees,
            ClientInputState.MinFieldOfViewDegrees,
            ClientInputState.MaxFieldOfViewDegrees);
    }

    public void AdjustMouseSensitivity(float delta)
    {
        MouseSensitivity = Math.Clamp(MouseSensitivity + delta, MinMouseSensitivity, MaxMouseSensitivity);
    }

    private void Clamp()
    {
        FieldOfViewDegrees = Math.Clamp(
            FieldOfViewDegrees,
            ClientInputState.MinFieldOfViewDegrees,
            ClientInputState.MaxFieldOfViewDegrees);
        MouseSensitivity = Math.Clamp(MouseSensitivity, MinMouseSensitivity, MaxMouseSensitivity);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
