using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            var harmony = new Harmony("com.Matathias.Empire");

            if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.Linux)
            {
                FixLinuxHarmonyCrash(harmony);
            }

            harmony.PatchAll();
            LogUtil.MessageForce("harmony patch complete");
        }

        // Fix a crash related to a harmony bug on Linux
        // This gets all patches Empire makes, gets the ones that would crash on Linux, and fixes them
        // Note from Matathias to future maintainers: I haven't the slightest idea what this function is doing; I
        //  inherited it. But it seems to work, per the report of one Linux user. So don't touch it unless something
        //  breaks, or you know what you're doing.
        static void FixLinuxHarmonyCrash(Harmony harmony)
        {
            bool WouldCrash(MethodInfo method)
            {
                if (method is null || !method.IsVirtual || method.IsAbstract || method.IsFinal)
                {
                    return false;
                }

                byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes is null || bytes.Length == 0 || (bytes.Length == 1 && bytes.First() == 0x2A))
                {
                    return true;
                }
                return false;
            }

            var methods = typeof(FactionFC).Assembly.GetTypes().Where(t0 => t0 != null && t0.IsClass && !typeof(Delegate).IsAssignableFrom(t0) && t0.GetCustomAttributes(typeof(HarmonyPatch)).Any()).SelectMany(t1 =>
            {
                Type declaringType = null;
                string methodName = null;
                foreach (HarmonyPatch attr in t1.GetCustomAttributes(typeof(HarmonyPatch), false))
                {
                    if (attr.info.declaringType != null) declaringType = attr.info.declaringType;
                    if (attr.info.methodName != null) methodName = attr.info.methodName;
                }

                MethodInfo[] m = declaringType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m is null) return new List<MethodInfo>();

                return m.Where(met => met.Name == methodName);
            }).Where(WouldCrash);

            foreach (MethodInfo i in methods)
            {
                // Patching methods without any Prefixes/Postfixes before actually patching them fixes it. Idk why
                harmony.Patch(i);
            }
            LogUtil.MessageForce($"FixLinuxHarmonyCrash complete");
        }
    }
}