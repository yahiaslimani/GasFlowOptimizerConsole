namespace GasPipelineOptimization.Models
{
    /// <summary>
    /// Status of the optimization process
    /// </summary>
    public enum OptimizationStatus
    {
        NotSolved,
        Optimal,
        Feasible,
        Infeasible,
        Unbounded,
        Error
    }

    /// <summary>
    /// Result of flow optimization for a specific segment
    /// </summary>
    public class SegmentFlowResult
    {
        /// <summary>
        /// Segment identifier
        /// </summary>
        public string SegmentId { get; set; } = string.Empty;

        /// <summary>
        /// Optimized flow through the segment (MMscfd)
        /// </summary>
        public double Flow { get; set; }

        /// <summary>
        /// Utilization percentage of segment capacity
        /// </summary>
        public double UtilizationPercentage => Math.Abs(Flow) / Math.Max(1e-6, Capacity) * 100;

        /// <summary>
        /// Segment capacity
        /// </summary>
        public double Capacity { get; set; }

        /// <summary>
        /// Transportation cost for this flow
        /// </summary>
        public double TransportationCost { get; set; }
    }

    /// <summary>
    /// Result of pressure optimization for a specific point
    /// </summary>
    public class PointPressureResult
    {
        /// <summary>
        /// Point identifier
        /// </summary>
        public string PointId { get; set; } = string.Empty;

        /// <summary>
        /// Optimized pressure at the point (psia)
        /// </summary>
        public double Pressure { get; set; }

        /// <summary>
        /// Pressure squared (used in optimization)
        /// </summary>
        public double PressureSquared { get; set; }

        /// <summary>
        /// Whether pressure constraints are satisfied
        /// </summary>
        public bool IsWithinConstraints { get; set; }

        /// <summary>
        /// For compressor stations: pressure boost applied
        /// </summary>
        public double PressureBoost { get; set; }

        /// <summary>
        /// Fuel consumption at this point (for compressors)
        /// </summary>
        public double FuelConsumption { get; set; }
    }

    /// <summary>
    /// Comprehensive optimization result
    /// </summary>
    public class OptimizationResult
    {
        /// <summary>
        /// Status of the optimization
        /// </summary>
        public OptimizationStatus Status { get; set; } = OptimizationStatus.NotSolved;

        /// <summary>
        /// Objective function value
        /// </summary>
        public double ObjectiveValue { get; set; }

        /// <summary>
        /// Optimization algorithm used
        /// </summary>
        public string AlgorithmUsed { get; set; } = string.Empty;

        /// <summary>
        /// Solver used (GLOP, SCIP, etc.)
        /// </summary>
        public string SolverUsed { get; set; } = string.Empty;

        /// <summary>
        /// Time taken for optimization (milliseconds)
        /// </summary>
        public double SolutionTimeMs { get; set; }

        /// <summary>
        /// Flow results for each segment
        /// </summary>
        public Dictionary<string, SegmentFlowResult> SegmentFlows { get; set; } = new();

        /// <summary>
        /// Pressure results for each point
        /// </summary>
        public Dictionary<string, PointPressureResult> PointPressures { get; set; } = new();

        /// <summary>
        /// Total cost breakdown
        /// </summary>
        public CostBreakdown TotalCost { get; set; } = new();

        /// <summary>
        /// Additional metrics and statistics
        /// </summary>
        public OptimizationMetrics Metrics { get; set; } = new();

        /// <summary>
        /// Any warnings or informational messages
        /// </summary>
        public List<string> Messages { get; set; } = new();

        /// <summary>
        /// Whether all demand requirements were satisfied
        /// </summary>
        public bool AllDemandSatisfied => Metrics.TotalDemandSatisfied >= Metrics.TotalDemandRequired - 1e-6;

        /// <summary>
        /// Whether all capacity constraints were respected
        /// </summary>
        public bool AllCapacityConstraintsRespected => SegmentFlows.Values.All(s => Math.Abs(s.Flow) <= s.Capacity + 1e-6);

        /// <summary>
        /// Adds a segment flow result
        /// </summary>
        public void AddSegmentFlow(string segmentId, double flow, double capacity, double cost)
        {
            SegmentFlows[segmentId] = new SegmentFlowResult
            {
                SegmentId = segmentId,
                Flow = flow,
                Capacity = capacity,
                TransportationCost = cost
            };
        }

        /// <summary>
        /// Adds a point pressure result
        /// </summary>
        public void AddPointPressure(string pointId, double pressure, double pressureSquared, 
            bool withinConstraints, double pressureBoost = 0, double fuelConsumption = 0)
        {
            PointPressures[pointId] = new PointPressureResult
            {
                PointId = pointId,
                Pressure = pressure,
                PressureSquared = pressureSquared,
                IsWithinConstraints = withinConstraints,
                PressureBoost = pressureBoost,
                FuelConsumption = fuelConsumption
            };
        }

        /// <summary>
        /// Generates a summary report of the optimization results
        /// </summary>
        public string GenerateSummaryReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== Gas Pipeline Optimization Results ===");
            report.AppendLine($"Status: {Status}");
            report.AppendLine($"Algorithm: {AlgorithmUsed}");
            report.AppendLine($"Solver: {SolverUsed}");
            report.AppendLine($"Solution Time: {SolutionTimeMs:F2} ms");
            report.AppendLine($"Objective Value: {ObjectiveValue:F2}");
            report.AppendLine();

            report.AppendLine("=== Cost Breakdown ===");
            report.AppendLine($"Transportation Cost: ${TotalCost.TransportationCost:F2}");
            report.AppendLine($"Fuel Cost: ${TotalCost.FuelCost:F2}");
            report.AppendLine($"Compressor Cost: ${TotalCost.CompressorCost:F2}");
            report.AppendLine($"Total Cost: ${TotalCost.TotalCost:F2}");
            report.AppendLine();

            report.AppendLine("=== Network Metrics ===");
            report.AppendLine($"Total Supply Used: {Metrics.TotalSupplyUsed:F2} MMscfd");
            report.AppendLine($"Total Demand Satisfied: {Metrics.TotalDemandSatisfied:F2} MMscfd");
            report.AppendLine($"Total Demand Required: {Metrics.TotalDemandRequired:F2} MMscfd");
            report.AppendLine($"Average Capacity Utilization: {Metrics.AverageCapacityUtilization:F1}%");
            report.AppendLine($"Peak Capacity Utilization: {Metrics.PeakCapacityUtilization:F1}%");
            report.AppendLine();

            if (SegmentFlows.Any())
            {
                report.AppendLine("=== Segment Flows ===");
                foreach (var flow in SegmentFlows.Values.OrderBy(s => s.SegmentId))
                {
                    report.AppendLine($"{flow.SegmentId}: {flow.Flow:F2} MMscfd ({flow.UtilizationPercentage:F1}% utilization)");
                }
                report.AppendLine();
            }

            if (PointPressures.Any())
            {
                report.AppendLine("=== Point Pressures ===");
                foreach (var pressure in PointPressures.Values.OrderBy(p => p.PointId))
                {
                    var status = pressure.IsWithinConstraints ? "OK" : "VIOLATION";
                    report.AppendLine($"{pressure.PointId}: {pressure.Pressure:F1} psia [{status}]");
                    if (pressure.PressureBoost > 0)
                    {
                        report.AppendLine($"  Pressure Boost: {pressure.PressureBoost:F1} psi");
                    }
                    if (pressure.FuelConsumption > 0)
                    {
                        report.AppendLine($"  Fuel Consumption: {pressure.FuelConsumption:F3} MMscf");
                    }
                }
                report.AppendLine();
            }

            if (Messages.Any())
            {
                report.AppendLine("=== Messages ===");
                foreach (var message in Messages)
                {
                    report.AppendLine($"- {message}");
                }
            }

            return report.ToString();
        }
    }

    /// <summary>
    /// Cost breakdown for optimization results
    /// </summary>
    public class CostBreakdown
    {
        /// <summary>
        /// Total transportation cost through segments
        /// </summary>
        public double TransportationCost { get; set; }

        /// <summary>
        /// Total fuel cost for compressors
        /// </summary>
        public double FuelCost { get; set; }

        /// <summary>
        /// Total compressor operation cost
        /// </summary>
        public double CompressorCost { get; set; }

        /// <summary>
        /// Other miscellaneous costs
        /// </summary>
        public double OtherCosts { get; set; }

        /// <summary>
        /// Total cost of all components
        /// </summary>
        public double TotalCost => TransportationCost + FuelCost + CompressorCost + OtherCosts;
    }

    /// <summary>
    /// Optimization metrics and statistics
    /// </summary>
    public class OptimizationMetrics
    {
        /// <summary>
        /// Total supply capacity used
        /// </summary>
        public double TotalSupplyUsed { get; set; }

        /// <summary>
        /// Total demand actually satisfied
        /// </summary>
        public double TotalDemandSatisfied { get; set; }

        /// <summary>
        /// Total demand required
        /// </summary>
        public double TotalDemandRequired { get; set; }

        /// <summary>
        /// Average capacity utilization across all segments
        /// </summary>
        public double AverageCapacityUtilization { get; set; }

        /// <summary>
        /// Peak capacity utilization (highest among all segments)
        /// </summary>
        public double PeakCapacityUtilization { get; set; }

        /// <summary>
        /// Total throughput in the network
        /// </summary>
        public double TotalThroughput { get; set; }

        /// <summary>
        /// Number of active segments in the solution
        /// </summary>
        public int ActiveSegments { get; set; }

        /// <summary>
        /// Number of compressors operating
        /// </summary>
        public int ActiveCompressors { get; set; }
    }
}
