using Silk.NET.Input;

namespace AstroCraft.Client.Game;

public enum PauseMenuScreen
{
    Main,
    Settings,
}

public enum PauseMenuAction
{
    None,
    Resume,
    OpenSettings,
    Disconnect,
    Back,
}

public sealed class PauseMenuState
{
    public const int MainOptionCount = 3;
    public const int SettingsOptionCount = 4;

    public PauseMenuScreen Screen { get; set; } = PauseMenuScreen.Main;
    public int SelectedIndex { get; set; }
    public PauseMenuAction PendingAction { get; private set; } = PauseMenuAction.None;

    public int CurrentOptionCount => Screen == PauseMenuScreen.Main ? MainOptionCount : SettingsOptionCount;

    public void ResetPendingAction() => PendingAction = PauseMenuAction.None;

    public void OnOpened()
    {
        Screen = PauseMenuScreen.Main;
        SelectedIndex = 0;
        ResetPendingAction();
    }

    public void HandleKeyDown(Key key, ClientSettings settings)
    {
        if (key == Key.Escape)
        {
            PendingAction = Screen == PauseMenuScreen.Main ? PauseMenuAction.Resume : PauseMenuAction.Back;
            return;
        }

        if (Screen == PauseMenuScreen.Settings && (key == Key.Left || key == Key.A || key == Key.Right || key == Key.D))
        {
            AdjustSelectedSetting(settings, key == Key.Right || key == Key.D);
            return;
        }

        if (key == Key.Up || key == Key.W)
        {
            SelectedIndex = (SelectedIndex - 1 + CurrentOptionCount) % CurrentOptionCount;
            return;
        }

        if (key == Key.Down || key == Key.S)
        {
            SelectedIndex = (SelectedIndex + 1) % CurrentOptionCount;
            return;
        }

        if (key == Key.Enter || key == Key.Space)
        {
            ActivateSelected(settings);
        }
    }

    public void HandleMouseClick(double screenY, double viewportHeight, ClientSettings settings)
    {
        float centerY = (float)viewportHeight * 0.5f;
        int optionCount = CurrentOptionCount;
        float firstOptionY = settingsOpenFirstOptionY(centerY);

        for (int i = 0; i < optionCount; i++)
        {
            float optionY = firstOptionY + i * 44f;
            if (Math.Abs((float)screenY - optionY) < 22f)
            {
                SelectedIndex = i;
                ActivateSelected(settings);
                return;
            }
        }
    }

    private float settingsOpenFirstOptionY(float centerY) =>
        Screen == PauseMenuScreen.Settings ? centerY - 52f : centerY - 20f;

    private void AdjustSelectedSetting(ClientSettings settings, bool increase)
    {
        switch (SelectedIndex)
        {
            case 0:
                settings.AdjustFov(increase ? 5f : -5f);
                break;
            case 1:
                settings.AdjustMouseSensitivity(increase ? 0.1f : -0.1f);
                break;
            case 2:
                settings.InvertMouseY = !settings.InvertMouseY;
                break;
        }

        settings.Save();
    }

    private void ActivateSelected(ClientSettings settings)
    {
        if (Screen == PauseMenuScreen.Main)
        {
            PendingAction = SelectedIndex switch
            {
                0 => PauseMenuAction.Resume,
                1 => PauseMenuAction.OpenSettings,
                2 => PauseMenuAction.Disconnect,
                _ => PauseMenuAction.None,
            };
            return;
        }

        if (SelectedIndex == 3)
        {
            PendingAction = PauseMenuAction.Back;
            return;
        }

        if (SelectedIndex == 2)
        {
            settings.InvertMouseY = !settings.InvertMouseY;
            settings.Save();
            return;
        }

        AdjustSelectedSetting(settings, increase: true);
    }
}
