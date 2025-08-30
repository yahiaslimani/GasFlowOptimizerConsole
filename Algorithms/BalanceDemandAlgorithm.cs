using Google.OrTools.LinearSolver;
using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to balance demand across multiple paths while minimizing variance in utilization
    /// </summary>
    public class BalanceDemandAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Balance Demand";
        public string Description => "Balances gas flow across multiple paths to minimize utilization variance and improve network resilience";

        private readonly PressureConstraintService _pressureService;
        private readonly CompressorService _compressorService;

        public BalanceDemandAlgorithm()
        {
            _pressureService = new PressureConstraintService();
            _compressorService = new CompressorService();
        }

        public bool CanHandle(PipelineNetwork network, OptimizationSettings settings)
        {
            // This algorithm works best with networks that have multiple paths
            var receiptPoints = network.GetReceiptPoints().Count();
            var deliveryPoints = network.GetDeliveryPoints().Count();
            return network.IsValid(out _) && receiptPoints >= 1 && deliveryPoints >= 1;
        }

        public Dictionary<string, string> GetParameters()
        {
            return new Dictionary<string, string>
            {
                { "BalanceWeight", "Weight for utilization balance objective (default: 1.0)" },
                { "ThroughputWeight", "Weight for throughput maximization (default: 0.5)" },
                { "CostWeight", "Weight for cost minimization (default: 0.3)" },
                { "TargetUtilization", "Target utilization percentage for segments (default: 70)" },
                { "UtilizationTolerance", "Tolerance for utilization variance (default: 10)" },
                { "PathDiversityBonus", "Bonus for using multiple paths (default: 0.1)" }
            };
        }

        public OptimizationResult Optimize(PipelineNetwork network, OptimizationSettings settings)
        {
            var startTime = DateTime.Now;
            var result = new OptimizationResult
            {
                AlgorithmUsed = Name,
                Status = OptimizationStatus.NotSolved
            };

            try
            {
                // Determine solver type based on constraints
                var solverType = DetermineSolverType(settings);
                result.SolverUsed = solverType;

                using var solver = Solver.CreateSolver(solverType);
                if (solver == null)
                {
                    result.Status = OptimizationStatus.Error;
                    result.Messages.Add($"Failed to create {solverType} solver");
                    return result;
                }

                // Set solver parameters
                ConfigureSolver(solver, settings);

                // Create decision variables
                var flowVars = CreateFlowVariables(solver, network);
                var pressureVars = CreatePressureVariables(solver, network, settings);
                var compressorVars = CreateCompressorVariables(solver, network, settings);
                var balanceVars = CreateBalanceVariables(solver, network);

                // Add constraints
                AddFlowBalanceConstraints(solver, network, flowVars);
                AddCapacityConstraints(solver, network, flowVars);
                AddDemandSatisfactionConstraints(solver, network, flowVars);
                AddUtilizationBalanceConstraints(solver, network, flowVars, balanceVars, settings);
                
                if (settings.EnablePressureConstraints)
                {
                    _pressureService.AddPressureConstraints(solver, network, flowVars, pressureVars, settings);
                }

                if (settings.EnableCompressorStations)
                {
                    _compressorService.AddCompressorConstraints(solver, network, flowVars, pressureVars, compressorVars, settings);
                }

                // Create multi-objective function
                var objective = CreateBalancedObjective(solver, network, flowVars, balanceVars, settings);

                // Solve the optimization problem
                var solverStatus = solver.Solve();
                result.Status = ConvertSolverStatus(solverStatus);

                if (solverStatus == Solver.ResultStatus.OPTIMAL || solverStatus == Solver.ResultStatus.FEASIBLE)
                {
                    // Extract results
                    ExtractFlowResults(result, network, flowVars);
                    
                    if (settings.EnablePressureConstraints)
                    {
                        ExtractPressureResults(result, network, pressureVars);
                    }

                    if (settings.EnableCompressorStations)
                    {
                        ExtractCompressorResults(result, network, compressorVars);
                    }

                    // Calculate metrics and costs
                    CalculateMetrics(result, network);
                    CalculateCosts(result, network);

                    result.ObjectiveValue = objective.Value();
                    
                    var variance = CalculateUtilizationVariance(result);
                    result.Messages.Add($"Balanced utilization with variance: {variance:F2}%");
                    result.Messages.Add($"Average utilization: {result.Metrics.AverageCapacityUtilization:F1}%");
                }
                else
                {
                    result.Messages.Add($"Solver failed with status: {solverStatus}");
                }
            }
            catch (Exception ex)
            {
                result.Status = OptimizationStatus.Error;
                result.Messages.Add($"Optimization error: {ex.Message}");
            }

            result.SolutionTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            return result;
        }

        private string DetermineSolverType(OptimizationSettings settings)
        {
            // Balance algorithm may need SCIP for quadratic objective
            if (settings.EnablePressureConstraints && !settings.UseLinearPressureApproximation)
            {
                return "SCIP";
            }
            return "SCIP"; // Prefer SCIP for quadratic balance objectives
        }

        private void ConfigureSolver(Solver solver, OptimizationSettings settings)
        {
            solver.SetTimeLimit((long)(settings.MaxSolutionTimeSeconds * 1000));
        }

        private Dictionary<string, Variable> CreateFlowVariables(Solver solver, PipelineNetwork network)
        {
            var flowVars = new Dictionary<string, Variable>();

            foreach (var segment in network.GetActiveSegments())
            {
                var minFlow = segment.GetEffectiveMinFlow();
                var maxFlow = segment.GetEffectiveCapacity();
                
                flowVars[segment.Id] = solver.MakeNumVar(minFlow, maxFlow, $"flow_{segment.Id}");
            }

            return flowVars;
        }

        private Dictionary<string, Variable> CreatePressureVariables(Solver solver, PipelineNetwork network, OptimizationSettings settings)
        {
            var pressureVars = new Dictionary<string, Variable>();

            if (!settings.EnablePressureConstraints)
                return pressureVars;

            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                var minPressureSquared = point.MinPressure * point.MinPressure;
                var maxPressureSquared = point.MaxPressure * point.MaxPressure;
                
                pressureVars[point.Id] = solver.MakeNumVar(minPressureSquared, maxPressureSquared, $"pressure_sq_{point.Id}");
            }

            return pressureVars;
        }

        private Dictionary<string, Variable> CreateCompressorVariables(Solver solver, PipelineNetwork network, OptimizationSettings settings)
        {
            var compressorVars = new Dictionary<string, Variable>();

            if (!settings.EnableCompressorStations)
                return compressorVars;

            foreach (var compressor in network.GetCompressorStations())
            {
                // Binary variable for compressor operation
                compressorVars[$"{compressor.Id}_active"] = solver.MakeBoolVar($"comp_active_{compressor.Id}");
                
                // Pressure boost variable
                compressorVars[$"{compressor.Id}_boost"] = solver.MakeNumVar(0, compressor.MaxPressureBoost, $"comp_boost_{compressor.Id}");
                
                // Fuel consumption variable
                compressorVars[$"{compressor.Id}_fuel"] = solver.MakeNumVar(0, double.PositiveInfinity, $"comp_fuel_{compressor.Id}");
            }

            return compressorVars;
        }

        private Dictionary<string, Variable> CreateBalanceVariables(Solver solver, PipelineNetwork network)
        {
            var balanceVars = new Dictionary<string, Variable>();

            // Variables for utilization balance
            foreach (var segment in network.GetActiveSegments())
            {
                balanceVars[$"util_{segment.Id}"] = solver.MakeNumVar(0, 100, $"utilization_{segment.Id}");
                balanceVars[$"util_dev_{segment.Id}"] = solver.MakeNumVar(-100, 100, $"util_deviation_{segment.Id}");
                balanceVars[$"util_dev_abs_{segment.Id}"] = solver.MakeNumVar(0, 100, $"util_deviation_abs_{segment.Id}");
            }

            // Average utilization variable
            balanceVars["avg_util"] = solver.MakeNumVar(0, 100, "average_utilization");

            // Path diversity variables
            foreach (var receiptPoint in network.GetReceiptPoints())
            {
                foreach (var deliveryPoint in network.GetDeliveryPoints())
                {
                    balanceVars[$"path_{receiptPoint.Id}_{deliveryPoint.Id}"] = solver.MakeBoolVar($"path_active_{receiptPoint.Id}_{deliveryPoint.Id}");
                }
            }

            return balanceVars;
        }

        private void AddFlowBalanceConstraints(Solver solver, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                var constraint = solver.MakeConstraint(-double.PositiveInfinity, double.PositiveInfinity, $"balance_{point.Id}");

                // Add inflows (positive)
                foreach (var segment in network.GetIncomingSegments(point.Id))
                {
                    constraint.SetCoefficient(flowVars[segment.Id], 1.0);
                }

                // Add outflows (negative)
                foreach (var segment in network.GetOutgoingSegments(point.Id))
                {
                    constraint.SetCoefficient(flowVars[segment.Id], -1.0);
                }

                // Set bounds based on point type
                switch (point.Type)
                {
                    case PointType.Receipt:
                        constraint.SetBounds(-point.SupplyCapacity, 0);
                        break;
                    case PointType.Delivery:
                        constraint.SetBounds(0, point.DemandRequirement);
                        break;
                    case PointType.Compressor:
                        constraint.SetBounds(0, 0);
                        break;
                }
            }
        }

        private void AddCapacityConstraints(Solver solver, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            foreach (var segment in network.GetActiveSegments())
            {
                var flowVar = flowVars[segment.Id];
                
                solver.MakeConstraint(
                    segment.GetEffectiveMinFlow(), 
                    segment.GetEffectiveCapacity(), 
                    $"capacity_{segment.Id}"
                ).SetCoefficient(flowVar, 1.0);
            }
        }

        private void AddDemandSatisfactionConstraints(Solver solver, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            foreach (var deliveryPoint in network.GetDeliveryPoints())
            {
                var constraint = solver.MakeConstraint(deliveryPoint.DemandRequirement, deliveryPoint.DemandRequirement, 
                    $"demand_{deliveryPoint.Id}");

                foreach (var segment in network.GetIncomingSegments(deliveryPoint.Id))
                {
                    constraint.SetCoefficient(flowVars[segment.Id], 1.0);
                }
            }
        }

        private void AddUtilizationBalanceConstraints(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> balanceVars, OptimizationSettings settings)
        {
            var activeSegments = network.GetActiveSegments().ToList();
            var targetUtilization = GetParameterValue(settings, "TargetUtilization", 70.0);

            // Calculate utilization for each segment
            foreach (var segment in activeSegments)
            {
                var utilizationVar = balanceVars[$"util_{segment.Id}"];
                var flowVar = flowVars[segment.Id];

                // utilization = (|flow| / capacity) * 100
                var utilizationConstraint = solver.MakeConstraint(0, 0, $"utilization_calc_{segment.Id}");
                utilizationConstraint.SetCoefficient(utilizationVar, segment.Capacity);
                utilizationConstraint.SetCoefficient(flowVar, -100.0); // Simplified linear approximation
            }

            // Calculate average utilization
            if (activeSegments.Any())
            {
                var avgUtilConstraint = solver.MakeConstraint(0, 0, "average_utilization_calc");
                avgUtilConstraint.SetCoefficient(balanceVars["avg_util"], activeSegments.Count);

                foreach (var segment in activeSegments)
                {
                    avgUtilConstraint.SetCoefficient(balanceVars[$"util_{segment.Id}"], -1.0);
                }
            }

            // Calculate deviations from average
            foreach (var segment in activeSegments)
            {
                var utilizationVar = balanceVars[$"util_{segment.Id}"];
                var deviationVar = balanceVars[$"util_dev_{segment.Id}"];
                var absDeviationVar = balanceVars[$"util_dev_abs_{segment.Id}"];

                // deviation = utilization - average_utilization
                var deviationConstraint = solver.MakeConstraint(0, 0, $"deviation_calc_{segment.Id}");
                deviationConstraint.SetCoefficient(deviationVar, 1.0);
                deviationConstraint.SetCoefficient(utilizationVar, -1.0);
                deviationConstraint.SetCoefficient(balanceVars["avg_util"], 1.0);

                // abs_deviation >= deviation
                solver.MakeConstraint(0, double.PositiveInfinity, $"abs_dev_pos_{segment.Id}")
                    .SetCoefficient(absDeviationVar, 1.0);
                solver.MakeConstraint(0, double.PositiveInfinity, $"abs_dev_pos_{segment.Id}")
                    .SetCoefficient(deviationVar, -1.0);

                // abs_deviation >= -deviation
                solver.MakeConstraint(0, double.PositiveInfinity, $"abs_dev_neg_{segment.Id}")
                    .SetCoefficient(absDeviationVar, 1.0);
                solver.MakeConstraint(0, double.PositiveInfinity, $"abs_dev_neg_{segment.Id}")
                    .SetCoefficient(deviationVar, 1.0);
            }
        }

        private Objective CreateBalancedObjective(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> balanceVars, OptimizationSettings settings)
        {
            var objective = solver.Objective();
            objective.SetMaximization(); // Maximize balance (minimize variance)

            // Get weights
            var balanceWeight = GetParameterValue(settings, "BalanceWeight", 1.0);
            var throughputWeight = GetParameterValue(settings, "ThroughputWeight", 0.5);
            var costWeight = GetParameterValue(settings, "CostWeight", 0.3);
            var pathDiversityBonus = GetParameterValue(settings, "PathDiversityBonus", 0.1);

            // Minimize utilization variance (maximize balance)
            foreach (var segment in network.GetActiveSegments())
            {
                if (balanceVars.TryGetValue($"util_dev_abs_{segment.Id}", out var absDeviationVar))
                {
                    objective.SetCoefficient(absDeviationVar, -balanceWeight); // Minimize variance
                }
            }

            // Maximize total throughput
            foreach (var receiptPoint in network.GetReceiptPoints())
            {
                foreach (var segment in network.GetOutgoingSegments(receiptPoint.Id))
                {
                    objective.SetCoefficient(flowVars[segment.Id], throughputWeight);
                }
            }

            // Minimize transportation costs
            foreach (var segment in network.GetActiveSegments())
            {
                objective.SetCoefficient(flowVars[segment.Id], -costWeight * segment.TransportationCost);
            }

            // Bonus for path diversity
            foreach (var receiptPoint in network.GetReceiptPoints())
            {
                foreach (var deliveryPoint in network.GetDeliveryPoints())
                {
                    if (balanceVars.TryGetValue($"path_{receiptPoint.Id}_{deliveryPoint.Id}", out var pathVar))
                    {
                        objective.SetCoefficient(pathVar, pathDiversityBonus);
                    }
                }
            }

            return objective;
        }

        private double GetParameterValue(OptimizationSettings settings, string paramName, double defaultValue)
        {
            if (settings.AlgorithmParameters.TryGetValue(paramName, out var value))
            {
                if (value is double doubleValue)
                    return doubleValue;
                if (double.TryParse(value.ToString(), out var parsedValue))
                    return parsedValue;
            }
            return defaultValue;
        }

        private OptimizationStatus ConvertSolverStatus(Solver.ResultStatus status)
        {
            return status switch
            {
                Solver.ResultStatus.OPTIMAL => OptimizationStatus.Optimal,
                Solver.ResultStatus.FEASIBLE => OptimizationStatus.Feasible,
                Solver.ResultStatus.INFEASIBLE => OptimizationStatus.Infeasible,
                Solver.ResultStatus.UNBOUNDED => OptimizationStatus.Unbounded,
                _ => OptimizationStatus.Error
            };
        }

        private void ExtractFlowResults(OptimizationResult result, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            foreach (var segment in network.GetActiveSegments())
            {
                var flow = flowVars[segment.Id].SolutionValue();
                var cost = flow * segment.TransportationCost;
                
                result.AddSegmentFlow(segment.Id, flow, segment.Capacity, cost);
            }
        }

        private void ExtractPressureResults(OptimizationResult result, PipelineNetwork network, Dictionary<string, Variable> pressureVars)
        {
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                if (pressureVars.TryGetValue(point.Id, out var pressureVar))
                {
                    var pressureSquared = pressureVar.SolutionValue();
                    var pressure = Math.Sqrt(Math.Max(0, pressureSquared));
                    var withinConstraints = pressure >= point.MinPressure && pressure <= point.MaxPressure;
                    
                    result.AddPointPressure(point.Id, pressure, pressureSquared, withinConstraints);
                }
            }
        }

        private void ExtractCompressorResults(OptimizationResult result, PipelineNetwork network, Dictionary<string, Variable> compressorVars)
        {
            foreach (var compressor in network.GetCompressorStations())
            {
                if (result.PointPressures.TryGetValue(compressor.Id, out var pressureResult))
                {
                    if (compressorVars.TryGetValue($"{compressor.Id}_boost", out var boostVar))
                    {
                        pressureResult.PressureBoost = boostVar.SolutionValue();
                    }

                    if (compressorVars.TryGetValue($"{compressor.Id}_fuel", out var fuelVar))
                    {
                        pressureResult.FuelConsumption = fuelVar.SolutionValue();
                    }
                }
            }
        }

        private void CalculateMetrics(OptimizationResult result, PipelineNetwork network)
        {
            var metrics = result.Metrics;

            // Calculate total throughput
            metrics.TotalThroughput = result.SegmentFlows.Values.Where(f => f.Flow > 0).Sum(f => f.Flow);

            // Calculate supply and demand metrics
            metrics.TotalSupplyUsed = 0;
            metrics.TotalDemandSatisfied = 0;
            metrics.TotalDemandRequired = network.GetTotalDemandRequirement();

            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                if (point.Type == PointType.Receipt)
                {
                    var outflow = network.GetOutgoingSegments(point.Id)
                        .Where(s => result.SegmentFlows.ContainsKey(s.Id))
                        .Sum(s => result.SegmentFlows[s.Id].Flow);
                    metrics.TotalSupplyUsed += outflow;
                }
                else if (point.Type == PointType.Delivery)
                {
                    var inflow = network.GetIncomingSegments(point.Id)
                        .Where(s => result.SegmentFlows.ContainsKey(s.Id))
                        .Sum(s => result.SegmentFlows[s.Id].Flow);
                    metrics.TotalDemandSatisfied += inflow;
                }
            }

            // Calculate utilization metrics
            var utilizationRates = result.SegmentFlows.Values.Select(f => f.UtilizationPercentage).ToList();
            metrics.AverageCapacityUtilization = utilizationRates.Any() ? utilizationRates.Average() : 0;
            metrics.PeakCapacityUtilization = utilizationRates.Any() ? utilizationRates.Max() : 0;

            // Count active elements
            metrics.ActiveSegments = result.SegmentFlows.Values.Count(f => Math.Abs(f.Flow) > 1e-6);
            metrics.ActiveCompressors = result.PointPressures.Values.Count(p => p.PressureBoost > 1e-6);
        }

        private void CalculateCosts(OptimizationResult result, PipelineNetwork network)
        {
            var costs = result.TotalCost;

            // Transportation costs
            costs.TransportationCost = result.SegmentFlows.Values.Sum(f => f.TransportationCost);

            // Fuel and compressor costs
            costs.FuelCost = result.PointPressures.Values.Sum(p => p.FuelConsumption * 3.50);
            costs.CompressorCost = result.PointPressures.Values.Where(p => p.PressureBoost > 0).Sum(p => p.PressureBoost * 0.01);
        }

        private double CalculateUtilizationVariance(OptimizationResult result)
        {
            var utilizationRates = result.SegmentFlows.Values.Select(f => f.UtilizationPercentage).ToList();
            
            if (!utilizationRates.Any())
                return 0;

            var mean = utilizationRates.Average();
            var variance = utilizationRates.Sum(u => Math.Pow(u - mean, 2)) / utilizationRates.Count;
            
            return Math.Sqrt(variance);
        }
    }
}
