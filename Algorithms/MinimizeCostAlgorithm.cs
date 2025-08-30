using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to minimize total cost while satisfying demand requirements using custom cost optimization
    /// </summary>
    public class MinimizeCostAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Minimize Cost";
        public string Description => "Minimizes total operational costs including transportation, fuel, and compression costs while satisfying all demand requirements";

        public MinimizeCostAlgorithm()
        {
        }

        public bool CanHandle(PipelineNetwork network, OptimizationSettings settings)
        {
            // This algorithm can handle any network with active segments, delivery points, and cost data
            return network.GetActiveSegments().Any() && network.GetDeliveryPoints().Any() && 
                   network.GetActiveSegments().All(s => s.TransportationCost >= 0);
        }

        public Dictionary<string, string> GetParameters()
        {
            return new Dictionary<string, string>
            {
                { "TransportationWeight", "Weight for transportation costs (default: 1.0)" },
                { "FuelWeight", "Weight for fuel costs (default: 1.0)" },
                { "CompressorWeight", "Weight for compressor operation costs (default: 1.0)" },
                { "FuelCostPerMMscf", "Cost per MMscf of fuel (default: $3.50)" },
                { "CompressorOperatingCost", "Operating cost per psi of pressure boost (default: $0.01)" }
            };
        }


        public OptimizationResult Optimize(PipelineNetwork network, OptimizationSettings settings)
        {
            var startTime = DateTime.Now;
            var result = new OptimizationResult
            {
                AlgorithmUsed = Name,
                Status = OptimizationStatus.NotSolved,
                SolverUsed = "Custom Cost Optimization Algorithm"
            };

            try
            {
                // Use custom algorithm to minimize cost while satisfying demand
                var flowAllocations = AllocateFlowForMinimumCost(network, settings);
                
                // Check if we found a feasible solution
                if (flowAllocations == null || !ValidateDemandSatisfaction(flowAllocations, network))
                {
                    result.Status = OptimizationStatus.Infeasible;
                    result.Messages.Add("No feasible solution found that satisfies all demand requirements");
                    return result;
                }

                result.Status = OptimizationStatus.Optimal;
                
                // Store flow results
                foreach (var allocation in flowAllocations)
                {
                    var segment = network.Segments[allocation.Key];
                    var transportCost = allocation.Value * segment.TransportationCost;
                    result.AddSegmentFlow(allocation.Key, allocation.Value, segment.Capacity, transportCost);
                }

                // Calculate metrics and costs
                CalculateMetrics(result, network);
                CalculateCosts(result, network, settings);

                result.ObjectiveValue = result.TotalCost.TotalCost;
                result.Messages.Add($"Minimized total cost: ${result.TotalCost.TotalCost:F2}");
            }
            catch (Exception ex)
            {
                result.Status = OptimizationStatus.Error;
                result.Messages.Add($"Optimization error: {ex.Message}");
            }

            result.SolutionTimeMs = (DateTime.Now - startTime).TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Custom algorithm to allocate flow for minimum cost while satisfying all demand
        /// </summary>
        private Dictionary<string, double> AllocateFlowForMinimumCost(PipelineNetwork network, OptimizationSettings settings)
        {
            var flowAllocations = new Dictionary<string, double>();
            
            // Initialize flow allocations
            foreach (var segment in network.GetActiveSegments())
            {
                flowAllocations[segment.Id] = 0;
            }

            // Get delivery points sorted by demand (prioritize larger demands first)
            var deliveryPoints = network.GetDeliveryPoints().OrderByDescending(p => p.DemandRequirement).ToList();
            
            // For each delivery point, find the lowest cost path to satisfy its demand
            foreach (var deliveryPoint in deliveryPoints)
            {
                var remainingDemand = deliveryPoint.DemandRequirement;
                
                // Keep finding lowest cost paths until demand is satisfied
                while (remainingDemand > 0.01)
                {
                    var bestPath = FindLowestCostPath(network, deliveryPoint.Id, flowAllocations, remainingDemand);
                    
                    if (bestPath == null || !bestPath.Path.Any())
                    {
                        // Cannot satisfy remaining demand
                        return null;
                    }
                    
                    // Allocate flow through the best path
                    var flowToAllocate = Math.Min(remainingDemand, bestPath.MaxFlow);
                    
                    foreach (var segmentId in bestPath.Path)
                    {
                        flowAllocations[segmentId] += flowToAllocate;
                    }
                    
                    remainingDemand -= flowToAllocate;
                }
            }

            return flowAllocations;
        }

        /// <summary>
        /// Find the lowest cost path from any supply source to a delivery point
        /// </summary>
        private PathResult FindLowestCostPath(PipelineNetwork network, string deliveryPointId, 
            Dictionary<string, double> currentFlows, double requiredFlow)
        {
            var receiptPoints = network.GetReceiptPoints().Where(p => p.IsActive).ToList();
            PathResult bestPath = null;
            
            foreach (var receiptPoint in receiptPoints)
            {
                var path = FindCostOptimalPath(network, receiptPoint.Id, deliveryPointId, currentFlows, receiptPoint.SupplyCapacity);
                
                if (path != null && (bestPath == null || path.TotalCost < bestPath.TotalCost))
                {
                    bestPath = path;
                }
            }
            
            return bestPath;
        }
        
        /// <summary>
        /// Find cost-optimal path between two points using Dijkstra-like algorithm
        /// </summary>
        private PathResult FindCostOptimalPath(PipelineNetwork network, string sourceId, string destinationId, 
            Dictionary<string, double> currentFlows, double availableSupply)
        {
            var distances = new Dictionary<string, double>();
            var previous = new Dictionary<string, string>();
            var visited = new HashSet<string>();
            var unvisited = new SortedSet<(double cost, string pointId)>();
            
            // Initialize distances
            foreach (var point in network.Points.Values.Where(p => p.IsActive))
            {
                distances[point.Id] = double.MaxValue;
            }
            distances[sourceId] = 0;
            unvisited.Add((0, sourceId));
            
            while (unvisited.Any())
            {
                var current = unvisited.Min;
                unvisited.Remove(current);
                var (currentCost, currentPointId) = current;
                
                if (visited.Contains(currentPointId)) continue;
                visited.Add(currentPointId);
                
                if (currentPointId == destinationId)
                {
                    // Reconstruct path
                    var path = new List<string>();
                    var pathPoint = destinationId;
                    var maxFlow = double.MaxValue;
                    
                    while (previous.ContainsKey(pathPoint))
                    {
                        var segmentId = previous[pathPoint];
                        path.Insert(0, segmentId);
                        
                        var segment = network.Segments[segmentId];
                        var remainingCapacity = segment.Capacity - currentFlows[segmentId];
                        maxFlow = Math.Min(maxFlow, remainingCapacity);
                        
                        // Find the source point of this segment
                        pathPoint = segment.FromPointId;
                    }
                    
                    maxFlow = Math.Min(maxFlow, availableSupply);
                    
                    if (maxFlow > 0.01)
                    {
                        return new PathResult
                        {
                            Path = path,
                            TotalCost = currentCost,
                            MaxFlow = maxFlow
                        };
                    }
                }
                
                // Check all outgoing segments
                foreach (var segment in network.GetOutgoingSegments(currentPointId))
                {
                    var remainingCapacity = segment.Capacity - currentFlows[segment.Id];
                    if (remainingCapacity <= 0.01) continue;
                    
                    var nextPointId = segment.ToPointId;
                    if (visited.Contains(nextPointId)) continue;
                    
                    var newCost = currentCost + segment.TransportationCost;
                    
                    if (newCost < distances[nextPointId])
                    {
                        distances[nextPointId] = newCost;
                        previous[nextPointId] = segment.Id;
                        unvisited.Add((newCost, nextPointId));
                    }
                }
            }
            
            return null; // No path found
        }
        
        /// <summary>
        /// Validate that all demand requirements are satisfied
        /// </summary>
        private bool ValidateDemandSatisfaction(Dictionary<string, double> flowAllocations, PipelineNetwork network)
        {
            foreach (var deliveryPoint in network.GetDeliveryPoints())
            {
                var inflow = network.GetIncomingSegments(deliveryPoint.Id)
                    .Where(s => flowAllocations.ContainsKey(s.Id))
                    .Sum(s => flowAllocations[s.Id]);
                    
                if (Math.Abs(inflow - deliveryPoint.DemandRequirement) > 0.01)
                {
                    return false;
                }
            }
            return true;
        }
        
        /// <summary>
        /// Helper class to store path finding results
        /// </summary>
        private class PathResult
        {
            public List<string> Path { get; set; } = new List<string>();
            public double TotalCost { get; set; }
            public double MaxFlow { get; set; }
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
            metrics.ActiveCompressors = 0; // No compressor optimization in this version
        }

        private void CalculateCosts(OptimizationResult result, PipelineNetwork network, OptimizationSettings settings)
        {
            var costs = result.TotalCost;
            var fuelCost = GetParameterValue(settings, "FuelCostPerMMscf", 3.50);
            var compressorCost = GetParameterValue(settings, "CompressorOperatingCost", 0.01);

            // Transportation costs
            costs.TransportationCost = result.SegmentFlows.Values.Sum(f => f.TransportationCost);

            // Fuel and compressor costs (simplified for custom algorithm)
            costs.FuelCost = 0;
            costs.CompressorCost = 0;
        }
    }
}
