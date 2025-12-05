using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using SharpDX.XInput;
using UnityEngine;

[BepInPlugin("com.beefy.spt.sharpdxcontroller", "Beefy SharpDX Controller", "1.1.0")]
public class BeefySharpDXPlugin : BaseUnityPlugin
{
    private Controller _controller;
    private bool _connected;
    private State _prevState;

    // --- movement key states ---
    private bool _wDown, _aDown, _sDown, _dDown;
    private bool _shiftDown;      // sprint
    private bool _breathDown;     // hold breath (Alt)
    //private bool _qDown, _eDown;  // lean (if we decide to use them directly)

    // --- mouse button states ---
    private bool _lmbDown, _rmbDown;

    // --- continuous actions ---
    private bool _interactDown;

    // --- face button timing for hold/double ---
    private float _bDownTime, _xDownTime, _yDownTime;
    private float _xLastTapTime, _yLastTapTime;
    private const float HoldThreshold = 0.35f;
    private const float DoubleClickWindow = 0.30f; // from Control.ini DoubleClickTimeout

    // --- stick & trigger tuning ---
    private const short Deadzone = 8000;
    private const float MoveThreshold = 0.40f;
    private const float LookSensitivity = 20f;
    private const byte TriggerThreshold = 30;

    private ManualLogSource Log => Logger;

    private void Awake()
    {
        Log.LogInfo("[BeefySharpDX] Plugin loaded.");
        _controller = new Controller(UserIndex.One);
        _connected = _controller.IsConnected;

        if (_connected)
            Log.LogInfo("[BeefySharpDX] XInput controller detected.");
        else
            Log.LogWarning("[BeefySharpDX] No controller detected yet, will retry each frame.");
    }

    private void Update()
    {
        // Reconnect if needed
        if (!_connected || !_controller.IsConnected)
        {
            _controller = new Controller(UserIndex.One);
            _connected = _controller.IsConnected;

            if (!_connected)
                return;

            Log.LogInfo("[BeefySharpDX] Controller connected.");
        }

        State state = _controller.GetState();
        Gamepad gp = state.Gamepad;

        bool lb = gp.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
        bool rb = gp.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);
        bool lt = gp.LeftTrigger > TriggerThreshold;
        bool rt = gp.RightTrigger > TriggerThreshold;

        HandleMovement(gp, lt);
        HandleLook(gp);
        HandleTriggers(lt, rt);
        HandleFaceButtons(gp, lb, rb, lt, rt);
        HandleDPad(gp, lb, rb, lt);
        HandleSystemButtons(gp);

