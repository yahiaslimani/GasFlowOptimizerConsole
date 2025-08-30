using GasPipelineOptimization.Algorithms;
using GasPipelineOptimization.Models;

namespace GasPipelineOptimization.Services
{
    /// <summary>
    /// Main optimization engine that coordinates different algorithms and manages optimization execution
    /// </summary>
    public class OptimizationEngine
    {
        private readonly Dictionary<string, IOptimizationAlgorithm> _algorithms;

        public OptimizationEngine()
        {
            _algorithms = new Dictionary<string, IOptimizationAlgorithm>
            {
                { "MaximizeThroughput", new MaximizeThroughputAlgorithm() },
                { "MinimizeCost", new MinimizeCostAlgorithm() },
                { "BalanceDemand", new BalanceDemandAlgorithm() }
            };
        }

        /// <summary>
        /// Gets all available optimization algorithms
        /// </summary>
        public IEnumerable<string> GetAvailableAlgorithms()
        {
            return _algorithms.Keys;
        }

        /// <summary>
        /// Gets information about a specific algorithm
        /// </summary>
        public IOptimizationAlgorithm? GetAlgorithm(string algorithmName)
        {
            _algorithms.TryGetValue(algorithmName, out var algorithm);
            return algorithm;
        }

        /// <summary>
        /// Runs optimization using the specified algorithm
        /// </summary>
        public OptimizationResult RunOptimization(string algorithmName, PipelineNetwork network, OptimizationSettings settings)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrEmpty(algorithmName))
                {
                    return CreateErrorResult("Algorithm name cannot be empty");
                }

                if (network == null)
                {
                    return CreateErrorResult("Pipeline network cannot be null");
                }

                if (settings == null)
                {
                    return CreateErrorResult("Optimization settings cannot be null");
                }

                // Validate settings
                if (!settings.IsValid(out string settingsError))
                {
                    return CreateErrorResult($"Invalid optimization settings: {settingsError}");
                }

                // Validate network if requested
                if (settings.ValidateNetworkBeforeOptimization)
                {
                    if (!network.IsValid(out List<string> networkErrors))
                    {
                        return CreateErrorResult($"Invalid network configuration: {string.Join(", ", networkErrors)}");
                    }
                }

                // Get the algorithm
                if (!_algorithms.TryGetValue(algorithmName, out var algorithm))
                {
                    return CreateErrorResult($"Unknown algorithm: {algorithmName}. Available algorithms: {string.Join(", ", _algorithms.Keys)}");
                }

                // Check if algorithm can handle the network
                if (!algorithm.CanHandle(network, settings))
                {
                    return CreateErrorResult($"Algorithm '{algorithmName}' cannot handle the current network configuration");
                }

                // Log optimization start
                if (settings.EnableDetailedLogging)
                {
                    Console.WriteLine($"Starting optimization with algorithm: {algorithmName}");
                    Console.WriteLine($"Network: {network.Points.Count} points, {network.Segments.Count} segments");
                    Console.WriteLine($"Settings: Pressure constraints={settings.EnablePressureConstraints}, Compressors={settings.EnableCompressorStations}");
                }

                // Run the optimization
                var result = algorithm.Optimize(network, settings);

