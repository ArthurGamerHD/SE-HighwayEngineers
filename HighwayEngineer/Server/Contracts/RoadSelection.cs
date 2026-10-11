using System;
using System.Collections.Generic;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Server.Helpers;
using Sandbox.ModAPI;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// Checks the built roads a player marked on the map (to bulldoze or repair): road library
    /// segments are the player's when they built them, road lines of the voxel API outside the
    /// library are checked against the API's own listing and are the player's when their
    /// modification carries one of their contracts' tags. Admins and the host may pick any.
    /// </summary>
    public sealed class RoadSelection
    {
        const string ContractTag = "HighwayEngineer:contract=";

        readonly RoadLibraryService _library;

        public RoadSelection(RoadLibraryService library)
        {
            _library = library;
        }

        /// <summary>The refusal key for <paramref name="verb"/> ("bulldoze" or "repair").</summary>
        static string VerbKey(string key, string verb)
        {
            return key + (verb == "repair" ? "_Repair" : "_Bulldoze");
        }

        /// <summary>
        /// The library segments asked for, each bridge whole. Null with the reason when one is
        /// unknown, on another planet, or not the player's; <paramref name="verb"/> ("bulldoze")
        /// words the refusal.
        /// </summary>
        public List<RoadSegmentRecord> Resolve(ulong steamId, long planetId, long[] ids, string verb,
            out RoadRejectReason reason, out string error)
        {
            reason = RoadRejectReason.InvalidGeometry;
            error = null;
            bool anyone = MayPickAny(steamId);
            long identity = MyAPIGateway.Players.TryGetIdentityId(steamId);
            var found = new List<RoadSegmentRecord>();
            var seen = new HashSet<long>();
            foreach (long id in ids)
            {
                RoadSegmentRecord segment = _library.FindSegment(id);
                if (segment == null || segment.PlanetId != planetId)
                {
                    error = LocHelper.Message("HighwayEngineer_Selection_Gone");
                    return null;
                }
                if (!anyone && segment.OwnerIdentityId != identity)
                {
                    reason = RoadRejectReason.NoPermission;
                    error = LocHelper.Message(VerbKey("HighwayEngineer_Selection_OtherOwner", verb));
                    return null;
                }
                List<RoadSegmentRecord> whole = segment.IsBridge
                    ? _library.BridgeSegments(segment.BridgeId)
                    : new List<RoadSegmentRecord> { segment };
                foreach (RoadSegmentRecord part in whole)
                    if (seen.Add(part.SegmentId))
                        found.Add(part);
            }
            reason = RoadRejectReason.None;
            return found;
        }

        /// <summary>
        /// Reads the voxel API's listing of a planet (parsed on a background thread);
        /// <paramref name="done"/> runs on the game thread with it, or null with the reason.
        /// </summary>
        public static void ReadListing(long planetId, Action<PlanetModificationListing, RoadRejectReason, string> done)
        {
            ApiProvider api;
            string error;
            string xml = null;
            try
            {
                if (RoadApi.TryValidate(out api, out error))
                    xml = api.GetPlanetModificationsXml(planetId);
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            if (xml == null)
            {
                done(null, RoadRejectReason.ApiUnavailable, error ?? LocHelper.Message("HighwayEngineer_Selection_NoListing"));
                return;
            }
            PlanetModificationListing listing = null;
            MyAPIGateway.Parallel.Start(() =>
            {
                try
                {
                    listing = MyAPIGateway.Utilities.SerializeFromXML<PlanetModificationListing>(xml);
                }
                catch (Exception)
                {
                    listing = null;
                }
            }, () =>
            {
                if (listing == null || listing.Modifications == null)
                    done(null, RoadRejectReason.ApiUnavailable, LocHelper.Message("HighwayEngineer_Selection_ListingUnreadable"));
                else
                    done(listing, RoadRejectReason.None, null);
            });
        }

        /// <summary>
        /// The listed road line <paramref name="line"/> points at, when it is still there with the
        /// geometry the player saw; null with the error otherwise. <paramref name="modification"/>
        /// gets the modification holding it.
        /// </summary>
        public static RuntimeProceduralLineOperation FindLine(PlanetModificationListing listing, RoadLineRef line,
            out PlanetModificationInfo modification, out string error)
        {
            error = null;
            modification = listing.Modifications.Find(m => m != null && m.Id == line.ModificationId);
            RuntimeProceduralLineOperation listed = modification == null || modification.Lines == null ||
                                                    line.LineIndex < 0 || line.LineIndex >= modification.Lines.Count
                ? null
                : modification.Lines[line.LineIndex];
            if (listed == null || !string.Equals(listed.Layer, "material", StringComparison.OrdinalIgnoreCase) ||
                !SameGeometry(listed.Geometry, line.Geometry))
            {
                error = LocHelper.Message("HighwayEngineer_Selection_Changed");
                return null;
            }
            return listed;
        }

        /// <summary>
        /// Checks road lines that are not in the library against the API's listing: each must
        /// still be there, as the player saw it, and be theirs. <paramref name="done"/> runs on the
        /// game thread with the lines (each once) and the listing, or null with the reason.
        /// </summary>
        public void VerifyLines(ulong steamId, long planetId, List<RoadLineRef> asked, string verb,
            Action<List<RoadLineRef>, PlanetModificationListing, RoadRejectReason, string> done)
        {
            if (asked == null || asked.Count == 0)
            {
                done(new List<RoadLineRef>(), null, RoadRejectReason.None, null);
                return;
            }
            ReadListing(planetId, (listing, reason, error) =>
            {
                if (listing == null)
                {
                    done(null, null, reason, error);
                    return;
                }
                bool anyone = MayPickAny(steamId);
                long identity = MyAPIGateway.Players.TryGetIdentityId(steamId);
                var lines = new List<RoadLineRef>();
                var seen = new HashSet<string>();
                foreach (RoadLineRef line in asked)
                {
                    if (line == null || line.ModificationId == null || !seen.Add(line.ModificationId + "#" + line.LineIndex))
                        continue;
                    PlanetModificationInfo modification;
                    string lineError;
                    if (FindLine(listing, line, out modification, out lineError) == null)
                    {
                        done(null, null, RoadRejectReason.InvalidGeometry, lineError);
                        return;
                    }
                    if (!anyone)
                    {
                        long owner = ContractOwner(modification.CustomData);
                        if (owner != identity)
                        {
                            done(null, null, RoadRejectReason.NoPermission, owner == 0
                                ? LocHelper.Message(VerbKey("HighwayEngineer_Selection_NotContract", verb))
                                : LocHelper.Message(VerbKey("HighwayEngineer_Selection_OtherOwner", verb)));
                            return;
                        }
                    }
                    lines.Add(line);
                }
                done(lines, listing, RoadRejectReason.None, null);
            });
        }

        /// <summary>The owner of the contract a modification's tag names; 0 when it has none.</summary>
        long ContractOwner(string customData)
        {
            long contractId;
            if (customData == null || !customData.StartsWith(ContractTag, StringComparison.Ordinal) ||
                !long.TryParse(customData.Substring(ContractTag.Length), out contractId))
                return 0;
            return _library.ContractOwner(contractId);
        }

        static bool MayPickAny(ulong steamId)
        {
            return ServerPlayers.IsAdmin(steamId) || ServerPlayers.IsLocal(steamId);
        }

        public static bool SameGeometry(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length != Constants.GEOMETRY_VALUES_PER_SEGMENT || b.Length != a.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i] - b[i]) > 1e-9) return false;
            return true;
        }
    }
}