        _prevState = state;
    }

    // -----------------------------------------------------------
    // Helper: normalize stick axis with deadzone
    // -----------------------------------------------------------
    private static float Normalize(short v)
    {
        if (Math.Abs(v) < Deadzone) return 0f;
        float sign = Mathf.Sign(v);
        float mag = (Math.Abs(v) - Deadzone) / (32767f - Deadzone);
        return Mathf.Clamp01(mag) * sign;
    }

    // -----------------------------------------------------------
    // Movement: LS -> WASD, L3 = sprint / hold breath when aiming
    // -----------------------------------------------------------
    private void HandleMovement(Gamepad gp, bool aiming)
    {
        float lx = Normalize(gp.LeftThumbX);
        float ly = Normalize(gp.LeftThumbY);

        bool wantW = ly > MoveThreshold;
        bool wantS = ly < -MoveThreshold;
        bool wantD = lx > MoveThreshold;
        bool wantA = lx < -MoveThreshold;

        SetKey(ref _wDown, wantW, VK.W);
        SetKey(ref _sDown, wantS, VK.S);
        SetKey(ref _dDown, wantD, VK.D);
        SetKey(ref _aDown, wantA, VK.A);

        // L3 = Sprint normally, Hold breath when aiming
        bool l3 = gp.Buttons.HasFlag(GamepadButtonFlags.LeftThumb);

        bool wantSprint = l3 && !aiming;
        bool wantBreath = l3 && aiming;

        SetKey(ref _shiftDown, wantSprint, VK.SHIFT);
        SetKey(ref _breathDown, wantBreath, VK.ALT);
    }

    // -----------------------------------------------------------
    // Look: RS -> mouse move
    // -----------------------------------------------------------
    private void HandleLook(Gamepad gp)
    {
        // Raw stick in [-1, 1]
        float rx = gp.RightThumbX / 32767f;
        float ry = gp.RightThumbY / 32767f;

        // Circular deadzone
        const float dead = 0.20f; // 20% radial deadzone
        Vector2 v = new Vector2(rx, ry);
        float mag = v.magnitude;

        if (mag < dead)
            return;

        // Re-scale outside deadzone -> [0, 1]
        float t = (mag - dead) / (1f - dead);
        t = Mathf.Clamp01(t);

        // Optional response curve: square to make it smoother in the centre
        t = t * t;

        Vector2 dir = v / mag;           // unit vector
        Vector2 adjusted = dir * t;      // scaled by curve

        float lookX = adjusted.x;
        float lookY = adjusted.y;

        int dx = (int)(lookX * LookSensitivity);
        int dy = (int)(-lookY * LookSensitivity); // minus so up on stick = look up

        if (dx != 0 || dy != 0)
            Win.SendMouseMove(dx, dy);
    }


    // -----------------------------------------------------------
    // Triggers: LT = Aim (RMB), RT = Shoot (LMB)
    // -----------------------------------------------------------
    private void HandleTriggers(bool lt, bool rt)
    {
        SetMouse(ref _rmbDown, lt, false); // right mouse
        SetMouse(ref _lmbDown, rt, true);  // left mouse
    }

    // -----------------------------------------------------------
    // Face buttons: A / B / X / Y with LB/RB modes
    // -----------------------------------------------------------
    private void HandleFaceButtons(Gamepad gp, bool lb, bool rb, bool lt, bool rt)
    {
        var cur = gp.Buttons;
        var prev = _prevState.Gamepad.Buttons;

        bool Just(GamepadButtonFlags b) =>
            cur.HasFlag(b) && !prev.HasFlag(b);

        bool JustUp(GamepadButtonFlags b) =>
            !cur.HasFlag(b) && prev.HasFlag(b);

        // --- A button ---
        if (!lb && !rb)
        {
            // Default: A = Jump (Space)
            if (Just(GamepadButtonFlags.A))
                Win.KeyTap(VK.SPACE);
        }
        else if (lb && !rb)
        {
            // LB + A = Slot 4
            if (Just(GamepadButtonFlags.A))
                UseSlot(4);
        }
        else if (!lb && rb)
        {
            // RB + A = Toggle on-head equipment (NVG) (N)
            if (Just(GamepadButtonFlags.A))
                Win.KeyTap(VK.N);
        }
        else if (lb && rb)
        {
            // LB + RB + A = Slot 8
            if (Just(GamepadButtonFlags.A))
                UseSlot(8);
        }

        // --- B button ---
        if (Just(GamepadButtonFlags.B))
        {
            _bDownTime = Time.time;
        }

        if (JustUp(GamepadButtonFlags.B))
        {
            float held = Time.time - _bDownTime;

            if (!lb && !rb)
            {
                // Default B:
                //   tap   -> crouch (C)
                //   hold  -> prone (X)
                if (held >= HoldThreshold)
                    Win.KeyTap(VK.X);
                else
                    Win.KeyTap(VK.C);
            }
            else if (!lb && rb)
            {
                // RB + B = Movement set -> toggle Walk (CapsLock)
                Win.KeyTap(VK.CAPS);
            }
            else if (lb && !rb)
            {
                // LB + B = Slot 5
                UseSlot(5);
            }
            else if (lb && rb)
            {
                // LB + RB + B = Slot 9
                UseSlot(9);
            }
        }

        // --- X button ---
        if (Just(GamepadButtonFlags.X))
        {
            _xDownTime = Time.time;
        }

        if (JustUp(GamepadButtonFlags.X))
        {
            float held = Time.time - _xDownTime;
            float sinceLast = Time.time - _xLastTapTime;

            if (lb && rb)
            {
                // LB + RB + X = Interact (continuous F)
                // On release, stop; handled below
            }
            else if (!lb && !rb)
            {
                if (held >= HoldThreshold)
                {
                    // Hold X -> Check Ammo (Alt + T)
                    CheckAmmo();
                }
                else
                {
                    // Tap X -> Reload (R)
                    Reload();

                    // Double tap window additionally = Quick Reload (double R)
                    if (sinceLast < DoubleClickWindow)
                        QuickReload();

                    _xLastTapTime = Time.time;
                }
            }
            else if (lb && !rb)
            {
                // LB + X = Slot 6
                UseSlot(6);
            }
            else if (!lb && rb)
            {
                // RB + X:
                //  tap       -> Unload chamber (Ctrl + R)
                //  hold      -> Check chamber / fix malfunction (Shift + T)
                //  doubleTap -> Detach magazine (Alt + R)
                if (held >= HoldThreshold)
                {
                    CheckChamber();
                }
                else
                {
                    if (sinceLast < DoubleClickWindow)
                    {
                        UnloadMagazine();
                    }
                    else
                    {
                        ChamberUnload();
                    }
                    _xLastTapTime = Time.time;
                }
            }
        }

        // LB+RB+X continuous interact
        bool comboInteract = lb && rb && cur.HasFlag(GamepadButtonFlags.X);
        SetKey(ref _interactDown, comboInteract, VK.F);

        // --- Y button ---
        if (Just(GamepadButtonFlags.Y))
        {
            _yDownTime = Time.time;
        }

        if (JustUp(GamepadButtonFlags.Y))
        {
            float held = Time.time - _yDownTime;
            float sinceLast = Time.time - _yLastTapTime;

            if (!lb && !rb)
            {
                // Default Y:
                //  tap       -> quick weapon swap (quick secondary)
                //  hold      -> Examine weapon
                if (held >= HoldThreshold)
                {
                    ExamineWeapon();
                }
                else
                {
                    QuickSwapWeapon();
                    if (sinceLast < DoubleClickWindow)
                    {
                        // second tap can just also quick swap again – harmless
                    }
                    _yLastTapTime = Time.time;
                }
            }
            else if (lb && !rb)
            {
                // LB + Y = Slot 7
                UseSlot(7);
            }
            else if (!lb && rb)
            {
                // RB + Y:
                //  tap       -> Fold stock
                //  doubleTap -> Detach magazine (same as RB+X double)
                if (sinceLast < DoubleClickWindow)
                {
                    UnloadMagazine();
                }
                else
                {
                    FoldStock();
                }
                _yLastTapTime = Time.time;
            }
            else if (lb && rb)
            {
                // LB + RB + Y:
                //  tap       -> Check time (O)
                //  doubleTap -> Check time + exits (double O)
                if (sinceLast < DoubleClickWindow)
                {
                    WatchTimeAndExits();
                }
                else
                {
                    WatchTime();
                }
                _yLastTapTime = Time.time;
            }
        }

        // --- Right stick click: melee ---
        bool r3 = cur.HasFlag(GamepadButtonFlags.RightThumb);
        bool r3Prev = _prevState.Gamepad.Buttons.HasFlag(GamepadButtonFlags.RightThumb);

        if (r3 && !r3Prev)
        {
            // tap = Quick melee (double U), hold = select melee (U)
            float now = Time.time;
            float downTime = now; // simple: we don't distinguish hold here much, use tap vs hold
            // For simplicity: always quick melee on press, select melee on LB+RB+R3 could be added later
            QuickMelee();
        }
    }

    // -----------------------------------------------------------
    // D-Pad: tactical, scopes, modes, blindfire, slots 8–0
    // -----------------------------------------------------------
    private void HandleDPad(Gamepad gp, bool lb, bool rb, bool lt)
    {
        var cur = gp.Buttons;
        var prev = _prevState.Gamepad.Buttons;

        bool Just(GamepadButtonFlags b) =>
            cur.HasFlag(b) && !prev.HasFlag(b);

        // BASE MODE (no LB/RB)
        if (!lb && !rb)
        {
            if (Just(GamepadButtonFlags.DPadUp))
            {
                // Change scope magnification (Mouse1 + Alt)
                ChangeScopeMagnification();
            }
            if (Just(GamepadButtonFlags.DPadDown))
            {
                // Switch between sights (Mouse1 + Ctrl)
                ChangeSights();
            }
            if (Just(GamepadButtonFlags.DPadLeft))
            {
                // Switch tactical device mode (T + Ctrl)
                NextTacticalDevice();
            }
            if (Just(GamepadButtonFlags.DPadRight))
            {
                // Toggle tactical device (T)
                ToggleTactical();
            }
        }
        // LB MODE
        else if (lb && !rb && !lt)
        {
            if (Just(GamepadButtonFlags.DPadUp))
            {
                // Drop backpack (double Z)
                DropBackpack();
            }
            if (Just(GamepadButtonFlags.DPadLeft))
            {
                // Grenade (G)
                ThrowGrenade();
            }
            if (Just(GamepadButtonFlags.DPadDown))
            {
                // Sidearm / secondary weapon (1)
                SecondaryWeapon();
            }
            if (Just(GamepadButtonFlags.DPadRight))
            {
                // Weapon on back / sling -> use primary weapon first (2)
                PrimaryWeaponFirst();
            }
        }
        // RB MODE (non-aiming; D-pad mostly unused in the layout here)
        else if (rb && !lb && !lt)
        {
            // You can map extra functions here if you like later
        }
        // AIMING + RB MODE
        else if (lt && rb)
        {
            if (Just(GamepadButtonFlags.DPadUp))
            {
                // Scope elevation up (PageUp)
                OpticCalibUp();
            }
            if (Just(GamepadButtonFlags.DPadDown))
            {
                // Scope elevation down (PageDown)
                OpticCalibDown();
            }
        }
        // LB + RB MODE (blindfire + sidestep)
        else if (lb && rb && !lt)
        {
            if (Just(GamepadButtonFlags.DPadUp))
            {
                // Overhead blind fire (W + Alt)
                BlindFireAbove();
            }
            if (Just(GamepadButtonFlags.DPadDown))
            {
                // Blind fire right (S + Alt)
                BlindFireRight();
            }
            if (Just(GamepadButtonFlags.DPadLeft))
            {
                // Sidestep left (Q + Alt)
                StepLeft();
            }
            if (Just(GamepadButtonFlags.DPadRight))
            {
                // Sidestep right (E + Alt)
                StepRight();
            }
        }
    }

    // -----------------------------------------------------------
    // Menu / system buttons
    // -----------------------------------------------------------
    private void HandleSystemButtons(Gamepad gp)
    {
        var cur = gp.Buttons;
        var prev = _prevState.Gamepad.Buttons;

        bool Just(GamepadButtonFlags b) =>
            cur.HasFlag(b) && !prev.HasFlag(b);

        // View button -> Inventory (Tab)
        if (Just(GamepadButtonFlags.Back))
        {
            Win.KeyTap(VK.TAB);
        }

        // Menu button -> Escape
        if (Just(GamepadButtonFlags.Start))
        {
            Win.KeyTap(VK.ESC);
        }
    }

    // -----------------------------------------------------------
    // Utility: key and mouse state helpers
    // -----------------------------------------------------------
    private static void SetKey(ref bool state, bool pressed, ushort key)
    {
        if (pressed && !state)
        {
            state = true;
            Win.KeyDown(key);
        }
        else if (!pressed && state)
        {
            state = false;
            Win.KeyUp(key);
        }
    }

    private static void SetMouse(ref bool state, bool pressed, bool left)
    {
        if (pressed && !state)
        {
            state = true;
            if (left) Win.MouseLeftDown();
            else Win.MouseRightDown();
        }
        else if (!pressed && state)
        {
            state = false;
            if (left) Win.MouseLeftUp();
            else Win.MouseRightUp();
        }
    }

    private static void UseSlot(int n)
    {
        switch (n)
        {
            case 4: Win.KeyTap(VK.D4); break;
            case 5: Win.KeyTap(VK.D5); break;
            case 6: Win.KeyTap(VK.D6); break;
            case 7: Win.KeyTap(VK.D7); break;
            case 8: Win.KeyTap(VK.D8); break;
            case 9: Win.KeyTap(VK.D9); break;
            case 0: Win.KeyTap(VK.D0); break;
        }
    }

    // -----------------------------------------------------------
    // Action wrappers – mapped to default EFT keys
    // -----------------------------------------------------------
    private static void Reload() => Win.KeyTap(VK.R);
    private static void QuickReload() => Win.DoubleTap(VK.R);

    private static void CheckAmmo() => Win.KeyChord(VK.ALT, VK.T);
    private static void CheckChamber() => Win.KeyChord(VK.SHIFT, VK.T);
    private static void ChamberUnload() => Win.KeyChord(VK.CTRL, VK.R);
    private static void UnloadMagazine() => Win.KeyChord(VK.ALT, VK.R);

    private static void ExamineWeapon() => Win.KeyTap(VK.L);
    private static void FoldStock() => Win.KeyChord(VK.ALT, VK.L);

    private static void QuickSwapWeapon() => Win.DoubleTap(VK.D1);
    private static void SecondaryWeapon() => Win.KeyTap(VK.D1);
    private static void PrimaryWeaponFirst() => Win.KeyTap(VK.D2);
    private static void PrimaryWeaponSecond() => Win.KeyTap(VK.D3);

    private static void ThrowGrenade() => Win.KeyTap(VK.G);

    private static void ToggleTactical() => Win.KeyTap(VK.T);
    private static void NextTacticalDevice() => Win.KeyChord(VK.CTRL, VK.T);

    private static void ChangeSights() => Win.KeyChord(VK.CTRL, VK.MOUSE_R); // Mouse1 + Ctrl
    private static void ChangeScopeMagnification() => Win.KeyChord(VK.ALT, VK.MOUSE_R); // Mouse1 + Alt

    private static void OpticCalibUp() => Win.KeyTap(VK.PGUP);
    private static void OpticCalibDown() => Win.KeyTap(VK.PGDN);

    private static void DropBackpack() => Win.DoubleTap(VK.Z);

    private static void BlindFireAbove() => Win.KeyChord(VK.ALT, VK.W);
    private static void BlindFireRight() => Win.KeyChord(VK.ALT, VK.S);
    private static void StepLeft() => Win.KeyChord(VK.ALT, VK.Q);
    private static void StepRight() => Win.KeyChord(VK.ALT, VK.E);

    private static void WatchTime() => Win.KeyTap(VK.O);
    private static void WatchTimeAndExits() => Win.DoubleTap(VK.O);

    private static void QuickMelee() => Win.DoubleTap(VK.U);
}

