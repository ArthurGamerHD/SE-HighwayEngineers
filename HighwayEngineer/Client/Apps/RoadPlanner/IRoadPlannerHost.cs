using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Controls.Chrome;
using Acrux.Controls.Primitives;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Styling;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    /// <summary>What the road planner needs from whatever shows it.</summary>
    internal interface IRoadPlannerHost
    {
        /// <summary>False on LCDs: no close chrome, and no separate settings screen.</summary>
        bool IsFullScreen { get; }
        void Close();
        void CloseNow();
        void OpenSettings();
    }
}
