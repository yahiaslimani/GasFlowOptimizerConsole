using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GasPipelineOptimization.Models
{
    /// <summary>
    /// Represents different types of points in the gas pipeline network
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PointType
    {
        Receipt,    // Supply point
        Delivery,   // Demand point
        Compressor  // Compressor station
    }

    /// <summary>
    /// Represents a point in the gas pipeline network
    /// </summary>
    public class Point
    {
        /// <summary>
        /// Unique identifier for the point
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the point
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Type of the point (Receipt, Delivery, or Compressor)
        /// </summary>
        public PointType Type { get; set; }

        /// <summary>
        /// Supply capacity for Receipt points (MMscfd)
        /// </summary>
        public double SupplyCapacity { get; set; }

        /// <summary>
        /// Demand requirement for Delivery points (MMscfd)
        /// </summary>
        public double DemandRequirement { get; set; }

        /// <summary>
        /// Minimum pressure at this point (psia)
        /// </summary>
        public double MinPressure { get; set; }

        /// <summary>
        /// Maximum pressure at this point (psia)
        /// </summary>
        public double MaxPressure { get; set; }

        /// <summary>
        /// Current pressure at this point (psia)
        /// </summary>
        public double CurrentPressure { get; set; }

        /// <summary>
        /// For compressor stations: maximum pressure boost capability (psi)
        /// </summary>
        public double MaxPressureBoost { get; set; }

        /// <summary>
        /// For compressor stations: fuel consumption rate (MMscf per MMscfd throughput)
        /// </summary>
        public double FuelConsumptionRate { get; set; }

        /// <summary>
        /// Cost per unit of gas at this point ($/MMscf)
        /// </summary>
        public double UnitCost { get; set; }

        /// <summary>
        /// X coordinate for visualization
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y coordinate for visualization
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// Whether this point is active in the current optimization
        /// </summary>
        public bool IsActive { get; set; } = true;

        public Point()
        {
        }

        public Point(string id, string name, PointType type)
        {
            Id = id;
            Name = name;
            Type = type;
        }

        /// <summary>
        /// Validates the point configuration
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(Id))
            {
                errorMessage = "Point ID cannot be empty";
                return false;
            }

            if (string.IsNullOrEmpty(Name))
            {
                errorMessage = "Point name cannot be empty";
                return false;
            }

            if (MinPressure < 0)
            {
                errorMessage = "Minimum pressure cannot be negative";
                return false;
            }

            if (MaxPressure <= MinPressure)
            {
                errorMessage = "Maximum pressure must be greater than minimum pressure";
                return false;
            }

            if (Type == PointType.Receipt && SupplyCapacity <= 0)
            {
                errorMessage = "Receipt points must have positive supply capacity";
                return false;
            }

            if (Type == PointType.Delivery && DemandRequirement <= 0)
            {
                errorMessage = "Delivery points must have positive demand requirement";
                return false;
            }

            if (Type == PointType.Compressor && MaxPressureBoost <= 0)
            {
                errorMessage = "Compressor stations must have positive pressure boost capability";
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return $"{Name} ({Type}) - ID: {Id}";
        }
    }
}