                // Log optimization completion
                if (settings.EnableDetailedLogging)
                {
                    Console.WriteLine($"Optimization completed with status: {result.Status}");
                    Console.WriteLine($"Solution time: {result.SolutionTimeMs:F2} ms");
                    if (result.Status == OptimizationStatus.Optimal || result.Status == OptimizationStatus.Feasible)
                    {
                        Console.WriteLine($"Objective value: {result.ObjectiveValue:F2}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return CreateErrorResult($"Optimization engine error: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs multiple optimization algorithms for comparison
        /// </summary>
        public Dictionary<string, OptimizationResult> RunMultipleOptimizations(
            IEnumerable<string> algorithmNames, PipelineNetwork network, OptimizationSettings settings)
        {
            var results = new Dictionary<string, OptimizationResult>();

            foreach (var algorithmName in algorithmNames)
            {
                try
                {
                    var result = RunOptimization(algorithmName, network, settings);
                    results[algorithmName] = result;
                }
                catch (Exception ex)
                {
                    results[algorithmName] = CreateErrorResult($"Error running {algorithmName}: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Runs scenario analysis with different network configurations
        /// </summary>
        public Dictionary<string, OptimizationResult> RunScenarioAnalysis(
            string algorithmName, Dictionary<string, PipelineNetwork> scenarios, OptimizationSettings settings)
        {
            var results = new Dictionary<string, OptimizationResult>();

            foreach (var scenario in scenarios)
            {
                try
                {
                    var result = RunOptimization(algorithmName, scenario.Value, settings);
                    results[scenario.Key] = result;
                }
                catch (Exception ex)
                {
                    results[scenario.Key] = CreateErrorResult($"Error in scenario {scenario.Key}: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Generates a comparative report of multiple optimization results
        /// </summary>
        public string GenerateComparativeReport(Dictionary<string, OptimizationResult> results)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== Comparative Optimization Report ===");
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine();

            // Summary table
            report.AppendLine("Algorithm Comparison:");
            report.AppendLine("Algorithm".PadRight(20) + "Status".PadRight(12) + "Objective".PadRight(15) + "Time(ms)".PadRight(12) + "Total Cost");
            report.AppendLine(new string('-', 75));

            foreach (var result in results.OrderBy(r => r.Value.ObjectiveValue))
            {
                var algorithm = result.Key.PadRight(20);
                var status = result.Value.Status.ToString().PadRight(12);
                var objective = result.Value.ObjectiveValue.ToString("F2").PadRight(15);
                var time = result.Value.SolutionTimeMs.ToString("F0").PadRight(12);
                var cost = result.Value.TotalCost.TotalCost.ToString("F2");

                report.AppendLine($"{algorithm}{status}{objective}{time}${cost}");
            }

            report.AppendLine();

            // Detailed analysis
            foreach (var result in results)
            {
                report.AppendLine($"=== {result.Key} Details ===");
                if (result.Value.Status == OptimizationStatus.Optimal || result.Value.Status == OptimizationStatus.Feasible)
                {
                    report.AppendLine($"Total Throughput: {result.Value.Metrics.TotalThroughput:F2} MMscfd");
                    report.AppendLine($"Average Utilization: {result.Value.Metrics.AverageCapacityUtilization:F1}%");
                    report.AppendLine($"Peak Utilization: {result.Value.Metrics.PeakCapacityUtilization:F1}%");
                    report.AppendLine($"Transportation Cost: ${result.Value.TotalCost.TransportationCost:F2}");
                    report.AppendLine($"Fuel Cost: ${result.Value.TotalCost.FuelCost:F2}");
                    report.AppendLine($"Compressor Cost: ${result.Value.TotalCost.CompressorCost:F2}");
                }
                else
                {
                    report.AppendLine($"Optimization failed: {result.Value.Status}");
                    if (result.Value.Messages.Any())
                    {
                        report.AppendLine($"Messages: {string.Join(", ", result.Value.Messages)}");
                    }
                }
                report.AppendLine();
            }

            return report.ToString();
        }

        /// <summary>
        /// Validates that an optimization result is feasible and consistent
        /// </summary>
        public bool ValidateOptimizationResult(OptimizationResult result, PipelineNetwork network, 
            out List<string> validationErrors)
        {
            validationErrors = new List<string>();
            
            if (result == null)
            {
                validationErrors.Add("Result is null");
                return false;
            }

            if (result.Status != OptimizationStatus.Optimal && result.Status != OptimizationStatus.Feasible)
            {
                validationErrors.Add($"Optimization was not successful: {result.Status}");
                return false;
            }

            // Validate flow balance at each point
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                var inflow = network.GetIncomingSegments(point.Id)
                    .Where(s => result.SegmentFlows.ContainsKey(s.Id))
                    .Sum(s => result.SegmentFlows[s.Id].Flow);

                var outflow = network.GetOutgoingSegments(point.Id)
                    .Where(s => result.SegmentFlows.ContainsKey(s.Id))
                    .Sum(s => result.SegmentFlows[s.Id].Flow);

                var balance = inflow - outflow;

                switch (point.Type)
                {
                    case PointType.Receipt:
                        if (balance > 1e-6 || balance < -point.SupplyCapacity - 1e-6)
                        {
                            validationErrors.Add($"Flow balance violation at receipt point {point.Id}: {balance:F3}");
                        }
                        break;
                    case PointType.Delivery:
                        if (Math.Abs(balance - point.DemandRequirement) > 1e-6)
                        {
                            validationErrors.Add($"Demand not satisfied at delivery point {point.Id}: expected {point.DemandRequirement:F3}, got {balance:F3}");
                        }
                        break;
                    case PointType.Compressor:
                        if (Math.Abs(balance) > 1e-6)
                        {
                            validationErrors.Add($"Flow balance violation at compressor {point.Id}: {balance:F3}");
                        }
                        break;
                }
            }

            // Validate capacity constraints
            foreach (var segment in network.GetActiveSegments())
            {
                if (result.SegmentFlows.TryGetValue(segment.Id, out var flowResult))
                {
                    if (Math.Abs(flowResult.Flow) > segment.Capacity + 1e-6)
                    {
                        validationErrors.Add($"Capacity violation at segment {segment.Id}: flow {flowResult.Flow:F3} exceeds capacity {segment.Capacity:F3}");
                    }
                }
            }

            // Validate pressure constraints if enabled
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                if (result.PointPressures.TryGetValue(point.Id, out var pressureResult))
                {
                    if (pressureResult.Pressure < point.MinPressure - 1e-6 || 
                        pressureResult.Pressure > point.MaxPressure + 1e-6)
                    {
                        validationErrors.Add($"Pressure constraint violation at point {point.Id}: {pressureResult.Pressure:F1} psia (limits: {point.MinPressure:F1}-{point.MaxPressure:F1})");
                    }
                }
            }

            return !validationErrors.Any();
        }

        private OptimizationResult CreateErrorResult(string errorMessage)
        {
            return new OptimizationResult
            {
                Status = OptimizationStatus.Error,
                Messages = new List<string> { errorMessage },
                SolutionTimeMs = 0
            };
        }
    }
}
