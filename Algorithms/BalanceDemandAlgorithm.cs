using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Algorithm to balance demand across multiple paths while minimizing variance in utilization using custom load balancing
    /// </summary>
    public class BalanceDemandAlgorithm : IOptimizationAlgorithm
    {
        public string Name => "Balance Demand";
        public string Description => "Balances gas flow across multiple paths to minimize utilization variance and improve network resilience";

        public BalanceDemandAlgorithm()
        {
        }

        public bool CanHandle(PipelineNetwork network, OptimizationSettings settings)
        {
            // This algorithm can handle any network with active segments and delivery points
            return network.GetActiveSegments().Any() && network.GetDeliveryPoints().Any();
        }

        public Dictionary<string, string> GetParameters()
        {
            return new Dictionary<string, string>
            {
                { "TargetUtilization", "Target utilization percentage for pipeline segments (default: 70%)" },
                { "BalanceWeight", "Weight for utilization balance in optimization (default: 1.0)" },
                { "ThroughputWeight", "Weight for throughput in optimization (default: 0.5)" },
                { "CostWeight", "Weight for cost minimization in optimization (default: 0.3)" },
                { "PathDiversityBonus", "Bonus weight for using diverse paths (default: 0.1)" }
            };
        }

        public OptimizationResult Optimize(PipelineNetwork network, OptimizationSettings settings)
        {
            var startTime = DateTime.Now;
            var result = new OptimizationResult
            {
                AlgorithmUsed = Name,
                Status = OptimizationStatus.NotSolved,
                SolverUsed = "Custom Load Balancing Algorithm"
            };

            try
            {
                // Use custom algorithm to balance demand across paths
                var flowAllocations = AllocateFlowForBalancedUtilization(network, settings);
                
                // Check if we found a feasible solution
                if (flowAllocations == null || !ValidateDemandSatisfaction(flowAllocations, network))
                {
                    result.Status = OptimizationStatus.Infeasible;
                    result.Messages.Add("No feasible solution found that balances utilization while satisfying demand");
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

                var variance = CalculateUtilizationVariance(result);
                result.ObjectiveValue = -variance; // Negative variance as objective (higher is better)
                result.Messages.Add($"Balanced utilization with variance: {variance:F2}%");
                result.Messages.Add($"Average utilization: {result.Metrics.AverageCapacityUtilization:F1}%");
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
        /// Custom algorithm to allocate flow for balanced utilization across all segments
        /// </summary>
        private Dictionary<string, double> AllocateFlowForBalancedUtilization(PipelineNetwork network, OptimizationSettings settings)
        {
            var flowAllocations = new Dictionary<string, double>();
            var targetUtilization = GetParameterValue(settings, "TargetUtilization", 70.0) / 100.0;
            
            // Initialize flow allocations
            foreach (var segment in network.GetActiveSegments())
            {
                flowAllocations[segment.Id] = 0;
            }

            // Get delivery points sorted by demand
            var deliveryPoints = network.GetDeliveryPoints().ToList();
            
            // Iteratively balance flow allocation
            foreach (var deliveryPoint in deliveryPoints)
            {
                var remainingDemand = deliveryPoint.DemandRequirement;
                
                // Find all possible paths to this delivery point
                var allPaths = FindAllPaths(network, deliveryPoint.Id);
                
                if (!allPaths.Any())
                {
                    return null; // Cannot reach this delivery point
                }
                
                // Distribute demand across multiple paths to balance utilization
                DistributeDemandAcrossPaths(allPaths, remainingDemand, flowAllocations, network, targetUtilization);
            }

            return flowAllocations;
        }

        /// <summary>
        /// Find all available paths from supply sources to a delivery point
        /// </summary>
        private List<PathInfo> FindAllPaths(PipelineNetwork network, string deliveryPointId)
        {
            var allPaths = new List<PathInfo>();
            var receiptPoints = network.GetReceiptPoints().ToList();
            
            foreach (var receiptPoint in receiptPoints)
            {
                var paths = FindPathsFromSource(network, receiptPoint.Id, deliveryPointId, receiptPoint.SupplyCapacity);
                allPaths.AddRange(paths);
            }
            
            return allPaths;
        }

        /// <summary>
        /// Find paths from a specific source to destination
        /// </summary>
        private List<PathInfo> FindPathsFromSource(PipelineNetwork network, string sourceId, string destinationId, double maxSupply)
        {
            var paths = new List<PathInfo>();
            var visited = new HashSet<string>();
            var currentPath = new List<string>();
            
            FindPathsRecursive(network, sourceId, destinationId, visited, currentPath, paths, maxSupply);
            
            return paths;
        }

        /// <summary>
        /// Recursive path finding to discover multiple routes
        /// </summary>
        private void FindPathsRecursive(PipelineNetwork network, string currentId, string destinationId, 
            HashSet<string> visited, List<string> currentPath, List<PathInfo> allPaths, double maxSupply)
        {
            if (currentId == destinationId)
            {
                if (currentPath.Any())
                {
                    // Calculate path capacity and cost
                    var pathCapacity = Math.Min(maxSupply, currentPath.Min(segmentId => network.Segments[segmentId].Capacity));
                    var pathCost = currentPath.Sum(segmentId => network.Segments[segmentId].TransportationCost);
                    
                    allPaths.Add(new PathInfo
                    {
                        SegmentIds = new List<string>(currentPath),
                        Capacity = pathCapacity,
                        TotalCost = pathCost,
                        SupplySourceId = currentPath.Any() ? network.Segments[currentPath[0]].FromPointId : currentId
                    });
                }
                return;
            }
            
            visited.Add(currentId);
            
            foreach (var segment in network.GetOutgoingSegments(currentId))
            {
                if (!visited.Contains(segment.ToPointId))
                {
                    currentPath.Add(segment.Id);
                    FindPathsRecursive(network, segment.ToPointId, destinationId, visited, currentPath, allPaths, maxSupply);
                    currentPath.RemoveAt(currentPath.Count - 1);
                }
            }
            
            visited.Remove(currentId);
        }

        /// <summary>
        /// Distribute demand across multiple paths to achieve balanced utilization
        /// </summary>
        private void DistributeDemandAcrossPaths(List<PathInfo> paths, double totalDemand, 
            Dictionary<string, double> flowAllocations, PipelineNetwork network, double targetUtilization)
        {
            if (!paths.Any()) return;
            
            // Sort paths by current utilization level (prefer less utilized paths)
            var pathUtilizations = paths.Select(path => new
            {
                Path = path,
                CurrentUtilization = path.SegmentIds.Max(segId => 
                    flowAllocations.ContainsKey(segId) ? flowAllocations[segId] / network.Segments[segId].Capacity : 0)
            }).OrderBy(x => x.CurrentUtilization).ToList();
            
            var remainingDemand = totalDemand;
            
            // Distribute demand starting with least utilized paths
            while (remainingDemand > 0.01 && pathUtilizations.Any(pu => pu.CurrentUtilization < 0.95))
            {
                var availablePaths = pathUtilizations.Where(pu => pu.CurrentUtilization < 0.95).ToList();
                if (!availablePaths.Any()) break;
                
                // Calculate how much flow to allocate to each path
                var demandPerPath = remainingDemand / availablePaths.Count;
                
                foreach (var pathUtil in availablePaths)
                {
                    var path = pathUtil.Path;
                    
                    // Calculate remaining capacity for this path
                    var pathCapacity = path.SegmentIds.Min(segId => 
                        network.Segments[segId].Capacity - flowAllocations[segId]);
                    
                    var flowToAllocate = Math.Min(demandPerPath, Math.Min(remainingDemand, pathCapacity));
                    
                    if (flowToAllocate > 0.01)
                    {
                        // Allocate flow through this path
                        foreach (var segmentId in path.SegmentIds)
                        {
                            flowAllocations[segmentId] += flowToAllocate;
                        }
                        
                        remainingDemand -= flowToAllocate;
                    }
                }
                
                // Update utilizations for next iteration
                foreach (var pathUtil in pathUtilizations)
                {
                    pathUtil.GetType().GetProperty("CurrentUtilization")?.SetValue(pathUtil,
                        pathUtil.Path.SegmentIds.Max(segId => flowAllocations[segId] / network.Segments[segId].Capacity));
                }
            }
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
        /// Calculate utilization variance to measure balance quality
        /// </summary>
        private double CalculateUtilizationVariance(OptimizationResult result)
        {
            var utilizationRates = result.SegmentFlows.Values
                .Where(f => f.Flow > 0.01)
                .Select(f => f.UtilizationPercentage)
                .ToList();
                
            if (!utilizationRates.Any()) return 0;
            
            var mean = utilizationRates.Average();
            var variance = utilizationRates.Sum(x => Math.Pow(x - mean, 2)) / utilizationRates.Count;
            
            return Math.Sqrt(variance);
        }

        /// <summary>
        /// Helper class to store path information
        /// </summary>
        private class PathInfo
        {
            public List<string> SegmentIds { get; set; } = new List<string>();
            public double Capacity { get; set; }
            public double TotalCost { get; set; }
            public string SupplySourceId { get; set; } = string.Empty;
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