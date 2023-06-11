using HarmonyLib;
using Verse;

namespace forcedOrder;

public class harmonyPatch : Mod
{
    public harmonyPatch(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("yayo.forcedOrder");
        harmony.PatchAll();
    }
}