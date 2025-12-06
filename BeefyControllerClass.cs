using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using EFT;
using EFT.InputSystem;

[BepInPlugin("com.beefy.spt.controller", "Beefy Controller", "1.0.0")]
public class BeefyControllerClass : BaseUnityPlugin
{
    private Harmony _harmony;

    private const float MoveDeadzone = 0.12f;
    private const float LookDeadzone = 0.12f;
    private const float MoveSensitivity = 1.0f;
    private const float LookBaseSensitivity = 2.5f;   // overall look speed
    private const float LookVerticalScale = 0.7f;     // vertical slower than horizontal

    private void Awake()
    {
        _harmony = new Harmony("com.beefy.spt.controller");
        _harmony.PatchAll();
        Logger.LogInfo("[BeefyController] Loaded and patched.");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    private static float ApplyDeadzone(float v, float dz)
    {
        if (Mathf.Abs(v) < dz) return 0f;
        float n = (Mathf.Abs(v) - dz) / (1f - dz);
        // cubic curve for fine aim near centre
        n = n * n * n;
        return Mathf.Sign(v) * Mathf.Clamp01(n);
    }

    // ---------- HARMONY PATCH ----------

    [HarmonyPatch(typeof(Class1728), nameof(Class1728.TranslateAxes))]
    public static class Patch_TranslateAxes
    {
        // X button timing (reload / quick-reload / check ammo)
        private const float DoubleTapWindow = 0.30f;
        private const float HoldThreshold = 0.35f;

        private static bool _xIsDown;
        private static float _xDownTime;
        private static bool _xWaitingSecondTap;
        private static float _xFirstTapTime;
        private static bool _xPendingReload;   // single tap waiting to fire

        // Y button timing (swap / examine)
        private static bool _yIsDown;
        private static float _yDownTime;

        static void Prefix(Class1728 __instance, ref float[] axes)
        {
            if (axes == null || axes.Length < 7)
                return;

            float now = Time.unscaledTime;

            // =======================
            // LEFT STICK – movement
            // =======================
            float rawLX = Input.GetAxis("Horizontal");
            float rawLY = Input.GetAxis("Vertical");

            float moveX = ApplyDeadzone(rawLX, MoveDeadzone) * MoveSensitivity;
            float moveY = ApplyDeadzone(rawLY, MoveDeadzone) * MoveSensitivity;

            axes[0] = moveX;
            axes[1] = moveY;

            // =======================
            // RIGHT STICK – look
            // =======================
            float rawRX = Input.GetAxis("Mouse X"); // your working axes from before
            float rawRY = Input.GetAxis("Mouse Y");

            float lookX = ApplyDeadzone(rawRX, LookDeadzone) * LookBaseSensitivity;
            float lookY = ApplyDeadzone(rawRY, LookDeadzone) * LookBaseSensitivity * LookVerticalScale;

            axes[2] = lookX;
            axes[3] = lookY;

            axes[4] = 0f;
            axes[5] = 0f;
            axes[6] = 0f;

            // ================
            // BUTTON LOGIC
            // ================

            try
            {
                // ----- A : jump -----
                if (Input.GetKeyDown(KeyCode.JoystickButton0))
                    __instance.TranslateCommand(ECommand.Jump);

                // ----- B : crouch / prone (hold) -----
                if (Input.GetKeyDown(KeyCode.JoystickButton1))
                    __instance.TranslateCommand(ECommand.ToggleDuck);
                if (Input.GetKey(KeyCode.JoystickButton1) &&
                    !Input.GetKey(KeyCode.JoystickButton0)) // avoid spam if mashing
                {
                    // optional: after holding B for a while go prone
                    // tweak threshold if you want this
                }

                // ----- X : reload / check ammo / quick reload -----
                bool xDown = Input.GetKeyDown(KeyCode.JoystickButton2);
                bool xUp = Input.GetKeyUp(KeyCode.JoystickButton2);

                if (xDown)
                {
                    if (!_xIsDown)
                    {
                        _xIsDown = true;
                        _xDownTime = now;

                        // second tap within window => QUICK RELOAD immediately
                        if (_xWaitingSecondTap && (now - _xFirstTapTime) <= DoubleTapWindow)
                        {
                            _xWaitingSecondTap = false;
                            _xPendingReload = false; // cancel normal reload
                            __instance.TranslateCommand(ECommand.QuickReloadWeapon);
                        }
                    }
                }

                if (xUp && _xIsDown)
                {
                    _xIsDown = false;
                    float held = now - _xDownTime;

                    if (held >= HoldThreshold)
                    {
                        // HOLD X => Check ammo
                        __instance.TranslateCommand(ECommand.CheckAmmo);
                        _xWaitingSecondTap = false;
                        _xPendingReload = false;
                    }
                    else
                    {
                        // short tap – maybe reload or part of double tap
                        _xWaitingSecondTap = true;
                        _xFirstTapTime = now;
                        _xPendingReload = true;
                    }
                }

                // timer to resolve single-tap reload if second tap never comes
                if (_xWaitingSecondTap && _xPendingReload &&
                    (now - _xFirstTapTime) > DoubleTapWindow)
                {
                    _xWaitingSecondTap = false;
                    _xPendingReload = false;
                    __instance.TranslateCommand(ECommand.ReloadWeapon);
                }

                // ----- Y : quick swap / examine (hold) -----
                bool yDown = Input.GetKeyDown(KeyCode.JoystickButton3);
                bool yUp = Input.GetKeyUp(KeyCode.JoystickButton3);

                if (yDown && !_yIsDown)
                {
                    _yIsDown = true;
                    _yDownTime = now;
                }

                if (yUp && _yIsDown)
                {
                    _yIsDown = false;
                    float held = now - _yDownTime;

                    if (held >= HoldThreshold)
                    {
                        // HOLD Y => examine weapon
                        __instance.TranslateCommand(ECommand.ExamineWeapon);
                    }
                    else
                    {
                        // TAP Y => quick swap weapon
                        __instance.TranslateCommand(ECommand.QuickSelectSecondaryWeapon);
                    }
                }

                // ----- LB : hold to lean left -----
                if (Input.GetKeyDown(KeyCode.JoystickButton4))
                    __instance.TranslateCommand(ECommand.ToggleLeanLeft);
                if (Input.GetKeyUp(KeyCode.JoystickButton4))
                    __instance.TranslateCommand(ECommand.EndLeanLeft);

                // ----- RB : hold to lean right -----
                if (Input.GetKeyDown(KeyCode.JoystickButton5))
                    __instance.TranslateCommand(ECommand.ToggleLeanRight);
                if (Input.GetKeyUp(KeyCode.JoystickButton5))
                    __instance.TranslateCommand(ECommand.EndLeanRight);

                // ----- LT : aim (hold) -----
                if (Input.GetAxis("LT") > 0.5f) // or however you read LT
                    __instance.TranslateCommand(ECommand.ToggleAlternativeShooting);
                else
                    __instance.TranslateCommand(ECommand.EndAlternativeShooting);

                // ----- RT : fire -----
                if (Input.GetAxis("RT") > 0.5f)
                    __instance.TranslateCommand(ECommand.ToggleShooting);
                else
                    __instance.TranslateCommand(ECommand.EndShooting);

                // ----- L3 : sprint toggle (click once to toggle) -----
                if (Input.GetKeyDown(KeyCode.JoystickButton8))
                    __instance.TranslateCommand(ECommand.ToggleSprinting);

                // ----- R3 : check chamber (you can change to CheckAmmo if you prefer) -----
                if (Input.GetKeyDown(KeyCode.JoystickButton9))
                    __instance.TranslateCommand(ECommand.CheckChamber);
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.LogWarning("[BeefyController] Exception in TranslateAxes patch: " + ex);
#endif
            }
        }

        // helper so we can call static from nested class
        private static float ApplyDeadzone(float v, float dz) => BeefyControllerClass.ApplyDeadzone(v, dz);
    }
}
