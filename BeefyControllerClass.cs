using System;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using SharpDX.XInput;
using UnityEngine;

namespace BeefySharpDXController
{
    [BepInPlugin("com.beefy.spt.sharpdxcontroller", "Beefy SharpDX Controller", "0.0.1")]
    public class BeefySharpDXPlugin : BaseUnityPlugin
    {
        private Controller _controller;
        private bool _connected;
        private State _prevState;

        // Movement key states
        private bool _wDown, _aDown, _sDown, _dDown;
        private bool _shiftDown;
        private bool _qDown, _eDown;

        // Mouse states
        private bool _lmbDown, _rmbDown;

        // Settings
        private const short Deadzone = 8000;
        private const float MoveThreshold = 0.4f;
        private const float LookSensitivity = 20f;
        private const byte TriggerThreshold = 30;

        private float _nextHeartbeat;

        private ManualLogSource LogSrc => Logger;

        private void Awake()
        {
            LogSrc.LogInfo("[BeefySharpDX] Plugin loaded.");
            _controller = new Controller(UserIndex.One);
            _connected = _controller.IsConnected;

            if (_connected)
                LogSrc.LogInfo("[BeefySharpDX] XInput controller detected!");
            else
                LogSrc.LogWarning("[BeefySharpDX] No controller detected, will retry each frame.");
        }

        private void Update()
        {
            // Simple heartbeat so you can see Update is running in the log (every 5s)
            if (Time.time >= _nextHeartbeat)
            {
                LogSrc.LogDebug("[BeefySharpDX] Update heartbeat");
                _nextHeartbeat = Time.time + 5f;
            }

            // Try reconnect if needed
            if (!_connected || !_controller.IsConnected)
            {
                _controller = new Controller(UserIndex.One);
                _connected = _controller.IsConnected;

                if (!_connected)
                    return;

                LogSrc.LogInfo("[BeefySharpDX] Controller connected.");
            }

            State state;
            try
            {
                state = _controller.GetState();
            }
            catch
            {
                _connected = false;
                LogSrc.LogWarning("[BeefySharpDX] Lost XInput controller, will retry.");
                return;
            }

            Gamepad gp = state.Gamepad;

            HandleMovement(gp);
            HandleLook(gp);
            HandleButtons(gp);

            _prevState = state;
        }

        private static float Normalize(short raw)
        {
            int v = raw;
            int dz = Deadzone;

            if (System.Math.Abs(v) < dz)
                return 0f;

            float sign = Mathf.Sign(v);
            float mag = (System.Math.Abs(v) - dz) / (32767f - (float)dz);
            mag = Mathf.Clamp01(mag);
            return mag * sign;
        }

        // ----------------- Movement: left stick -> WASD + Shift -----------------

        private void HandleMovement(Gamepad gp)
        {
            float lx = Normalize(gp.LeftThumbX);
            float ly = Normalize(gp.LeftThumbY);

            bool wantW = ly > MoveThreshold;
            bool wantS = ly < -MoveThreshold;
            bool wantD = lx > MoveThreshold;
            bool wantA = lx < -MoveThreshold;

            SetKey(ref _wDown, wantW, VK.W, "W");
            SetKey(ref _sDown, wantS, VK.S, "S");
            SetKey(ref _dDown, wantD, VK.D, "D");
            SetKey(ref _aDown, wantA, VK.A, "A");

            // L3 -> Sprint (Shift)
            bool sprint = gp.Buttons.HasFlag(GamepadButtonFlags.LeftThumb);
            SetKey(ref _shiftDown, sprint, VK.SHIFT, "Shift");
        }

        // ----------------- Look: right stick -> mouse move -----------------

        private void HandleLook(Gamepad gp)
        {
            float rx = Normalize(gp.RightThumbX);
            float ry = Normalize(gp.RightThumbY);

            int dx = (int)(rx * LookSensitivity);
            int dy = (int)(-ry * LookSensitivity); // invert Y

            if (dx != 0 || dy != 0)
                Win.SendMouse(dx, dy);
        }

        // ----------------- Buttons & triggers -----------------

