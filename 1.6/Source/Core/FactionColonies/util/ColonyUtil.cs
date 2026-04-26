using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using Verse;

namespace FactionColonies.util
{
    public static class ColonyUtil
    {
        /// <summary>
        /// Picks the default settlement type for a tile based on its planet layer.
        /// Use when creating a settlement where the caller has no specific type in mind
        /// (capturing a vanilla NPC settlement, fallback for null input, etc.).
        /// </summary>
        public static WorldSettlementDef DefaultSettlementDefForTile(PlanetTile tile)
        {
            if (ModsConfig.OdysseyActive && tile.Valid && tile.Layer == Find.WorldGrid.Orbit)
            {
                return WorldSettlementDefOf.WorldSettlementDef_Orbital;
            }
            return WorldSettlementDefOf.WorldSettlementDef_Surface;
        }

        public static WorldSettlementFC CreatePlayerColonySettlement(PlanetTile tile, WorldSettlementDef settlementType)
        {
            if (settlementType == null)
            {
                settlementType = DefaultSettlementDefForTile(tile);
                LogUtil.Error($"Tried to create a settlement with null WorldSettlementDef! Defaulting to {settlementType.defName} based on tile layer.");
            }

            /* Do any pre-settlement-creation demanded of the settlement type */
            settlementType.GetSettlementTypeExtension().PreCreation(ref tile, ref settlementType);

            LogUtil.Message($"Creating settlement of type {settlementType.defName}");
            Faction faction = FactionCache.PlayerColonyFaction;

            FactionFC worldcomp = FactionCache.FactionComp;
            if (!worldcomp.settlements.Any())
            {
                FactionCache.FactionComp.timeStart = Find.TickManager.TicksGame;
            }

            WorldSettlementFC settlement = (WorldSettlementFC)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldSettlementDef>.GetNamed(settlementType.defName));
            settlement.PostPostMake(tile);

            settlement.SetFaction(faction);
            Find.WorldObjects.Add(settlement);

            worldcomp.AddSettlement(settlement);
            worldcomp.roadBuilder.FlagUpdateRoadQueues();

            /* Do any post-settlement-creation demanded of the settlement type */
            settlementType.GetSettlementTypeExtension().PostCreation(settlement);

            LifecycleRegistry.InvokeOnSettlementCreated(settlement);

            Find.LetterStack.ReceiveLetter("FCSettlementFormed".Translate(),
                "FCSettleEventCompletedDesc".Translate(settlement.Name, settlementType.LabelCap, tile.Tile.PrimaryBiome.LabelCap),
                LetterDefOf.PositiveEvent);

            return settlement;
        }

        public static void RemovePlayerSettlement(WorldSettlementFC settlement)
        {
            settlement.settlementDef.GetSettlementTypeExtension()?.PreDestruction(settlement);
            settlement.PrepareDestroy();
            FactionFC faction = FactionCache.FactionComp;
            LifecycleRegistry.InvokeOnSettlementRemoved(settlement);
            faction.settlements.Remove(settlement);

            // Clean up any pending bills for the destroyed settlement
            for (int i = faction.Bills.Count - 1; i >= 0; i--)
            {
                if (faction.Bills[i].settlement == settlement)
                {
                    LogUtil.Message("RemovePlayerSettlement: removing orphaned bill (loadID=" + faction.Bills[i].loadID + ") for destroyed settlement " + settlement.Name);
                    faction.Bills.RemoveAt(i);
                }
            }

            faction.DirtyFactionProfitCache();
            faction.DirtyAveragesCache();
            faction.roadBuilder.FlagUpdateRoadQueues();
            Messages.Message("FCSettlementRemoved".Translate(settlement.Name), MessageTypeDefOf.NegativeEvent);

            Find.WorldObjects.Remove(Find.World.worldObjects.WorldObjectOfDefAt(DefDatabase<WorldObjectDef>.GetNamed(settlement.def.defName), settlement.Tile));

            //clear military events
            settlement.MilitaryComp?.ReturnMilitary(false);

            HashSet<FCEvent> toRemove = new HashSet<FCEvent>();

            // Settlement-removal sweep reads the warning event's [Obsolete] military force fields
            // to decide which events belong to the doomed settlement. New ops mirror those fields
            // so this back-compat path keeps working until the wave model reads from the op directly.
#pragma warning disable 0618
            foreach (FCEvent evt in faction.Events)
            {
                //military event removal
                if (evt.def == FCEventDefOf.captureEnemySettlement || evt.def == FCEventDefOf.raidEnemySettlement)
                {
                    if (evt.militaryForceAttacking?.homeSettlement == settlement)
                    {
                        toRemove.Add(evt);
                    }
                }

                if (evt.def == FCEventDefOf.settlementBeingAttacked)
                {
                    if (evt.militaryForceDefending?.homeSettlement == settlement)
                    {
                        if (evt.settlementFCDefending == settlement)
                        {
                            toRemove.Add(evt);
                        }
                        else if (evt.settlementFCDefending is WorldSettlementFC targetSettlement)
                        {
                            //if not defending settlement, reset to target's own defense
                            MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, targetSettlement);
                        }
                        else
                        {
                            // External raid target (outpost etc.) — defender removed, remove event
                            toRemove.Add(evt);
                            IRaidTarget raidTarget = RaidTargetRegistry.FindByWorldObject(evt.settlementFCDefending);
                            if (raidTarget != null) raidTarget.IsUnderAttack = false;
                        }
                    }
                    else
                    {
                        //if force belongs to other settlement
                        evt.militaryForceDefending?.homeSettlement?.MilitaryComp?.CooldownMilitaryFinal();

                        toRemove.Add(evt);
                    }
                }


                //settlement event removal
                if (evt.def == FCEventDefOf.constructBuilding || evt.def == FCEventDefOf.enactSettlementPolicy ||
                    evt.def == FCEventDefOf.upgradeSettlement || evt.def == FCEventDefOf.cooldownMilitary)
                {
                    if (evt.source == settlement.Tile)
                    {
                        toRemove.Add(evt);
                    }
                }

                if (evt.def.isRandomEvent && evt.settlementTraitLocations.Count > 0)
                {
                    if (evt.settlementTraitLocations.Contains(settlement))
                    {
                        evt.settlementTraitLocations.Remove(settlement);
                        if (evt.settlementTraitLocations.Count == 0)
                        {
                            toRemove.Add(evt);
                        }
                    }
                }

                // Let extensions cancel their own custom events
                if (!toRemove.Contains(evt))
                {
                    FCEventHandlerExtension handler = evt.def.GetModExtension<FCEventHandlerExtension>();
                    if (handler != null && handler.ShouldCancelOnSettlementRemoval(evt, settlement))
                    {
                        toRemove.Add(evt);
                    }
                }
            }
#pragma warning restore 0618

