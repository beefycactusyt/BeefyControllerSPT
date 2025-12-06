using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using SharpDX.XInput;
using UnityEngine;
using EFT.InputSystem;

namespace BeefyController
{
    [BepInPlugin("com.beefy.spt.controller", "Beefy InGame Controller", "2.3.0")]
    public class BeefyControllerPlugin : BaseUnityPlugin
    {
        private Harmony _harmony;
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony("com.beefy.spt.controller.ingame");
            _harmony.PatchAll();
            Log.LogInfo("[BeefyController] Controller plugin loaded (Amanda DEFAULT combat layout).");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            Log?.LogInfo("[BeefyController] Controller plugin unloaded.");
        }
    }

    /// <summary>
    /// Placeholder for future UI / looting handling.
    /// </summary>
    internal static class UiLootingController
    {
        // TODO: implement cursor / inventory navigation here later.
    }

    [HarmonyPatch(typeof(InputManager), "method_3")]
    internal static class InputManager_ControllerPatch
    {
        private static Controller _controller = new Controller(UserIndex.One);
        private static bool _connected = _controller.IsConnected;
        private static State _prevState;

        private const short DEADZONE = 8000;
        private const float MAX_MAG = 32767f;

        // trigger state
        private static bool _rtHeld;
        private static bool _ltHeld;

        // sprint toggle state
        private static bool _sprinting;

        // tap / hold / double-tap timing
        private const float HOLD_TIME = 0.35f;
        private const float DOUBLE_TIME = 0.30f;

        // B (crouch / prone)
        private static float _bDownTime;
        private static bool _bHoldSent;

        // X (reload / check ammo / quick reload)
        private static float _xDownTime;
        private static float _xLastTapTime;
        private static bool _xHoldSent;
        private static bool _xDoubleReady;

        // Y (quick swap / examine)
        private static float _yDownTime;
        private static bool _yHoldSent;

        // R3 (quick melee / select melee)
        private static float _r3DownTime;
        private static bool _r3HoldSent;

        // D-Pad Up (switch tac mode / toggle tac device)
        private static float _upDownTime;
        private static bool _upHoldSent;

        // D-Pad Right (fire mode / check fire mode / force auto)
        private static float _rightDownTime;
        private static float _rightLastTapTime;
        private static bool _rightHoldSent;
        private static bool _rightDoubleReady;

        // D-Pad Down (scope magnification / switch sights)
        private static float _downDownTime;
        private static bool _downHoldSent;

        static void Prefix(List<ECommand> commandsList, float[] axesList)
        {
            // Reconnect if needed
            if (!_connected || !_controller.IsConnected)
            {
                _controller = new Controller(UserIndex.One);
                _connected = _controller.IsConnected;

                if (!_connected)
                    return;

                BeefyControllerPlugin.Log?.LogInfo("[BeefyController] XInput controller connected.");
            }

            State state = _controller.GetState();
            Gamepad gp = state.Gamepad;

            // -------- AXES --------
            HandleMovement(gp, axesList);
            HandleLook(gp, axesList);

            // -------- BUTTONS / COMMANDS --------
            var cur = gp.Buttons;
            var prev = _prevState.Gamepad.Buttons;

            bool JustPressed(GamepadButtonFlags b) => cur.HasFlag(b) && !prev.HasFlag(b);
            bool JustReleased(GamepadButtonFlags b) => !cur.HasFlag(b) && prev.HasFlag(b);

            float now = Time.time;

            // convenience flags so combos don't double-trigger
            bool usedA = false, usedB = false, usedX = false, usedY = false;
            bool usedUp = false, usedRight = false, usedDown = false;

            // ===== FACE BUTTONS: Amanda DEFAULT =====

            // --- A: Jump (simple tap) ---
            if (JustPressed(GamepadButtonFlags.A))
            {
                commandsList.Add(ECommand.Jump);
                usedA = true;
            }

            // --- B: tap = crouch, hold = prone ---
            bool bNow = cur.HasFlag(GamepadButtonFlags.B);
            bool bPrev = prev.HasFlag(GamepadButtonFlags.B);

            if (bNow && !bPrev)
            {
                _bDownTime = now;
                _bHoldSent = false;
            }
            if (bNow && !_bHoldSent && now - _bDownTime >= HOLD_TIME)
            {
                commandsList.Add(ECommand.ToggleProne);
                _bHoldSent = true;
            }
            if (!bNow && bPrev)
            {
                if (!_bHoldSent)
                    commandsList.Add(ECommand.ToggleDuck); // crouch toggle
                usedB = true;
            }

            // --- X: tap = reload, hold = check ammo, double = quick reload ---
            bool xNow = cur.HasFlag(GamepadButtonFlags.X);
            bool xPrev = prev.HasFlag(GamepadButtonFlags.X);

            if (xNow && !xPrev)
            {
                _xDownTime = now;
                _xHoldSent = false;

                // check for double-tap window
                _xDoubleReady = (now - _xLastTapTime <= DOUBLE_TIME);
            }
            if (xNow && !_xHoldSent && now - _xDownTime >= HOLD_TIME)
            {
                // hold -> check ammo
                commandsList.Add(ECommand.CheckAmmo);
                _xHoldSent = true;
            }
            if (!xNow && xPrev)
            {
                if (_xDoubleReady && !_xHoldSent)
                {
                    // double tap -> quick reload
                    commandsList.Add(ECommand.QuickReloadWeapon);
                    _xDoubleReady = false;
                }
                else if (!_xHoldSent)
                {
                    // single tap -> normal reload
                    commandsList.Add(ECommand.ReloadWeapon);
                    _xLastTapTime = now;
                }
                usedX = true;
            }

            // --- Y: tap = quick swap weapon, hold = examine weapon ---
            bool yNow = cur.HasFlag(GamepadButtonFlags.Y);
            bool yPrev = prev.HasFlag(GamepadButtonFlags.Y);

            if (yNow && !yPrev)
            {
                _yDownTime = now;
                _yHoldSent = false;
            }
            if (yNow && !_yHoldSent && now - _yDownTime >= HOLD_TIME)
            {
                // hold -> examine weapon
                commandsList.Add(ECommand.ExamineWeapon);
                _yHoldSent = true;
            }
            if (!yNow && yPrev)
            {
                if (!_yHoldSent)
                {
                    // tap -> quick weapon swap
                    commandsList.Add(ECommand.QuickSelectSecondaryWeapon);
                }
                usedY = true;
            }

            // ===== D-PAD: Tactical / Fire Mode / Optics =====

            bool upNow = cur.HasFlag(GamepadButtonFlags.DPadUp);
            bool upPrev = prev.HasFlag(GamepadButtonFlags.DPadUp);
            bool rightNow = cur.HasFlag(GamepadButtonFlags.DPadRight);
            bool rightPrev = prev.HasFlag(GamepadButtonFlags.DPadRight);
            bool downNow = cur.HasFlag(GamepadButtonFlags.DPadDown);
            bool downPrev = prev.HasFlag(GamepadButtonFlags.DPadDown);
            bool leftNow = cur.HasFlag(GamepadButtonFlags.DPadLeft);
            bool leftPrev = prev.HasFlag(GamepadButtonFlags.DPadLeft);

            // --- Up: tap = switch tactical device mode, hold = toggle tactical device ---
            if (upNow && !upPrev)
            {
                _upDownTime = now;
                _upHoldSent = false;
            }
            if (upNow && !_upHoldSent && now - _upDownTime >= HOLD_TIME)
            {
                commandsList.Add(ECommand.ToggleTacticalDevice);
                _upHoldSent = true;
            }
            if (!upNow && upPrev)
            {
                if (!_upHoldSent)
                    commandsList.Add(ECommand.NextTacticalDevice);
                usedUp = true;
            }

            // --- Right: tap = change fire mode, hold = check fire mode, double = force auto ---
            if (rightNow && !rightPrev)
            {
                _rightDownTime = now;
                _rightHoldSent = false;
                _rightDoubleReady = (now - _rightLastTapTime <= DOUBLE_TIME);
            }
            if (rightNow && !_rightHoldSent && now - _rightDownTime >= HOLD_TIME)
            {
                commandsList.Add(ECommand.CheckFireMode);
                _rightHoldSent = true;
            }
            if (!rightNow && rightPrev)
            {
                if (_rightDoubleReady && !_rightHoldSent)
                {
                    commandsList.Add(ECommand.ForceAutoWeaponMode);
                    _rightDoubleReady = false;
                }
                else if (!_rightHoldSent)
                {
                    commandsList.Add(ECommand.ChangeWeaponMode);
                    _rightLastTapTime = now;
                }
                usedRight = true;
            }

            // --- Down: tap = change scope magnification, hold = switch sights ---
            if (downNow && !downPrev)
            {
                _downDownTime = now;
                _downHoldSent = false;
            }
            if (downNow && !_downHoldSent && now - _downDownTime >= HOLD_TIME)
            {
                commandsList.Add(ECommand.ChangeScope);
                _downHoldSent = true;
            }
            if (!downNow && downPrev)
            {
                if (!_downHoldSent)
                    commandsList.Add(ECommand.ChangeScopeMagnification);
                usedDown = true;
            }

            // --- Left: extra quick sight toggle (simple) ---
            if (leftNow && !leftPrev)
            {
                commandsList.Add(ECommand.ChangeScope);
            }

            // ===== LB / RB: Lean (continuous) =====
            if (JustPressed(GamepadButtonFlags.LeftShoulder))
                commandsList.Add(ECommand.ToggleLeanLeft);
            if (JustReleased(GamepadButtonFlags.LeftShoulder))
                commandsList.Add(ECommand.EndLeanLeft);

            if (JustPressed(GamepadButtonFlags.RightShoulder))
                commandsList.Add(ECommand.ToggleLeanRight);
            if (JustReleased(GamepadButtonFlags.RightShoulder))
                commandsList.Add(ECommand.EndLeanRight);

            // ===== L3: Sprint TOGGLE (click) =====
            if (JustPressed(GamepadButtonFlags.LeftThumb))
            {
                if (!_sprinting)
                {
                    commandsList.Add(ECommand.ToggleSprinting);
                    _sprinting = true;
                }
                else
                {
                    commandsList.Add(ECommand.EndSprinting);
                    _sprinting = false;
                }
            }

            // ===== R3: tap = quick melee, hold = select melee =====
            bool r3Now = cur.HasFlag(GamepadButtonFlags.RightThumb);
            bool r3Prev = prev.HasFlag(GamepadButtonFlags.RightThumb);

            if (r3Now && !r3Prev)
            {
                _r3DownTime = now;
                _r3HoldSent = false;
            }
            if (r3Now && !_r3HoldSent && now - _r3DownTime >= HOLD_TIME)
            {
                commandsList.Add(ECommand.SelectKnife);
                _r3HoldSent = true;
            }
            if (!r3Now && r3Prev)
            {
                if (!_r3HoldSent)
                    commandsList.Add(ECommand.QuickKnifeKick);
            }

            // ===== Start / Back =====
            if (JustPressed(GamepadButtonFlags.Back))
                commandsList.Add(ECommand.ToggleInventory);

            if (JustPressed(GamepadButtonFlags.Start))
                commandsList.Add(ECommand.Escape);

            // ===== Triggers: fire / aim (hold) =====
            bool fire = gp.RightTrigger > 30;
            bool aim = gp.LeftTrigger > 30;

            if (fire && !_rtHeld)
                commandsList.Add(ECommand.ToggleShooting);
            if (!fire && _rtHeld)
                commandsList.Add(ECommand.EndShooting);
            _rtHeld = fire;

            if (aim && !_ltHeld)
                commandsList.Add(ECommand.ToggleAlternativeShooting);
            if (!aim && _ltHeld)
                commandsList.Add(ECommand.EndAlternativeShooting);
            _ltHeld = aim;

            _prevState = state;
        }

        // ---------- MOVEMENT (LS) ----------
        private static void HandleMovement(Gamepad gp, float[] axes)
        {
            if (axes == null || axes.Length < 7)
                return;

            float lx = gp.LeftThumbX;
            float ly = gp.LeftThumbY;

            Vector2 raw = new Vector2(lx, ly);
            float mag = raw.magnitude;

            if (mag < DEADZONE)
            {
                axes[(int)EAxis.MoveX] = 0f;
                axes[(int)EAxis.MoveY] = 0f;
                return;
            }

            float t = (mag - DEADZONE) / (MAX_MAG - DEADZONE);
            t = Mathf.Clamp01(t);

            Vector2 dir = raw / mag;
            Vector2 final = dir * t;

            axes[(int)EAxis.MoveX] = final.x;
            axes[(int)EAxis.MoveY] = final.y;
        }

        // ---------- LOOK (RS) ----------
        private static void HandleLook(Gamepad gp, float[] axes)
        {
            if (axes == null || axes.Length < 7)
                return;

            float rx = gp.RightThumbX;
            float ry = gp.RightThumbY;

            const float LOOK_DEADZONE = 4000f;
            const float LOOK_SCALE_X = 4f;   // horizontal sensitivity
            const float LOOK_SCALE_Y = 3f;   // vertical sensitivity (lower)

            Vector2 raw = new Vector2(rx, ry);
            float mag = raw.magnitude;

            if (mag < LOOK_DEADZONE)
            {
                axes[(int)EAxis.TurnX] = 0f;
                axes[(int)EAxis.TurnY] = 0f;
                return;
            }

            // 0..1 outside deadzone
            float t = (mag - LOOK_DEADZONE) / (MAX_MAG - LOOK_DEADZONE);
            t = Mathf.Clamp01(t);

            // acceleration curve
            float tCurve = t * t;

            Vector2 dir = raw / mag;

            // apply different scale for X vs Y
            float finalX = dir.x * tCurve * LOOK_SCALE_X;
            float finalY = dir.y * tCurve * LOOK_SCALE_Y;

            axes[(int)EAxis.TurnX] = finalX;
            axes[(int)EAxis.TurnY] = -finalY; // invert so up on stick = look up
        }

    }
}
