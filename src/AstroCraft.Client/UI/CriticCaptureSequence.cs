using AstroCraft.Core.Players;

namespace AstroCraft.Client.UI;

public sealed class CriticCaptureSequence
{
    public readonly record struct CameraShot(string Name, float YawOffset, float PitchOffset, double StartSeconds);

    private readonly CameraShot[] _shots;
    private readonly HashSet<string> _captured = new(StringComparer.OrdinalIgnoreCase);

    public CriticCaptureSequence(IReadOnlyList<CameraShot>? shots = null)
    {
        _shots = shots?.ToArray() ?? CreateDefaultShots();
    }

    public IReadOnlyList<CameraShot> Shots => _shots;

    public bool TryGetActiveShot(double elapsedSeconds, out CameraShot shot)
    {
        CameraShot lastEligible = default;
        bool hasEligible = false;

        for (int i = 0; i < _shots.Length; i++)
        {
            if (elapsedSeconds < _shots[i].StartSeconds)
            {
                break;
            }

            lastEligible = _shots[i];
            hasEligible = true;
            if (!_captured.Contains(_shots[i].Name))
            {
                shot = _shots[i];
                return true;
            }
        }

        if (hasEligible)
        {
            shot = lastEligible;
            return true;
        }

        shot = default;
        return false;
    }

    public bool ShouldCapture(double elapsedSeconds, double holdSeconds, out CameraShot shot)
    {
        if (!TryGetActiveShot(elapsedSeconds, out shot))
        {
            return false;
        }

        if (_captured.Contains(shot.Name))
        {
            return false;
        }

        double phaseElapsed = elapsedSeconds - shot.StartSeconds;
        if (phaseElapsed < holdSeconds)
        {
            return false;
        }

        return true;
    }

    public void MarkCaptured(string shotName) => _captured.Add(shotName);

    public void ReleaseShot(string shotName) => _captured.Remove(shotName);

    public void ResetCaptured() => _captured.Clear();

    public bool IsComplete(double elapsedSeconds, double totalSeconds)
    {
        if (elapsedSeconds < totalSeconds)
        {
            return false;
        }

        return _captured.Count >= _shots.Length;
    }

    public static CameraShot[] CreateDefaultShots() =>
    [
        new("center", 0f, PlayerState.CriticCenterPitchOffsetRadians, 2.0),
        new("look-left", -0.65f, PlayerState.CriticHorizonPitchOffsetRadians, 6.0),
        new("look-right", 0.65f, PlayerState.CriticHorizonPitchOffsetRadians, 10.0),
        new("look-up", 0f, PlayerState.CriticLookUpPitchOffsetRadians, 14.0),
        new("look-down", 0f, PlayerState.CriticLookDownPitchOffsetRadians, 18.0),
    ];
}
