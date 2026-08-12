using AstroCraft.Core;
using AstroCraft.Core.Blocks;
using AstroCraft.Core.Simulation;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Numerics;

namespace AstroCraft.Client.Input;

public sealed class ClientInputState
{
    private const float LookSensitivity = 0.0022f;
    private readonly BlockRegistry _blockRegistry = BlockRegistry.CreateDefault();
    private readonly HashSet<Key> _keysDown = new();
    private IInputContext? _input;
    private IMouse? _mouse;
    private IKeyboard? _keyboard;
    private double _lastMouseX;
    private double _lastMouseY;
    private bool _cursorCaptured;
    private float _lookDeltaX;
    private float _lookDeltaY;
    private bool _holdBreak;
    private bool _placeRequested;
    private bool _useItemRequested;
    private bool _rightClickRequested;
    private bool _jumpHeld;
    private int _hotbarSelection;
    private int _scrollDelta;
    private float _criticMoveForward;
    private bool _rotateRequested;
    private IWindow? _window;

    public const float MinFieldOfViewDegrees = 30f;
    public const float MaxFieldOfViewDegrees = 110f;
    public const float DefaultFieldOfViewDegrees = 70f;

    public bool IsPaused { get; private set; }
    public bool IsInventoryOpen { get; private set; }
    public bool IsJeiOpen { get; private set; }
    public bool IsMainMenuActive { get; set; }
    public bool IsDead { get; set; }
    public bool InvertMouseY { get; set; }
    public float MouseSensitivity { get; set; } = 1f;
    public float FieldOfViewDegrees { get; set; } = DefaultFieldOfViewDegrees;
    public float FovSettingNormalized =>
        (FieldOfViewDegrees - MinFieldOfViewDegrees) / (MaxFieldOfViewDegrees - MinFieldOfViewDegrees);
    public event Action<Key>? MainMenuKeyDown;
    public event Action<double>? MainMenuMouseClick;
    public event Action<Key>? PauseMenuKeyDown;
    public event Action<double>? PauseMenuMouseClick;
    public event Action<Key>? JeiKeyDown;

    public void SetCriticMoveForward(float value) => _criticMoveForward = value;

    public void Attach(IWindow window)
    {
        _window = window;
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
            _mouse.MouseUp += OnMouseUp;
            _mouse.Scroll += OnMouseScroll;
            _lastMouseX = _mouse.Position.X;
            _lastMouseY = _mouse.Position.Y;
        }

