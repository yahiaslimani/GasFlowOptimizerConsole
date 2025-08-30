using Google.OrTools.LinearSolver;
using GasPipelineOptimization.Models;
using GasPipelineOptimization.Utilities;

namespace GasPipelineOptimization.Services
{
    /// <summary>
    /// Service for handling compressor station constraints and operations
    /// </summary>
    public class CompressorService
    {
        /// <summary>
        /// Adds compressor constraints to the optimization model
        /// </summary>
        public void AddCompressorConstraints(Solver solver, PipelineNetwork network,
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> pressureVars,
            Dictionary<string, Variable> compressorVars, OptimizationSettings settings)
        {
            if (!settings.EnableCompressorStations)
                return;

            foreach (var compressor in network.GetCompressorStations())
            {
                AddCompressorOperationConstraints(solver, compressor, flowVars, pressureVars, compressorVars, settings);
                AddFuelConsumptionConstraints(solver, compressor, flowVars, compressorVars, settings);
                AddPressureBoostConstraints(solver, compressor, pressureVars, compressorVars, settings);
            }
        }

        /// <summary>
        /// Adds constraints for compressor operation (on/off, capacity limits)
        /// </summary>
        private void AddCompressorOperationConstraints(Solver solver, Point compressor,
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> pressureVars,
            Dictionary<string, Variable> compressorVars, OptimizationSettings settings)
        {
            if (!compressorVars.TryGetValue($"{compressor.Id}_active", out var activeVar))
                return;

            // Compressor can only boost pressure when active
            if (compressorVars.TryGetValue($"{compressor.Id}_boost", out var boostVar))
            {
                // boost <= max_boost * active
                var maxBoostConstraint = solver.MakeConstraint(0, 0, $"max_boost_{compressor.Id}");
                maxBoostConstraint.SetCoefficient(boostVar, 1.0);
                maxBoostConstraint.SetCoefficient(activeVar, -compressor.MaxPressureBoost);
            }

            // Minimum operating flow when compressor is active
            var incomingSegments = GetIncomingSegments(solver, compressor.Id, flowVars);
            var totalInflowVar = CreateTotalInflowVariable(solver, compressor.Id, incomingSegments);

            if (totalInflowVar != null)
            {
                // Minimum flow constraint: total_inflow >= min_flow * active
                var minFlow = 10.0; // Minimum operating flow (MMscfd)
                var minFlowConstraint = solver.MakeConstraint(0, double.PositiveInfinity, $"min_flow_{compressor.Id}");
                minFlowConstraint.SetCoefficient(totalInflowVar, 1.0);
                minFlowConstraint.SetCoefficient(activeVar, -minFlow);
            }
        }

        /// <summary>
        /// Adds fuel consumption constraints for compressor operation
        /// </summary>
        private void AddFuelConsumptionConstraints(Solver solver, Point compressor,
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> compressorVars,
            OptimizationSettings settings)
        {
            if (!compressorVars.TryGetValue($"{compressor.Id}_fuel", out var fuelVar) ||
                !compressorVars.TryGetValue($"{compressor.Id}_active", out var activeVar))
                return;

            // Calculate total throughput for fuel consumption
            var incomingSegments = GetIncomingSegments(solver, compressor.Id, flowVars);
            var totalInflowVar = CreateTotalInflowVariable(solver, compressor.Id, incomingSegments);

            if (totalInflowVar != null)
            {
                // Base fuel consumption: fuel = base_rate * active + flow_rate * throughput
                var baseFuelRate = 1.0; // Base fuel consumption when running (MMscf/day)
                var flowFuelRate = compressor.FuelConsumptionRate; // Additional fuel per unit flow

                // fuel >= base_rate * active + flow_rate * throughput
                var fuelConstraint = solver.MakeConstraint(0, double.PositiveInfinity, $"fuel_consumption_{compressor.Id}");
                fuelConstraint.SetCoefficient(fuelVar, 1.0);
                fuelConstraint.SetCoefficient(activeVar, -baseFuelRate);
                fuelConstraint.SetCoefficient(totalInflowVar, -flowFuelRate);
            }

            // Additional fuel consumption based on pressure boost
            if (compressorVars.TryGetValue($"{compressor.Id}_boost", out var boostVar))
            {
                var boostFuelRate = 0.001; // Additional fuel per psi of boost (MMscf/psi/day)
                
                var boostFuelConstraint = solver.MakeConstraint(0, double.PositiveInfinity, $"boost_fuel_{compressor.Id}");
                boostFuelConstraint.SetCoefficient(fuelVar, 1.0);
                boostFuelConstraint.SetCoefficient(boostVar, -boostFuelRate);
            }
        }

