using RimWorld;
using System;
using Verse;

namespace FactionColonies
{
    public class RelationsUtilFC
    {
        public static void AttackFaction(Faction faction)
        {
            Find.FactionManager.OfPlayer.TryAffectGoodwillWith(faction, -50);
            TrySetRelationKind(Find.FactionManager.OfPlayer, faction, FactionRelationKind.Hostile);
        }

        public static void ResetPlayerColonyRelations()
        {
            Faction PCFaction = FindFC.EmpireFaction;
            foreach (Faction faction in Find.FactionManager.AllFactionsInViewOrder)
            {
                if (faction != Find.FactionManager.OfPlayer && faction != PCFaction)
                {
                    //if not player faction or player colony faction
                    PCFaction.TryAffectGoodwillWith(faction,
                        (Find.FactionManager.OfPlayer.RelationWith(faction).baseGoodwill -
                         PCFaction.RelationWith(faction).baseGoodwill));
                    TrySetRelationKind(PCFaction, faction, Find.FactionManager.OfPlayer.RelationKindWith(faction));
                }
            }
        }

        internal static bool TrySetRelationKind(Faction self, Faction other, FactionRelationKind kind, bool canSendLetter = true)
        {
            FactionRelation factionRelation = self.RelationWith(other);
            if (factionRelation.kind == kind)
            {
                return true;
            }
            if (!self.HasGoodwill)
            {
                self.SetRelationDirect(other, kind, canSendLetter);
                return true;
            }
            switch (kind)
            {
                case FactionRelationKind.Hostile:
                    self.TryAffectGoodwillWith(other, -75 - factionRelation.baseGoodwill, canSendMessage: false, canSendLetter);
                    return factionRelation.kind == FactionRelationKind.Hostile;
                case FactionRelationKind.Neutral:
                    self.TryAffectGoodwillWith(other, -factionRelation.baseGoodwill, canSendMessage: false, canSendLetter);
                    return factionRelation.kind == FactionRelationKind.Neutral;
                case FactionRelationKind.Ally:
                    self.TryAffectGoodwillWith(other, 75 - factionRelation.baseGoodwill, canSendMessage: false, canSendLetter);
                    return factionRelation.kind == FactionRelationKind.Ally;
                default:
                    throw new NotSupportedException(kind.ToString());
            }
        }
    }
}