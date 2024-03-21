using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace forcedOrder;

[HarmonyPatch(typeof(FloatMenuMakerMap), "AddJobGiverWorkOrders")]
internal class patch_FloatMenuMakerMap_AddJobGiverWorkOrders
{
    private static readonly FieldInfo f_equivalenceGroupTempStorage =
        AccessTools.Field(typeof(FloatMenuMakerMap), "equivalenceGroupTempStorage");

    private static readonly AccessTools.FieldRef<FloatMenuOption[]> s_equivalenceGroupTempStorage =
        AccessTools.StaticFieldRefAccess<FloatMenuOption[]>(f_equivalenceGroupTempStorage);

    [HarmonyPostfix]
    private static bool Prefix(Vector3 clickPos, Pawn pawn, List<FloatMenuOption> opts, bool drafted)
    {
        if (pawn.thinker.TryGetMainTreeThinkNode<JobGiver_Work>() == null)
        {
            return false;
        }

        var clickCell = IntVec3.FromVector3(clickPos);
        foreach (var t in GenUI.ThingsUnderMouse(clickPos, 1f, new TargetingParameters
                 {
                     canTargetPawns = true,
                     canTargetBuildings = true,
                     canTargetItems = true,
                     mapObjectTargetsMustBeAutoAttackable = false
                 }))
        {
            var hasEquivalence = false;
            foreach (var workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var index = 0; index < workTypeDef.workGiversByPriority.Count; ++index)
                {
                    var workGiver = workTypeDef.workGiversByPriority[index];
                    if (drafted && !workGiver.canBeDoneWhileDrafted ||
                        workGiver.Worker is not WorkGiver_Scanner worker ||
                        !worker.def.directOrderable)
                    {
                        continue;
                    }

                    JobFailReason.Clear();
                    if (!worker.PotentialWorkThingRequest.Accepts(t) &&
                        (worker.PotentialWorkThingsGlobal(pawn) == null ||
                         !worker.PotentialWorkThingsGlobal(pawn).Contains(t)) || worker.ShouldSkip(pawn, true))
                    {
                        continue;
                    }

                    Action action = null;
                    var pawnCapacityDef = worker.MissingRequiredCapacity(pawn);
                    string label;
                    if (pawnCapacityDef != null)
                    {
                        label = "CannotMissingHealthActivities".Translate((NamedArgument)pawnCapacityDef.label);
                    }
                    else
                    {
                        var job = worker.HasJobOnThing(pawn, t, true) ? worker.JobOnThing(pawn, t, true) : null;
                        if (job == null)
                        {
                            if (JobFailReason.HaveReason)
                            {
                                label = $"{(JobFailReason.CustomJobString.NullOrEmpty()
                                    ? (string)"CannotGenericWork".Translate((NamedArgument)worker.def.verb,
                                        (NamedArgument)t.LabelShort, (NamedArgument)t)
                                    : (string)"CannotGenericWorkCustom".Translate(
                                        (NamedArgument)JobFailReason.CustomJobString))}: {JobFailReason.Reason.CapitalizeFirst()}";
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            var workType = worker.def.workType;
                            if (pawn.WorkTagIsDisabled(worker.def.workTags))
                            {
                                label = "CannotPrioritizeWorkGiverDisabled".Translate(
                                    (NamedArgument)worker.def.label);
                            }
                            else if (pawn.jobs.curJob != null && pawn.jobs.curJob.JobIsSameAs(pawn, job))
                            {
                                label = "CannotGenericAlreadyAm".Translate(
                                    (NamedArgument)worker.PostProcessedGerund(job), (NamedArgument)t.LabelShort,
                                    (NamedArgument)t);
                            }
                            else if (pawn.workSettings.GetPriority(workType) == 0)
                            {
                                label = !pawn.WorkTypeIsDisabled(workType)
                                    ? !"CannotPrioritizeNotAssignedToWorkType".CanTranslate()
                                        ? "CannotPrioritizeWorkTypeDisabled".Translate(
                                            (NamedArgument)workType.pawnLabel)
                                        : (string)"CannotPrioritizeNotAssignedToWorkType".Translate(
                                            (NamedArgument)workType.gerundLabel)
                                    : (string)"CannotPrioritizeWorkTypeDisabled".Translate(
                                        (NamedArgument)workType.gerundLabel);
                            }
                            else if (job.def == JobDefOf.Research && t is Building_ResearchBench)
                            {
                                label = "CannotPrioritizeResearch".Translate();
                            }
                            else if (t.IsForbidden(pawn))
                            {
                                // here
                                //label = t.Position.InAllowedArea(pawn) ? (string)"CannotPrioritizeForbidden".Translate((NamedArgument)t.Label, (NamedArgument)t) : (string)("CannotPrioritizeForbiddenOutsideAllowedArea".Translate() + ": " + pawn.playerSettings.EffectiveAreaRestriction.Label);
                                label = "PrioritizeGeneric".Translate(
                                    (NamedArgument)worker.PostProcessedGerund(job), (NamedArgument)t.Label);
                                var localJob = job;
                                var localScanner = worker;
                                job.workGiverDef = worker.def;
                                action = () =>
                                {
                                    if (!pawn.jobs.TryTakeOrderedJobPrioritizedWork(localJob, localScanner,
                                            clickCell) || workGiver.forceMote == null)
                                    {
                                        return;
                                    }

                                    MoteMaker.MakeStaticMote(clickCell, pawn.Map, workGiver.forceMote);
                                };
                            }

                            else if (!pawn.CanReach((LocalTargetInfo)t, worker.PathEndMode, Danger.Deadly))
                            {
                                label = ($"{t.Label}: " + "NoPath".Translate().CapitalizeFirst())
                                    .CapitalizeFirst();
                            }
                            else
                            {
                                label = "PrioritizeGeneric".Translate(
                                    (NamedArgument)worker.PostProcessedGerund(job), (NamedArgument)t.Label);
                                var localJob = job;
                                var localScanner = worker;
                                job.workGiverDef = worker.def;
                                action = () =>
                                {
                                    if (!pawn.jobs.TryTakeOrderedJobPrioritizedWork(localJob, localScanner,
                                            clickCell) || workGiver.forceMote == null)
                                    {
                                        return;
                                    }

                                    MoteMaker.MakeStaticMote(clickCell, pawn.Map, workGiver.forceMote);
                                };
                            }
                        }
                    }

                    if (DebugViewSettings.showFloatMenuWorkGivers)
                    {
                        label += $" (from {workGiver.defName})";
                    }

                    var menuOption =
                        FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, action), pawn,
                            (LocalTargetInfo)t);
                    if (drafted && workGiver.autoTakeablePriorityDrafted != -1)
                    {
                        menuOption.autoTakeable = true;
                        menuOption.autoTakeablePriority = workGiver.autoTakeablePriorityDrafted;
                    }

