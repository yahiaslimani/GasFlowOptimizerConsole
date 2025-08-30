using Google.OrTools.LinearSolver;
using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to maximize total throughput in the gas pipeline network
    /// </summary>
    public class MaximizeThroughputAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Maximize Throughput";
        public string Description => "Maximizes the total gas flow through the pipeline network while respecting capacity and pressure constraints";

        private readonly PressureConstraintService _pressureService;
        private readonly CompressorService _compressorService;

        public MaximizeThroughputAlgorithm()
        {
            _pressureService = new PressureConstraintService();
            _compressorService = new CompressorService();
        }

        public bool CanHandle(PipelineNetwork network, OptimizationSettings settings)
        {
            // This algorithm can handle any valid network
            return network.IsValid(out _);
        }

        public Dictionary<string, string> GetParameters()
        {
            return new Dictionary<string, string>
            {
                { "ThroughputWeight", "Weight for throughput maximization objective (default: 1.0)" },
                { "DemandPriority", "Priority for satisfying demand vs maximizing total flow (default: 1.0)" },
                { "SupplyUtilization", "Target supply utilization percentage (default: 100)" }
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

                // Add constraints
                AddFlowBalanceConstraints(solver, network, flowVars);
                AddCapacityConstraints(solver, network, flowVars);
                
                if (settings.EnablePressureConstraints)
                {
                    _pressureService.AddPressureConstraints(solver, network, flowVars, pressureVars, settings);
                }

                if (settings.EnableCompressorStations)
                {
                    _compressorService.AddCompressorConstraints(solver, network, flowVars, pressureVars, compressorVars, settings);
                }

                // Create objective function to maximize throughput
                var objective = CreateThroughputObjective(solver, network, flowVars, settings);

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
                    result.Messages.Add($"Maximized throughput: {result.Metrics.TotalThroughput:F2} MMscfd");
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
            // Use SCIP for nonlinear pressure constraints, GLOP for linear problems
            if (settings.EnablePressureConstraints && !settings.UseLinearPressureApproximation)
            {
                return "SCIP";
            }
            return settings.PreferredSolver;
        }

        private void ConfigureSolver(Solver solver, OptimizationSettings settings)
        {
            solver.SetTimeLimit((long)(settings.MaxSolutionTimeSeconds * 1000));
            
            if (solver.SolverVersion().Contains("GLOP"))
            {
                solver.SetSolverSpecificParametersAsString($"solution_feasibility_tolerance:{settings.FeasibilityTolerance}");
            }
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
                        constraint.SetBounds(-point.SupplyCapacity, 0); // Can supply up to capacity
                        break;
                    case PointType.Delivery:
                        constraint.SetBounds(0, point.DemandRequirement); // Can satisfy up to demand requirement
                        break;
                    case PointType.Compressor:
                        constraint.SetBounds(0, 0); // Flow balance at compressor
                        break;
                }
            }
        }

        private void AddCapacityConstraints(Solver solver, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            foreach (var segment in network.GetActiveSegments())
            {
                var flowVar = flowVars[segment.Id];
                
                // Flow must be within segment capacity limits
                solver.MakeConstraint(
                    segment.GetEffectiveMinFlow(), 
                    segment.GetEffectiveCapacity(), 
                    $"capacity_{segment.Id}"
                ).SetCoefficient(flowVar, 1.0);
            }
        }

        private Objective CreateThroughputObjective(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> flowVars, OptimizationSettings settings)
        {
            var objective = solver.Objective();
            objective.SetMaximization();

            // Get algorithm-specific parameters
            var throughputWeight = GetParameterValue(settings, "ThroughputWeight", 1.0);
            var demandPriority = GetParameterValue(settings, "DemandPriority", 1.0);

            // Maximize total throughput through receipt points
            foreach (var receiptPoint in network.GetReceiptPoints())
            {
                foreach (var segment in network.GetOutgoingSegments(receiptPoint.Id))
                {
                    objective.SetCoefficient(flowVars[segment.Id], throughputWeight);
                }
            }

            // Add higher weight for satisfying demand
            foreach (var deliveryPoint in network.GetDeliveryPoints())
            {
                foreach (var segment in network.GetIncomingSegments(deliveryPoint.Id))
                {
                    objective.SetCoefficient(flowVars[segment.Id], demandPriority);
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
            costs.FuelCost = result.PointPressures.Values.Sum(p => p.FuelConsumption * 3.50); // Assume $3.50/MMscf fuel cost
            costs.CompressorCost = result.PointPressures.Values.Where(p => p.PressureBoost > 0).Sum(p => p.PressureBoost * 0.01); // $0.01 per psi boost
        }
    }
}
