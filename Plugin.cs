using UnityEngine;
using HarmonyLib;
using System.Reflection;
using EFT.InputSystem;
using EFT;

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
}