                    if (opts.Any(op => op.Label == menuOption.Label))
                    {
                        continue;
                    }

                    if (workGiver.equivalenceGroup != null)
                    {
                        if (s_equivalenceGroupTempStorage.Invoke()[workGiver.equivalenceGroup.index] !=
                            null && (!s_equivalenceGroupTempStorage.Invoke()[workGiver.equivalenceGroup.index]
                                .Disabled || menuOption.Disabled))
                        {
                            continue;
                        }

                        s_equivalenceGroupTempStorage.Invoke()[workGiver.equivalenceGroup.index] =
                            menuOption;
                        hasEquivalence = true;
                    }
                    else
                    {
                        opts.Add(menuOption);
                    }
                }
            }

            if (!hasEquivalence)
            {
                continue;
            }

            for (var index = 0; index < s_equivalenceGroupTempStorage.Invoke().Length; ++index)
            {
                if (s_equivalenceGroupTempStorage.Invoke()[index] == null)
                {
                    continue;
                }

                opts.Add(s_equivalenceGroupTempStorage.Invoke()[index]);
                s_equivalenceGroupTempStorage.Invoke()[index] = null;
            }
        }

        foreach (var workTypeDef in DefDatabase<WorkTypeDef>.AllDefsListForReading)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var index = 0; index < workTypeDef.workGiversByPriority.Count; ++index)
            {
                var workGiver = workTypeDef.workGiversByPriority[index];
                if (drafted && !workGiver.canBeDoneWhileDrafted || workGiver.Worker is not WorkGiver_Scanner worker ||
                    !worker.def.directOrderable)
                {
                    continue;
                }

                JobFailReason.Clear();
                if (!worker.PotentialWorkCellsGlobal(pawn).Contains(clickCell) || worker.ShouldSkip(pawn, true))
                {
                    continue;
                }

                Action action = null;
                var pawnCapacityDef = worker.MissingRequiredCapacity(pawn);
                string label;
                if (pawnCapacityDef != null)
                {
                    label = "CannotMissingHealthActivities".Translate((NamedArgument)pawnCapacityDef.label);
                }
                else
                {
                    var job = worker.HasJobOnCell(pawn, clickCell, true)
                        ? worker.JobOnCell(pawn, clickCell, true)
                        : null;
                    if (job == null)
                    {
                        if (JobFailReason.HaveReason)
                        {
                            label = JobFailReason.CustomJobString.NullOrEmpty()
                                ? "CannotGenericWork".Translate((NamedArgument)worker.def.verb,
                                    "AreaLower".Translate())
                                : (string)"CannotGenericWorkCustom".Translate(
                                    (NamedArgument)JobFailReason.CustomJobString);
                            label = $"{label}: {JobFailReason.Reason.CapitalizeFirst()}";
                        }
                        else if (clickCell.IsForbidden(pawn))
                        {
                            // here
                            label = clickCell.InAllowedArea(pawn)
                                ? "CannotPrioritizeCellForbidden".Translate()
                                : (string)("CannotPrioritizeForbiddenOutsideAllowedArea".Translate() + ": " +
                                           pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap.Label);
                        }

                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        var workType = worker.def.workType;
                        if (pawn.jobs.curJob != null && pawn.jobs.curJob.JobIsSameAs(pawn, job))
                        {
                            label = "CannotGenericAlreadyAmCustom".Translate(
                                (NamedArgument)worker.PostProcessedGerund(job));
                        }
                        else if (pawn.workSettings.GetPriority(workType) == 0)
                        {
                            label = !pawn.WorkTypeIsDisabled(workType)
                                ? !"CannotPrioritizeNotAssignedToWorkType".CanTranslate()
                                    ? "CannotPrioritizeWorkTypeDisabled".Translate(
                                        (NamedArgument)workType.pawnLabel)
                                    : (string)"CannotPrioritizeNotAssignedToWorkType".Translate(
                                        (NamedArgument)workType.gerundLabel)
                                : (string)"CannotPrioritizeWorkTypeDisabled".Translate(
                                    (NamedArgument)workType.gerundLabel);
                        }
                        else if (clickCell.IsForbidden(pawn))
                        {
                            // here
                            label = clickCell.InAllowedArea(pawn)
                                ? "CannotPrioritizeCellForbidden".Translate()
                                : (string)("CannotPrioritizeForbiddenOutsideAllowedArea".Translate() + ": " +
                                           pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap.Label);
                        }

                        else if (!pawn.CanReach(clickCell, PathEndMode.Touch, Danger.Deadly))
                        {
                            var taggedString1 = "AreaLower".Translate();
                            var taggedString2 = taggedString1.CapitalizeFirst() + ": ";
                            taggedString1 = "NoPath".Translate();
                            var taggedString3 = taggedString1.CapitalizeFirst();
                            label = taggedString2 + taggedString3;
                        }
                        else
                        {
                            label = "PrioritizeGeneric".Translate(
                                (NamedArgument)worker.PostProcessedGerund(job), "AreaLower".Translate());
                            var localJob = job;
                            var localScanner = worker;
                            job.workGiverDef = worker.def;
                            action = () =>
                            {
                                if (!pawn.jobs.TryTakeOrderedJobPrioritizedWork(localJob, localScanner,
                                        clickCell) || workGiver.forceMote == null)
                                {
                                    return;
                                }

                                MoteMaker.MakeStaticMote(clickCell, pawn.Map, workGiver.forceMote);
                            };
                        }
                    }
                }

                var label1 = label;
                if (opts.Any(op => op.Label == label1.TrimEnd([])))
                {
                    continue;
                }

                var floatMenuOption =
                    FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, action), pawn,
                        clickCell);
                if (drafted && workGiver.autoTakeablePriorityDrafted != -1)
                {
                    floatMenuOption.autoTakeable = true;
                    floatMenuOption.autoTakeablePriority = workGiver.autoTakeablePriorityDrafted;
                }

                opts.Add(floatMenuOption);
            }
        }

        return false;
    }
}