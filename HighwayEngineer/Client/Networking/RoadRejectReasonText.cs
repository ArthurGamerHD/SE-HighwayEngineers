using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Client.Networking
{
    /// <summary>Player-facing wording for server rejections.</summary>
    public static class RoadRejectReasonText
    {
        public static string Describe(RoadRejectReason reason, string message, float retryAfterSeconds)
        {
            string text = string.IsNullOrWhiteSpace(message) ? Default(reason) : LocHelper.Resolve(message);
            if (retryAfterSeconds > 0f)
                text = LocHelper.Format("HighwayEngineer_Reject_RetryIn", text, System.Math.Ceiling(retryAfterSeconds));
            return text;
        }

        static string Default(RoadRejectReason reason)
        {
            switch (reason)
            {
                case RoadRejectReason.InvalidGeometry: return LocHelper.GetLoc("HighwayEngineer_Reject_InvalidGeometry");
                case RoadRejectReason.SlopeExceeded: return LocHelper.GetLoc("HighwayEngineer_Reject_SlopeExceeded");
                case RoadRejectReason.SelfCrossing: return LocHelper.GetLoc("HighwayEngineer_Reject_SelfCrossing");
                case RoadRejectReason.OutOfLimits: return LocHelper.GetLoc("HighwayEngineer_Reject_OutOfLimits");
                case RoadRejectReason.MaterialNotAllowed: return LocHelper.GetLoc("HighwayEngineer_Reject_MaterialNotAllowed");
                case RoadRejectReason.NoPermission: return LocHelper.GetLoc("HighwayEngineer_Reject_NoPermission");
                case RoadRejectReason.InsufficientCredits: return LocHelper.GetLoc("HighwayEngineer_Reject_InsufficientCredits");
                case RoadRejectReason.RateLimited: return LocHelper.GetLoc("HighwayEngineer_Reject_RateLimited");
                case RoadRejectReason.ServerBusy: return LocHelper.GetLoc("HighwayEngineer_Reject_ServerBusy");
                case RoadRejectReason.Superseded: return LocHelper.GetLoc("HighwayEngineer_Reject_Superseded");
                case RoadRejectReason.QuoteExpired: return LocHelper.GetLoc("HighwayEngineer_Reject_QuoteExpired");
                case RoadRejectReason.UnknownContract: return LocHelper.GetLoc("HighwayEngineer_Reject_UnknownContract");
                case RoadRejectReason.ApiUnavailable: return LocHelper.GetLoc("HighwayEngineer_Reject_ApiUnavailable");
                case RoadRejectReason.PushFailed: return LocHelper.GetLoc("HighwayEngineer_Reject_PushFailed");
                case RoadRejectReason.NoServiceProvider: return LocHelper.GetLoc("HighwayEngineer_Reject_NoServiceProvider");
                case RoadRejectReason.ContractRejected: return LocHelper.GetLoc("HighwayEngineer_Reject_ContractRejected");
                case RoadRejectReason.TooManyContracts: return LocHelper.GetLoc("HighwayEngineer_Reject_TooManyContracts");
                default: return LocHelper.GetLoc("HighwayEngineer_Reject_Default");
            }
        }
    }
}
