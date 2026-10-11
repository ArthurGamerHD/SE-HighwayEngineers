using System;
using LcdEyeTracking.Api;
using Sandbox.ModAPI;
using VRage.Utils;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// The optional LCD Eye Tracking API mod, which tells interactive LCD surfaces
    /// where the player looks and clicks. Without it the surfaces still render but
    /// receive no input.
    /// </summary>
    internal static class LcdEyeTracking
    {
        const long REPLY_CHANNEL = 0x48574945594501L;
        const int RETRY_FRAMES = 300;

        static LcdEyeTrackingApi _api;
        static int _nextAttemptFrame;
        static bool _reported;

        /// <summary>The API, or null while the mod is not (yet) available.</summary>
        public static LcdEyeTrackingApi Api
        {
            get
            {
                if (_api != null || MyAPIGateway.Session == null)
                    return _api;
                int frame = MyAPIGateway.Session.GameplayFrameCounter;
                if (frame < _nextAttemptFrame)
                    return null;
                // The API mod may register after the first scripts run; retry slowly.
                _nextAttemptFrame = frame + RETRY_FRAMES;
                try
                {
                    _api = AcruxClient.TryGet(REPLY_CHANNEL);
                }
                catch (Exception error)
                {
                    _api = null;
                    if (!_reported)
                        MyLog.Default.WriteLine("[HighwayEngineer] LCD Eye Tracking API: " + error.Message);
                    _reported = true;
                }
                return _api;
            }
        }

        public static void Reset()
        {
            _api = null;
            _nextAttemptFrame = 0;
            _reported = false;
        }
    }
}
