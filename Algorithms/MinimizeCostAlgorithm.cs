using Google.OrTools.LinearSolver;
using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to minimize total cost while satisfying demand requirements
    /// </summary>
    public class MinimizeCostAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Minimize Cost";
        public string Description => "Minimizes total operational costs including transportation, fuel, and compression costs while satisfying all demand requirements";

        private readonly PressureConstraintService _pressureService;
        private readonly CompressorService _compressorService;

        public MinimizeCostAlgorithm()
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
                { "FuelCostPerMMscf", "Cost of fuel per MMscf (default: 3.50)" },
                { "CompressorOperatingCost", "Cost per psi of compression (default: 0.01)" },
                { "TransportationWeight", "Weight for transportation costs (default: 1.0)" },
                { "FuelWeight", "Weight for fuel costs (default: 1.0)" },
                { "CompressorWeight", "Weight for compressor costs (default: 1.0)" }
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
                var costVars = CreateCostVariables(solver, network, settings);

                // Add constraints
                AddFlowBalanceConstraints(solver, network, flowVars);
                AddCapacityConstraints(solver, network, flowVars);
                AddDemandSatisfactionConstraints(solver, network, flowVars);
                
                if (settings.EnablePressureConstraints)
                {
                    _pressureService.AddPressureConstraints(solver, network, flowVars, pressureVars, settings);
                }

                if (settings.EnableCompressorStations)
                {
                    _compressorService.AddCompressorConstraints(solver, network, flowVars, pressureVars, compressorVars, settings);
                }

                // Add cost calculation constraints
                AddCostCalculationConstraints(solver, network, flowVars, compressorVars, costVars, settings);

                // Create objective function to minimize total cost
                var objective = CreateCostMinimizationObjective(solver, costVars, settings);

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
                    CalculateCosts(result, network, settings);

                    result.ObjectiveValue = objective.Value();
                    result.Messages.Add($"Minimized total cost: ${result.TotalCost.TotalCost:F2}");
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

        private Dictionary<string, Variable> CreateCostVariables(Solver solver, PipelineNetwork network, OptimizationSettings settings)
        {
            var costVars = new Dictionary<string, Variable>();

            // Transportation cost variables
            foreach (var segment in network.GetActiveSegments())
            {
                costVars[$"transport_{segment.Id}"] = solver.MakeNumVar(0, double.PositiveInfinity, $"transport_cost_{segment.Id}");
            }

            // Fuel cost variables
            if (settings.EnableCompressorStations)
            {
                foreach (var compressor in network.GetCompressorStations())
                {
                    costVars[$"fuel_{compressor.Id}"] = solver.MakeNumVar(0, double.PositiveInfinity, $"fuel_cost_{compressor.Id}");
                    costVars[$"compressor_{compressor.Id}"] = solver.MakeNumVar(0, double.PositiveInfinity, $"compressor_cost_{compressor.Id}");
                }
            }

            // Total cost variable
            costVars["total"] = solver.MakeNumVar(0, double.PositiveInfinity, "total_cost");

            return costVars;
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

        private void AddDemandSatisfactionConstraints(Solver solver, PipelineNetwork network, Dictionary<string, Variable> flowVars)
        {
            // Ensure all delivery point demands are satisfied exactly
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

        private void AddCostCalculationConstraints(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> compressorVars, 
            Dictionary<string, Variable> costVars, OptimizationSettings settings)
        {
            // Transportation cost constraints
            foreach (var segment in network.GetActiveSegments())
            {
                var transportCostVar = costVars[$"transport_{segment.Id}"];
                var flowVar = flowVars[segment.Id];
                
                // Linear approximation: transport_cost = flow * unit_cost
                var constraint = solver.MakeConstraint(0, 0, $"transport_cost_{segment.Id}");
                constraint.SetCoefficient(transportCostVar, 1.0);
                constraint.SetCoefficient(flowVar, -segment.TransportationCost);
            }

            // Fuel and compressor cost constraints
            if (settings.EnableCompressorStations)
            {
                var fuelCost = GetParameterValue(settings, "FuelCostPerMMscf", 3.50);
                var compressorCost = GetParameterValue(settings, "CompressorOperatingCost", 0.01);

                foreach (var compressor in network.GetCompressorStations())
                {
                    if (compressorVars.TryGetValue($"{compressor.Id}_fuel", out var fuelVar) &&
                        costVars.TryGetValue($"fuel_{compressor.Id}", out var fuelCostVar))
                    {
                        // fuel_cost = fuel_consumption * fuel_price
                        var fuelConstraint = solver.MakeConstraint(0, 0, $"fuel_cost_{compressor.Id}");
                        fuelConstraint.SetCoefficient(fuelCostVar, 1.0);
                        fuelConstraint.SetCoefficient(fuelVar, -fuelCost);
                    }

                    if (compressorVars.TryGetValue($"{compressor.Id}_boost", out var boostVar) &&
                        costVars.TryGetValue($"compressor_{compressor.Id}", out var compCostVar))
                    {
                        // compressor_cost = pressure_boost * operating_cost
                        var compConstraint = solver.MakeConstraint(0, 0, $"compressor_cost_{compressor.Id}");
                        compConstraint.SetCoefficient(compCostVar, 1.0);
                        compConstraint.SetCoefficient(boostVar, -compressorCost);
                    }
                }
            }

            // Total cost constraint
            var totalCostConstraint = solver.MakeConstraint(0, 0, "total_cost_calculation");
            totalCostConstraint.SetCoefficient(costVars["total"], 1.0);

            // Add transportation costs
            foreach (var segment in network.GetActiveSegments())
            {
                totalCostConstraint.SetCoefficient(costVars[$"transport_{segment.Id}"], -1.0);
            }

            // Add fuel and compressor costs
            if (settings.EnableCompressorStations)
            {
                foreach (var compressor in network.GetCompressorStations())
                {
                    if (costVars.TryGetValue($"fuel_{compressor.Id}", out var fuelCostVar))
                    {
                        totalCostConstraint.SetCoefficient(fuelCostVar, -1.0);
                    }
                    if (costVars.TryGetValue($"compressor_{compressor.Id}", out var compCostVar))
                    {
                        totalCostConstraint.SetCoefficient(compCostVar, -1.0);
                    }
                }
            }
        }

        private Objective CreateCostMinimizationObjective(Solver solver, Dictionary<string, Variable> costVars, OptimizationSettings settings)
        {
            var objective = solver.Objective();
            objective.SetMinimization();

            // Get weights for different cost components
            var transportWeight = GetParameterValue(settings, "TransportationWeight", 1.0);
            var fuelWeight = GetParameterValue(settings, "FuelWeight", 1.0);
            var compressorWeight = GetParameterValue(settings, "CompressorWeight", 1.0);

            // Minimize total cost with component weights
            objective.SetCoefficient(costVars["total"], 1.0);

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

        private void CalculateCosts(OptimizationResult result, PipelineNetwork network, OptimizationSettings settings)
        {
            var costs = result.TotalCost;
            var fuelCost = GetParameterValue(settings, "FuelCostPerMMscf", 3.50);
            var compressorCost = GetParameterValue(settings, "CompressorOperatingCost", 0.01);

            // Transportation costs
            costs.TransportationCost = result.SegmentFlows.Values.Sum(f => f.TransportationCost);

            // Fuel and compressor costs
            costs.FuelCost = result.PointPressures.Values.Sum(p => p.FuelConsumption * fuelCost);
            costs.CompressorCost = result.PointPressures.Values.Where(p => p.PressureBoost > 0).Sum(p => p.PressureBoost * compressorCost);
        }
    }
}
