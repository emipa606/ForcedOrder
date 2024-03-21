using HarmonyLib;
using Verse;

namespace forcedOrder;

public class harmonyPatch : Mod
{
    public harmonyPatch(ModContentPack content) : base(content)
    {
        new Harmony("yayo.forcedOrder").PatchAll();
    }
}