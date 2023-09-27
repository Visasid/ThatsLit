﻿using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using ThatsLit.Components;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.Utilities;
using System.Globalization;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace ThatsLit.Patches.Vision
{
    public class SprintingEncounterPatch : ModulePatch
    {
        private static PropertyInfo _GoalEnemyProp;
        protected override MethodBase GetTargetMethod()
        {
            _GoalEnemyProp = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            return AccessTools.Method(_GoalEnemyProp.PropertyType, nameof(GClass478.SetVisible));
        }

        public struct State
        {
            public bool triggered;
            public bool unexpected;
            public bool sprinting;
        }

        [PatchPrefix]
        public static void PatchPrefix(GClass478 __instance, bool value, ref State __state)
        {
            var aim = __instance.Owner.AimingData as GClass547;

            if (aim == null) return;
            if (!__instance.IsVisible && value && Time.time - __instance.PersonalSeenTime > 10f)
            {
                __state = new State () { triggered = true, unexpected = __instance.Owner.Memory.GoalEnemy != __instance || Time.time - __instance.TimeLastSeen > 30f * UnityEngine.Random.Range(1, 2f), sprinting = __instance.Owner.Mover.Sprinting };
                if (__instance.Person.IsYourPlayer)
                    Debug.LogFormat("SEEN by {0} at {1}", __instance.Owner.Id, Time.time);
            }   
        }
        [PatchPostfix]
        public static void PatchPostfix(GClass478 __instance, State __state)
        {
            var aim = __instance.Owner.AimingData as GClass547;

            if (aim == null) return;
            if (__state.triggered)
            {
                if (__instance.Person.IsYourPlayer)
                    Debug.LogFormat("SEEN by {0} at {1}", __instance.Owner.Id, Time.time);
                if (__state.sprinting)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(aim.AimingSettings.MAX_AIM_TIME * UnityEngine.Random.Range(0.2f, 1f) * (__state.unexpected? 1f : 0.5f));
                    if (UnityEngine.Random.Range(0f, 1f) < 0.25f  * (__state.unexpected? 1f : 0.5f)) aim.NextShotMiss();
                }
                else if (__state.unexpected)
                {
                    __instance.Owner.AimingData.SetNextAimingDelay(aim.AimingSettings.MAX_AIM_TIME * UnityEngine.Random.Range(0f, 0.25f));
                }
            }
        }
    }

    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;

        protected override MethodBase GetTargetMethod()
        {
            _enemyRel = AccessTools.Property(typeof(BotMemoryClass), "GoalEnemy");
            Type lookType = _enemyRel.PropertyType;

            return AccessTools.Method(lookType, "method_7");
        }

        private static int lastFrame;
        private static float closetLastFrame;

        [PatchPostfix]
        public static void PatchPostfix(GClass478 __instance, BifacialTransform BotTransform, BifacialTransform enemy, ref float __result)
        {
            // if (ThatsLitPlugin.DevMode.Value && ThatsLitPlugin.DevModeInvisible.Value)
            // {
            //     __result = 8888;
            //     return;
            // }
            if (__result == 8888 || !ThatsLitPlugin.EnabledMod.Value) return;
            ThatsLitMainPlayerComponent mainPlayer = Singleton<ThatsLitMainPlayerComponent>.Instance;

            var original = __result;

            if (Time.frameCount != lastFrame)
            {
                lastFrame = Time.frameCount;
                closetLastFrame = float.MaxValue;
                if (mainPlayer) mainPlayer.calcedLastFrame = 0;
            }
            if (__instance.Person.IsYourPlayer)
            {
                if (!mainPlayer) return;
                if (mainPlayer.disableVisionPatch) return;

                Vector3 DirToPlayer = enemy.position - BotTransform.position;
                var dis = DirToPlayer.magnitude;
                var disFactor = Mathf.Clamp01((dis  - 10) / 100f);
                // To scale down various sneaking bonus
                // The bigger the distance the bigger it is, capped to 110m
                disFactor = disFactor * disFactor; // A slow accelerating curve, 110m => 1, 10m => 0

                bool isGoalEnemy = __instance.Owner.Memory.GoalEnemy == __instance;
                if (isGoalEnemy && __instance.Owner.WeaponManager.ShootController.IsAiming)
                {
                    float v = __instance.Owner?.WeaponManager?.CurrentWeapon?.GetSightingRange() ?? 50;
                    if (__instance.Owner.NightVision.UsingNow) Mathf.Min(v, 50); // AIs using NVGs does not get the scope buff
                    disFactor *= 1 + 0.1f * (300 - v) / 100;
                    disFactor = Mathf.Clamp01(disFactor);
                    // 10m sight? => 1.29x... 10m -> 0, 110m -> 1.29x
                    // 50m sight => 1.25x... 10m -> 0, 110m -> 1.25x
                    // 100m sight => 1.2x... 10m -> 0, 110m -> 1.2x
                    // 300m sight => 1x... 110m -> 0.8
                    // 600m sight => 0.8x... 110m -> 0.64
                    // 1000m sight => 0.3x... 110m -> 0.24
                }

                Vector3 from = BotTransform.rotation * Vector3.forward;
                Vector3 to = enemy.position - BotTransform.position;
                var angle = Vector3.Angle(from, to);

                var poseFactor = __instance.Person.AIData.Player.PoseLevel / __instance.Person.AIData.Player.Physical.MaxPoseLevel * 0.6f + 0.4f;
                if (__instance.Person.AIData.Player.IsInPronePose) poseFactor -= 0.4f;
                poseFactor += 0.05f; // base
                float globalOverlookChance = Mathf.Clamp01(ThatsLitPlugin.GlobalRandomOverlookChance.Value) * disFactor / poseFactor;
                if (isGoalEnemy)
                {
                    if (Time.time - __instance.TimeLastSeen < 5f) globalOverlookChance = 0;
                    else globalOverlookChance *= UnityEngine.Random.Range(0.1f, 0.5f);
                }
                if (UnityEngine.Random.Range(0f, 1f) < globalOverlookChance)
                {
                    __result *= 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
                    // prone, 110m, about 8% 
                    // prone, 50m, about 1.08%
                    // prone, 10m, 0
                    // stand, 110m, about 0.8% 
                    // stand, 50m, about 0.108%
                    // prone, 10m, 0
                }

                float score, factor;

                if (mainPlayer.disabledLit)
                {
                    score = factor = 0;
                }
                else
                {
                    score = mainPlayer.MultiFrameLitScore; // -1 ~ 1
                    if (!mainPlayer.disabledLit && score < 0 && __instance.Owner.NightVision.UsingNow) // The score was not reduced (toward 0) for IR lights, process the score here
                    {
                        if (mainPlayer.scoreCalculator.irLight) score /= 2;
                        else if (mainPlayer.scoreCalculator.irLaser) score /= 2f;
                        else if (mainPlayer.scoreCalculator.irLightSub) score /= 1.3f;
                        else if (mainPlayer.scoreCalculator.irLaserSub) score /= 1.1f;
                    }

                    factor = Mathf.Pow(score, ThatsLitMainPlayerComponent.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3

                }

                bool foundCloser = false;
                if (dis < closetLastFrame)
                {
                    closetLastFrame = dis;
                    foundCloser = true;
                    if (Time.frameCount % 47 == 0)
                    {
                        mainPlayer.lastCalcFrom = original;
                        mainPlayer.lastScore = score;
                        mainPlayer.lastFactor1 = factor;
                    }
                }

                var foliageImpact = mainPlayer.foliageScore * (1f - factor);
                if (mainPlayer.foliageDir != Vector2.zero) foliageImpact *= 1 - Mathf.Clamp01(Vector2.Angle(new Vector2(-DirToPlayer.x, -DirToPlayer.z), mainPlayer.foliageDir) / 90f); // 0deg -> 1, 90+deg -> 0
                // Maybe randomly lose vision for foliages
                // Pose higher than half will reduce the change
                if (UnityEngine.Random.Range(0f, 1f) < disFactor * foliageImpact * ThatsLitPlugin.FoliageImpactScale.Value * Mathf.Clamp01(0.75f - poseFactor) / 0.75f) // Among bushes, from afar
                {
                    __result *= 10f;
                    if (Time.frameCount % 47 == 0 && foundCloser)
                    {
                        mainPlayer.lastCalcTo = __result;
                        mainPlayer.lastFactor2 = factor;
                    }
                    __result += ThatsLitPlugin.FinalOffset.Value;
                }

                if (!mainPlayer.disabledLit)
                {
                    if (factor < 0) factor *= 1 + disFactor * (mainPlayer.fog / 0.35f);

                    if (factor < 0 && __instance.Owner.NightVision.UsingNow)
                        factor *= UnityEngine.Random.Range(0.15f, 0.3f); // Negative factor is reduced to only 10% regardless distance

                    if (factor < 0) factor *= 1 + disFactor * ((1 - poseFactor) * 0.8f); // Darkness will be far more effective from afar
                    else if (factor > 0) factor /= 1 + disFactor; // Highlight will be less effective from afar

                    var cqb = 1f - Mathf.Clamp01((dis - 1f) / 5f); // 6+ -> 0, 1f -> 1
                    // Fix for blind bots who are already touching us

                    var cqbSmooth = 1 - Mathf.Clamp01((dis - 1) / 10f); // 11+ -> 0, 1 -> 1, 6 ->0.5
                    cqbSmooth *= cqbSmooth; // 6m -> 25%, 1m -> 100%

                    factor = Mathf.Clamp(factor, -0.95f, 0.95f);

                    // Absoulute offset
                    // factor: -0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -1 => 0 ~ -0.5 (1m), -0.5 ~ -1 (6m)
                    // f-1, 1m => 
                    var reducingSeconds = (Mathf.Pow(Mathf.Abs(factor), 2)) * Mathf.Sign(factor) * UnityEngine.Random.Range(0.5f - 0.5f * cqb, 1f - 0.5f*cqb);
                    reducingSeconds *= factor < 0 ? 1 : 0.1f; // Give positive factor a smaller offset because the normal values are like 0.15 or something
                    reducingSeconds *= reducingSeconds > 0 ? ThatsLitPlugin.DarknessImpactScale.Value : ThatsLitPlugin.BrightnessImpactScale.Value;
                    __result -= reducingSeconds;

                    // The scaling here allows the player to stay in the dark without being seen
                    // The reason why scaling is needed is because SeenCoef will change dramatically depends on vision angles
                    // Absolute offset alone won't work for different vision angles
                    if (factor < 0 && UnityEngine.Random.Range(-1f, 0f) > factor * Mathf.Clamp01(1 - cqbSmooth - cqb)) __result = 8888f;
                    else if (factor > 0 && UnityEngine.Random.Range(0, 1) < factor) __result *= (1f - factor * 0.5f * ThatsLitPlugin.BrightnessImpactScale.Value); // Half the reaction time regardles angle half of the time at 100% score
                    else if (factor < -0.9f) __result *= 1 - (factor * (2f - cqb - cqbSmooth) * ThatsLitPlugin.DarknessImpactScale.Value);
                    else if (factor < -0.5f) __result *= 1 - (factor * (1.5f - 0.75f * cqb - 0.75f * cqbSmooth) * ThatsLitPlugin.DarknessImpactScale.Value);
                    else if (factor < -0.2f) __result *= 1 - factor * cqb * ThatsLitPlugin.DarknessImpactScale.Value;
                    else if (factor < 0f) __result *= 1 - factor / 1.5f * ThatsLitPlugin.DarknessImpactScale.Value;
                    else if (factor > 0f) __result /= (1 + factor / 2f * ThatsLitPlugin.BrightnessImpactScale.Value); // 0.66x at 100% score
                }

                if (factor < 0)
                {
                    float sinceSeen = Time.time - __instance.PersonalSeenTime;
                    __result = Mathf.Lerp(__result, original, 1f - Mathf.Clamp01(sinceSeen / 0.1f)); // just seen (0s) => original, 0.1s => modified
                }
                // This probably will let bots stay unaffected until losing the visual

                __result += ThatsLitPlugin.FinalOffset.Value;
                if (__result < 0.001f) __result = 0.001f;

                if (Time.frameCount % 47 == 0 && foundCloser)
                {
                    mainPlayer.lastCalcTo = __result;
                    mainPlayer.lastFactor2 = factor;
                }
                mainPlayer.calced++;
                mainPlayer.calcedLastFrame++;
    
            }
        }
    }

    // Thanks to SAIN
    internal class EFTInfo
    {
        public static bool IsEnemyMainPlayer(BotOwner bot) => EFTInfo.IsPlayerMainPlayer(EFTInfo.GetPlayer(bot?.Memory?.GoalEnemy?.Person));

        public static bool IsPlayerMainPlayer(Player player) => (UnityEngine.Object)player != (UnityEngine.Object)null && EFTInfo.Compare(player, EFTInfo.MainPlayer);

        public static bool IsPlayerMainPlayer(IAIDetails player) => player != null && EFTInfo.Compare(player, EFTInfo.MainPlayer);

        public static Player GetPlayer(BotOwner bot) => EFTInfo.GetPlayer(bot?.ProfileId);

        public static Player GetPlayer(IAIDetails person) => EFTInfo.GetPlayer(person?.ProfileId);

        public static Player GetPlayer(string profileID) => EFTInfo.GameWorld?.GetAlivePlayerByProfileID(profileID);

        public static bool Compare(IAIDetails A, IAIDetails B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, IAIDetails B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(IAIDetails A, Player B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, Player B) => EFTInfo.Compare(A?.ProfileId, B?.ProfileId);

        public static bool Compare(Player A, string B) => EFTInfo.Compare(A?.ProfileId, B);

        public static bool Compare(string A, Player B) => EFTInfo.Compare(A, B);

        public static bool Compare(string A, string B) => A == B;

        public static GameWorld GameWorld => Singleton<GameWorld>.Instance;

        public static Player MainPlayer => EFTInfo.GameWorld?.MainPlayer;

        public static List<IAIDetails> AllPlayers => EFTInfo.GameWorld?.RegisteredPlayers;

        public static List<Player> AlivePlayers => EFTInfo.GameWorld?.AllAlivePlayersList;

        public static Dictionary<string, Player> AlivePlayersDictionary => EFTInfo.GameWorld?.allAlivePlayersByID;
    }
}