// ======================================================================
// VK codes and Win32 SendInput helpers
// ======================================================================
internal static class VK
{
    public const ushort W = 0x57;
    public const ushort A = 0x41;
    public const ushort S = 0x53;
    public const ushort D = 0x44;

    public const ushort Q = 0x51;
    public const ushort E = 0x45;
    public const ushort F = 0x46;     // ADDED ← INTERACT
    public const ushort X = 0x58;
    public const ushort C = 0x43;
    public const ushort Z = 0x5A;
    public const ushort V = 0x56;
    public const ushort G = 0x47;
    public const ushort U = 0x55;
    public const ushort L = 0x4C;
    public const ushort T = 0x54;
    public const ushort N = 0x4E;
    public const ushort O = 0x4F;

    public const ushort R = 0x52;

    public const ushort D1 = 0x31;
    public const ushort D2 = 0x32;
    public const ushort D3 = 0x33;
    public const ushort D4 = 0x34;
    public const ushort D5 = 0x35;
    public const ushort D6 = 0x36;
    public const ushort D7 = 0x37;
    public const ushort D8 = 0x38;
    public const ushort D9 = 0x39;
    public const ushort D0 = 0x30;

    public const ushort SPACE = 0x20;
    public const ushort TAB = 0x09;
    public const ushort ESC = 0x1B;

