using System;
using ProceduralCubemapApi.Api;

namespace HighwayEngineer.Common.Helpers
{
    internal static class RoadApi
    {
        internal static readonly Version MinimumVersion = new Version(0, 0, 17);
        // Keep the reply channel private to HighwayEngineer and distinct from cartography's channel.
        const long ReplyChannel = (0x52444352L << 8) | 3L;

        internal static bool TryValidate(out ApiProvider api, out string error)
        {
            api = null;
            error = null;
            try
            {
                api = ProceduralCubemapApiClient.TryGet(ReplyChannel);
                if (api == null)
                {
                    error = LocHelper.Message("HighwayEngineer_Api_NotFound", MinimumVersion.ToString());
                    return false;
                }

                Version version = api.GetApiVersion();
                if (version == null || version.CompareTo(MinimumVersion) < 0)
                {
                    error = version == null
                        ? LocHelper.Message("HighwayEngineer_Api_UnknownVersion", MinimumVersion.ToString())
                        : LocHelper.Message("HighwayEngineer_Api_Incompatible", version.ToString(), MinimumVersion.ToString());
                    api = null;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                api = null;
                error = LocHelper.Message("HighwayEngineer_Api_ValidateFailed", e.Message);
                return false;
            }
        }
    }
}
