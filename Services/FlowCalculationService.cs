using GasPipelineOptimization.Models;

namespace GasPipelineOptimization.Services
{
    /// <summary>
    /// Service for calculating gas flow from delivery points upstream and validating feasibility
    /// </summary>
    public class FlowCalculationService
    {
        /// <summary>
        /// Calculates flow through each segment from delivery points upstream
        /// </summary>
        public FlowCalculationResult CalculateUpstreamFlow(PipelineNetwork network)
        {
            var result = new FlowCalculationResult();
            var segmentFlows = new Dictionary<string, double>();
            var visitedPoints = new HashSet<string>();
            var validationIssues = new List<string>();

            try
            {
                // Step 1: Initialize flows for all segments to zero
                foreach (var segment in network.GetActiveSegments())
                {
                    segmentFlows[segment.Id] = 0.0;
                }

                // Step 2: Start from each delivery point and trace upstream
                foreach (var deliveryPoint in network.GetDeliveryPoints())
                {
                    if (deliveryPoint.DemandRequirement > 0)
                    {
                        TraceUpstreamFlow(network, deliveryPoint.Id, deliveryPoint.DemandRequirement, 
                                        segmentFlows, new HashSet<string>());
                    }
                }

                // Step 3: Calculate usage percentages and validate capacity constraints
                foreach (var segment in network.GetActiveSegments())
                {
                    var flow = segmentFlows[segment.Id];
                    var utilizationPercent = (flow / segment.Capacity) * 100.0;
                    var isOverCapacity = flow > segment.Capacity;

                    var flowResult = new SegmentFlowAnalysis
                    {
                        SegmentId = segment.Id,
                        SegmentName = segment.Name,
                        RequiredFlow = flow,
                        Capacity = segment.Capacity,
                        UtilizationPercentage = utilizationPercent,
                        IsOverCapacity = isOverCapacity,
                        ExcessFlow = isOverCapacity ? flow - segment.Capacity : 0.0,
                        FromPointId = segment.FromPointId,
                        ToPointId = segment.ToPointId
                    };

                    result.SegmentAnalysis[segment.Id] = flowResult;

                    if (isOverCapacity)
                    {
                        validationIssues.Add($"Segment {segment.Id} ({segment.Name}): " +
                                           $"Required flow {flow:F2} MMscfd exceeds capacity {segment.Capacity:F2} MMscfd " +
                                           $"({utilizationPercent:F1}% utilization)");
                    }
                }

                // Step 4: Calculate network-wide metrics
                CalculateNetworkMetrics(network, result);

                result.ValidationIssues = validationIssues;
                result.IsNetworkFeasible = !validationIssues.Any();
                result.CalculationStatus = FlowCalculationStatus.Success;

            }
            catch (Exception ex)
            {
                result.CalculationStatus = FlowCalculationStatus.Error;
                result.ValidationIssues.Add($"Flow calculation error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Recursively traces flow upstream from a delivery point
        /// </summary>
        private void TraceUpstreamFlow(PipelineNetwork network, string currentPointId, double requiredFlow,
                                     Dictionary<string, double> segmentFlows, HashSet<string> visitedInPath)
        {
            // Prevent infinite loops in case of cycles
            if (visitedInPath.Contains(currentPointId))
            {
                return;
            }

            visitedInPath.Add(currentPointId);
            var currentPoint = network.Points[currentPointId];

            // Get all incoming segments to this point
            var incomingSegments = network.GetIncomingSegments(currentPointId).ToList();

            if (!incomingSegments.Any())
            {
                // This is likely a receipt point or isolated point
                visitedInPath.Remove(currentPointId);
                return;
            }

            // If this is a receipt point, we don't need to trace further upstream
            if (currentPoint.Type == PointType.Receipt)
            {
                // Add the required flow to all incoming segments (though there should be none for receipt points)
                foreach (var segment in incomingSegments)
                {
                    segmentFlows[segment.Id] += requiredFlow;
                }
                visitedInPath.Remove(currentPointId);
                return;
            }

            // For compressor stations and intermediate points, distribute the flow among incoming segments
            // Simple approach: distribute equally among active incoming segments
            if (incomingSegments.Count == 1)
            {
                // Single upstream path - all flow goes through this segment
                var segment = incomingSegments.First();
                segmentFlows[segment.Id] += requiredFlow;
                
                // Continue upstream
                TraceUpstreamFlow(network, segment.FromPointId, requiredFlow, segmentFlows, visitedInPath);
            }
            else
            {
                // Multiple upstream paths - need to distribute flow
                // For now, distribute proportionally based on segment capacity
                var totalUpstreamCapacity = incomingSegments.Sum(s => s.Capacity);
                
                foreach (var segment in incomingSegments)
                {
                    var proportionalFlow = (segment.Capacity / totalUpstreamCapacity) * requiredFlow;
                    segmentFlows[segment.Id] += proportionalFlow;
                    
                    // Continue upstream with the proportional flow
                    TraceUpstreamFlow(network, segment.FromPointId, proportionalFlow, segmentFlows, visitedInPath);
                }
            }

            visitedInPath.Remove(currentPointId);
        }

        /// <summary>
        /// Calculates network-wide flow metrics
        /// </summary>
        private void CalculateNetworkMetrics(PipelineNetwork network, FlowCalculationResult result)
        {
            var metrics = new NetworkFlowMetrics();

            // Calculate total demand and supply
            metrics.TotalDemandRequired = network.GetDeliveryPoints().Sum(p => p.DemandRequirement);
            metrics.TotalSupplyAvailable = network.GetReceiptPoints().Sum(p => p.SupplyCapacity);

            // Calculate utilization metrics
            var utilizationValues = result.SegmentAnalysis.Values
                .Where(s => s.Capacity > 0)
                .Select(s => s.UtilizationPercentage)
                .ToList();

            if (utilizationValues.Any())
            {
                metrics.AverageUtilization = utilizationValues.Average();
                metrics.PeakUtilization = utilizationValues.Max();
                metrics.MinimumUtilization = utilizationValues.Min();
            }

            // Count capacity violations
            metrics.SegmentsOverCapacity = result.SegmentAnalysis.Values.Count(s => s.IsOverCapacity);
            metrics.TotalSegments = result.SegmentAnalysis.Count;

            // Calculate total flow through the network
            metrics.TotalNetworkFlow = result.SegmentAnalysis.Values
                .Where(s => network.GetReceiptPoints().Any(r => r.Id == s.FromPointId))
                .Sum(s => s.RequiredFlow);

            // Calculate supply-demand balance
            metrics.SupplyDemandBalance = metrics.TotalSupplyAvailable - metrics.TotalDemandRequired;
            metrics.IsSupplyAdequate = metrics.SupplyDemandBalance >= 0;

            result.NetworkMetrics = metrics;
        }

        /// <summary>
        /// Generates a comprehensive flow analysis report with detailed tabular data
        /// </summary>
        public string GenerateDetailedFlowReport(FlowCalculationResult result, PipelineNetwork network)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("=== COMPREHENSIVE GAS PIPELINE FLOW ANALYSIS ===");
            report.AppendLine($"Analysis Status: {result.CalculationStatus}");
            report.AppendLine($"Network Feasible: {(result.IsNetworkFeasible ? "YES" : "NO")}");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // Network overview
            report.AppendLine("=== NETWORK OVERVIEW ===");
            report.AppendLine($"Total Demand Required: {result.NetworkMetrics.TotalDemandRequired:F2} MMscfd");
            report.AppendLine($"Total Supply Available: {result.NetworkMetrics.TotalSupplyAvailable:F2} MMscfd");
            report.AppendLine($"Supply-Demand Balance: {result.NetworkMetrics.SupplyDemandBalance:F2} MMscfd");
            report.AppendLine($"Supply Adequate: {(result.NetworkMetrics.IsSupplyAdequate ? "YES" : "NO")}");
            report.AppendLine();

            // KEY METRICS
            report.AppendLine("=== KEY METRICS ===");
            report.AppendLine($"Average Utilization: {result.NetworkMetrics.AverageUtilization:F1}%");
            report.AppendLine($"Peak Utilization: {result.NetworkMetrics.PeakUtilization:F1}%");
            report.AppendLine($"Minimum Utilization: {result.NetworkMetrics.MinimumUtilization:F1}%");
            report.AppendLine($"Segments Over Capacity: {result.NetworkMetrics.SegmentsOverCapacity} of {result.NetworkMetrics.TotalSegments}");
            report.AppendLine();

            // CAPACITY VIOLATIONS
            if (result.ValidationIssues.Any())
            {
                report.AppendLine("=== CAPACITY VIOLATIONS DETECTED ===");
                foreach (var issue in result.ValidationIssues)
                {
                    report.AppendLine($"⚠ {issue}");
                }
                report.AppendLine();
            }
            else
            {
                report.AppendLine("✓ All segments are within capacity limits");
                report.AppendLine();
            }

            // COMPREHENSIVE SEGMENT TABLE
            report.AppendLine("=== COMPREHENSIVE SEGMENT ANALYSIS TABLE ===");
            var headerLine = string.Format("{0,-10} {1,-25} {2,-15} {3,-15} {4,-12} {5,-12} {6,-10} {7,-12} {8,-8} {9,-15}",
                "Segment", "Name", "From Point", "To Point", "Flow", "Capacity", "Usage %", "Status", "Length", "Diameter");
            report.AppendLine(headerLine);
            report.AppendLine(new string('=', headerLine.Length));

            foreach (var analysis in result.SegmentAnalysis.Values.OrderByDescending(s => s.UtilizationPercentage))
            {
                var networkSegment = network.Segments[analysis.SegmentId];
                var status = analysis.IsOverCapacity ? "OVER CAP" : "OK";
                
                var line = string.Format("{0,-10} {1,-25} {2,-15} {3,-15} {4,-12:F2} {5,-12:F2} {6,-10:F1} {7,-12} {8,-8:F1} {9,-15:F0}\"",
                    analysis.SegmentId,
                    analysis.SegmentName.Length > 25 ? analysis.SegmentName.Substring(0, 22) + "..." : analysis.SegmentName,
                    analysis.FromPointId,
                    analysis.ToPointId,
                    analysis.RequiredFlow,
                    analysis.Capacity,
                    analysis.UtilizationPercentage,
                    status,
                    networkSegment.Length,
                    networkSegment.Diameter);
                
                report.AppendLine(line);
            }
            report.AppendLine();

            // TOP MOST UTILIZED SEGMENTS
            var topSegments = result.SegmentAnalysis.Values
                .OrderByDescending(s => s.UtilizationPercentage)
                .Take(5)
                .ToList();

            if (topSegments.Any())
            {
                report.AppendLine("=== TOP 5 MOST UTILIZED SEGMENTS ===");
                report.AppendLine(string.Format("{0,-10} {1,-12} {2,-12} {3,-12} {4,-10}",
                    "Segment", "Flow", "Capacity", "Utilization", "Status"));
                report.AppendLine(new string('-', 55));
                
                foreach (var segment in topSegments)
                {
                    var status = segment.IsOverCapacity ? "OVER CAP" : "OK";
                    report.AppendLine(string.Format("{0,-10} {1,-12:F2} {2,-12:F2} {3,-12:F1}% {4,-10}",
                        segment.SegmentId,
                        segment.RequiredFlow,
                        segment.Capacity,
                        segment.UtilizationPercentage,
                        status));
                }
                report.AppendLine();
            }

            // RECEIPT POINTS ANALYSIS
            var receiptPoints = network.GetReceiptPoints().ToList();
            if (receiptPoints.Any())
            {
                report.AppendLine("=== RECEIPT POINTS (SUPPLY SOURCES) ===");
                report.AppendLine(string.Format("{0,-8} {1,-25} {2,-15} {3,-15} {4,-12}",
                    "Point", "Name", "Supply Cap.", "Current Press.", "Unit Cost"));
                report.AppendLine(new string('-', 80));
                
                foreach (var point in receiptPoints.OrderBy(p => p.Id))
                {
                    report.AppendLine(string.Format("{0,-8} {1,-25} {2,-15:F2} {3,-15:F1} {4,-12:F2}",
                        point.Id,
                        point.Name.Length > 25 ? point.Name.Substring(0, 22) + "..." : point.Name,
                        point.SupplyCapacity,
                        point.CurrentPressure,
                        point.UnitCost));
                }
                report.AppendLine();
            }

            // DELIVERY POINTS ANALYSIS
            var deliveryPoints = network.GetDeliveryPoints().ToList();
            if (deliveryPoints.Any())
            {
                report.AppendLine("=== DELIVERY POINTS (DEMAND LOCATIONS) ===");
                report.AppendLine(string.Format("{0,-8} {1,-25} {2,-15} {3,-15} {4,-12}",
                    "Point", "Name", "Demand Req.", "Current Press.", "Min Press."));
                report.AppendLine(new string('-', 80));
                
                foreach (var point in deliveryPoints.OrderBy(p => p.Id))
                {
                    report.AppendLine(string.Format("{0,-8} {1,-25} {2,-15:F2} {3,-15:F1} {4,-12:F1}",
                        point.Id,
                        point.Name.Length > 25 ? point.Name.Substring(0, 22) + "..." : point.Name,
                        point.DemandRequirement,
                        point.CurrentPressure,
                        point.MinPressure));
                }
                report.AppendLine();
            }

            return report.ToString();
        }

        /// <summary>
        /// Generates the original simplified flow report
        /// </summary>
        public string GenerateFlowReport(FlowCalculationResult result)
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("=== Gas Pipeline Flow Analysis Report ===");
            report.AppendLine($"Analysis Status: {result.CalculationStatus}");
            report.AppendLine($"Network Feasible: {(result.IsNetworkFeasible ? "YES" : "NO")}");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // Network overview
            report.AppendLine("=== Network Overview ===");
            report.AppendLine($"Total Demand Required: {result.NetworkMetrics.TotalDemandRequired:F2} MMscfd");
            report.AppendLine($"Total Supply Available: {result.NetworkMetrics.TotalSupplyAvailable:F2} MMscfd");
            report.AppendLine($"Supply-Demand Balance: {result.NetworkMetrics.SupplyDemandBalance:F2} MMscfd");
            report.AppendLine($"Supply Adequate: {(result.NetworkMetrics.IsSupplyAdequate ? "YES" : "NO")}");
            report.AppendLine();

            // Utilization summary
            report.AppendLine("=== Utilization Summary ===");
            report.AppendLine($"Average Utilization: {result.NetworkMetrics.AverageUtilization:F1}%");
            report.AppendLine($"Peak Utilization: {result.NetworkMetrics.PeakUtilization:F1}%");
            report.AppendLine($"Minimum Utilization: {result.NetworkMetrics.MinimumUtilization:F1}%");
            report.AppendLine($"Segments Over Capacity: {result.NetworkMetrics.SegmentsOverCapacity} of {result.NetworkMetrics.TotalSegments}");
            report.AppendLine();

            // Capacity violations
            if (result.ValidationIssues.Any())
            {
                report.AppendLine("=== CAPACITY VIOLATIONS ===");
                foreach (var issue in result.ValidationIssues)
                {
                    report.AppendLine($"⚠ {issue}");
                }
                report.AppendLine();
            }

            // Detailed segment analysis
            report.AppendLine("=== Detailed Segment Analysis ===");
            report.AppendLine("Segment".PadRight(15) + "Flow".PadRight(12) + "Capacity".PadRight(12) + 
                            "Utilization".PadRight(12) + "Status".PadRight(10) + "Route");
            report.AppendLine(new string('-', 75));

            foreach (var analysis in result.SegmentAnalysis.Values.OrderByDescending(s => s.UtilizationPercentage))
            {
                var segmentId = analysis.SegmentId.PadRight(15);
                var flow = analysis.RequiredFlow.ToString("F2").PadRight(12);
                var capacity = analysis.Capacity.ToString("F2").PadRight(12);
                var utilization = $"{analysis.UtilizationPercentage:F1}%".PadRight(12);
                var status = (analysis.IsOverCapacity ? "OVER CAP" : "OK").PadRight(10);
                var route = $"{analysis.FromPointId} → {analysis.ToPointId}";

                report.AppendLine($"{segmentId}{flow}{capacity}{utilization}{status}{route}");
            }

            return report.ToString();
        }
    }

