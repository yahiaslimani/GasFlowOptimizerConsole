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
