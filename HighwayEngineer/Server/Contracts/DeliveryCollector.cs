using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IMyInventory = VRage.Game.ModAPI.IMyInventory;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// Takes a contract's item from inventories inside its delivery box: blocks whose inventory
    /// the owner may open (owned by them, shared with their faction, or unowned) and the owner's
    /// own character. Other players' ships parked in the box are never drawn from.
    /// </summary>
    public static class DeliveryCollector
    {
        static readonly List<IMySlimBlock> Blocks = new List<IMySlimBlock>();

        /// <summary>Removes up to <paramref name="wanted"/> of the item; returns the amount taken.</summary>
        public static double Collect(DeliveryZone zone, long ownerIdentityId, IMyCharacter ownerCharacter,
            string itemType, string itemSubtype, double wanted)
        {
            if (zone == null || wanted <= 0.0) return 0.0;
            MyDefinitionId id;
            if (!MyDefinitionId.TryParse(itemType, itemSubtype, out id)) return 0.0;
            MyOrientedBoundingBoxD box = zone.ToOrientedBox();
            var sphere = new BoundingSphereD(box.Center, box.HalfExtent.Length());
            double taken = 0.0;
            foreach (IMyEntity entity in MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere))
            {
                if (taken >= wanted) break;
                var grid = entity as IMyCubeGrid;
                if (grid != null)
                {
                    taken += CollectFromGrid(grid, box, ownerIdentityId, id, wanted - taken);
                    continue;
                }
                Vector3D position = entity.GetPosition();
                if (ownerCharacter != null && ReferenceEquals(entity, ownerCharacter) && box.Contains(ref position))
                    taken += Take(entity.GetInventory(0) as IMyInventory, id, wanted - taken);
            }
            return taken;
        }

        static double CollectFromGrid(IMyCubeGrid grid, MyOrientedBoundingBoxD box, long owner, MyDefinitionId id,
            double wanted)
        {
            Blocks.Clear();
            grid.GetBlocks(Blocks, slim => slim.FatBlock != null && slim.FatBlock.HasInventory);
            double taken = 0.0;
            foreach (IMySlimBlock slim in Blocks)
            {
                if (taken >= wanted) break;
                IMyCubeBlock block = slim.FatBlock;
                Vector3D position = block.GetPosition();
                if (!box.Contains(ref position) || !MayAccess(block, owner)) continue;
                for (int i = 0; i < block.InventoryCount && taken < wanted; i++)
                    taken += Take(block.GetInventory(i) as IMyInventory, id, wanted - taken);
            }
            Blocks.Clear();
            return taken;
        }

        static bool MayAccess(IMyCubeBlock block, long owner)
        {
            MyRelationsBetweenPlayerAndBlock relation = block.GetUserRelationToOwner(owner);
            return relation == MyRelationsBetweenPlayerAndBlock.Owner ||
                   relation == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                   relation == MyRelationsBetweenPlayerAndBlock.NoOwnership;
        }

        static double Take(IMyInventory inventory, MyDefinitionId id, double wanted)
        {
            if (inventory == null || wanted <= 0.0) return 0.0;
            double available = (double)inventory.GetItemAmount(new MyItemType(id.TypeId.ToString(), id.SubtypeName));
            double amount = Math.Min(available, wanted);
            if (amount <= 0.0) return 0.0;
            inventory.RemoveItemsOfType((MyFixedPoint)amount, id);
            return amount;
        }
    }
}
