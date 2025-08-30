namespace GasPipelineOptimization.Models
{
    /// <summary>
    /// Represents a pipeline segment connecting two points
    /// </summary>
    public class Segment
    {
        /// <summary>
        /// Unique identifier for the segment
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name for the segment
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Starting point ID
        /// </summary>
        public string FromPointId { get; set; } = string.Empty;

        /// <summary>
        /// Ending point ID
        /// </summary>
        public string ToPointId { get; set; } = string.Empty;

        /// <summary>
        /// Maximum flow capacity through this segment (MMscfd)
        /// </summary>
        public double Capacity { get; set; }

        /// <summary>
        /// Length of the segment (miles)
        /// </summary>
        public double Length { get; set; }

        /// <summary>
        /// Diameter of the pipeline (inches)
        /// </summary>
        public double Diameter { get; set; }

        /// <summary>
        /// Friction factor for pressure drop calculations
        /// </summary>
        public double FrictionFactor { get; set; }

        /// <summary>
        /// Constant k for pressure drop approximation: square(Pfrom) - square(Pto) >= k * square(q)
        /// </summary>
        public double PressureDropConstant { get; set; }

        /// <summary>
        /// Cost per unit flow through this segment ($/MMscf)
        /// </summary>
        public double TransportationCost { get; set; }

        /// <summary>
        /// Current flow through this segment (MMscfd)
        /// </summary>
        public double CurrentFlow { get; set; }

        /// <summary>
        /// Whether this segment is active (available for flow)
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Whether this segment is bidirectional
        /// </summary>
        public bool IsBidirectional { get; set; } = false;

        /// <summary>
        /// Minimum flow through this segment (MMscfd) - can be negative for bidirectional
        /// </summary>
        public double MinFlow { get; set; } = 0;

        public Segment()
        {
        }

        public Segment(string id, string name, string fromPointId, string toPointId, double capacity)
        {
            Id = id;
            Name = name;
            FromPointId = fromPointId;
            ToPointId = toPointId;
            Capacity = capacity;
        }

        /// <summary>
        /// Calculates the pressure drop constant based on physical properties
        /// </summary>
        public void CalculatePressureDropConstant()
        {
            if (Length > 0 && Diameter > 0 && FrictionFactor > 0)
            {
                // Simplified pressure drop calculation
                // In practice, this would use more complex gas flow equations
                PressureDropConstant = (FrictionFactor * Length) / (Math.Pow(Diameter, 5) * 1000);
            }
        }

        /// <summary>
        /// Validates the segment configuration
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(Id))
            {
                errorMessage = "Segment ID cannot be empty";
                return false;
            }

            if (string.IsNullOrEmpty(Name))
            {
                errorMessage = "Segment name cannot be empty";
                return false;
            }

            if (string.IsNullOrEmpty(FromPointId))
            {
                errorMessage = "From point ID cannot be empty";
                return false;
            }

            if (string.IsNullOrEmpty(ToPointId))
            {
                errorMessage = "To point ID cannot be empty";
                return false;
            }

            if (FromPointId == ToPointId)
            {
                errorMessage = "From and To points cannot be the same";
                return false;
            }

            if (Capacity <= 0)
            {
                errorMessage = "Segment capacity must be positive";
                return false;
            }

            if (Length <= 0)
            {
                errorMessage = "Segment length must be positive";
                return false;
            }

            if (Diameter <= 0)
            {
                errorMessage = "Segment diameter must be positive";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Gets the effective capacity considering bidirectional flow
        /// </summary>
        public double GetEffectiveCapacity()
        {
            return IsBidirectional ? Capacity : Math.Max(0, Capacity);
        }

        /// <summary>
        /// Gets the effective minimum flow considering bidirectional capability
        /// </summary>
        public double GetEffectiveMinFlow()
        {
            return IsBidirectional ? -Capacity : Math.Max(0, MinFlow);
        }

        public override string ToString()
        {
            return $"{Name} ({FromPointId} -> {ToPointId}) - Capacity: {Capacity:F2} MMscfd";
        }
    }
}
