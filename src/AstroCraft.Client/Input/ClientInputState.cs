using AstroCraft.Core;
using AstroCraft.Core.Simulation;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace AstroCraft.Client.Input;

public sealed class ClientInputState
{
    private const float LookSensitivity = 0.0022f;
    private readonly HashSet<Key> _keysDown = new();
    private IInputContext? _input;
    private IMouse? _mouse;
    private IKeyboard? _keyboard;
    private double _lastMouseX;
    private double _lastMouseY;
    private bool _cursorCaptured;
    private float _lookDeltaX;
    private float _lookDeltaY;
    private bool _breakBlock;
    private bool _placeBlock;

    public bool IsPaused { get; private set; }

    public void Attach(IWindow window)
    {
        _input = window.CreateInput();
        _mouse = _input.Mice.FirstOrDefault();
        _keyboard = _input.Keyboards.FirstOrDefault();

        if (_keyboard is not null)
        {
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
        }

        if (_mouse is not null)
        {
            _mouse.MouseDown += OnMouseDown;
            _lastMouseX = _mouse.Position.X;
            _lastMouseY = _mouse.Position.Y;
        }
    }

    public void BeginFrame(IWindow window)
    {
        if (_mouse is null || _keyboard is null)
        {
            return;
        }

        if (IsPaused)
        {
            _lookDeltaX = 0f;
            _lookDeltaY = 0f;
            return;
        }

        if (!_cursorCaptured)
        {
            CaptureCursor(window);
        }

        _lookDeltaX = (float)(_mouse.Position.X - _lastMouseX) * LookSensitivity;
        _lookDeltaY = (float)(_lastMouseY - _mouse.Position.Y) * LookSensitivity;
        _lastMouseX = _mouse.Position.X;
        _lastMouseY = _mouse.Position.Y;
    }

    public PlayerInput BuildInput()
    {
        if (_keyboard is null || IsPaused)
        {
            return new PlayerInput(0f, 0f, 0f, 0f, false, false, false, false, false, 0);
        }

        float forward = Axis(_keysDown, Key.W, Key.S);
        float right = Axis(_keysDown, Key.D, Key.A);
        bool jump = _keysDown.Contains(Key.Space);
        bool sneak = _keysDown.Contains(Key.ShiftLeft) || _keysDown.Contains(Key.ShiftRight);
        bool sprint = _keysDown.Contains(Key.ControlLeft) || _keysDown.Contains(Key.ControlRight);
        int hotbar = ResolveHotbarSelection();

        PlayerInput input = new(
            forward,
            right,
            _lookDeltaX,
            _lookDeltaY,
            jump,
            sneak,
            sprint,
            _breakBlock,
            _placeBlock,
            hotbar);

        _lookDeltaX = 0f;
        _lookDeltaY = 0f;
        _breakBlock = false;
        _placeBlock = false;
        return input;
    }

    public void SetPaused(bool paused, IWindow window)
    {
        IsPaused = paused;
        if (paused)
        {
            ReleaseCursor(window);
            return;
        }

        CaptureCursor(window);
    }

    public void Dispose()
    {
        if (_keyboard is not null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
        }

        if (_mouse is not null)
        {
            _mouse.MouseDown -= OnMouseDown;
        }

        _input?.Dispose();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int arg3)
    {
        if (key == Key.Escape)
        {
            IsPaused = !IsPaused;
            return;
        }

        _keysDown.Add(key);
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int arg3) => _keysDown.Remove(key);

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (IsPaused)
        {
            return;
        }

        if (button == MouseButton.Left)
        {
            _breakBlock = true;
        }

        if (button == MouseButton.Right)
        {
            _placeBlock = true;
        }
    }

    private int ResolveHotbarSelection()
    {
        for (int i = 0; i < GameConstants.HotbarSize; i++)
        {
            Key key = Key.Number1 + i;
            if (_keysDown.Contains(key))
            {
                return i;
            }
        }

        return -1;
    }

    private static float Axis(HashSet<Key> keys, Key positive, Key negative)
    {
        float value = 0f;
        if (keys.Contains(positive))
        {
            value += 1f;
        }

        if (keys.Contains(negative))
        {
            value -= 1f;
        }

        return value;
    }

    private void CaptureCursor(IWindow window)
    {
        _cursorCaptured = true;
        if (_mouse is not null)
        {
            _mouse.Cursor.CursorMode = CursorMode.Disabled;
            _lastMouseX = _mouse.Position.X;
            _lastMouseY = _mouse.Position.Y;
        }
    }

    private void ReleaseCursor(IWindow window)
    {
        _cursorCaptured = false;
        if (_mouse is not null)
        {
            _mouse.Cursor.CursorMode = CursorMode.Normal;
        }
    }
}