    public const ushort SHIFT = 0x10;
    public const ushort CTRL = 0x11;
    public const ushort ALT = 0x12;

    public const ushort PGUP = 0x21;
    public const ushort PGDN = 0x22;
    public const ushort CAPS = 0x14;

    public const ushort MOUSE_L = 0x01;
    public const ushort MOUSE_R = 0x02;
}


internal static class Win
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int data;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort key;
        public ushort scan;
        public uint flags;
        public uint time;
        public IntPtr extra;
    }

    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    // -------------------- Mouse helpers --------------------
    public static void SendMouseMove(int dx, int dy)
    {
        INPUT i = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT { dx = dx, dy = dy, flags = MOUSEEVENTF_MOVE }
            }
        };
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void MouseLeftDown() => MouseButton(MOUSEEVENTF_LEFTDOWN);
    public static void MouseLeftUp() => MouseButton(MOUSEEVENTF_LEFTUP);
    public static void MouseRightDown() => MouseButton(MOUSEEVENTF_RIGHTDOWN);
    public static void MouseRightUp() => MouseButton(MOUSEEVENTF_RIGHTUP);

    private static void MouseButton(uint flags)
    {
        INPUT i = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { flags = flags } }
        };
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    // -------------------- Keyboard helpers --------------------
    public static void KeyDown(ushort key)
    {
        INPUT i = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    key = key,
                    scan = 0,
                    flags = 0,
                    time = 0,
                    extra = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void KeyUp(ushort key)
    {
        INPUT i = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    key = key,
                    scan = 0,
                    flags = KEYEVENTF_KEYUP,
                    time = 0,
                    extra = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void KeyTap(ushort key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    public static void DoubleTap(ushort key)
    {
        KeyTap(key);
        KeyTap(key);
    }

    /// <summary>
    /// Presses all keys in sequence except the last (which is tapped), then releases them in reverse.
    /// Used for combos like Alt+T, Ctrl+R, etc.
    /// </summary>
    public static void KeyChord(ushort modifier, ushort key)
    {
        KeyDown(modifier);
        KeyTap(key);
        KeyUp(modifier);
    }

    public static void KeyChord(ushort mod1, ushort mod2, ushort key)
    {
        KeyDown(mod1);
        KeyDown(mod2);
        KeyTap(key);
        KeyUp(mod2);
        KeyUp(mod1);
    }
}
