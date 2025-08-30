using Google.OrTools.LinearSolver;
using GasPipelineOptimization.Models;
using GasPipelineOptimization.Utilities;

namespace GasPipelineOptimization.Services
{
    /// <summary>
    /// Service for handling pressure constraints in gas pipeline optimization
    /// </summary>
    public class PressureConstraintService
    {
        /// <summary>
        /// Adds pressure constraints to the optimization model
        /// </summary>
        public void AddPressureConstraints(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> pressureVars, 
            OptimizationSettings settings)
        {
            if (!settings.EnablePressureConstraints)
                return;

            // Add pressure bounds for each point
            AddPressureBounds(solver, network, pressureVars);

            // Add pressure drop constraints for each segment
            if (settings.UseLinearPressureApproximation)
            {
                AddLinearPressureDropConstraints(solver, network, flowVars, pressureVars, settings);
            }
            else
            {
                AddNonlinearPressureDropConstraints(solver, network, flowVars, pressureVars, settings);
            }
        }

        /// <summary>
        /// Adds pressure bounds constraints for each point
        /// </summary>
        private void AddPressureBounds(Solver solver, PipelineNetwork network, 
            Dictionary<string, Variable> pressureVars)
        {
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                if (pressureVars.TryGetValue(point.Id, out var pressureVar))
                {
                    var minPressureSquared = point.MinPressure * point.MinPressure;
                    var maxPressureSquared = point.MaxPressure * point.MaxPressure;

                    // Pressure squared bounds
                    solver.MakeConstraint(minPressureSquared, maxPressureSquared, $"pressure_bounds_{point.Id}")
                        .SetCoefficient(pressureVar, 1.0);
                }
            }
        }

        /// <summary>
        /// Adds linear approximation of pressure drop constraints
        /// </summary>
        private void AddLinearPressureDropConstraints(Solver solver, PipelineNetwork network,
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> pressureVars,
            OptimizationSettings settings)
        {
            foreach (var segment in network.GetActiveSegments())
            {
                if (!pressureVars.TryGetValue(segment.FromPointId, out var fromPressureVar) ||
                    !pressureVars.TryGetValue(segment.ToPointId, out var toPressureVar) ||
                    !flowVars.TryGetValue(segment.Id, out var flowVar))
                {
                    continue;
                }

                // Linear approximation: P_from^2 - P_to^2 >= k * |flow|
                // Since we can't handle absolute value directly in linear programming,
                // we'll use a piecewise linear approximation

                var segments = settings.LinearApproximationSegments;
                var maxFlow = segment.Capacity;
                var k = segment.PressureDropConstant;

                for (int i = 0; i < segments; i++)
                {
                    var flowPoint = (i + 1) * maxFlow / segments;
                    var pressureDrop = k * flowPoint * flowPoint;

                    // Create constraint: P_from^2 - P_to^2 >= k * flow_point^2 when flow >= flow_point
                    var constraint = solver.MakeConstraint(-double.PositiveInfinity, pressureDrop, 
                        $"pressure_drop_{segment.Id}_seg_{i}");
                    
                    constraint.SetCoefficient(fromPressureVar, 1.0);
                    constraint.SetCoefficient(toPressureVar, -1.0);

                    // Add flow constraint for this segment of the approximation
                    if (i > 0)
                    {
                        var prevFlowPoint = i * maxFlow / segments;
                        var slope = (pressureDrop - k * prevFlowPoint * prevFlowPoint) / (flowPoint - prevFlowPoint);
                        constraint.SetCoefficient(flowVar, -slope);
                    }
                }
            }
        }

