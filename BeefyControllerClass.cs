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

    private const float StickDeadzone = 0.12f; // deadzone for sticks
    private const float MoveSensitivity = 1.0f; // multiplier for left stick movement

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

    private static float ApplyDeadzone(float v, float deadzone)
    {
        if (Mathf.Abs(v) < deadzone) return 0f;
        return Mathf.Sign(v) * ((Mathf.Abs(v) - deadzone) / (1f - deadzone));
    }

    private static float ReadAxisSafe(params string[] names)
    {
        float val = 0f;
        foreach (var name in names)
        {
            try
            {
                val = Input.GetAxis(name);
            }
            catch { val = 0f; }

            if (Mathf.Abs(val) > 1e-4f) return val;
        }
        return val;
    }

    [HarmonyPatch(typeof(Class1728), nameof(Class1728.TranslateAxes))]
    public static class Patch_TranslateAxes
    {
        static void Prefix(Class1728 __instance, ref float[] axes)
        {
            if (axes == null || axes.Length < 7)
                return;

            // LEFT STICK = movement
            float rawMoveX = ReadAxisSafe("Horizontal", "LeftStickX", "X Axis");
            float rawMoveY = ReadAxisSafe("Vertical", "LeftStickY", "Y Axis");

            rawMoveX = ApplyDeadzone(rawMoveX, StickDeadzone) * MoveSensitivity;
            rawMoveY = ApplyDeadzone(rawMoveY, StickDeadzone) * MoveSensitivity;

            axes[0] = rawMoveX;
            axes[1] = rawMoveY;

            // Zero out other axes for now
            axes[2] = 0f;
            axes[3] = 0f;
            axes[4] = 0f;
            axes[5] = 0f;
            axes[6] = 0f;

            try
            {
                // Face buttons
                if (Input.GetKeyDown(KeyCode.JoystickButton0)) // A = Jump
                    __instance.TranslateCommand(ECommand.Jump);
                if (Input.GetKeyDown(KeyCode.JoystickButton1)) // B = crouch
                    __instance.TranslateCommand(ECommand.ToggleDuck);

                // Bumpers = lean
                if (Input.GetKeyDown(KeyCode.JoystickButton4)) // LB = lean left
                    __instance.TranslateCommand(ECommand.ToggleLeanLeft);
                if (Input.GetKeyDown(KeyCode.JoystickButton5)) // RB = lean right
                    __instance.TranslateCommand(ECommand.ToggleLeanRight);

                // Stick clicks
                if (Input.GetKeyDown(KeyCode.JoystickButton8)) // L3 = sprint
                    __instance.TranslateCommand(ECommand.ToggleSprinting);

            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.LogWarning("[BeefyController] Exception in TranslateAxes patch: " + ex);
#endif
            }
        }
    }
}
