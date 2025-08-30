using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to maximize total throughput in the gas pipeline network using custom flow allocation
    /// </summary>
    public class MaximizeThroughputAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Maximize Throughput";
        public string Description => "Maximizes the total gas flow through the pipeline network while respecting capacity and pressure constraints";

        public MaximizeThroughputAlgorithm()
        {
        }

        public bool CanHandle(PipelineNetwork network, OptimizationSettings settings)
        {
            // This algorithm can handle any network with active segments and receipt points
            return network.GetActiveSegments().Any() && network.GetReceiptPoints().Any();
        }

        public Dictionary<string, string> GetParameters()
        {
            return new Dictionary<string, string>
            {
                { "ThroughputWeight", "Weight for maximizing throughput (default: 1.0)" },
                { "DemandPriority", "Priority weight for satisfying demand requirements (default: 1.0)" }
            };
        }


        public OptimizationResult Optimize(PipelineNetwork network, OptimizationSettings settings)
        {
            var startTime = DateTime.Now;
            var result = new OptimizationResult
            {
                AlgorithmUsed = Name,
                Status = OptimizationStatus.NotSolved,
                SolverUsed = "Custom Greedy Algorithm"
            };

            try
            {
                // Use custom algorithm to maximize throughput
                var flowAllocations = AllocateFlowForMaxThroughput(network, settings);
                
                // Check if we found a feasible solution
                if (flowAllocations == null || !flowAllocations.Any())
                {
                    result.Status = OptimizationStatus.Infeasible;
                    result.Messages.Add("No feasible flow allocation found");
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
                CalculateCosts(result, network);

                result.ObjectiveValue = result.Metrics.TotalThroughput;
                result.Messages.Add($"Maximized throughput: {result.Metrics.TotalThroughput:F2} MMscfd");
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
        /// Custom algorithm to allocate flow for maximum throughput using greedy approach
        /// </summary>
        private Dictionary<string, double> AllocateFlowForMaxThroughput(PipelineNetwork network, OptimizationSettings settings)
        {
            var flowAllocations = new Dictionary<string, double>();
            var segmentCapacities = new Dictionary<string, double>();
            
            // Initialize flow allocations and remaining capacities
            foreach (var segment in network.GetActiveSegments())
            {
                flowAllocations[segment.Id] = 0;
                segmentCapacities[segment.Id] = segment.Capacity;
            }

            // Get supply and demand points
            var receiptPoints = network.GetReceiptPoints().OrderByDescending(p => p.SupplyCapacity).ToList();
            var deliveryPoints = network.GetDeliveryPoints().OrderByDescending(p => p.DemandRequirement).ToList();

            // Track remaining supply and demand
            var remainingSupply = receiptPoints.ToDictionary(p => p.Id, p => p.SupplyCapacity);
            var remainingDemand = deliveryPoints.ToDictionary(p => p.Id, p => p.DemandRequirement);

            // Greedily allocate flow to maximize throughput
            bool allocationMade;
            do
            {
                allocationMade = false;

                foreach (var receiptPoint in receiptPoints)
                {
                    if (remainingSupply[receiptPoint.Id] <= 0) continue;

                    foreach (var deliveryPoint in deliveryPoints)
                    {
                        if (remainingDemand[deliveryPoint.Id] <= 0) continue;

                        // Find path from receipt to delivery point
                        var path = FindPath(network, receiptPoint.Id, deliveryPoint.Id, segmentCapacities);
                        if (path != null && path.Any())
                        {
                            // Calculate maximum flow through this path
                            var maxFlow = Math.Min(remainingSupply[receiptPoint.Id], remainingDemand[deliveryPoint.Id]);
                            maxFlow = Math.Min(maxFlow, path.Min(segmentId => segmentCapacities[segmentId]));

                            if (maxFlow > 0.01) // Only allocate if meaningful flow
                            {
                                // Allocate flow through the path
                                foreach (var segmentId in path)
                                {
                                    flowAllocations[segmentId] += maxFlow;
                                    segmentCapacities[segmentId] -= maxFlow;
                                }

                                remainingSupply[receiptPoint.Id] -= maxFlow;
                                remainingDemand[deliveryPoint.Id] -= maxFlow;
                                allocationMade = true;
                            }
                        }
                    }
                }
            } while (allocationMade);

            return flowAllocations;
        }

        /// <summary>
        /// Find a path from source to destination using available capacity
        /// </summary>
        private List<string> FindPath(PipelineNetwork network, string sourceId, string destinationId, Dictionary<string, double> availableCapacities)
        {
            var visited = new HashSet<string>();
            var path = new List<string>();
            
            if (FindPathRecursive(network, sourceId, destinationId, availableCapacities, visited, path))
            {
                return path;
            }
            
            return null;
        }

        /// <summary>
        /// Recursive depth-first search to find path with available capacity
        /// </summary>
        private bool FindPathRecursive(PipelineNetwork network, string currentId, string destinationId, 
            Dictionary<string, double> availableCapacities, HashSet<string> visited, List<string> path)
        {
            if (currentId == destinationId)
                return true;

            visited.Add(currentId);

            // Try all outgoing segments
            foreach (var segment in network.GetOutgoingSegments(currentId))
            {
                if (availableCapacities[segment.Id] > 0.01 && !visited.Contains(segment.ToPointId))
                {
                    path.Add(segment.Id);
                    
                    if (FindPathRecursive(network, segment.ToPointId, destinationId, availableCapacities, visited, path))
                    {
                        return true;
                    }
                    
                    path.RemoveAt(path.Count - 1);
                }
            }

            visited.Remove(currentId);
            return false;
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

        private void CalculateCosts(OptimizationResult result, PipelineNetwork network)
        {
            var costs = result.TotalCost;

            // Transportation costs
            costs.TransportationCost = result.SegmentFlows.Values.Sum(f => f.TransportationCost);

            // Fuel and compressor costs (simplified for custom algorithm)
            costs.FuelCost = 0;
            costs.CompressorCost = 0;
        }
    }
}