            bool anyRemoved = false;
            foreach (FCEvent evt in toRemove)
            {
                if (faction.RemoveEvent(evt)) anyRemoved = true;
            }
            if (anyRemoved)
            {
                faction.InvalidateFactionStatCache();
            }
        }
        public static Faction CreatePlayerColonyFaction()
        {
            FactionFC worldcomp = FactionCache.FactionComp;
            if (worldcomp == null)
            {
                LogUtil.Error("FactionFC world component is missing! Cannot create player colony faction.");
                return null;
            }
            LogUtil.Message("Creating new player faction");
            worldcomp.SetCapital();

            FactionDef facDef = DefDatabase<FactionDef>.GetNamed("PColony");
            Faction faction = new Faction
            {
                def = facDef
            };
            faction.def.techLevel = Faction.OfPlayer.def.techLevel;
            faction.loadID = Find.UniqueIDsManager.GetNextFactionID();
            faction.colorFromSpectrum = FactionGenerator.NewRandomColorFromSpectrum(faction);
            faction.Name = "FCPlayerColony".Translate();
            faction.def.classicIdeo = Faction.OfPlayer.def.classicIdeo;
            faction.ideos = Faction.OfPlayer.ideos;

            worldcomp.DirtyTechLevelCache();
            //<DevAdd> Copy player faction relationships  
            foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
            {
                faction.TryMakeInitialRelationsWith(other);
            }
            // Set starting goodwill to Player
            faction.TryAffectGoodwillWith(Faction.OfPlayer, 200);

            // Generate Leader
            if (faction.leader == null || faction.leader.Dead)
            {
                CreatePlayerFactionLeader(faction);
            }

            Find.FactionManager.Add(faction);
            RelationsUtilFC.ResetPlayerColonyRelations();
            worldcomp.OnCreation();
            return faction;
        }

        public static bool CreatePlayerFactionLeader(Faction faction)
        {
            bool success = true;
            bool leaderGenerated = false;
            try
            {
                leaderGenerated = faction.TryGenerateNewLeader();
            }
            catch (System.Exception ex)
            {
                LogUtil.Warning("TryGenerateNewLeader threw an exception. Falling back to manual generation. Exception: " + ex);
            }

            if (!leaderGenerated)
            {
                LogUtil.Warning("TryGenerateNewLeader failed. Falling back to manual generation.");
                PawnKindDef fallbackKind = faction.RandomPawnKind();
                if (fallbackKind is null)
                {
                    fallbackKind = PawnKindDefOf.Villager;
                    LogUtil.Warning("RandomPawnKind returned null. Using Villager as last-resort fallback.");
                }
                LogUtil.Message($"Fallback pawnkind: {fallbackKind?.defName ?? "null"}");
                faction.leader = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind: fallbackKind,
                faction: faction, context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: true, mustBeCapableOfViolence: true, colonistRelationChanceFactor: 0,
                forceAddFreeWarmLayerIfNeeded: false, worldPawnFactionDoesntMatter: false));
                if (faction.leader == null)
                {
                    LogUtil.Warning("Fallback leader generation also failed!");
                    success = false;
                }
                else
                {
                    if (!Find.WorldPawns.Contains(faction.leader))
                    {
                        Find.WorldPawns.PassToWorld(faction.leader, PawnDiscardDecideMode.KeepForever);
                    }
                    LogUtil.Message($"Created leader {faction.leader.Name} ({faction.leader.ThingID}), " +
                                    $"pawnKind: {faction.leader.kindDef?.defName ?? "null"}, " +
                                    $"title: {faction.LeaderTitle}, " +
                                    $"ideo: {faction.leader.Ideo?.name ?? "none"}, " +
                                    $"faction: {faction.Name}");
                }
            }
            else
            {
                LogUtil.Message($"TryGenerateNewLeader succeeded. Leader: {faction.leader?.Name} ({faction.leader?.ThingID}), " +
                                $"pawnKind: {faction.leader?.kindDef?.defName ?? "null"}, " +
                                $"title: {faction.LeaderTitle}, " +
                                $"ideo: {faction.leader?.Ideo?.name ?? "none"}");
            }

            return success;
        }
    }
}