        /// <summary>
        /// Adds nonlinear pressure drop constraints (requires SCIP solver)
        /// </summary>
        private void AddNonlinearPressureDropConstraints(Solver solver, PipelineNetwork network,
            Dictionary<string, Variable> flowVars, Dictionary<string, Variable> pressureVars,
            OptimizationSettings settings)
        {
            // Note: This is a simplified implementation for nonlinear constraints
            // In practice, this would require more sophisticated constraint handling
            // or the use of specialized nonlinear optimization solvers

            foreach (var segment in network.GetActiveSegments())
            {
                if (!pressureVars.TryGetValue(segment.FromPointId, out var fromPressureVar) ||
                    !pressureVars.TryGetValue(segment.ToPointId, out var toPressureVar) ||
                    !flowVars.TryGetValue(segment.Id, out var flowVar))
                {
                    continue;
                }

                // For SCIP solver, we can add quadratic constraints
                // P_from^2 - P_to^2 >= k * flow^2

                if (solver.SolverVersion().Contains("SCIP"))
                {
                    // Create auxiliary variables for flow squared
                    var flowSquaredVar = solver.MakeNumVar(0, segment.Capacity * segment.Capacity, 
                        $"flow_squared_{segment.Id}");

                    // Constraint: flow_squared = flow^2 (approximated by linearization around operating points)
                    AddQuadraticApproximation(solver, flowVar, flowSquaredVar, segment.Capacity, settings);

                    // Main pressure drop constraint: P_from^2 - P_to^2 >= k * flow^2
                    var pressureDropConstraint = solver.MakeConstraint(0, double.PositiveInfinity, 
                        $"pressure_drop_{segment.Id}");
                    
                    pressureDropConstraint.SetCoefficient(fromPressureVar, 1.0);
                    pressureDropConstraint.SetCoefficient(toPressureVar, -1.0);
                    pressureDropConstraint.SetCoefficient(flowSquaredVar, -segment.PressureDropConstant);
                }
                else
                {
                    // Fall back to linear approximation for GLOP solver
                    AddLinearPressureDropConstraints(solver, network, flowVars, pressureVars, settings);
                    return;
                }
            }
        }

        /// <summary>
        /// Adds quadratic approximation constraints for flow squared
        /// </summary>
        private void AddQuadraticApproximation(Solver solver, Variable flowVar, Variable flowSquaredVar, 
            double maxFlow, OptimizationSettings settings)
        {
            var segments = settings.LinearApproximationSegments;
            
            // Piecewise linear approximation of flow^2
            for (int i = 0; i < segments; i++)
            {
                var x1 = i * maxFlow / segments;
                var x2 = (i + 1) * maxFlow / segments;
                var y1 = x1 * x1;
                var y2 = x2 * x2;

                // Linear segment: flow_squared >= slope * flow + intercept
                var slope = (y2 - y1) / (x2 - x1);
                var intercept = y1 - slope * x1;

                var constraint = solver.MakeConstraint(intercept, double.PositiveInfinity, 
                    $"quad_approx_{flowVar.Name()}_seg_{i}");
                
                constraint.SetCoefficient(flowSquaredVar, 1.0);
                constraint.SetCoefficient(flowVar, -slope);
            }
        }

        /// <summary>
        /// Validates pressure constraint satisfaction in the solution
        /// </summary>
        public bool ValidatePressureConstraints(PipelineNetwork network, OptimizationResult result, 
            OptimizationSettings settings, out List<string> violations)
        {
            violations = new List<string>();

            if (!settings.EnablePressureConstraints)
                return true;

            // Check pressure bounds
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                if (result.PointPressures.TryGetValue(point.Id, out var pressureResult))
                {
                    if (pressureResult.Pressure < point.MinPressure - settings.FeasibilityTolerance)
                    {
                        violations.Add($"Point {point.Id}: Pressure {pressureResult.Pressure:F1} below minimum {point.MinPressure:F1}");
                    }
                    
                    if (pressureResult.Pressure > point.MaxPressure + settings.FeasibilityTolerance)
                    {
                        violations.Add($"Point {point.Id}: Pressure {pressureResult.Pressure:F1} above maximum {point.MaxPressure:F1}");
                    }
                }
            }

            // Check pressure drop constraints
            foreach (var segment in network.GetActiveSegments())
            {
                if (result.PointPressures.TryGetValue(segment.FromPointId, out var fromPressure) &&
                    result.PointPressures.TryGetValue(segment.ToPointId, out var toPressure) &&
                    result.SegmentFlows.TryGetValue(segment.Id, out var flowResult))
                {
                    var pressureDropLeft = fromPressure.PressureSquared - toPressure.PressureSquared;
                    var pressureDropRight = segment.PressureDropConstant * flowResult.Flow * Math.Abs(flowResult.Flow);
                    
                    if (pressureDropLeft < pressureDropRight - settings.FeasibilityTolerance)
                    {
                        violations.Add($"Segment {segment.Id}: Pressure drop constraint violated. " +
                            $"Required: {pressureDropRight:F2}, Actual: {pressureDropLeft:F2}");
                    }
                }
            }

