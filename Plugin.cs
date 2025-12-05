/*NOT CURRENTLY USED as i could not get it working with Harmony. using SharpDX and Windows INPUT API to read Xbox controller state directly.
 * 
 * using UnityEngine;
using HarmonyLib;
using System.Reflection;
using EFT.InputSystem;
using EFT;
using SharpDX.XInput;
using SharpDX;

namespace BeefyController
{
    public class XboxControllerMod
    {
        public static void Init()
        {
            var harmony = new Harmony("com.beefy.beefycontroller");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("[XboxControllerMod] Initialized");
        }
    }
}*/