using GasPipelineOptimization.Models;

namespace GasPipelineOptimization.Algorithms
{
    /// <summary>
    /// Interface for all optimization algorithms
    /// </summary>
    public interface IOptimizationAlgorithm
    {
        /// <summary>
        /// Name of the optimization algorithm
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the algorithm optimizes for
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes the optimization algorithm
        /// </summary>
        /// <param name="network">The pipeline network to optimize</param>
        /// <param name="settings">Optimization settings and constraints</param>
        /// <returns>Optimization result</returns>
        OptimizationResult Optimize(PipelineNetwork network, OptimizationSettings settings);

        /// <summary>
        /// Validates if the algorithm can handle the given network and settings
        /// </summary>
        /// <param name="network">The pipeline network</param>
        /// <param name="settings">Optimization settings</param>
        /// <returns>True if algorithm is applicable, false otherwise</returns>
        bool CanHandle(PipelineNetwork network, OptimizationSettings settings);

        /// <summary>
        /// Gets algorithm-specific parameters and their descriptions
        /// </summary>
        Dictionary<string, string> GetParameters();
    }
}
