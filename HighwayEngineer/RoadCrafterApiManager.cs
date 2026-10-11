using Generated;
using HighwayEngineer.Common.Api;

namespace HighwayEngineer
{
    /// <summary>
    /// Publishes <see cref="HighwayEngineerApiServer"/> to other mods on this machine (a mod-message
    /// request on its port answers with the API). It reads the server's road library where this
    /// machine is the server, and the client's copy of it otherwise.
    /// </summary>
    [APIManager(0x5244435241500001L, Provider = nameof(_api))]
    public partial class HighwayEngineerApiManager
    {
        readonly HighwayEngineerApiServer _api = new HighwayEngineerApiServer(RouteSource);

        static IRoadRouteSource RouteSource()
        {
            if (HighwayEngineerSessionComponent.Server != null)
                return HighwayEngineerSessionComponent.Server.Library;
            return HighwayEngineerSessionComponent.Client == null ? null : HighwayEngineerSessionComponent.Client.Library;
        }
    }
}
