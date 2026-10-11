using Adk.Mvvm;
using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Binding;
using Acrux.Core.Input;
using Acrux.Styling;
using HighwayEngineer.Client.Apps.BridgeSettings;
using HighwayEngineer.Client.Apps.RoadPlanner;
using HighwayEngineer.Client.Gui;
using HighwayEngineer.Client.Networking;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Apps.PushRoad
{
    /// <summary>
    /// State of the push confirmation: the draft's road settings, its cost as an invoice
    /// (local estimate first, then the server's quote) and whether Push is allowed.
    /// </summary>
    internal sealed class PushRoadDialogViewModel : ObservableObject
    {
        const string CreditsIcon = "HighwayEngineer_Icon_SpaceCredits";
        const string GravelIcon = "HighwayEngineer_Icon_Gravel";
        // The game's own sprite for Iron Ingot, which "Steel Rebar" is delivered as.
        const string RebarIcon = "MyObjectBuilder_Ingot/Iron";
        // The Concrete Tool mod's Concrete Mix, which bridge concrete is delivered as with that mod.
        const string ConcreteMixIcon = "MyObjectBuilder_AmmoMagazine/ConcreteMix";

        string _summary;
        string _widthText;
        string _depthText;
        string _slopeText;
        string _distanceText;
        string _nodesText;
        string _embankmentText;
        bool _hasEmbankment;
        string _lengthText;
        string _material;
        string _materialName;
        bool _isBridge;
        bool _isRoad;
        string _designText;
        string _structureMaterial;
        string _structureMaterialName;
        string _quoteStatus;
        DateTime _validUntil;
        bool _hasValidUntil;
        bool _canPush = true;
        IReadOnlyList<InvoiceCellViewModel> _lines = Array.Empty<InvoiceCellViewModel>();
        RoadPlannerViewModel _planner;
        BackgroundBrush _pushBackground;
        float _dialogWidth = 560f;
        float _scale = 1f;
        int _quoteRequest;
        int _session;

        public PushRoadDialogViewModel()
        {
            PushCommand = new DelegateCommand(_ => AcceptContract(), _ => CanPush);
            CancelCommand = new DelegateCommand(_ => Finish(false));
        }

        /// <summary>Raised with true for Push and false for Cancel.</summary>
        public Action<bool> Closed { get; set; }

        /// <summary>Raised when the server accepted the road contract (before the dialog closes).</summary>
        public Action<RoadContractInfo> Accepted { get; set; }

        long _quoteId;
        string _providerName;
        string _providerStation;
        string _providerIcon;
        BackgroundBrush _providerIconBrush;
        BackgroundBrush _providerBackground;
        bool _hasProvider;

        /// <summary>"[TAG] Faction name" of the faction taking the contract.</summary>
        public string ProviderName { get { return _providerName; } private set { RaiseAndSetIfChanged(ref _providerName, value, nameof(ProviderName)); } }
        public string ProviderStation { get { return _providerStation; } private set { RaiseAndSetIfChanged(ref _providerStation, value, nameof(ProviderStation)); } }
        /// <summary>Faction logo texture path (a mask, tinted with <see cref="ProviderIconBrush"/>).</summary>
        public string ProviderIcon { get { return _providerIcon; } private set { RaiseAndSetIfChanged(ref _providerIcon, value, nameof(ProviderIcon)); } }
        public BackgroundBrush ProviderIconBrush { get { return _providerIconBrush; } private set { RaiseAndSetIfChanged(ref _providerIconBrush, value, nameof(ProviderIconBrush)); } }
        public BackgroundBrush ProviderBackground { get { return _providerBackground; } private set { RaiseAndSetIfChanged(ref _providerBackground, value, nameof(ProviderBackground)); } }
        public bool HasProvider { get { return _hasProvider; } private set { RaiseAndSetIfChanged(ref _hasProvider, value, nameof(HasProvider)); } }

        public ICommand PushCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        public string Summary { get { return _summary; } private set { RaiseAndSetIfChanged(ref _summary, value, nameof(Summary)); } }
        public string WidthText { get { return _widthText; } private set { RaiseAndSetIfChanged(ref _widthText, value, nameof(WidthText)); } }
        public string DepthText { get { return _depthText; } private set { RaiseAndSetIfChanged(ref _depthText, value, nameof(DepthText)); } }
        public string SlopeText { get { return _slopeText; } private set { RaiseAndSetIfChanged(ref _slopeText, value, nameof(SlopeText)); } }
        public string DistanceText { get { return _distanceText; } private set { RaiseAndSetIfChanged(ref _distanceText, value, nameof(DistanceText)); } }
        /// <summary>Cut/fill side angle; shown only when cut/fill sides are enabled.</summary>
        public string EmbankmentText { get { return _embankmentText; } private set { RaiseAndSetIfChanged(ref _embankmentText, value, nameof(EmbankmentText)); } }
        public bool HasEmbankment { get { return _hasEmbankment; } private set { RaiseAndSetIfChanged(ref _hasEmbankment, value, nameof(HasEmbankment)); } }
        /// <summary>Number of nodes in the draft.</summary>
        public string NodesText { get { return _nodesText; } private set { RaiseAndSetIfChanged(ref _nodesText, value, nameof(NodesText)); } }
        /// <summary>Length along the road's curves; the estimate's, then the server's.</summary>
        public string LengthText { get { return _lengthText; } private set { RaiseAndSetIfChanged(ref _lengthText, value, nameof(LengthText)); } }
        /// <summary>Voxel material subtype; also drives the thumbnail.</summary>
        public string Material { get { return _material; } private set { RaiseAndSetIfChanged(ref _material, value, nameof(Material)); } }
        /// <summary>What the invoice shows for <see cref="Material"/>.</summary>
        public string MaterialName { get { return _materialName; } private set { RaiseAndSetIfChanged(ref _materialName, value, nameof(MaterialName)); } }
        /// <summary>The draft has bridges: shows their style and structure material.</summary>
        public bool IsBridge { get { return _isBridge; } private set { RaiseAndSetIfChanged(ref _isBridge, value, nameof(IsBridge)); } }
        /// <summary>The draft has segments built as road: shows the road-only settings.</summary>
        public bool IsRoad { get { return _isRoad; } private set { RaiseAndSetIfChanged(ref _isRoad, value, nameof(IsRoad)); } }
        /// <summary>The bridge style asked for, then the design the server chose.</summary>
        public string DesignText { get { return _designText; } private set { RaiseAndSetIfChanged(ref _designText, value, nameof(DesignText)); } }
        /// <summary>The bridge's structure voxel material; also drives its thumbnail.</summary>
        public string StructureMaterial { get { return _structureMaterial; } private set { RaiseAndSetIfChanged(ref _structureMaterial, value, nameof(StructureMaterial)); } }
        /// <summary>What the invoice shows for <see cref="StructureMaterial"/>.</summary>
        public string StructureMaterialName { get { return _structureMaterialName; } private set { RaiseAndSetIfChanged(ref _structureMaterialName, value, nameof(StructureMaterialName)); } }
        public string QuoteStatus { get { return _quoteStatus; } private set { RaiseAndSetIfChanged(ref _quoteStatus, value, nameof(QuoteStatus)); } }
        /// <summary>Session game time the server quote expires; shown through the "gameDateTime" converter.</summary>
        public DateTime ValidUntil { get { return _validUntil; } private set { RaiseAndSetIfChanged(ref _validUntil, value, nameof(ValidUntil)); } }
        public bool HasValidUntil { get { return _hasValidUntil; } private set { RaiseAndSetIfChanged(ref _hasValidUntil, value, nameof(HasValidUntil)); } }
        public bool CanPush { get { return _canPush; } private set { RaiseAndSetIfChanged(ref _canPush, value, nameof(CanPush)); } }
        public IReadOnlyList<InvoiceCellViewModel> Lines { get { return _lines; } private set { RaiseAndSetIfChanged(ref _lines, value, nameof(Lines)); } }
        /// <summary>The faction colour, like the ✓ button.</summary>
        public BackgroundBrush PushBackground { get { return _pushBackground; } private set { RaiseAndSetIfChanged(ref _pushBackground, value, nameof(PushBackground)); } }

        // Layout, scaled with the planner.
        public float DialogWidth { get { return _dialogWidth; } private set { RaiseAndSetIfChanged(ref _dialogWidth, value, nameof(DialogWidth)); } }
        public float Scale { get { return _scale; } private set { RaiseAndSetIfChanged(ref _scale, value, nameof(Scale)); } }
        public float Spacing => 6f * _scale;
        public Vector4 Padding => new Vector4(12f * _scale);
        public float IconSize => 22f * _scale;
        public float ProviderLogoSize => 36f * _scale;
        public Vector4 ProviderLogoRadius => new Vector4(18f * _scale);
        public Vector4 ProviderLogoInset => new Vector4(6f * _scale);
        /// <summary>Gap between road-setting items in the wrap panel.</summary>
        public float DetailSpacing => 14f * _scale;
        public Vector4 IconMargin => new Vector4(0f, 2f * _scale, 6f * _scale, 2f * _scale);
        public float ButtonWidth => 110f * _scale;
        public float ButtonHeight => 30f * _scale;
        public Vector4 InvoicePadding => new Vector4(8f * _scale, 4f * _scale, 8f * _scale, 6f * _scale);
        public float InvoiceIconSize => 20f * _scale;
        public Vector4 InvoiceIconMargin => new Vector4(0f, 2f * _scale, 8f * _scale, 2f * _scale);
        public Vector4 RuleMargin => new Vector4(0f, 3f * _scale, 0f, 3f * _scale);

        public void ApplyScale(float width, float scale)
        {
            DialogWidth = width;
            if (!RaiseAndSetIfChanged(ref _scale, scale, nameof(Scale))) return;
            RaisePropertyChanged<float>(nameof(Spacing));
            RaisePropertyChanged<Vector4>(nameof(Padding));
            RaisePropertyChanged<float>(nameof(IconSize));
            RaisePropertyChanged<float>(nameof(ProviderLogoSize));
            RaisePropertyChanged<Vector4>(nameof(ProviderLogoRadius));
            RaisePropertyChanged<Vector4>(nameof(ProviderLogoInset));
            RaisePropertyChanged<float>(nameof(DetailSpacing));
            RaisePropertyChanged<Vector4>(nameof(IconMargin));
            RaisePropertyChanged<float>(nameof(ButtonWidth));
            RaisePropertyChanged<float>(nameof(ButtonHeight));
            RaisePropertyChanged<Vector4>(nameof(InvoicePadding));
            RaisePropertyChanged<float>(nameof(InvoiceIconSize));
            RaisePropertyChanged<Vector4>(nameof(InvoiceIconMargin));
            RaisePropertyChanged<Vector4>(nameof(RuleMargin));
        }

        /// <summary>Fills the dialog for the current draft and requests the server quote.</summary>
        public void Show(RoadPlannerViewModel viewModel, Color accent)
        {
            _planner = viewModel;
            _session++;
            CancelQuote();
            PushBackground = new SolidColorBrush(accent);
            // Push accepts the server's quote, so it waits for one.
            CanPush = false;
            _quoteId = 0;
            HasValidUntil = false;
            HasProvider = false;
            IsBridge = viewModel.DraftHasBridges;
            IsRoad = viewModel.DraftHasRoads;
            Summary = LocHelper.GetLoc(!IsRoad ? "HighwayEngineer_PushRoad_Summary_Bridge"
                : IsBridge ? "HighwayEngineer_PushRoad_Summary_RoadAndBridge"
                : "HighwayEngineer_PushRoad_Summary_Road");
            WidthText = FormatAmount(viewModel.RoadWidthMeters) + " m";
            // Depth steps by 0.1 m, so keep its decimal.
            DepthText = viewModel.RoadMaterialDepthMeters.ToString("#,0.#", CultureInfo.CurrentCulture) + " m";
            SlopeText = FormatAmount(viewModel.RoadMaximumSlopeDegrees) + "°";
            NodesText = LocHelper.Format("HighwayEngineer_PushRoad_NodeCount", viewModel.RoadNodes.Count);
            HasEmbankment = IsRoad && viewModel.RoadFillShoulders;
            EmbankmentText = FormatAmount(viewModel.RoadFillAngleDegrees) + "°";
            LengthText = "—";
            BridgeSettingsViewModel bridge = viewModel.BridgeSettings;
            DesignText = BridgeSettingsViewModel.DesignName(bridge.Design);
            StructureMaterial = bridge.StructureMaterial;
            StructureMaterialName = VoxelMaterialNames.Get(StructureMaterial);
            DistanceText = FormatAmount(IsRoad ? viewModel.RoadSegmentDistanceMeters : bridge.SegmentMeters) + " m";
            Material = IsRoad ? viewModel.RoadMaterialSubtype : bridge.PavementMaterial;
            MaterialName = VoxelMaterialNames.Get(Material);
            ShowEstimate(viewModel);

            PacketRoadQuoteRequest request;
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (client == null || !viewModel.TryBuildRoadQuoteRequest(out request))
            {
                QuoteStatus = LocHelper.GetLoc("HighwayEngineer_PushRoad_QuoteUnavailable");
                return;
            }
            QuoteStatus = LocHelper.GetLoc("HighwayEngineer_PushRoad_QuoteRequesting");
            int session = _session;
            _quoteRequest = client.RequestRoadQuote(request, response =>
            {
                if (session != _session) return;
                _quoteRequest = 0;
                ShowQuote(response);
            });
        }

        /// <summary>Stops waiting for the quote; call when the dialog is dismissed any other way.</summary>
        public void Dismiss()
        {
            _session++;
            CancelQuote();
        }

        void Finish(bool push)
        {
            Dismiss();
            if (Closed != null)
                Closed(push);
        }

        void CancelQuote()
        {
            if (_quoteRequest != 0 && HighwayEngineerSessionComponent.Client != null)
                HighwayEngineerSessionComponent.Client.CancelRoadQuote(_quoteRequest);
            _quoteRequest = 0;
        }

        void ShowEstimate(RoadPlannerViewModel viewModel)
        {
            // Only the server can plan a bridge, so the estimate covers the road alone.
            var lines = new List<InvoiceCellViewModel>();
            double cut, fill, length;
            RoadCostSettings prices = RoadCostSettings.Load();
            if (IsRoad && viewModel.TryEstimateRoadEarthwork(out cut, out fill, out length))
            {
                double moved = cut + fill;
                double material = length * viewModel.RoadWidthMeters * viewModel.RoadMaterialDepthMeters;
                LengthText = FormatLength(length);
                lines.Add(EarthMovementLine(moved, prices.EarthMovementCreditsPerCubicMeter,
                    moved * prices.EarthMovementCreditsPerCubicMeter));
                lines.Add(PavementLine(material, prices.RoadMaterialGravelKgPerCubicMeter,
                    material * prices.RoadMaterialGravelKgPerCubicMeter));
            }
            if (IsBridge)
                lines.Add(Line(CreditsIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_BridgeStructure"), "—", "—",
                    LocHelper.GetLoc("HighwayEngineer_Invoice_ServerQuote"), null));
            if (lines.Count > 0)
                lines.Add(InfrastructureLine(prices.RoadInfrastructureFeeCredits));
            Lines = lines;
        }

        void ShowQuote(PacketRoadQuoteResponse response)
        {
            if (response.Reason != RoadRejectReason.None || response.Costs == null)
            {
                QuoteStatus = LocHelper.Format("HighwayEngineer_PushRoad_QuoteRejected", RoadRejectReasonText.Describe(
                    response.Reason, response.Message, response.RetryAfterSeconds));
                CanPush = false;
                return;
            }

            RoadCostBreakdown costs = response.Costs;
            _quoteId = response.QuoteId;
            // The map highlights the depot the server picked for this draft.
            if (_planner != null)
                _planner.SetQuotedDepot(response.DepotGridId);
            CanPush = true;
            ShowProvider(response.Provider);
            LengthText = FormatLength(costs.LengthMeters);
            InvoiceCellViewModel pavement = costs.ItemSubtype == "Stone"
                ? PavementLine(costs.MaterialCubicMeters, costs.ItemPerCubicMeter, costs.ItemAmount)
                : Line(null, LocHelper.GetLoc("HighwayEngineer_Invoice_Pavement"), FormatQuantity(costs.MaterialCubicMeters),
                    "—", LocHelper.GetLoc("HighwayEngineer_Invoice_NotCharged"), null);
            var lines = new List<InvoiceCellViewModel>();
            // Bridges' approach ramps are fill too, so a bridge alone may move earth.
            if (IsRoad || costs.CutCubicMeters + costs.FillCubicMeters > 0.0)
                lines.Add(EarthMovementLine(costs.CutCubicMeters + costs.FillCubicMeters, costs.CreditsPerCubicMeter,
                    costs.EarthMovementCredits));
            lines.Add(pavement);
            if (costs.BridgeCount > 0)
            {
                ShowBridgeDesign(costs);
                if (costs.Item3Amount > 0.0)
                    lines.Add(Line(ConcreteMixIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_BridgeStructure"),
                        FormatQuantity(costs.StructureCubicMeters), FormatPrice(costs.Item3PerCubicMeter) + "/m³",
                        FormatAmount(costs.Item3Amount) + " " + ItemNames.Display(costs.Item3Type, costs.Item3Subtype,
                            costs.Item3Name), null));
                else
                    lines.Add(Line(CreditsIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_BridgeStructure"), FormatQuantity(costs.StructureCubicMeters),
                        FormatPrice(costs.StructureCreditsPerCubicMeter) + " SC/m³", FormatAmount(costs.StructureCredits),
                        CreditsIcon));
                lines.Add(Line(RebarIcon, LocHelper.Resolve(costs.Item2Name) ?? LocHelper.GetLoc("HighwayEngineer_Item_SteelRebar"),
                    FormatQuantity(costs.StructureCubicMeters),
                    FormatPrice(costs.Item2PerCubicMeter) + " kg/m³", FormatAmount(costs.Item2Amount) + " kg", null));
            }
            if (costs.InfrastructureCredits > 0)
                lines.Add(InfrastructureLine(costs.InfrastructureCredits));
            else if (costs.ReusesDepot)
                lines.Add(Line(CreditsIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_Infrastructure"),
                    LocHelper.GetLoc("HighwayEngineer_Invoice_ExistingDepot"),
                    "—", "0", CreditsIcon));
            Lines = lines;
            ValidUntil = MyAPIGateway.Session.GameDateTime + TimeSpan.FromSeconds(response.ExpiresSeconds);
            HasValidUntil = true;
            QuoteStatus = LocHelper.Format(costs.Exempt ? "HighwayEngineer_PushRoad_InvoiceExempt" : "HighwayEngineer_PushRoad_Invoice",
                response.QuoteId, costs.CreditsUpfront);
        }

        /// <summary>The design the server chose for the bridges (Auto: they differ), and how many there are.</summary>
        void ShowBridgeDesign(RoadCostBreakdown costs)
        {
            string chosen = costs.Design == BridgeDesign.Auto
                ? LocHelper.GetLoc("HighwayEngineer_PushRoad_DesignMixed")
                : BridgeSettingsViewModel.DesignName(costs.Design);
            if (costs.Design != BridgeDesign.Auto && _planner != null && _planner.BridgeSettings.Design == BridgeDesign.Auto)
                chosen = LocHelper.Format("HighwayEngineer_PushRoad_DesignCheapest", chosen);
            DesignText = costs.BridgeCount > 1
                ? LocHelper.Format("HighwayEngineer_PushRoad_DesignBridges", chosen, costs.BridgeCount)
                : chosen;
        }

        void ShowProvider(RoadServiceProvider provider)
        {
            HasProvider = provider != null;
            if (provider == null) return;
            GameIconSprites.Ensure(provider.Icon);
            ProviderName = string.IsNullOrWhiteSpace(provider.FactionTag)
                ? provider.FactionName
                : "[" + provider.FactionTag + "] " + provider.FactionName;
            ProviderStation = provider.StationName;
            ProviderIcon = provider.Icon;
            ProviderIconBrush = new SolidColorBrush(provider.IconColor);
            ProviderBackground = new SolidColorBrush(provider.BackgroundColor);
        }

        /// <summary>Push: turns the quote into a road contract on the server.</summary>
        void AcceptContract()
        {
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (_quoteId == 0 || client == null) return;
            CanPush = false;
            QuoteStatus = LocHelper.GetLoc("HighwayEngineer_PushRoad_Accepting");
            int session = _session;
            client.AcceptRoadContract(_quoteId, status =>
            {
                if (session != _session) return;
                if (status.Reason != RoadRejectReason.None || status.Contract == null)
                {
                    // The quote is used up either way; reopening the plan asks for a new one.
                    QuoteStatus = LocHelper.Format("HighwayEngineer_PushRoad_NotAccepted", RoadRejectReasonText.Describe(
                        status.Reason, status.Message, status.RetryAfterSeconds));
                    return;
                }
                if (Accepted != null)
                    Accepted(status.Contract);
                Finish(true);
            });
        }

        InvoiceCellViewModel EarthMovementLine(double cubicMeters, double creditsPerCubicMeter, double credits)
        {
            return Line(CreditsIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_EarthMovement"), FormatAmount(cubicMeters) + " m³",
                FormatPrice(creditsPerCubicMeter) + " SC/m³", FormatAmount(credits), CreditsIcon);
        }

        /// <summary>The fixed fee for the contract's Road Logistic Depot.</summary>
        InvoiceCellViewModel InfrastructureLine(long credits)
        {
            return Line(CreditsIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_Infrastructure"), "1",
                FormatAmount(credits) + " SC", FormatAmount(credits), CreditsIcon);
        }

        InvoiceCellViewModel PavementLine(double cubicMeters, double kilogramsPerCubicMeter,
            double kilograms)
        {
            return Line(GravelIcon, LocHelper.GetLoc("HighwayEngineer_Invoice_Pavement"), FormatQuantity(cubicMeters),
                FormatPrice(kilogramsPerCubicMeter) + " kg/m³", FormatAmount(kilograms) + " kg", null);
        }

        InvoiceCellViewModel Line(string icon, string item, string quantity, string unitPrice, string total,
            string totalIcon)
        {
            return new InvoiceCellViewModel
            {
                Icon = icon,
                Item = item,
                Quantity = quantity,
                UnitPrice = unitPrice,
                Total = total,
                TotalIcon = totalIcon,
                IconSize = InvoiceIconSize,
                IconMargin = InvoiceIconMargin
            };
        }

        static string FormatQuantity(double cubicMeters) => FormatAmount(cubicMeters) + " m³";

        static string FormatLength(double meters)
        {
            return meters >= 1000.0
                ? (meters / 1000.0).ToString("0.##", CultureInfo.CurrentCulture) + " km"
                : FormatAmount(meters) + " m";
        }

        /// <summary>Unit prices are small (e.g. 0.006 kg/m³), so keep their significant decimals.</summary>
        static string FormatPrice(double value)
        {
            return value.ToString("#,0.######", CultureInfo.CurrentCulture);
        }

        static string FormatAmount(double value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
