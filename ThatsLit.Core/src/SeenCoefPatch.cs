﻿#define DEBUG_DETAILS
using Aki.Reflection.Patching;
using EFT;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;
using Comfort.Common;
using EFT.InventoryLogic;
using System.Diagnostics;
using EFT.HealthSystem;


namespace ThatsLit
{
    public class SeenCoefPatch : ModulePatch
    {
        private static PropertyInfo _enemyRel;

        protected override MethodBase GetTargetMethod()
        {
            return ReflectionHelper.FindMethodByArgTypes(typeof(EnemyInfo), new Type[] { typeof(BifacialTransform), typeof(BifacialTransform), typeof(BotDifficultySettingsClass), typeof(AIData), typeof(float), typeof(Vector3) }); ;
        }

        private static float nearestRecent;

        [PatchPostfix]
        [HarmonyAfter("me.sol.sain")]
        public static void PatchPostfix(EnemyInfo __instance, BifacialTransform BotTransform, BifacialTransform enemy, float personalLastSeenTime, Vector3 personalLastSeenPos, ref float __result)
        {
            // Don't use GoalEnemy here because it only change when engaging new enemy (it'll stay forever if not engaged with new enemy)
            // Also they could search without having visual?

            if (__result == 8888 || !ThatsLitPlugin.EnabledMod.Value || (ThatsLitPlugin.FinalImpactScaleDelaying.Value == 0 && ThatsLitPlugin.FinalImpactScaleFastening.Value == 0)) return;

            WildSpawnType spawnType = __instance.Owner?.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;
            BotImpactType botImpactType = Utility.GetBotImpactType(spawnType);
            if ((botImpactType == BotImpactType.BOSS && !ThatsLitPlugin.IncludeBosses.Value) // Is a boss, and not including bosses
             || Utility.IsExcludedSpawnType(spawnType)) // 
                return;

            ThatsLitPlayer player = null;
            if (Singleton<ThatsLitGameworld>.Instance?.AllThatsLitPlayers?.TryGetValue(__instance.Person, out player) != true
             || player == null
             || player.Player == null)
                return;

            var original = __result;

            if (player.DebugInfo != null && ThatsLitPlayer.IsDebugSampleFrame)
            {
                player.DebugInfo.calcedLastFrame = 0;
                player.DebugInfo.IsBushRatting = false;
            }

            ThatsLitPlugin.swSeenCoef.MaybeResume();

            float pSpeedFactor = Mathf.Clamp01((player.Player.Velocity.magnitude - 1f) / 4f);

            var caution = __instance.Owner.Id % 10; // 0 -> HIGH, 1 -> HIGH-MID, 2,3,4 -> MID, 5,6,7,8,9 -> LOW
            float sinceSeen = Time.time - personalLastSeenTime;
            float lastSeenPosDelta = (__instance.Person.Position - __instance.EnemyLastPositionReal).magnitude;
            float lastSeenPosDeltaSqr = lastSeenPosDelta * lastSeenPosDelta;

            float deNullification = 0;

            System.Collections.Generic.Dictionary<BodyPartType, EnemyPart> playerParts = player.Player.MainParts;
            Vector3 eyeToPlayerBody = playerParts[BodyPartType.body].Position - __instance.Owner.MainParts[BodyPartType.head].Position;
            Vector3 eyeToLastSeenPos = __instance.EnemyLastPositionReal - __instance.Owner.MainParts[BodyPartType.head].Position;

            bool isInPronePose = __instance.Person.AIData.Player.IsInPronePose;
            float pPoseFactor = Utility.GetPoseFactor(__instance.Person.AIData.Player.PoseLevel, __instance.Person.AIData.Player.Physical.MaxPoseLevel, isInPronePose);

            float rand1 = UnityEngine.Random.Range(0f, 1f);
            float rand2 = UnityEngine.Random.Range(0f, 1f);
            float rand3 = UnityEngine.Random.Range(0f, 1f);
            float rand4 = UnityEngine.Random.Range(0f, 1f);
            float rand5 = UnityEngine.Random.Range(0f, 1f);
            
            Player.FirearmController botFC = __instance.Owner?.GetPlayer?.HandsController as Player.FirearmController;
            Player.FirearmController playerFC = player.Player?.HandsController as Player.FirearmController;
            Vector3 botVisionDir = __instance.Owner.GetPlayer.LookDirection;
            var visionAngleDelta = Vector3.Angle(botVisionDir, eyeToPlayerBody);
            var visionAngleDelta90Clamped = Mathf.InverseLerp(0, 90f, visionAngleDelta);
            var visionAngleDeltaHorizontalSigned = Vector3.SignedAngle(botVisionDir, eyeToPlayerBody, Vector3.up);
            var visionAngleDeltaHorizontal = Mathf.Abs(visionAngleDeltaHorizontalSigned);
            // negative if looking down (from higher pos), 0 when looking straight...
            var visionAngleDeltaVertical = Vector3.Angle(new Vector3(eyeToPlayerBody.x, 0, eyeToPlayerBody.z), eyeToPlayerBody); 
            var visionAngleDeltaVerticalSigned = visionAngleDeltaVertical * (eyeToPlayerBody.y >= 0 ? 1f : -1f); 
            var visionAngleDeltaToLast = Vector3.Angle(eyeToLastSeenPos, eyeToPlayerBody);
            if (!__instance.HaveSeen) visionAngleDeltaToLast = 180f;

            var sinceSeenFactorSqr = Mathf.Clamp01(sinceSeen / __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN);
            var sinceSeenFactorSqrSlow = Mathf.Clamp01(sinceSeen / __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN * 2f);
            var seenPosDeltaFactorSqr = Mathf.Clamp01((float) (lastSeenPosDelta / __instance.Owner.Settings.FileSettings.Look.DIST_REPEATED_SEEN / 4f));
            seenPosDeltaFactorSqr = Mathf.Lerp(seenPosDeltaFactorSqr, 0, Mathf.InverseLerp(45f, 0f, visionAngleDeltaToLast));
            sinceSeenFactorSqr = sinceSeenFactorSqr * sinceSeenFactorSqr;
            sinceSeenFactorSqrSlow = sinceSeenFactorSqrSlow * sinceSeenFactorSqrSlow;
            seenPosDeltaFactorSqr = seenPosDeltaFactorSqr * seenPosDeltaFactorSqr;

            float notSeenRecentAndNear = Mathf.Clamp01(seenPosDeltaFactorSqr + sinceSeenFactorSqrSlow) + sinceSeenFactorSqr / 3f;

            var dis = eyeToPlayerBody.magnitude;
            float zoomedDis = dis;
            float disFactor = Mathf.InverseLerp(10f, 110f, dis);
            float disFactorSmooth = 0;
            bool inThermalView = false;
            bool inNVGView = false;
            bool gearBlocking = false; // Not blokcing for now, because AIs don't look around (nvg/thermal still ineffective when out of FOV)
            float insideTime = Mathf.Max(0, Time.time - player.lastOutside);

            float shotAngleDelta = Vector3.Angle(-eyeToPlayerBody, player.lastShotVector);
            float facingShotFactor = 0f;
            if (player.lastShotVector != Vector3.zero && Time.time - player.lastShotTime < 0.5f)
                facingShotFactor = Mathf.InverseLerp(15f, 0f, shotAngleDelta) * Mathf.InverseLerp(0.5f, 0f, Time.time - player.lastShotTime);

            ThatsLitCompat.ScopeTemplate activeScope = null;
            BotNightVisionData nightVision = __instance.Owner.NightVision;
            ThatsLitCompat.GoggleTemplate activeGoggle = null;
            if (nightVision?.UsingNow == true) 
                if (ThatsLitCompat.Goggles.TryGetValue(nightVision.NightVisionItem.Item.TemplateId, out var goggle))
                    activeGoggle = goggle?.TemplateInstance;
            if (activeGoggle != null) 
            {
                if (nightVision.NightVisionItem?.Template?.Mask == NightVisionComponent.EMask.Thermal
                 && activeGoggle.thermal != null
                 && activeGoggle.thermal.effectiveDistance > dis)
                 {
                    if (activeGoggle.thermal.verticalFOV > visionAngleDeltaVertical
                     && activeGoggle.thermal.horizontalFOV > visionAngleDeltaHorizontal)
                    {
                        inThermalView = true;
                    }
                    else
                    {
                        gearBlocking = true;
                    }
                 }
                else if (nightVision.NightVisionItem?.Template?.Mask != NightVisionComponent.EMask.Thermal
                      && activeGoggle.nightVision != null)
                {
                    if (activeGoggle.nightVision.verticalFOV > visionAngleDeltaVertical
                     && activeGoggle.nightVision.horizontalFOV > visionAngleDeltaHorizontal)
                    {
                        inNVGView = true;
                    }
                    else
                    {
                        gearBlocking = true;
                    }
                }
            }
            else if (botFC?.IsAiming ?? false) // ADS
            {
                EFT.InventoryLogic.SightComponent sightMod = __instance.Owner?.GetPlayer?.ProceduralWeaponAnimation?.CurrentAimingMod;
                if (sightMod != null)
                    if (ThatsLitCompat.Scopes.TryGetValue(sightMod.Item.TemplateId, out var scope))
                        activeScope = scope?.TemplateInstance;
                if (activeScope != null) {
                    if (rand1 < 0.1f) sightMod.SetScopeMode(UnityEngine.Random.Range(0, sightMod.ScopesCount), UnityEngine.Random.Range(0, 2));
                    float currentZoom = sightMod.GetCurrentOpticZoom();
                    if (currentZoom == 0) currentZoom = 1;

                    if (visionAngleDelta <= 60f / currentZoom) // In scope fov
                    {
                        disFactor = Mathf.InverseLerp(10, 110f, dis / currentZoom);
                        zoomedDis /= currentZoom;
                        if (activeScope?.thermal != null  && dis <= activeScope.thermal.effectiveDistance)
                        {
                            inThermalView = true;
                        }
                        else if (activeScope?.nightVision != null)
                            inNVGView = true;
                    }
                }
            }

            if (disFactor > 0)
            {
                // var disFactorLong = Mathf.Clamp01((dis - 10) / 300f);
                // To scale down various sneaking bonus
                // The bigger the distance the bigger it is, capped to 110m
                disFactorSmooth = (disFactor + disFactor * disFactor) * 0.5f; // 0.25df => 0.156dfs / 0.5df => 0.325dfs / 0.75df => 0.656dfs
                disFactor = disFactor * disFactor; // A slow accelerating curve, 110m => 1, 10m => 0, 50m => 0.16
                                                   // The disFactor is to scale up effectiveness of various mechanics by distance
                                                   // Once player is seen, it should be suppressed unless the player is out fo visual for sometime, to prevent interrupting long range fight
                float t = sinceSeen / (8f * (1.2f - disFactor)) / (0.33f + 0.67f * seenPosDeltaFactorSqr * sinceSeenFactorSqr);
                disFactor = Mathf.Lerp(0, disFactor, t); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
                                                                                                                          // disFactorLong = Mathf.Lerp(0, disFactorLong, sinceSeen / (8f * (1.2f - disFactorLong)) / (isGoalEnemy ? 0.33f : 1f)); // Takes 1.6 seconds out of visual for the disFactor to reset for AIs at 110m away, 9.6s for 10m, 8.32s for 50m, if it's targeting the player, 3x the time
                disFactorSmooth = Mathf.Lerp(0, disFactorSmooth, t);
            }

            var canSeeLight = player.LightAndLaserState.VisibleLight;
            if (!canSeeLight && inNVGView && player.LightAndLaserState.IRLight) canSeeLight = true;
            var canSeeLightSub = player.LightAndLaserState.VisibleLightSub;
            if (!canSeeLightSub && inNVGView && player.LightAndLaserState.IRLightSub) canSeeLightSub = true;
            var canSeeLaser = player.LightAndLaserState.VisibleLaser;
            if (!canSeeLaser && inNVGView && player.LightAndLaserState.IRLaser) canSeeLaser = true;
            var canSeeLaserSub = player.LightAndLaserState.VisibleLaserSub;
            if (!canSeeLaserSub && inNVGView && player.LightAndLaserState.IRLaserSub) canSeeLaserSub = true;
            if (visionAngleDelta > 110) canSeeLight = false;
            if (visionAngleDelta > 85) canSeeLaser = false;
            if (visionAngleDelta > 110) canSeeLightSub = false;
            if (visionAngleDelta > 85) canSeeLaserSub = false;

            nearestRecent += 0.6f;
            bool nearestAI = false;
            if (player.DebugInfo != null && dis <= nearestRecent)
            {
                nearestRecent = dis;
                nearestAI = true;
                player.DebugInfo.lastNearest = nearestRecent;
                if (Time.frameCount % ThatsLitPlayer.DEBUG_INTERVAL == ThatsLitPlayer.DEBUG_INTERVAL - 1)
                {
                    player.DebugInfo.lastCalcFrom = original;
                }
            }
            // ======
            // Overhead overlooking
            // ======
            // Overlook close enemies at higher attitude and in low pose
            if (!canSeeLight)
            {
                var overheadChance = Mathf.InverseLerp(15f, 90f, visionAngleDeltaVerticalSigned);
                overheadChance *= overheadChance;
                overheadChance = overheadChance / Mathf.Clamp(pPoseFactor, 0.2f, 1f);
                overheadChance *= notSeenRecentAndNear;
                overheadChance *= Mathf.Clamp01(1f - pSpeedFactor * 2f);
                overheadChance *= 1 + disFactor;

                switch (caution)
                {
                    case 0:
                        overheadChance /= 2f;
                        break;
                    case 1:
                        overheadChance /= 1.4f;
                        break;
                    case 2:
                        overheadChance /= 1.15f;
                        break;
                    case 3:
                    case 4:
                    case 5:
                        break;
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                        overheadChance *= 1.2f;
                        break;
                }

                if (rand1 < Mathf.Clamp(overheadChance, 0, 0.995f))
                {
                    __result += rand5 * 0.1f;
                    __result *= 10 + rand2 * 100;
                }
            }
            // ======
            // Inside overlooking
            // ======
            if (!canSeeLight)
            {
                bool botIsInside = __instance.Owner.AIData.IsInside;
                bool playerIsInside = player.Player.AIData.IsInside;
                if (!botIsInside && playerIsInside && insideTime >= 1)
                {
                    var insideImpact = dis * Mathf.Clamp01(visionAngleDeltaVerticalSigned / 40f) * Mathf.Clamp01(visionAngleDelta / 60f) * (0.3f * seenPosDeltaFactorSqr + 0.7f * sinceSeenFactorSqr); // 50m -> 2.5/25 (BEST ANGLE), 10m -> 0.5/5
                    __result *= 1 + insideImpact * (0.75f + rand3 * 0.05f * caution);
                }
            }

            // ======
            // Global random overlooking
            // ======
            float globalOverlookChance = 0.01f / pPoseFactor;
            globalOverlookChance *= 1 + (6f + 0.5f * caution) * Mathf.InverseLerp(10f, 110f, zoomedDis) * notSeenRecentAndNear; // linear
            if (canSeeLight) globalOverlookChance /= 2f - Mathf.InverseLerp(10f, 110f, dis);
            // 110m unseen => 200% (Prone), 22% (Crouch), 10% (Stand) !1 CHECK
            // 40m unseen => 74% (Prone), 8.14% (Crouch)
            // 20m unseen => 38% (Prone), 4.18%% (Crouch)
            // 10m unseen => 20% (Prone), 2.2% (Crouch)

            globalOverlookChance *= 0.35f + 0.65f * Mathf.InverseLerp(5f, 90f, visionAngleDelta);
            if (player.DebugInfo != null && nearestAI)
            {
                player.DebugInfo.lastGlobalOverlookChance = globalOverlookChance;
            }
            globalOverlookChance *= botImpactType != BotImpactType.DEFAULT? 0.5f : 1f;
            if (rand5 < globalOverlookChance)
            {
                __result *= 10 + rand1 * 10; // Instead of set it to flat 8888, so if the player has been in the vision for quite some time, this don't block
            }

            float score, factor;

            if (player.PlayerLitScoreProfile == null)
            {
                score = factor = 0;
            }
            else if (inThermalView)
            {
                score = factor = 0.7f;
                if (player.CheckEffectDelegate(EStimulatorBuffType.BodyTemperature))
                    score = factor = 0.7f;
            }
            else
            {
                score = player.PlayerLitScoreProfile.frame0.multiFrameLitScore; // -1 ~ 1

                if (score < 0 && inNVGView)
                {
                    float fluctuation = 1f + (rand2 - 0.5f) * 0.2f;
                    if (activeGoggle?.nightVision != null)
                    {
                        if (score < -0.85f)
                        {
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullificationExtremeDark * fluctuation); // It's really dark, slightly scale down
                        }
                        else if (score < -0.65f)
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullificationDarker * fluctuation); // It's quite dark, scale down
                        else if (score < 0)
                            score *= 1f - Mathf.Clamp01(activeGoggle.nightVision.nullification); // It's not really that dark, scale down massively
                    }
                    else if (activeScope?.nightVision != null)
                    {
                        if (score < -0.85f)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullificationExtremeDark * fluctuation); // It's really dark, slightly scale down
                        else if (score < -0.65f)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullificationDarker * fluctuation); // It's quite dark, scale down
                        else if (score < 0)
                            score *= 1f - Mathf.Clamp01(activeScope.nightVision.nullification); // It's not really that dark, scale down massively
                    }
                }

                if (inNVGView) // IR lights are not accounted in the score, process the score for each bot here
                {
                    float compensation = 0;
                    if (player.LightAndLaserState.IRLight)          compensation = Mathf.Clamp(0.4f - score, 0, 2) * player.LightAndLaserState.deviceStateCache.irLight;
                    else if (player.LightAndLaserState.IRLaser)     compensation = Mathf.Clamp(0.2f - score, 0, 2) * player.LightAndLaserState.deviceStateCache.irLaser;
                    else if (player.LightAndLaserState.IRLightSub)  compensation = Mathf.Clamp(0f - score, 0, 2) * player.LightAndLaserState.deviceStateCacheSub.irLight;
                    else if (player.LightAndLaserState.IRLaserSub)  compensation = Mathf.Clamp(0f - score, 0, 2) * player.LightAndLaserState.deviceStateCacheSub.irLaser;
                    score += compensation * Mathf.InverseLerp(0f, -1f, score);
                }

                factor = Mathf.Pow(score, ThatsLitPlayer.POWER); // -1 ~ 1, the graph is basically flat when the score is between ~0.3 and 0.3

                if (factor < 0) factor *= 1 + disFactor * Mathf.Clamp01(1.2f - pPoseFactor) * (canSeeLight ? 0.2f : 1f) * (canSeeLaser ? 0.9f : 1f); // Darkness will be far more effective from afar
                else if (factor > 0) factor /= 1 + Mathf.InverseLerp(100f, 10f, dis); // Highlight will be less effective from afar
            }

            if (player.DebugInfo != null && nearestAI)
            {
                if (Time.frameCount % ThatsLitPlayer.DEBUG_INTERVAL == ThatsLitPlayer.DEBUG_INTERVAL - 1)
                {
                    player.DebugInfo.lastScore = score;
                    player.DebugInfo.lastFactor1 = factor;
                }
                player.DebugInfo.nearestCaution = caution;
            }

            if (player.Foliage != null)
            {
                Vector2 bestMatchFoliageDir = Vector2.zero;
                float bestMatchDeg = 360f;
                float bestMatchDis = 0;
                for (int i = 0; i < Math.Min(ThatsLitPlugin.FoliageSamples.Value, player.Foliage.FoliageCount); i++) {
                    var f = player.Foliage.Foliage[i];
                    if (f == default) break;
                    var fDeg = Vector2.Angle(new Vector2(-eyeToPlayerBody.x, -eyeToPlayerBody.z), f.dir);
                    if (fDeg < bestMatchDeg)
                    {
                        bestMatchDeg = fDeg;
                        bestMatchFoliageDir = f.dir;
                        bestMatchDis = f.dis;
                    }
                }
                var foliageImpact = player.Foliage.FoliageScore;
                foliageImpact *= 1 + Mathf.InverseLerp(0f, -1f, factor);
                if (bestMatchFoliageDir != Vector2.zero)
                    foliageImpact *= Mathf.InverseLerp(90f, 0f, bestMatchDeg);
                                                                                                                // Maybe randomly lose vision for foliages
                float foliageBlindChance = Mathf.Clamp01(
                                                disFactor // Mainly works for far away enemies
                                                * foliageImpact
                                                * ThatsLitPlugin.FoliageImpactScale.Value
                                                * Mathf.Clamp01(2f - pPoseFactor)); // Lower chance for higher poses
                // 60m, standing, fs0.5 => 0.125 / 110m => 0.5
                foliageBlindChance *= 0.5f + 0.5f * visionAngleDelta90Clamped; // 50% effective when looking straight at the player
                if (UnityEngine.Random.Range(0f, 1.01f) < foliageBlindChance) // Among bushes, from afar, always at least 5% to be uneffective
                {
                    __result += rand2;
                    __result *= 1 + disFactor + rand4 * (5f + caution);
                }

                if (ThatsLitPlugin.SAINLoaded && ThatsLitPlugin.InterruptSAINNoBush.Value) // Compensation for SAINNoBushOverride
                {
                    // Extra stealth at the other side of foliage
                    __result += 15f * sinceSeenFactorSqr * Mathf.InverseLerp(30f, 5f, bestMatchDeg) * Mathf.InverseLerp(15f, 1f, dis) * Mathf.InverseLerp(0, 1f, bestMatchDis);
                }
            }

            // CBQ Factors =====
            // The closer it is, the higher the factors
            var cqb6mTo1m = Mathf.InverseLerp(5f, 0f, dis - 1f); // 6+ -> 0, 1f -> 1
            var cqb16mTo1m = Mathf.InverseLerp(15f, 0f, dis - 1f); // 16+ -> 0, 1f -> 1                                                               // Fix for blind bots who are already touching us

            var cqb11mTo1mSquared = Mathf.InverseLerp(10f, 0, dis - 1f); // 11+ -> 0, 1 -> 1, 6 ->0.5
            cqb11mTo1mSquared *= cqb11mTo1mSquared; // 6m -> 25%, 1m -> 100%

            // Scale down cqb factors for AIs facing away
            // not scaled down when ~15deg
            cqb6mTo1m *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);
            cqb16mTo1m *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);
            cqb11mTo1mSquared *= Mathf.InverseLerp(100f, 15f, visionAngleDelta);

            var xyFacingFactor = 0f;
            var layingVerticaltInVisionFactor = 0f;
            var detailScore = 0f;
            var detailScoreRaw = 0f;
            if (!inThermalView && player.TerrainDetails != null)
            {
                var terrainScore = Singleton<ThatsLitGameworld>.Instance.CalculateDetailScore(player.TerrainDetails, -eyeToPlayerBody, dis, visionAngleDeltaVerticalSigned);
                if (terrainScore.prone > 0.1f || terrainScore.regular > 0.1f)
                {
                    if (isInPronePose) // Handles cases where the player is laying on slopes and being very visible even with grasses
                    {
                        Vector3 playerLegPos = (playerParts[BodyPartType.leftLeg].Position + playerParts[BodyPartType.rightLeg].Position) / 2f;
                        var playerLegToHead = playerParts[BodyPartType.head].Position - playerLegPos;
                        var playerLegToHeadFlattened = new Vector2(playerLegToHead.x, playerLegToHead.z);
                        var playerLegToBotEye = __instance.Owner.MainParts[BodyPartType.head].Position - playerLegPos;
                        var playerLegToBotEyeFlatted = new Vector2(playerLegToBotEye.x, playerLegToBotEye.z);
                        var facingAngleDelta = Vector2.Angle(playerLegToHeadFlattened, playerLegToBotEyeFlatted); // Close to 90 when the player is facing right or left in the vision
                        if (facingAngleDelta >= 90) xyFacingFactor = (180f - facingAngleDelta) / 90f;
                        else if (facingAngleDelta <= 90) xyFacingFactor = (facingAngleDelta) / 90f;
#if DEBUG_DETAILS
                        if (player.DebugInfo != null && nearestAI) player.DebugInfo.lastRotateAngle = facingAngleDelta;
#endif
                        xyFacingFactor = 1f - xyFacingFactor; // 0 ~ 1

                        // Calculate how flat it is in the vision
                        var normal = Vector3.Cross(BotTransform.up, -playerLegToBotEye);
                        var playerLegToHeadAlongVision = Vector3.ProjectOnPlane(playerLegToHead, normal);
                        layingVerticaltInVisionFactor = Vector3.SignedAngle(playerLegToBotEye, playerLegToHeadAlongVision, normal); // When the angle is 90, it means the player looks straight up in the vision, vice versa for -90.
#if DEBUG_DETAILS
                        if (player.DebugInfo != null && nearestAI)
                        {
                            if (layingVerticaltInVisionFactor >= 90f) player.DebugInfo.lastTiltAngle = (180f - layingVerticaltInVisionFactor);
                            else if (layingVerticaltInVisionFactor <= 0)  player.DebugInfo.lastTiltAngle = layingVerticaltInVisionFactor;
                        }
#endif

                        if (layingVerticaltInVisionFactor >= 90f) layingVerticaltInVisionFactor = (180f - layingVerticaltInVisionFactor) / 15f; // the player is laying head up feet down in the vision...   "-> /"
                        else if (layingVerticaltInVisionFactor <= 0 && layingVerticaltInVisionFactor >= -90f) layingVerticaltInVisionFactor = layingVerticaltInVisionFactor / -15f; // "-> /"
                        else layingVerticaltInVisionFactor = 0; // other cases grasses should take effect

                        detailScore = terrainScore.prone * Mathf.Clamp01(1f - layingVerticaltInVisionFactor * xyFacingFactor);
                    }
                    else
                    {
                        detailScore = Utility.GetPoseWeightedRegularTerrainScore(pPoseFactor, terrainScore);
                        detailScore *= (1f - cqb11mTo1mSquared) * Mathf.InverseLerp(-25f, 5, visionAngleDeltaVerticalSigned); // nerf when high pose or < 10m or looking down
                    }

                    detailScore = Mathf.Min(detailScore, 2.5f - pPoseFactor); // Cap extreme grasses for high poses

                    detailScoreRaw = detailScore;
                    detailScore *= 1f + disFactor / 2f; // At 110m+, 1.5x effective
                    if (canSeeLight) detailScore /= 2f - disFactor; // Flashlights impact less from afar
                    if (canSeeLaser) detailScore *= 0.8f + 0.2f * disFactor; // Flashlights impact less from afar

                    switch (caution)
                    {
                        case 0:
                            detailScore /= 1.5f;
                            detailScore *= 1f - cqb16mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 30f); // nerf starting from looking 5deg up to down (scaled by down to -25deg) and scaled by dis 15 ~ 0
                            break;
                        case 1:
                            detailScore /= 1.25f;
                            detailScore *= 1f - cqb16mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 40f); // nerf starting from looking 5deg up (scaled by down to -40deg) and scaled by dis 15 ~ 0
                            break;
                        case 2:
                        case 3:
                        case 4:
                            detailScore *= 1f - cqb11mTo1mSquared * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 40f);
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            detailScore *= 1.2f;
                            detailScore *= 1f - cqb6mTo1m * Mathf.Clamp01((5f - visionAngleDeltaVerticalSigned) / 50f); // nerf starting from looking 5deg up (scaled by down to -50deg) and scaled by dis 5 ~ 0
                            break;
                    }

                    // Applying terrain detail stealth
                    // 0.1% chance does not work (very low because bots can track after spotting)
                    if (UnityEngine.Random.Range(0f, 1.001f) < Mathf.Clamp01(detailScore))
                    {
                        float detailImpact;
                        detailImpact = 19f * Mathf.Clamp01(notSeenRecentAndNear + 0.25f * Mathf.Clamp01(detailScore - 1f)) * (0.05f + disFactorSmooth); // The closer it is the more the player need to move to gain bonus from grasses, if has been seen;

                        // After spotted, the palyer could be tracked and lose all detail impact
                        // If the score is extra high and is proning, gives a change to get away  (the score is not capped to 1 even when crouching)
                        if (detailScore > 1 && isInPronePose)
                        {
                            detailImpact += (2f + rand5 * 3f) * (1f - disFactorSmooth) * (2f - visionAngleDelta90Clamped);
                            deNullification = 0.5f;
                        }
                        __result *= 1 + detailImpact;

                        if (player.DebugInfo != null && nearestAI)
                        {
                            player.DebugInfo.lastTriggeredDetailCoverDirNearest = -eyeToPlayerBody;
                        }
                    }
                }
                if (player.DebugInfo != null && nearestAI)
                {
                    player.DebugInfo.lastFinalDetailScoreNearest = detailScore;
                    player.DebugInfo.lastDisFactorNearest = disFactor;
                }
            }

            // BUSH RAT ----------------------------------------------------------------------------------------------------------------
            /// Overlook when the bot has no idea the player is nearby and the player is sitting inside a bush
            if (ThatsLitPlugin.EnabledBushRatting.Value
             && !inThermalView && player.Foliage != null && botImpactType != BotImpactType.BOSS
             && (!__instance.HaveSeen || lastSeenPosDelta > 30f + rand1 * 20f || sinceSeen > 150f + 150f*rand3 && lastSeenPosDelta > 10f + 10f*rand2))
            {
                float angleFactor = 0, foliageDisFactor = 0, poseScale = 0, enemyDisFactor = 0, yDeltaFactor = 1;
                bool bushRat = true;

                FoliageInfo nearestFoliage = player.Foliage.Foliage[0];
                switch (nearestFoliage.name)
                {
                    case "filbert_big01":
                        angleFactor             = 1; // works even if looking right at
                        foliageDisFactor        = Mathf.InverseLerp(1.5f, 0.8f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(2.5f, 0f, dis);
                        poseScale               = Mathf.InverseLerp(1f, 0.45f, pPoseFactor);
                        yDeltaFactor            = Mathf.InverseLerp(-60f, 0f, visionAngleDeltaVerticalSigned);
                        break;
                    case "filbert_big02":
                        angleFactor             = 0.4f + 0.6f * Mathf.InverseLerp(0, 20, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.1f, 0.6f, nearestFoliage.dis); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor          = Mathf.InverseLerp(0, 10f, dis);
                        poseScale               = pPoseFactor == 0.05f ? 0.7f : 1f; //
                        break;
                    case "filbert_big03":
                        angleFactor             = 0.4f + 0.6f * Mathf.InverseLerp(0, 30, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.45f, 0.2f, nearestFoliage.dis); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor          = Mathf.InverseLerp(0, 15f, dis);
                        poseScale               = pPoseFactor == 0.05f ? 0 : 0.1f + 0.9f * Mathf.InverseLerp(0.45f, 1f, pPoseFactor); // standing is better with this tall one
                        break;
                    case "filbert_01":
                        angleFactor             = 1;
                        foliageDisFactor        = Mathf.InverseLerp(0.6f, 0.25f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 12f, dis); Mathf.Clamp01(dis / 12f); // 100% at 2.5m+
                        poseScale               = Mathf.InverseLerp(0.75f, 0.3f, pPoseFactor);
                        break;
                    case "filbert_small01":
                        angleFactor             = 0.2f + 0.8f * Mathf.InverseLerp(0f, 35f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.3f, 0.15f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 10f, dis);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small02":
                        angleFactor             = 0.2f + 0.8f * Mathf.InverseLerp(0f, 25f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.3f, 0.15f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 8f, dis);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_small03":
                        angleFactor             = 0.2f + 0.8f * Mathf.InverseLerp(0f, 40f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(1f, 0.25f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 10f, dis);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "filbert_dry03":
                        angleFactor             = 0.4f + 0.6f * Mathf.InverseLerp(0f, 30f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.8f, 0.5f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 30f, dis);
                        poseScale               = pPoseFactor == 0.05f ? 0 : 0.1f + Mathf.InverseLerp(0.45f, 1f, pPoseFactor) * 0.9f;
                        break;
                    case "fibert_hedge01":
                        angleFactor             = Mathf.InverseLerp(0f, 40f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.2f, 0.1f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.Clamp01(dis / 30f);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "fibert_hedge02":
                        angleFactor             = 0.2f + 0.8f * Mathf.InverseLerp(0f, 40f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.3f, 0.1f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 20f, dis);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0; // Too narrow for proning
                        break;
                    case "privet_hedge":
                    case "privet_hedge_2":
                        angleFactor             = Mathf.InverseLerp(30f, 90f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(1f, 0f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 50f, dis);
                        poseScale               = pPoseFactor < 0.45f ? 1f : 0; // Prone only
                        break;
                    case "bush_dry01":
                        angleFactor             = 0.2f + 0.8f * Mathf.InverseLerp(0f, 35f, visionAngleDelta);
                        foliageDisFactor        = Mathf.InverseLerp(0.3f, 0.15f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 25f, dis);
                        poseScale               = pPoseFactor == 0.45f ? 1f : 0;
                        break;
                    case "bush_dry02":
                        angleFactor             = 1;
                        foliageDisFactor        = Mathf.InverseLerp(1.5f, 1f, nearestFoliage.dis);
                        enemyDisFactor          = Mathf.InverseLerp(0f, 15f, dis);
                        poseScale               = Mathf.InverseLerp(0.55f, 0.45f, pPoseFactor);
                        yDeltaFactor            = Mathf.InverseLerp(60f, 0f, -visionAngleDeltaVerticalSigned); // +60deg => 1, -60deg (looking down) => 0 (this flat bush is not effective against AIs up high)
                        break;
                    case "bush_dry03":
                        angleFactor = 0.4f + 0.6f * Mathf.Clamp01(visionAngleDelta / 20f);
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.3f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0.6f : 1 - Mathf.Clamp01((pPoseFactor - 0.45f) / 0.55f); // 100% at crouch
                        break;
                    case "tree02":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 45f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis * yDeltaFactor / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.1f + (pPoseFactor - 0.45f) / 0.55f * 0.9f; // standing is better with this tall one
                        break;
                    case "pine01":
                        yDeltaFactor = 0.7f + 0.5f * Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 10) / 40f); // bonus against bots up high
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 30f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = Mathf.InverseLerp(1.0f, 0.35f, nearestFoliage.dis);
                        enemyDisFactor = Mathf.InverseLerp(5f, 25f, dis * yDeltaFactor);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.5f + 0.5f * Mathf.InverseLerp(0.45f, 1f, pPoseFactor) * 0.5f; // standing is better with this tall one
                        break;
                    case "pine05":
                        angleFactor = 1; // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.5f) / 0.45f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 20f);
                        poseScale = pPoseFactor == 0.05f ? 0 : 0.5f + (pPoseFactor - 0.45f) / 0.55f * 0.5f; // standing is better with this tall one
                        yDeltaFactor = Mathf.Clamp01((-visionAngleDeltaVerticalSigned - 15) / 45f); // only against bots up high
                        break;
                    case "fern01":
                        angleFactor = 0.2f + 0.8f * Mathf.Clamp01(visionAngleDelta / 25f); // 0deg -> 0, 75 deg -> 1
                        foliageDisFactor = 1f - Mathf.Clamp01((nearestFoliage.dis - 0.1f) / 0.2f); // 0.3 -> 100%, 0.55 -> 0%
                        enemyDisFactor = Mathf.Clamp01(dis / 30f);
                        poseScale = pPoseFactor == 0.05f ? 1f : (1f - pPoseFactor) / 5f; // very low
                        break;
                    default:
                        bushRat = false;
                        break;
                }
                var bushRatFactor = Mathf.Clamp01(angleFactor * foliageDisFactor * enemyDisFactor * poseScale * yDeltaFactor);
                if (Singleton<ThatsLitGameworld>.Instance.IsWinter) bushRatFactor /= 1.15f;
                if (player.DebugInfo != null && nearestAI)
                {
                    player.DebugInfo.lastBushRat = bushRatFactor;
                }
                if (botImpactType == BotImpactType.FOLLOWER || canSeeLight || (canSeeLaser && rand3 < 0.2f)) bushRatFactor /= 2f;
                if (bushRat && bushRatFactor > 0.01f)
                {
                    if (player.DebugInfo != null && nearestAI)
                        player.DebugInfo.IsBushRatting = bushRat;
                    __result = Mathf.Max(__result, dis);
                    switch (caution)
                    {
                        case 0:
                        case 1:
                            if (rand2 > 0.01f) __result *= 1 + 4 * bushRatFactor * UnityEngine.Random.Range(0.2f, 0.4f);
                            cqb6mTo1m *= 1f - bushRatFactor * 0.5f;
                            cqb11mTo1mSquared *= 1f - bushRatFactor * 0.5f;
                            break;
                        case 2:
                        case 3:
                        case 4:
                            if (rand3 > 0.005f) __result *= 1 + 8 * bushRatFactor * UnityEngine.Random.Range(0.3f, 0.65f);
                            cqb6mTo1m *= 1f - bushRatFactor * 0.8f;
                            cqb11mTo1mSquared *= 1f - bushRatFactor * 0.8f;
                            break;
                        case 5:
                        case 6:
                        case 7:
                        case 8:
                        case 9:
                            if (rand1 > 0.001f) __result *= 1 + 6 * bushRatFactor * UnityEngine.Random.Range(0.5f, 1.0f);
                            cqb6mTo1m *= 1f - bushRatFactor;
                            cqb11mTo1mSquared *= 1f - bushRatFactor;
                            break;
                    }
                }
            }
            // BUSH RAT ----------------------------------------------------------------------------------------------------------------


            /// -0.7 -> 0, -0.8 -> 0.33, -0.9 -> 0.66, -1 -> 1
            var extremeDarkFactor = Mathf.Clamp01((score - -0.7f) / -0.3f);
            extremeDarkFactor *= extremeDarkFactor;
            var notSoExtremeDarkFactor = Mathf.Clamp01((score - -0.5f) / -0.5f);
            notSoExtremeDarkFactor *= notSoExtremeDarkFactor;

            if (player.PlayerLitScoreProfile == null && ThatsLitPlugin.AlternativeReactionFluctuation.Value)
            {
                // https://www.desmos.com/calculator/jbghqfxwha
                float cautionFactor = (caution / 9f - 0.5f) * (0.05f + 0.5f * rand4 * rand4); // -0.5(faster)~0.5(slower) squared curve distribution
                __result += cautionFactor;
                __result *= 1f + cautionFactor / 2f; // Factor in bot class
            }
            else if (player.PlayerLitScoreProfile != null && Mathf.Abs(score) >= 0.05f) // Skip works
            {
                if (Singleton<ThatsLitGameworld>.Instance.IsWinter && player.Foliage != null)
                {
                    var emptiness = 1f - player.Foliage.FoliageScore * detailScoreRaw;
                    emptiness *= 1f - insideTime;
                    disFactor *= 0.7f + 0.3f * emptiness; // When player outside is not surrounded by anything in winter, lose dis buff
                }

                factor = Mathf.Clamp(factor, -0.975f, 0.975f);

                // Absoulute offset
                // f-0.1 => -0.005~-0.01, factor: -0.2 => -0.02~-0.04, factor: -0.5 => -0.125~-0.25, factor: -1 => 0 ~ -0.5 (1m), -0.5 ~ -1 (10m)
                var secondsOffset = -1f * Mathf.Pow(factor, 2) * Mathf.Sign(factor) * (UnityEngine.Random.Range(0.5f, 1f) - 0.5f * cqb11mTo1mSquared); // Base
                secondsOffset += (original * (10f + rand1 * 20f) * (0.1f + 0.9f * sinceSeenFactorSqr * seenPosDeltaFactorSqr) * extremeDarkFactor) / pPoseFactor; // Makes night factory makes sense (filtered by extremeDarkFactor)
                secondsOffset *= botImpactType == BotImpactType.DEFAULT? 1f : 0.5f;
                secondsOffset *= secondsOffset > 0 ? ThatsLitPlugin.BrightnessImpactScale : ThatsLitPlugin.DarknessImpactScale;
                __result += secondsOffset;
                if (__result < 0) __result = 0;


                // The scaling here allows the player to stay in the dark without being seen
                // The reason why scaling is needed is because SeenCoef will change dramatically depends on vision angles
                // Absolute offset alone won't work for different vision angles
                if (factor < 0)
                {
                    float combinedCqb10x5To1 = 0.5f * cqb11mTo1mSquared * (0.7f + 0.3f * rand2 * pPoseFactor);
                    combinedCqb10x5To1 += 0.5f * cqb6mTo1m;
                    combinedCqb10x5To1 *= 0.9f + 0.4f * pSpeedFactor; // Buff bot CQB reaction when the player is moving fast
                    combinedCqb10x5To1 = Mathf.Clamp01(combinedCqb10x5To1);

                    // cqb factors are already scaled down by vision angle

                    var attentionCancelChanceScaleByExDark = 0.2f * rand5 * Mathf.InverseLerp(-0.8f, -1f, score);

                    // === Roll a forced stealth boost ===
                    // negative because factor is negative
                    float forceStealthChance = factor * Mathf.Clamp01(1f - combinedCqb10x5To1);
                    // 60% nullified by bot attention (if not cancelled by extreme darkness)
                    forceStealthChance *= 0.4f + 0.6f * Mathf.Clamp01(notSeenRecentAndNear + attentionCancelChanceScaleByExDark);
                    if (UnityEngine.Random.Range(-1f, 0f) > forceStealthChance)
                    {
                        __result *= 100 * ThatsLitPlugin.DarknessImpactScale;
                    }
                    else
                    {
                        var scale = factor * factor * 0.5f + 0.5f* Mathf.Abs(factor * factor * factor);
                        scale *= 3f;
                        scale *= ThatsLitPlugin.DarknessImpactScale;
                        scale *= 1f - combinedCqb10x5To1;
                        // -1 => 3
                        // -0.5 => 0.5625
                        // -0.2 => 0.072

                        scale *= 0.7f + 0.3f * notSeenRecentAndNear;
                        __result *= 1f+scale;
                    }

                }
                else if (factor > 0)
                {
                    if (rand5 < factor * factor) __result *= 1f - 0.5f * ThatsLitPlugin.BrightnessImpactScale;
                    else __result /= 1f + factor / 5f * ThatsLitPlugin.BrightnessImpactScale;
                }
            }

            // Vanilla is multiplying the final SeenCoef with 1E-05
            // Probably to guarantee the continuance of the bot attention
            // However this includes situations that the player has moved at least a bit and the bot is running/facing side/away
            // This part, in a very conservative way, tries to randomly delay the reaction
            if (sinceSeen < __instance.Owner.Settings.FileSettings.Look.SEC_REPEATED_SEEN
                && lastSeenPosDeltaSqr < __instance.Owner.Settings.FileSettings.Look.DIST_SQRT_REPEATED_SEEN
                && __result < 0.5f)
            {
                __result += (0.5f - __result)
                            * (rand1 * Mathf.Clamp01(visionAngleDelta / 90f)) // Scale-capped by horizontal vision angle delta
                            * (rand3 * Mathf.Clamp01(lastSeenPosDelta / 5f)) // Scale-capped by player position delta to last
                            * (__instance.Owner.Mover.Sprinting? 1f : 0.75f); 
            }

            if (ThatsLitPlugin.EnableMovementImpact.Value)
            {
                if (__instance.Owner.Mover.Sprinting)
                    __result *= 1 + (rand2 / (3f - caution * 0.1f)) * Mathf.InverseLerp(30f, 75f, visionAngleDelta); // When facing away (30~75deg), sprinting bots takes up to 33% longer to spot the player
                else if (!__instance.Owner.Mover.IsMoving)
                {
                    float delta = __result * (rand4 / (5f + caution * 0.1f)); // When not moving, bots takes up to 20% shorter to spot the player
                    __result -= delta;
                }

                if (pSpeedFactor > 0.01f)
                {
                    float delta = __result * (rand2 / (5f + caution * 0.1f)) * pSpeedFactor * (1f - extremeDarkFactor) * Mathf.Clamp01(pPoseFactor); // When the score is -0.7+, bots takes up to 20% shorter to spot the player according to player movement speed (when not proning);
                    __result -= delta;
                }
            }

            // ===== Visible Parts
            // Simulate the situation where part of a player is seen but the bot failed to recognize it due to the lack of full view
            var visiblePartsFactor = 6f;
            var upperVisible = 0;
            foreach (var p in __instance.AllActiveParts)
            {
                if (p.Value.LastVisibilityCastSucceed)
                {
                    switch (p.Key.BodyPartType)
                    {
                        case BodyPartType.head:
                            visiblePartsFactor -= 0.5f; // easier to recognize
                            upperVisible++;
                            break;
                        case BodyPartType.body:
                            visiblePartsFactor -= 2.0f;
                            upperVisible++;
                            break;
                        case BodyPartType.leftArm:
                        case BodyPartType.rightArm:
                            visiblePartsFactor -= 1f;
                            upperVisible++;
                            break;
                        default:
                            visiblePartsFactor -= 1f;
                            break;
                    }
                }
            }
            visiblePartsFactor = Mathf.InverseLerp(6f, 1f, visiblePartsFactor);
            visiblePartsFactor *= visiblePartsFactor;
            float partsHidingCutoff = visiblePartsFactor * Mathf.InverseLerp(0f, 45f - caution, visionAngleDelta) * notSeenRecentAndNear * Mathf.InverseLerp(0f, 10f, zoomedDis);
            if (player.DebugInfo != null && nearestAI)
            {
                player.DebugInfo.lastVisiblePartsFactor = visiblePartsFactor;
            }
            if (botImpactType != BotImpactType.DEFAULT) partsHidingCutoff /= 2f;
            __result += (0.04f * rand2) * partsHidingCutoff;
            __result *= 1f + (0.15f + 0.85f * rand1) * partsHidingCutoff * 9f;

            // Simulated Free Look
            float sin = 0.75f * Mathf.Sin(Time.time / ((float)(1f + caution))) + 0.25f * Mathf.Sin(Time.time);
            int focusLUTIndex = (int) ((Time.time + sin * 0.5f) / (float)(3f + caution / 5f));
            focusLUTIndex %= 50;
            Vector3 simFreeLookDir = botVisionDir;
            var lutLookup1 = focusLUTs[caution][focusLUTIndex];
            lutLookup1 += 15f * sin;
            lutLookup1 = Mathf.Clamp(lutLookup1, -90f, 90f);
            var lutLookup2 = focusLUTs[(caution + focusLUTIndex) % 10][focusLUTIndex];
            lutLookup2  = Mathf.Abs(lutLookup2 / 2f);
            lutLookup2 += 5f * sin;
            lutLookup2 = Mathf.Clamp(lutLookup2, 0, 45f);
            simFreeLookDir = simFreeLookDir.RotateAroundPivot(Vector3.up, new Vector3(0f, lutLookup1));
            simFreeLookDir = simFreeLookDir.RotateAroundPivot(Vector3.Cross(Vector3.up, botVisionDir), new Vector3(0f, -lutLookup2));
            if (playerFC?.IsAiming == true)
            {
                simFreeLookDir = new Vector3(
                    Mathf.Lerp(simFreeLookDir.x, botVisionDir.x, 0.5f),
                    Mathf.Lerp(simFreeLookDir.y, botVisionDir.y, 0.5f),
                    Mathf.Lerp(simFreeLookDir.z, botVisionDir.z, 0.5f));
            }
            if (activeGoggle != null)
            {
                simFreeLookDir = new Vector3(
                    Mathf.Lerp(simFreeLookDir.x, botVisionDir.x, 0.5f),
                    Mathf.Lerp(simFreeLookDir.y, botVisionDir.y, 0.5f),
                    Mathf.Lerp(simFreeLookDir.z, botVisionDir.z, 0.5f));
            }
            var simFocusDeltaFactor = Mathf.InverseLerp(0, 150f, Vector3.Angle(botVisionDir, simFreeLookDir));
            simFocusDeltaFactor -= 0.5f;
            __result *= 1f + 0.25f * notSeenRecentAndNear * simFocusDeltaFactor;
            if (player.DebugInfo != null && nearestAI)
            {
                player.DebugInfo.lastNearestFocusAngleX = lutLookup1;
                player.DebugInfo.lastNearestFocusAngleY = lutLookup2;
            }

            __result = Mathf.Lerp(__result, original, botImpactType == BotImpactType.DEFAULT? 0f : 0.5f);
            
            if (__result > original) // That's Lit delaying the bot
            {
                // In ~0.2s after being seen, stealth is nullfied (fading between 0.1~0.2)
                // To prevent interruption of ongoing fight
                float nullification = 1f - sinceSeen / 0.2f; // 0.1s => 50%, 0.2s => 0%
                nullification *= rand5;
                nullification -= deNullification; // Allow features to interrupt the nullification
                __result = Mathf.Lerp(__result, original, Mathf.Clamp01(nullification)); // just seen (0s) => original, 0.1s => modified

                // Cutoff from directly facing at shooting player
                __result *= 1f - 0.5f * facingShotFactor * Mathf.InverseLerp(-0.45f, 0.45f, factor);

                if (playerFC?.IsStationaryWeapon == true)
                {
                    __result *= 1f - 0.3f * rand4 * Mathf.InverseLerp(30f, 5f, zoomedDis);
                }
            }
            // This probably will let bots stay unaffected until losing the visual.1s => modified

            if (canSeeLight && playerFC != null)
            {
                float wCoFacingAngle = Vector3.Angle(-eyeToPlayerBody, playerFC.WeaponDirection);
                // If player flashlights directly shining against the bot
                if (upperVisible >= 3) // >= 3 upper parts visible
                {
                    __result *= 1 - 0.5f * Mathf.InverseLerp(5f, 0f, wCoFacingAngle) * Mathf.InverseLerp(40f, 7.5f, zoomedDis);
                }
            }

            // Up to 50% penalty
            if (__result < 0.5f * original)
            {
                __result = 0.5f * original;
            }

            __result = Mathf.Lerp(original, __result, __result < original ? ThatsLitPlugin.FinalImpactScaleFastening.Value : ThatsLitPlugin.FinalImpactScaleDelaying.Value);

            __result += ThatsLitPlugin.FinalOffset.Value;
            if (__result < 0.005f) __result = 0.005f;

            if (player.DebugInfo != null)
            {
                if (Time.frameCount % ThatsLitPlayer.DEBUG_INTERVAL == ThatsLitPlayer.DEBUG_INTERVAL - 1 && nearestAI)
                {
                    player.DebugInfo.lastCalcTo = __result;
                    player.DebugInfo.lastFactor2 = factor;
                    player.DebugInfo.rawTerrainScoreSample = detailScoreRaw;
                }
                player.DebugInfo.calced++;
                player.DebugInfo.calcedLastFrame++;
            }
            

            ThatsLitPlugin.swSeenCoef.Stop();
        }


        static readonly float[][] focusLUTs = new float[][] {
            new float[] {54.50f, 28.23f, -54.50f, 17.80f, -17.62f, -58.32f, 48.27f, -66.88f, 69.67f, -36.08f, 50.87f, -16.10f, -59.42f, 68.01f, 18.32f, 69.32f, -68.27f, -51.95f, -45.43f, 38.74f, -10.35f, 34.30f, -40.63f, -62.96f, -61.36f, 16.48f, 84.80f, 2.70f, 53.44f, -7.11f, -10.39f, 60.13f, -42.86f, 22.92f, 29.99f, -83.94f, 30.09f, -20.46f, 48.12f, 27.54f, -58.72f, -56.59f, 39.65f, 17.28f, -38.71f, -5.84f, -11.16f, -78.23f, -84.09f, 6.36f, },
            new float[] {-16.11f, 47.32f, -35.87f, -31.00f, 15.78f, -28.40f, 64.51f, -58.02f, 49.74f, -40.41f, -73.03f, 0.95f, 7.26f, -44.57f, -17.56f, -78.32f, 63.43f, -44.22f, 33.66f, -47.39f, 73.35f, 33.44f, -3.25f, 14.87f, -69.54f, 38.66f, -80.62f, -16.72f, -67.90f, -34.57f, -48.72f, 32.42f, -17.01f, -57.30f, 31.10f, 29.54f, 51.84f, 37.27f, -67.88f, 51.05f, -15.93f, -11.92f, 28.91f, -19.16f, 82.05f, -62.37f, 43.96f, -88.40f, -76.82f, -48.78f, },
            new float[]{-49.53f, 62.54f, -82.87f, 31.25f, -67.10f, 74.12f, -14.81f, 1.96f, 49.63f, -79.96f, -22.95f, 88.76f, 64.99f, 25.40f, -52.47f, -14.16f, -11.66f, -53.45f, -42.81f, -54.19f, -80.31f, 72.49f, -67.57f, 51.40f, 47.34f, -56.81f, -0.72f, 17.45f, 84.34f, 45.96f, 47.41f, 34.15f, -58.55f, -79.47f, 70.43f, 79.69f, 56.19f, 76.97f, -26.03f, -59.00f, 41.01f, -72.72f, 41.22f, -25.58f, 18.61f, -17.60f, -42.80f, 67.38f, -7.94f, -3.68f, },
            new float[]{-45.68f, -68.52f, -85.61f, -82.18f, -62.53f, 60.81f, -53.25f, -62.42f, -2.17f, 4.25f, -37.11f, 64.48f, 8.25f, 0.97f, -58.77f, 44.72f, -34.67f, -42.61f, 14.65f, -80.17f, 51.47f, 66.20f, -60.68f, -65.04f, -80.87f, -14.65f, -25.26f, -83.60f, -14.45f, 14.97f, 35.66f, -4.00f, -62.77f, 71.46f, -19.43f, 63.21f, -58.42f, -5.38f, -40.35f, 77.27f, -31.53f, -53.64f, -65.73f, 72.33f, -54.52f, -31.50f, 68.12f, -80.40f, -83.35f, 20.19f, },
            new float[]{67.26f, -12.92f, -85.54f, -40.74f, -49.06f, -63.74f, 8.11f, 72.04f, 70.22f, 21.47f, 62.14f, -61.84f, 26.96f, 32.89f, -13.55f, -30.90f, 54.59f, -88.60f, 15.45f, -74.99f, -82.73f, -4.15f, -44.30f, -13.88f, -86.44f, -25.44f, 89.95f, 64.59f, 6.46f, 80.01f, 89.29f, -81.85f, -51.50f, 85.21f, -60.00f, -45.53f, 37.28f, 24.17f, 45.13f, -61.79f, 38.30f, -22.89f, 58.03f, -59.23f, 22.35f, -44.53f, 53.58f, 4.29f, -45.47f, 76.20f, },
            new float[]{-60.77f, -10.91f, -18.86f, 11.65f, -49.81f, -11.79f, 34.44f, 54.10f, 27.66f, -4.31f, -84.01f, -69.11f, 48.67f, -85.23f, -83.99f, 23.79f, 78.87f, 53.05f, -42.09f, 63.23f, 85.31f, 81.66f, 52.04f, -60.44f, 16.42f, -74.05f, 43.25f, -54.74f, 89.70f, 58.13f, 56.97f, 27.70f, -48.73f, 36.54f, -39.29f, -65.99f, -83.48f, 52.16f, 73.87f, 64.80f, 81.86f, -18.26f, -67.03f, 60.58f, 5.15f, 27.45f, 9.52f, 80.36f, -13.85f, 57.30f, },
            new float[]{52.96f, 37.93f, -32.44f, -4.76f, 72.21f, 51.19f, -80.08f, -1.37f, -31.86f, -63.34f, 66.30f, -83.05f, -0.77f, -16.76f, -5.49f, 14.94f, 3.11f, -8.07f, -27.55f, 25.28f, 38.59f, -38.94f, -31.48f, 24.61f, -21.79f, -20.20f, 27.24f, 24.43f, 63.57f, -87.11f, 48.99f, -12.57f, -30.00f, -23.50f, -31.87f, 40.98f, -73.25f, 47.89f, 82.61f, -53.31f, 19.84f, -64.08f, -13.87f, -71.09f, 65.30f, -48.18f, 1.95f, 18.50f, -4.76f, -59.12f, },
            new float[]{-64.57f, 36.44f, 40.01f, -31.82f, 25.78f, 54.54f, 11.02f, 63.93f, 75.41f, 53.81f, 75.80f, -75.32f, -89.76f, 75.25f, -64.03f, 40.65f, 6.29f, -1.26f, -35.97f, 74.86f, 89.63f, -28.19f, 67.43f, -29.17f, 69.74f, 83.09f, -26.16f, 16.01f, -71.31f, 80.60f, -45.07f, 33.23f, -40.92f, 8.62f, 85.99f, 10.53f, 3.77f, -49.93f, -30.65f, 58.04f, 15.10f, 21.66f, 80.79f, 41.91f, -86.74f, -31.44f, 51.03f, 70.24f, -23.77f, 88.81f, },
            new float[]{22.96f, 37.25f, 30.70f, 40.43f, 20.07f, -57.04f, -48.55f, 65.37f, 43.31f, -73.80f, 1.59f, -43.19f, -32.10f, -32.81f, -24.28f, -18.44f, 65.97f, -64.20f, 55.00f, 68.86f, 49.26f, 69.38f, -71.39f, 84.26f, -77.68f, -37.36f, -22.46f, -1.41f, 53.86f, 5.09f, -1.23f, -39.88f, -77.28f, -65.89f, 80.69f, -47.02f, 62.67f, 22.80f, -88.46f, 56.65f, -34.39f, -47.66f, 63.19f, 44.12f, 27.04f, 20.89f, 8.33f, -68.87f, 43.41f, -64.82f, },
            new float[]{13.07f, -28.97f, -68.14f, 27.52f, 55.57f, 10.89f, -40.27f, 24.99f, -57.12f, 3.58f, 4.15f, 52.29f, -16.64f, 63.75f, -21.98f, -81.54f, -81.94f, -51.21f, -23.39f, 40.56f, -47.03f, -1.95f, -86.94f, -80.37f, -75.41f, -81.19f, -9.42f, -85.49f, -89.89f, 53.66f, -33.51f, 35.40f, -55.27f, -68.08f, -59.30f, -45.99f, 49.99f, 23.53f, 65.20f, -60.54f, -84.18f, 1.12f, 23.76f, -65.70f, 37.50f, 67.38f, 24.82f, 89.15f, -29.88f, -27.76f, },
        };
    }

}