            return !violations.Any();
        }

        /// <summary>
        /// Calculates pressure at delivery points based on flow and network topology
        /// </summary>
        public Dictionary<string, double> CalculateDeliveryPressures(PipelineNetwork network, 
            Dictionary<string, double> flows, Dictionary<string, double> sourcePressures)
        {
            var deliveryPressures = new Dictionary<string, double>();

            foreach (var deliveryPoint in network.GetDeliveryPoints())
            {
                var pressure = CalculatePressureAtPoint(network, deliveryPoint.Id, flows, sourcePressures);
                deliveryPressures[deliveryPoint.Id] = pressure;
            }

            return deliveryPressures;
        }

        /// <summary>
        /// Calculates pressure at a specific point using flow-based pressure drop
        /// </summary>
        private double CalculatePressureAtPoint(PipelineNetwork network, string pointId, 
            Dictionary<string, double> flows, Dictionary<string, double> sourcePressures)
        {
            // This is a simplified calculation - in practice, this would use
            // more sophisticated hydraulic calculations
            
            var incomingSegments = network.GetIncomingSegments(pointId).ToList();
            
            if (!incomingSegments.Any())
            {
                // Source point - return source pressure if available
                return sourcePressures.TryGetValue(pointId, out var sourcePressure) ? sourcePressure : 0;
            }

            // Calculate weighted average pressure from incoming segments
            var totalFlow = 0.0;
            var weightedPressure = 0.0;

            foreach (var segment in incomingSegments)
            {
                if (flows.TryGetValue(segment.Id, out var flow) && flow > 0)
                {
                    var upstreamPressure = CalculatePressureAtPoint(network, segment.FromPointId, flows, sourcePressures);
                    var pressureDrop = MathUtils.CalculatePressureDrop(flow, segment.PressureDropConstant);
                    var downstreamPressure = Math.Sqrt(Math.Max(0, upstreamPressure * upstreamPressure - pressureDrop));
                    
                    weightedPressure += downstreamPressure * flow;
                    totalFlow += flow;
                }
            }

            return totalFlow > 0 ? weightedPressure / totalFlow : 0;
        }

        /// <summary>
        /// Estimates required compressor boost to meet pressure requirements
        /// </summary>
        public Dictionary<string, double> EstimateRequiredCompressorBoost(PipelineNetwork network, 
            Dictionary<string, double> flows, OptimizationSettings settings)
        {
            var requiredBoosts = new Dictionary<string, double>();

            foreach (var compressor in network.GetCompressorStations())
            {
                var incomingSegments = network.GetIncomingSegments(compressor.Id);
                var outgoingSegments = network.GetOutgoingSegments(compressor.Id);

                if (incomingSegments.Any() && outgoingSegments.Any())
                {
                    // Calculate minimum inlet pressure
                    var minInletPressure = incomingSegments
                        .Where(s => flows.ContainsKey(s.Id) && flows[s.Id] > 0)
                        .Min(s => EstimateSegmentOutletPressure(network, s, flows[s.Id]));

                    // Calculate required outlet pressure
                    var requiredOutletPressure = outgoingSegments
                        .Where(s => flows.ContainsKey(s.Id) && flows[s.Id] > 0)
                        .Max(s => EstimateSegmentInletPressure(network, s, flows[s.Id]));

                    // Required boost
                    var requiredBoost = Math.Max(0, requiredOutletPressure - minInletPressure);
                    requiredBoosts[compressor.Id] = Math.Min(requiredBoost, compressor.MaxPressureBoost);
                }
            }

            return requiredBoosts;
        }

        private double EstimateSegmentOutletPressure(PipelineNetwork network, Segment segment, double flow)
        {
            if (network.Points.TryGetValue(segment.FromPointId, out var fromPoint))
            {
                var inletPressure = fromPoint.CurrentPressure;
                var pressureDrop = MathUtils.CalculatePressureDrop(flow, segment.PressureDropConstant);
                return Math.Sqrt(Math.Max(0, inletPressure * inletPressure - pressureDrop));
            }
            return 0;
        }

        private double EstimateSegmentInletPressure(PipelineNetwork network, Segment segment, double flow)
        {
            if (network.Points.TryGetValue(segment.ToPointId, out var toPoint))
            {
                var outletPressure = toPoint.MinPressure; // Target minimum delivery pressure
                var pressureDrop = MathUtils.CalculatePressureDrop(flow, segment.PressureDropConstant);
                return Math.Sqrt(outletPressure * outletPressure + pressureDrop);
            }
            return 0;
        }
    }
}
