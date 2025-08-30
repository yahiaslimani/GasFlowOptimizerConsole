namespace GasPipelineOptimization.Models
{
    /// <summary>
    /// Configuration settings for optimization algorithms
    /// </summary>
    public class OptimizationSettings
    {
        /// <summary>
        /// Whether to enable pressure constraints in optimization
        /// </summary>
        public bool EnablePressureConstraints { get; set; } = true;

        /// <summary>
        /// Whether to enable compressor station modeling
        /// </summary>
        public bool EnableCompressorStations { get; set; } = true;

        /// <summary>
        /// Maximum solution time in seconds
        /// </summary>
        public double MaxSolutionTimeSeconds { get; set; } = 300;

        /// <summary>
        /// Tolerance for optimization convergence
        /// </summary>
        public double OptimalityTolerance { get; set; } = 1e-6;

        /// <summary>
        /// Tolerance for constraint feasibility
        /// </summary>
        public double FeasibilityTolerance { get; set; } = 1e-6;

        /// <summary>
        /// Whether to use linear approximation for pressure constraints
        /// </summary>
        public bool UseLinearPressureApproximation { get; set; } = true;

        /// <summary>
        /// Number of segments for piecewise linear approximation
        /// </summary>
        public int LinearApproximationSegments { get; set; } = 10;

        /// <summary>
        /// Preferred solver (GLOP for linear, SCIP for nonlinear)
        /// </summary>
        public string PreferredSolver { get; set; } = "GLOP";

        /// <summary>
        /// Objective function weights
        /// </summary>
        public ObjectiveWeights Weights { get; set; } = new();

        /// <summary>
        /// Algorithm-specific parameters
        /// </summary>
        public Dictionary<string, object> AlgorithmParameters { get; set; } = new();

        /// <summary>
        /// Whether to enable detailed logging
        /// </summary>
        public bool EnableDetailedLogging { get; set; } = false;

        /// <summary>
        /// Whether to validate network before optimization
        /// </summary>
        public bool ValidateNetworkBeforeOptimization { get; set; } = true;

        /// <summary>
        /// Minimum flow threshold (flows below this are considered zero)
        /// </summary>
        public double MinimumFlowThreshold { get; set; } = 1e-3;

        /// <summary>
        /// Validates the optimization settings
        /// </summary>
        public bool IsValid(out string errorMessage)
        {
            errorMessage = string.Empty;

            if (MaxSolutionTimeSeconds <= 0)
            {
                errorMessage = "Maximum solution time must be positive";
                return false;
            }

            if (OptimalityTolerance <= 0 || OptimalityTolerance >= 1)
            {
                errorMessage = "Optimality tolerance must be between 0 and 1";
                return false;
            }

            if (FeasibilityTolerance <= 0 || FeasibilityTolerance >= 1)
            {
                errorMessage = "Feasibility tolerance must be between 0 and 1";
                return false;
            }

            if (LinearApproximationSegments < 1 || LinearApproximationSegments > 100)
            {
                errorMessage = "Linear approximation segments must be between 1 and 100";
                return false;
            }

            if (string.IsNullOrEmpty(PreferredSolver))
            {
                errorMessage = "Preferred solver cannot be empty";
                return false;
            }

            if (MinimumFlowThreshold < 0)
            {
                errorMessage = "Minimum flow threshold cannot be negative";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates default settings for different optimization scenarios
        /// </summary>
        public static OptimizationSettings CreateDefault(string scenario = "balanced")
        {
            var settings = new OptimizationSettings();

            switch (scenario.ToLower())
            {
                case "throughput":
                    settings.Weights.ThroughputWeight = 1.0;
                    settings.Weights.CostWeight = 0.1;
                    settings.Weights.BalanceWeight = 0.1;
                    break;

                case "cost":
                    settings.Weights.ThroughputWeight = 0.1;
                    settings.Weights.CostWeight = 1.0;
                    settings.Weights.BalanceWeight = 0.1;
                    break;

                case "balance":
                    settings.Weights.ThroughputWeight = 0.3;
                    settings.Weights.CostWeight = 0.3;
                    settings.Weights.BalanceWeight = 1.0;
                    break;

                default: // balanced
                    settings.Weights.ThroughputWeight = 0.5;
                    settings.Weights.CostWeight = 0.5;
                    settings.Weights.BalanceWeight = 0.3;
                    break;
            }

            return settings;
        }
    }

    /// <summary>
    /// Weights for different objectives in multi-objective optimization
    /// </summary>
    public class ObjectiveWeights
    {
        /// <summary>
        /// Weight for maximizing throughput
        /// </summary>
        public double ThroughputWeight { get; set; } = 0.5;

        /// <summary>
        /// Weight for minimizing cost
        /// </summary>
        public double CostWeight { get; set; } = 0.5;

        /// <summary>
        /// Weight for balancing demand across paths
        /// </summary>
        public double BalanceWeight { get; set; } = 0.3;

        /// <summary>
        /// Weight for pressure constraint violations (penalty)
        /// </summary>
        public double PressureViolationPenalty { get; set; } = 1000.0;

        /// <summary>
        /// Weight for capacity constraint violations (penalty)
        /// </summary>
        public double CapacityViolationPenalty { get; set; } = 1000.0;

        /// <summary>
        /// Normalizes weights to sum to 1.0
        /// </summary>
        public void Normalize()
        {
            var total = ThroughputWeight + CostWeight + BalanceWeight;
            if (total > 0)
            {
                ThroughputWeight /= total;
                CostWeight /= total;
                BalanceWeight /= total;
            }
        }
    }
}