        private void HandleButtons(Gamepad gp)
        {
            var cur = gp.Buttons;
            var prev = _prevState.Gamepad.Buttons;

            bool Just(GamepadButtonFlags b) =>
                cur.HasFlag(b) && !prev.HasFlag(b);

            // Face buttons
            if (Just(GamepadButtonFlags.A))
            {
                LogSrc.LogInfo("[BeefySharpDX] A -> SPACE (Jump)");
                TapKey(VK.SPACE);
            }
            if (Just(GamepadButtonFlags.B))
            {
                LogSrc.LogInfo("[BeefySharpDX] B -> C (Crouch toggle)");
                TapKey(VK.C);
            }
            if (Just(GamepadButtonFlags.X))
            {
                LogSrc.LogInfo("[BeefySharpDX] X -> R (Reload)");
                TapKey(VK.R);
            }
            if (Just(GamepadButtonFlags.Y))
            {
                LogSrc.LogInfo("[BeefySharpDX] Y -> T (Tactical toggle)");
                TapKey(VK.T);
            }

            // Lean: LB / RB -> Q / E (hold)
            bool leanLeft = cur.HasFlag(GamepadButtonFlags.LeftShoulder);
            bool leanRight = cur.HasFlag(GamepadButtonFlags.RightShoulder);
            SetKey(ref _qDown, leanLeft, VK.Q, "Q (Lean left)");
            SetKey(ref _eDown, leanRight, VK.E, "E (Lean right)");

            // Triggers: aim/fire
            bool aim = gp.LeftTrigger > TriggerThreshold;
            bool fire = gp.RightTrigger > TriggerThreshold;

            SetMouse(ref _rmbDown, aim, false, "RMB (Aim)");
            SetMouse(ref _lmbDown, fire, true, "LMB (Shoot)");

            // R3 = check ammo (Alt+T)
            if (Just(GamepadButtonFlags.RightThumb))
            {
                LogSrc.LogInfo("[BeefySharpDX] RightThumb -> Alt+T (Check ammo)");
                Win.KeyDown(VK.ALT);
                TapKey(VK.T);
                Win.KeyUp(VK.ALT);
            }
        }

        // ----------------- Helpers -----------------

        private void TapKey(ushort key)
        {
            Win.KeyDown(key);
            Win.KeyUp(key);
        }

        private void SetKey(ref bool state, bool pressed, ushort key, string label)
        {
            if (pressed && !state)
            {
                state = true;
                LogSrc.LogInfo($"[BeefySharpDX] KeyDown {label}");
                Win.KeyDown(key);
            }
            else if (!pressed && state)
            {
                state = false;
                LogSrc.LogInfo($"[BeefySharpDX] KeyUp {label}");
                Win.KeyUp(key);
            }
        }

        private void SetMouse(ref bool state, bool pressed, bool left, string label)
        {
            if (pressed && !state)
            {
                state = true;
                LogSrc.LogInfo($"[BeefySharpDX] MouseDown {label}");
                if (left) Win.MouseLeftDown();
                else Win.MouseRightDown();
            }
            else if (!pressed && state)
            {
                state = false;
                LogSrc.LogInfo($"[BeefySharpDX] MouseUp {label}");
                if (left) Win.MouseLeftUp();
                else Win.MouseRightUp();
            }
        }
    }

    // ----------------- Virtual key codes -----------------

    internal static class VK
    {
        public const ushort W = 0x57;
        public const ushort A = 0x41;
        public const ushort S = 0x53;
        public const ushort D = 0x44;

        public const ushort SPACE = 0x20;
        public const ushort SHIFT = 0x10;
        public const ushort C = 0x43;
        public const ushort R = 0x52;
        public const ushort T = 0x54;

        public const ushort Q = 0x51;
        public const ushort E = 0x45;
        public const ushort ALT = 0x12;
    }

    // ----------------- WinAPI SendInput wrapper -----------------

    internal static class Win
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort key;
            public ushort scan;
            public uint flags;
            public uint time;
            public IntPtr extraInfo;
        }

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public static void SendMouse(int dx, int dy)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        flags = MOUSEEVENTF_MOVE,
                        time = 0,
                        extraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void KeyDown(ushort key)
        {
            var input = new INPUT
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
                        extraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void KeyUp(ushort key)
        {
            var input = new INPUT
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
                        extraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void MouseLeftDown() => MouseButton(MOUSEEVENTF_LEFTDOWN);
        public static void MouseLeftUp() => MouseButton(MOUSEEVENTF_LEFTUP);
        public static void MouseRightDown() => MouseButton(MOUSEEVENTF_RIGHTDOWN);
        public static void MouseRightUp() => MouseButton(MOUSEEVENTF_RIGHTUP);

        private static void MouseButton(uint flag)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = 0,
                        dy = 0,
                        mouseData = 0,
                        flags = flag,
                        time = 0,
                        extraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