        /// <summary>
        /// Adds pressure boost constraints linking inlet and outlet pressures
        /// </summary>
        private void AddPressureBoostConstraints(Solver solver, Point compressor,
            Dictionary<string, Variable> pressureVars, Dictionary<string, Variable> compressorVars,
            OptimizationSettings settings)
        {
            if (!pressureVars.TryGetValue(compressor.Id, out var compressorPressureVar) ||
                !compressorVars.TryGetValue($"{compressor.Id}_boost", out var boostVar))
                return;

            // For simplification, we assume the compressor pressure variable represents outlet pressure
            // In a more detailed model, we would have separate inlet and outlet pressure variables

            // The pressure boost constraint would be: P_outlet^2 = P_inlet^2 + boost_factor * boost
            // This is simplified here - in practice, compressor performance curves would be used

            // Ensure compressor operates within pressure limits
            var minOutletPressure = compressor.MinPressure * compressor.MinPressure;
            var maxOutletPressure = compressor.MaxPressure * compressor.MaxPressure;

            solver.MakeConstraint(minOutletPressure, maxOutletPressure, $"compressor_pressure_limits_{compressor.Id}")
                .SetCoefficient(compressorPressureVar, 1.0);
        }

        /// <summary>
        /// Gets incoming segments for a compressor station
        /// </summary>
        private List<Variable> GetIncomingSegments(Solver solver, string compressorId, 
            Dictionary<string, Variable> flowVars)
        {
            var incomingVars = new List<Variable>();
            
            foreach (var flowVar in flowVars)
            {
                // This is a simplified approach - in practice, we would use the network topology
                // to determine which segments are actually incoming to the compressor
                if (flowVar.Key.Contains(compressorId) || flowVar.Key.EndsWith($"_{compressorId}"))
                {
                    incomingVars.Add(flowVar.Value);
                }
            }
            
            return incomingVars;
        }

        /// <summary>
        /// Creates a variable representing total inflow to a compressor
        /// </summary>
        private Variable? CreateTotalInflowVariable(Solver solver, string compressorId, 
            List<Variable> incomingSegments)
        {
            if (!incomingSegments.Any())
                return null;

            var totalInflowVar = solver.MakeNumVar(0, double.PositiveInfinity, $"total_inflow_{compressorId}");
            
            // total_inflow = sum of incoming flows
            var inflowConstraint = solver.MakeConstraint(0, 0, $"inflow_sum_{compressorId}");
            inflowConstraint.SetCoefficient(totalInflowVar, 1.0);
            
            foreach (var incomingVar in incomingSegments)
            {
                inflowConstraint.SetCoefficient(incomingVar, -1.0);
            }
            
            return totalInflowVar;
        }

        /// <summary>
        /// Calculates compressor efficiency based on operating conditions
        /// </summary>
        public double CalculateCompressorEfficiency(double throughput, double pressureRatio, 
            double designThroughput, double designPressureRatio)
        {
            // Simplified efficiency calculation
            // In practice, this would use detailed compressor performance curves
            
            var throughputRatio = throughput / Math.Max(designThroughput, 1e-6);
            var pressureRatioNormalized = pressureRatio / Math.Max(designPressureRatio, 1.1);
            
            // Peak efficiency around design point
            var throughputFactor = 1.0 - Math.Pow(throughputRatio - 1.0, 2) * 0.1;
            var pressureFactor = 1.0 - Math.Pow(pressureRatioNormalized - 1.0, 2) * 0.05;
            
            var baseEfficiency = 0.85; // Base mechanical efficiency
            
            return Math.Max(0.5, Math.Min(0.95, baseEfficiency * throughputFactor * pressureFactor));
        }

