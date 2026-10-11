using Adk.Mvvm;
using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Binding;
using Acrux.Core.Input;
using Acrux.Styling;
using HighwayEngineer.Client.Networking;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Apps.PushRoad
{
    /// <summary>One invoice row of the push dialog; sizes are copied in for its template.</summary>
    internal sealed class InvoiceCellViewModel : ObservableObject
    {
        public string Icon { get; set; }
        public string Item { get; set; }
        public string Quantity { get; set; }
        public string UnitPrice { get; set; }
        public string Total { get; set; }
        /// <summary>Currency icon after the total (half size, bottom aligned, money colour); null for a unit suffix.</summary>
        public string TotalIcon { get; set; }
        public bool HasTotalIcon => TotalIcon != null;
        public float IconSize { get; set; }
        public float CurrencyIconSize => IconSize * 0.5f;
        public Vector4 IconMargin { get; set; }
    }
}
