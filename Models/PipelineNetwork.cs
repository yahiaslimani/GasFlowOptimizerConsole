using System.Text.Json;

namespace GasPipelineOptimization.Models
{
    /// <summary>
    /// Represents the complete gas pipeline network
    /// </summary>
    public class PipelineNetwork
    {
        /// <summary>
        /// All points in the network
        /// </summary>
        public Dictionary<string, Point> Points { get; set; } = new();

        /// <summary>
        /// All segments in the network
        /// </summary>
        public Dictionary<string, Segment> Segments { get; set; } = new();

        /// <summary>
        /// Network name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Network description
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Adds a point to the network
        /// </summary>
        public void AddPoint(Point point)
        {
            if (point.IsValid(out string errorMessage))
            {
                Points[point.Id] = point;
            }
            else
            {
                throw new ArgumentException($"Invalid point: {errorMessage}");
            }
        }

        /// <summary>
        /// Adds a segment to the network
        /// </summary>
        public void AddSegment(Segment segment)
        {
            if (!segment.IsValid(out string errorMessage))
            {
                throw new ArgumentException($"Invalid segment: {errorMessage}");
            }

            if (!Points.ContainsKey(segment.FromPointId))
            {
                throw new ArgumentException($"From point {segment.FromPointId} does not exist in the network");
            }

            if (!Points.ContainsKey(segment.ToPointId))
            {
                throw new ArgumentException($"To point {segment.ToPointId} does not exist in the network");
            }

            Segments[segment.Id] = segment;
        }

        /// <summary>
        /// Gets all receipt (supply) points
        /// </summary>
        public IEnumerable<Point> GetReceiptPoints()
        {
            return Points.Values.Where(p => p.Type == PointType.Receipt && p.IsActive);
        }

        /// <summary>
        /// Gets all delivery (demand) points
        /// </summary>
        public IEnumerable<Point> GetDeliveryPoints()
        {
            return Points.Values.Where(p => p.Type == PointType.Delivery && p.IsActive);
        }

        /// <summary>
        /// Gets all compressor stations
        /// </summary>
        public IEnumerable<Point> GetCompressorStations()
        {
            return Points.Values.Where(p => p.Type == PointType.Compressor && p.IsActive);
        }

        /// <summary>
        /// Gets all active segments
        /// </summary>
        public IEnumerable<Segment> GetActiveSegments()
        {
            return Segments.Values.Where(s => s.IsActive);
        }

        /// <summary>
        /// Gets segments connected to a specific point
        /// </summary>
        public IEnumerable<Segment> GetConnectedSegments(string pointId)
        {
            return GetActiveSegments().Where(s => s.FromPointId == pointId || s.ToPointId == pointId);
        }

        /// <summary>
        /// Gets segments originating from a specific point
        /// </summary>
        public IEnumerable<Segment> GetOutgoingSegments(string pointId)
        {
            return GetActiveSegments().Where(s => s.FromPointId == pointId);
        }

        /// <summary>
        /// Gets segments ending at a specific point
        /// </summary>
        public IEnumerable<Segment> GetIncomingSegments(string pointId)
        {
            return GetActiveSegments().Where(s => s.ToPointId == pointId);
        }

        /// <summary>
        /// Gets trunk lines (main transmission lines) - segments with highest capacity or connecting receipt to delivery points
        /// </summary>
        public IEnumerable<Segment> GetTrunkLines()
        {
            var segments = GetActiveSegments().ToList();
            if (!segments.Any()) return new List<Segment>();

            // Identify trunk lines as segments that:
            // 1. Have high capacity (top 30% of capacities)
            // 2. Connect receipt points to the network
            // 3. Are main transmission lines (not small distribution lines)
            
            var sortedByCapacity = segments.OrderByDescending(s => s.Capacity).ToList();
            var capacityThreshold = sortedByCapacity.Take(Math.Max(1, sortedByCapacity.Count / 3)).Min(s => s.Capacity);
            
            var trunkLines = segments.Where(s => 
                s.Capacity >= capacityThreshold || // High capacity segments
                GetReceiptPoints().Any(r => r.Id == s.FromPointId) || // Segments from receipt points
                s.Name.ToLower().Contains("main") || // Segments with "main" in name
                s.Name.ToLower().Contains("trunk") || // Segments with "trunk" in name
                s.Name.ToLower().Contains("transmission") // Segments with "transmission" in name
            ).ToList();

            return trunkLines.Any() ? trunkLines : segments.Take(1); // Return at least one segment
        }