        /// <summary>
        /// Estimates compressor power requirements
        /// </summary>
        public double EstimateCompressorPower(double throughput, double inletPressure, 
            double outletPressure, double gasTemperature = 60.0)
        {
            // Simplified power calculation for gas compression
            // Power = (k/(k-1)) * (inlet_pressure * flow) * ((outlet/inlet)^((k-1)/k) - 1) / efficiency
            
            var k = 1.3; // Heat capacity ratio for natural gas
            var efficiency = 0.85; // Assumed compressor efficiency
            var gasConstant = 53.35; // ft·lbf/(lbm·°R) for natural gas
            var temperature = gasTemperature + 459.67; // Convert to Rankine
            
            if (inletPressure <= 0 || outletPressure <= inletPressure)
                return 0;
            
            var pressureRatio = outletPressure / inletPressure;
            var compressionRatio = Math.Pow(pressureRatio, (k - 1) / k);
            
            // Convert throughput to mass flow (simplified)
            var massFlow = throughput * 0.0416667; // MMscfd to lbm/s (approximate)
            
            var power = (k / (k - 1)) * gasConstant * temperature * massFlow * (compressionRatio - 1) / efficiency;
            
            // Convert to horsepower
            return power / 550.0;
        }

        /// <summary>
        /// Validates compressor constraints in the optimization result
        /// </summary>
        public bool ValidateCompressorConstraints(PipelineNetwork network, OptimizationResult result,
            OptimizationSettings settings, out List<string> violations)
        {
            violations = new List<string>();

            if (!settings.EnableCompressorStations)
                return true;

            foreach (var compressor in network.GetCompressorStations())
            {
                if (result.PointPressures.TryGetValue(compressor.Id, out var pressureResult))
                {
                    // Check pressure boost limits
                    if (pressureResult.PressureBoost > compressor.MaxPressureBoost + settings.FeasibilityTolerance)
                    {
                        violations.Add($"Compressor {compressor.Id}: Pressure boost {pressureResult.PressureBoost:F1} " +
                            $"exceeds maximum {compressor.MaxPressureBoost:F1}");
                    }

                    // Check fuel consumption reasonableness
                    var maxReasonableFuel = CalculateMaxReasonableFuel(compressor, result);
                    if (pressureResult.FuelConsumption > maxReasonableFuel)
                    {
                        violations.Add($"Compressor {compressor.Id}: Fuel consumption {pressureResult.FuelConsumption:F3} " +
                            $"exceeds reasonable maximum {maxReasonableFuel:F3}");
                    }

                    // Check pressure limits
                    if (pressureResult.Pressure > compressor.MaxPressure + settings.FeasibilityTolerance)
                    {
                        violations.Add($"Compressor {compressor.Id}: Outlet pressure {pressureResult.Pressure:F1} " +
                            $"exceeds maximum {compressor.MaxPressure:F1}");
                    }
                }
            }

            return !violations.Any();
        }

        /// <summary>
        /// Calculates maximum reasonable fuel consumption for a compressor
        /// </summary>
        private double CalculateMaxReasonableFuel(Point compressor, OptimizationResult result)
        {
            // Estimate maximum fuel based on maximum throughput and pressure boost
            var maxThroughput = 1000.0; // Assume maximum throughput (MMscfd)
            var maxFuelRate = compressor.FuelConsumptionRate * maxThroughput;
            var boostFuelRate = compressor.MaxPressureBoost * 0.001; // Additional fuel per psi
            
            return maxFuelRate + boostFuelRate + 2.0; // Base consumption + margin
        }

        /// <summary>
        /// Optimizes compressor staging for multi-stage compression
        /// </summary>
        public List<CompressorStage> OptimizeCompressorStaging(double totalPressureRatio, 
            double maxStageRatio = 3.0)
        {
            var stages = new List<CompressorStage>();
            
            if (totalPressureRatio <= 1.0)
                return stages;

            // Calculate optimal number of stages
            var idealStages = Math.Log(totalPressureRatio) / Math.Log(maxStageRatio);
            var numStages = Math.Max(1, (int)Math.Ceiling(idealStages));
            
            // Calculate pressure ratio per stage
            var stageRatio = Math.Pow(totalPressureRatio, 1.0 / numStages);
            
            for (int i = 0; i < numStages; i++)
            {
                stages.Add(new CompressorStage
                {
                    StageNumber = i + 1,
                    PressureRatio = stageRatio,
                    IsIntercooled = i < numStages - 1 // Intercooling between stages
                });
            }
            
            return stages;
        }
    }

    /// <summary>
    /// Represents a single stage in a multi-stage compressor
    /// </summary>
    public class CompressorStage
    {
        public int StageNumber { get; set; }
        public double PressureRatio { get; set; }
        public bool IsIntercooled { get; set; }
        public double Efficiency { get; set; } = 0.85;
        public double PowerRequirement { get; set; }
    }
}