    /// <summary>
    /// Result of flow calculation analysis
    /// </summary>
    public class FlowCalculationResult
    {
        public FlowCalculationStatus CalculationStatus { get; set; } = FlowCalculationStatus.NotCalculated;
        public Dictionary<string, SegmentFlowAnalysis> SegmentAnalysis { get; set; } = new();
        public NetworkFlowMetrics NetworkMetrics { get; set; } = new();
        public List<string> ValidationIssues { get; set; } = new();
        public bool IsNetworkFeasible { get; set; } = false;
    }

    /// <summary>
    /// Status of flow calculation
    /// </summary>
    public enum FlowCalculationStatus
    {
        NotCalculated,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Analysis results for a specific segment
    /// </summary>
    public class SegmentFlowAnalysis
    {
        public string SegmentId { get; set; } = string.Empty;
        public string SegmentName { get; set; } = string.Empty;
        public string FromPointId { get; set; } = string.Empty;
        public string ToPointId { get; set; } = string.Empty;
        public double RequiredFlow { get; set; }
        public double Capacity { get; set; }
        public double UtilizationPercentage { get; set; }
        public bool IsOverCapacity { get; set; }
        public double ExcessFlow { get; set; }
    }

    /// <summary>
    /// Network-wide flow metrics
    /// </summary>
    public class NetworkFlowMetrics
    {
        public double TotalDemandRequired { get; set; }
        public double TotalSupplyAvailable { get; set; }
        public double SupplyDemandBalance { get; set; }
        public bool IsSupplyAdequate { get; set; }
        public double AverageUtilization { get; set; }
        public double PeakUtilization { get; set; }
        public double MinimumUtilization { get; set; }
        public int SegmentsOverCapacity { get; set; }
        public int TotalSegments { get; set; }
        public double TotalNetworkFlow { get; set; }
    }
}