        /// <summary>
        /// Gets connected lines for a trunk line (segments that form a path with the trunk line)
        /// </summary>
        public IEnumerable<Segment> GetConnectedLines(Segment trunkLine)
        {
            var connectedLines = new List<Segment> { trunkLine };
            var visited = new HashSet<string> { trunkLine.Id };

            // Find downstream connected segments
            FindConnectedSegments(trunkLine.ToPointId, connectedLines, visited, true);
            
            // Find upstream connected segments  
            FindConnectedSegments(trunkLine.FromPointId, connectedLines, visited, false);

            return connectedLines;
        }

        /// <summary>
        /// Recursively finds connected segments in a direction (downstream or upstream)
        /// </summary>
        private void FindConnectedSegments(string pointId, List<Segment> connectedLines, 
                                         HashSet<string> visited, bool downstream)
        {
            var segments = downstream ? 
                GetOutgoingSegments(pointId).ToList() : 
                GetIncomingSegments(pointId).ToList();

            foreach (var segment in segments)
            {
                if (visited.Contains(segment.Id)) continue;
                
                visited.Add(segment.Id);
                connectedLines.Add(segment);

                // Continue tracing in the same direction
                var nextPointId = downstream ? segment.ToPointId : segment.FromPointId;
                FindConnectedSegments(nextPointId, connectedLines, visited, downstream);
            }
        }

        /// <summary>
        /// Calculates total supply capacity in the network
        /// </summary>
        public double GetTotalSupplyCapacity()
        {
            return GetReceiptPoints().Sum(p => p.SupplyCapacity);
        }

        /// <summary>
        /// Calculates total demand requirement in the network
        /// </summary>
        public double GetTotalDemandRequirement()
        {
            return GetDeliveryPoints().Sum(p => p.DemandRequirement);
        }

        /// <summary>
        /// Validates the network configuration
        /// </summary>
        public bool IsValid(out List<string> errorMessages)
        {
            errorMessages = new List<string>();

            // Validate points
            foreach (var point in Points.Values)
            {
                if (!point.IsValid(out string pointError))
                {
                    errorMessages.Add($"Point {point.Id}: {pointError}");
                }
            }

            // Validate segments
            foreach (var segment in Segments.Values)
            {
                if (!segment.IsValid(out string segmentError))
                {
                    errorMessages.Add($"Segment {segment.Id}: {segmentError}");
                }
            }

            // Check network connectivity
            var receiptPoints = GetReceiptPoints().ToList();
            var deliveryPoints = GetDeliveryPoints().ToList();

            if (!receiptPoints.Any())
            {
                errorMessages.Add("Network must have at least one receipt point");
            }

            if (!deliveryPoints.Any())
            {
                errorMessages.Add("Network must have at least one delivery point");
            }

            // Check if total supply can meet total demand
            var totalSupply = GetTotalSupplyCapacity();
            var totalDemand = GetTotalDemandRequirement();

            if (totalSupply < totalDemand)
            {
                errorMessages.Add($"Total supply ({totalSupply:F2}) is less than total demand ({totalDemand:F2})");
            }

            return !errorMessages.Any();
        }

        /// <summary>
        /// Loads network from JSON configuration
        /// </summary>
        public static PipelineNetwork LoadFromJson(string jsonPath)
        {
            try
            {
                var jsonString = File.ReadAllText(jsonPath);
                var network = JsonSerializer.Deserialize<PipelineNetwork>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (network == null)
                {
                    throw new InvalidOperationException("Failed to deserialize network from JSON");
                }

                // Calculate pressure drop constants for segments
                foreach (var segment in network.Segments.Values)
                {
                    segment.CalculatePressureDropConstant();
                }

                return network;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error loading network from JSON: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves network to JSON configuration
        /// </summary>
        public void SaveToJson(string jsonPath)
        {
            try
            {
                var jsonString = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                File.WriteAllText(jsonPath, jsonString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error saving network to JSON: {ex.Message}", ex);
            }
        }

        public override string ToString()
        {
            return $"Pipeline Network '{Name}' - Points: {Points.Count}, Segments: {Segments.Count}";
        }
    }
}
