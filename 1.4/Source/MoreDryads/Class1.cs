using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace MoreDryads
{
    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            new Harmony("MoreDryads.Mod").PatchAll();
            AutoArrangeDryadPositions();
        }

        private static void AutoArrangeDryadPositions()
        {
            float xStep = 0.833f;
            float yStep = 0.1665f;
            float curY = 0;
            float curX = 0;
            var defs = DefDatabase<GauranlenTreeModeDef>.AllDefs.OrderBy(x => x.drawPosition.x).ThenBy(x => x.drawPosition.y).ToList();
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                if (i > 0 && i % 7 == 0)
                {
                    curX += xStep;
                    curY = 0;
                }
                if (def.drawPosition.x != curX)
                {
                    def.drawPosition.x = curX;
                }
                if (def.drawPosition.y != curY)
                {
                    def.drawPosition.y = curY;
                }
                curY += yStep;
            }
        }
    }

	public class CompProperties_ThingCategorySpawner : CompProperties_Spawner
	{
		public ThingCategoryDef category;

		public StuffCategoryDef stuff;
		public CompProperties_ThingCategorySpawner()
		{
			compClass = typeof(CompSpawnerSelectFromCategory);
		}
	}

	public class CompSpawnerSelectFromCategory : CompSpawner
    {
        public CompProperties_ThingCategorySpawner Props => base.props as CompProperties_ThingCategorySpawner;

        public ThingDef selectedThingDef;
		public IEnumerable<ThingDef> Candidates => Props.category.DescendantThingDefs.Where(x => x.stuffProps?.categories?.Contains(Props.stuff) ?? false);
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                selectedThingDef = Candidates.RandomElement();
            }
		}

		[HarmonyPatch(typeof(Pawn), "GetGizmos")]
		public class Pawn_GetGizmos_Patch
		{
			public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
			{
				foreach (var g in __result)
				{
					yield return g;
				}
				if (__instance.RaceProps.Dryad)
                {
					var comp = __instance.TryGetComp<CompSpawnerSelectFromCategory>();
					if (comp != null)
                    {
						yield return new Command_Action
						{
							defaultLabel = "EMD.SelectSpawnThing".Translate(),
							defaultDesc = "EMD.SelectSpawnThingDesc".Translate(),
							action = delegate
							{
								var floatList = new List<FloatMenuOption>();
								foreach (var thingDef in comp.Candidates)
								{
									floatList.Add(new FloatMenuOption(thingDef.LabelCap, delegate
									{
										comp.selectedThingDef = thingDef;
									}));
								}
								Find.WindowStack.Add(new FloatMenu(floatList));
							},
							icon = comp.parent.def.uiIcon
						};
					}
                }
			}
		}
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Defs.Look(ref selectedThingDef, "selectedThingDef");
        }

		[HarmonyPatch(typeof(CompSpawner), "TryDoSpawn")]
		public static class TryDoSpawn_Patch
		{
			public static bool Prefix(CompSpawner __instance)
			{
				if (__instance is CompSpawnerSelectFromCategory fromCategory)
				{
					fromCategory.TryDoSpawn();
					return false;
				}
				return true;
			}
		}

		public new bool TryDoSpawn()
		{
			if (!parent.Spawned)
			{
				return false;
			}
			if (PropsSpawner.spawnMaxAdjacent >= 0)
			{
				int num = 0;
				for (int i = 0; i < 9; i++)
				{
					IntVec3 c = parent.Position + GenAdj.AdjacentCellsAndInside[i];
					if (!c.InBounds(parent.Map))
					{
						continue;
					}
					List<Thing> thingList = c.GetThingList(parent.Map);
					for (int j = 0; j < thingList.Count; j++)
					{
						if (thingList[j].def == selectedThingDef)
						{
							num += thingList[j].stackCount;
							if (num >= PropsSpawner.spawnMaxAdjacent)
							{
								return false;
							}
						}
					}
				}
			}
			if (TryFindSpawnCell(parent, selectedThingDef, PropsSpawner.spawnCount, out var result))
			{
				Thing thing = ThingMaker.MakeThing(selectedThingDef);
				thing.stackCount = PropsSpawner.spawnCount;
				if (thing == null)
				{
					Log.Error("Could not spawn anything for " + parent);
				}
				if (PropsSpawner.inheritFaction && thing.Faction != parent.Faction)
				{
					thing.SetFaction(parent.Faction);
				}
				GenPlace.TryPlaceThing(thing, result, parent.Map, ThingPlaceMode.Direct, out var lastResultingThing);
				if (PropsSpawner.spawnForbidden)
				{
					lastResultingThing.SetForbidden(value: true);
				}
				if (PropsSpawner.showMessageIfOwned && parent.Faction == Faction.OfPlayer)
				{
					Messages.Message("MessageCompSpawnerSpawnedItem".Translate(selectedThingDef.LabelCap), thing, MessageTypeDefOf.PositiveEvent);
				}
				return true;
			}
			return false;
		}

		public static new bool TryFindSpawnCell(Thing parent, ThingDef thingToSpawn, int spawnCount, out IntVec3 result)
		{
			foreach (IntVec3 item in GenAdj.CellsAdjacent8Way(parent).InRandomOrder())
			{
				if (!item.Walkable(parent.Map))
				{
					continue;
				}
				Building edifice = item.GetEdifice(parent.Map);
				if (edifice != null && thingToSpawn.IsEdifice())
				{
					continue;
				}
				Building_Door building_Door = edifice as Building_Door;
				if ((building_Door != null && !building_Door.FreePassage) || (parent.def.passability != Traversability.Impassable && !GenSight.LineOfSight(parent.Position, item, parent.Map)))
				{
					continue;
				}
				bool flag = false;
				List<Thing> thingList = item.GetThingList(parent.Map);
				for (int i = 0; i < thingList.Count; i++)
				{
					Thing thing = thingList[i];
					if (thing.def.category == ThingCategory.Item && (thing.def != thingToSpawn || thing.stackCount > thingToSpawn.stackLimit - spawnCount))
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					result = item;
					return true;
				}
			}
			result = IntVec3.Invalid;
			return false;
		}
		public override string CompInspectStringExtra()
        {
            if (PropsSpawner.writeTimeLeftToSpawn)
            {
                return "NextSpawnedItemIn".Translate(GenLabel.ThingLabel(this.selectedThingDef, null, PropsSpawner.spawnCount)).Resolve() + ": " + ticksUntilSpawn.ToStringTicksToPeriod().Colorize(ColoredText.DateTimeColor);
            }
            return null;
        }
    }
}