        window.FocusChanged += OnWindowFocusChanged;
    }

    public void BeginFrame(IWindow window)
    {
        if (_mouse is null || _keyboard is null)
        {
            return;
        }

        if (IsPaused || IsInventoryOpen || IsJeiOpen || IsMainMenuActive)
        {
            _lookDeltaX = 0f;
            _lookDeltaY = 0f;
            if ((IsInventoryOpen || IsJeiOpen || IsMainMenuActive) && _cursorCaptured)
            {
                ReleaseCursor(window);
            }

            return;
        }

        if (!_cursorCaptured)
        {
            CaptureCursor(window);
        }

        _lookDeltaX = (float)(_lastMouseX - _mouse.Position.X) * LookSensitivity * MouseSensitivity;
        _lookDeltaY = (float)(_lastMouseY - _mouse.Position.Y) * LookSensitivity * MouseSensitivity;
        if (InvertMouseY)
        {
            _lookDeltaY = -_lookDeltaY;
        }

        RecenterMouse(window);
    }

    private void RecenterMouse(IWindow window)
    {
        if (_mouse is null)
        {
            return;
        }

        int width = window.Size.X;
        int height = window.Size.Y;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        double centerX = width * 0.5;
        double centerY = height * 0.5;
        _mouse.Position = new Vector2((float)centerX, (float)centerY);
        _lastMouseX = centerX;
        _lastMouseY = centerY;
    }

    public void OnWindowFocusChanged(bool focused, IWindow window)
    {
        if (!focused)
        {
            _keysDown.Clear();
            _holdBreak = false;
            return;
        }

        if (!IsPaused && !IsInventoryOpen && !IsJeiOpen && !IsMainMenuActive)
        {
            CaptureCursor(window);
        }
    }

    private void OnWindowFocusChanged(bool focused)
    {
        if (_window is not null)
        {
            OnWindowFocusChanged(focused, _window);
        }
    }

    public PlayerInput BuildInput(BlockId selectedHotbarBlock = BlockId.Air)
    {
        if (_keyboard is null || IsPaused || IsInventoryOpen || IsJeiOpen || IsMainMenuActive)
        {
            return new PlayerInput(0f, 0f, _lookDeltaX, _lookDeltaY, false, false, false, false, false, -1);
        }

        if (IsDead)
        {
            return new PlayerInput(0f, 0f, _lookDeltaX, _lookDeltaY, false, false, false, false, false, -1);
        }

        float forward = Math.Max(Axis(_keysDown, Key.W, Key.S), _criticMoveForward);
        float right = Axis(_keysDown, Key.D, Key.A);
        bool jump = _keysDown.Contains(Key.Space) && !_jumpHeld;
        _jumpHeld = _keysDown.Contains(Key.Space);
        bool sneak = _keysDown.Contains(Key.ShiftLeft) || _keysDown.Contains(Key.ShiftRight);
        bool sprint = _keysDown.Contains(Key.ControlLeft) || _keysDown.Contains(Key.ControlRight);
        int hotbar = ResolveHotbarSelection();
        bool placeBlock = _placeRequested;
        bool useItem = _useItemRequested;
        _placeRequested = false;
        _useItemRequested = false;

        if (_rightClickRequested)
        {
            if (_blockRegistry.IsEdible(selectedHotbarBlock))
            {
                useItem = true;
            }
            else
            {
                placeBlock = true;
            }

            _rightClickRequested = false;
        }

        PlayerInput input = new(
            forward,
            right,
            _lookDeltaX,
            _lookDeltaY,
            jump,
            sneak,
            sprint,
            _holdBreak,
            placeBlock,
            hotbar,
            UseItem: useItem,
            RotateBlock: _rotateRequested);

        _rotateRequested = false;
        return input;
    }

    public void SetPaused(bool paused, IWindow window)
    {
        IsPaused = paused;
        if (paused)
        {
            IsInventoryOpen = false;
            IsJeiOpen = false;
            ReleaseCursor(window);
            return;
        }

        if (!IsInventoryOpen && !IsJeiOpen)
        {
            CaptureCursor(window);
        }
    }

    public void SetInventoryOpen(bool open, IWindow window)
    {
        if (IsPaused)
        {
            return;
        }

        if (open)
        {
            IsJeiOpen = false;
        }

        IsInventoryOpen = open;
        if (open)
        {
            ReleaseCursor(window);
            return;
        }

        if (!IsJeiOpen)
        {
            CaptureCursor(window);
        }
    }

    public void SetJeiOpen(bool open, IWindow window)
    {
        if (IsPaused)
        {
            return;
        }

        if (open)
        {
            IsInventoryOpen = false;
        }

        IsJeiOpen = open;
        if (open)
        {
            ReleaseCursor(window);
            return;
        }

        if (!IsInventoryOpen)
        {
            CaptureCursor(window);
        }
    }

    public void Dispose()
    {
        if (_keyboard is not null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
        }

        if (_window is not null)
        {
            _window.FocusChanged -= OnWindowFocusChanged;
        }

        if (_mouse is not null)
        {
            _mouse.MouseDown -= OnMouseDown;
            _mouse.MouseUp -= OnMouseUp;
            _mouse.Scroll -= OnMouseScroll;
        }

        _input?.Dispose();
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int arg3)
    {
        if (IsMainMenuActive)
        {
            MainMenuKeyDown?.Invoke(key);
            return;
        }

        if (key == Key.Escape)
        {
            if (IsJeiOpen && _window is not null)
            {
                SetJeiOpen(false, _window);
                return;
            }

            if (IsInventoryOpen && _window is not null)
            {
                SetInventoryOpen(false, _window);
                return;
            }

            if (IsPaused)
            {
                PauseMenuKeyDown?.Invoke(key);
                return;
            }

            if (_window is not null)
            {
                SetPaused(true, _window);
            }

            return;
        }

        if (IsPaused)
        {
            PauseMenuKeyDown?.Invoke(key);
            return;
        }

        if (key == Key.J && !_keysDown.Contains(Key.J) && _window is not null)
        {
            SetJeiOpen(!IsJeiOpen, _window);
            return;
        }

        if (IsJeiOpen)
        {
            JeiKeyDown?.Invoke(key);
            return;
        }

        if (key == Key.E && !_keysDown.Contains(Key.E) && _window is not null)
        {
            SetInventoryOpen(!IsInventoryOpen, _window);
        }

        if (key == Key.R && !IsPaused && !_keysDown.Contains(Key.R))
        {
            _rotateRequested = true;
        }

        _keysDown.Add(key);
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int arg3) => _keysDown.Remove(key);

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        if (IsMainMenuActive && button == MouseButton.Left && _window is not null)
        {
            MainMenuMouseClick?.Invoke(mouse.Position.Y);
            return;
        }

        if (IsPaused && button == MouseButton.Left && _window is not null)
        {
            PauseMenuMouseClick?.Invoke(mouse.Position.Y);
            return;
        }

        if (IsPaused || IsInventoryOpen || IsJeiOpen)
        {
            return;
        }

        if (button == MouseButton.Left)
        {
            _holdBreak = true;
        }

        if (button == MouseButton.Right)
        {
            _rightClickRequested = true;
        }
    }

    private void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
    {
        if (IsPaused || IsInventoryOpen || IsJeiOpen || IsMainMenuActive)
        {
            return;
        }

        if (scroll.Y > 0)
        {
            _scrollDelta++;
        }
        else if (scroll.Y < 0)
        {
            _scrollDelta--;
        }
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            _holdBreak = false;
        }
    }

    private int ResolveHotbarSelection()
    {
        for (int i = 0; i < GameConstants.HotbarSize; i++)
        {
            Key key = Key.Number1 + i;
            if (_keysDown.Contains(key))
            {
                _hotbarSelection = i;
                return i;
            }
        }

        while (_scrollDelta > 0)
        {
            _hotbarSelection = (_hotbarSelection + 1) % GameConstants.HotbarSize;
            _scrollDelta--;
        }

        while (_scrollDelta < 0)
        {
            _hotbarSelection = (_hotbarSelection + GameConstants.HotbarSize - 1) % GameConstants.HotbarSize;
            _scrollDelta++;
        }

        return _hotbarSelection;
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
