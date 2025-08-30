using System.Diagnostics;
using System.Text.Json;
using GasPipelineOptimization.Models;

namespace GasPipelineOptimization.Visualization.Services
{
    /// <summary>
    /// Service for launching and managing the web-based visualization interface
    /// This service handles the integration between the C# optimization engine and the HTML/JavaScript visualization
    /// </summary>
    public class VisualizationService
    {
        private readonly string _visualizationPath;
        private readonly int _defaultPort = 5000;

        /// <summary>
        /// Initialize the visualization service with the path to the HTML visualization file
        /// </summary>
        /// <param name="visualizationPath">Path to the HTML visualization file</param>
        public VisualizationService(string? visualizationPath = null)
        {
            // Default to the embedded visualization file in the project
            _visualizationPath = visualizationPath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, 
                "Visualization", "Web", "PipelineVisualization.html");
        }

        /// <summary>
        /// Launch the web-based visualization with the current network configuration
        /// This method starts a simple HTTP server to serve the visualization and opens it in the default browser
        /// </summary>
        /// <param name="network">Pipeline network to visualize</param>
        /// <param name="optimizationResult">Optional optimization results to overlay on the visualization</param>
        /// <returns>True if visualization was launched successfully</returns>
        public async Task<bool> LaunchVisualizationAsync(PipelineNetwork network, OptimizationResult? optimizationResult = null)
        {
            try
            {
                // Validate that the visualization file exists
                if (!File.Exists(_visualizationPath))
                {
                    Console.WriteLine($"Warning: Visualization file not found at {_visualizationPath}");
                    Console.WriteLine("Creating default visualization from template...");
                    await CreateDefaultVisualizationAsync();
                }

                // Save network and optimization data to temporary files for the web interface to load
                var tempDir = Path.Combine(Path.GetTempPath(), "GasPipelineVisualization");
                Directory.CreateDirectory(tempDir);

                var networkFile = Path.Combine(tempDir, "current_network.json");
                var resultFile = Path.Combine(tempDir, "current_results.json");

                // Serialize network data to JSON for web visualization
                await SaveNetworkForVisualizationAsync(network, networkFile);

                // Serialize optimization results if available
                if (optimizationResult != null)
                {
                    await SaveResultsForVisualizationAsync(optimizationResult, resultFile);
                }

                // Start a simple HTTP server to serve the visualization
                var serverProcess = await StartVisualizationServerAsync();

                if (serverProcess != null)
                {
                    Console.WriteLine($"Visualization server started on http://localhost:{_defaultPort}");
                    
                    // Open the visualization in the default browser
                    await OpenVisualizationInBrowserAsync();
                    
                    Console.WriteLine("Press any key to stop the visualization server...");
                    Console.ReadKey();
                    
                    // Clean up server process
                    serverProcess.Kill();
                    serverProcess.Dispose();
                    
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to launch visualization: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Export the current network and optimization results to files that can be loaded by the web visualization
        /// This allows users to manually open the visualization and load their data
        /// </summary>
        /// <param name="network">Pipeline network to export</param>
        /// <param name="optimizationResult">Optimization results to export</param>
        /// <param name="outputDirectory">Directory to save the exported files</param>
        /// <returns>Paths to the exported files</returns>
        public async Task<(string networkFile, string? resultsFile)> ExportVisualizationDataAsync(
            PipelineNetwork network, 
            OptimizationResult? optimizationResult = null, 
            string? outputDirectory = null)
        {
            outputDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "PipelineVisualization");
            Directory.CreateDirectory(outputDirectory);

            var networkFile = Path.Combine(outputDirectory, $"network_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await SaveNetworkForVisualizationAsync(network, networkFile);

            string? resultsFile = null;
            if (optimizationResult != null)
            {
                resultsFile = Path.Combine(outputDirectory, $"results_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                await SaveResultsForVisualizationAsync(optimizationResult, resultsFile);
            }

            // Copy the visualization HTML file to the output directory for convenience
            var visualizationCopy = Path.Combine(outputDirectory, "PipelineVisualization.html");
            if (File.Exists(_visualizationPath))
            {
                File.Copy(_visualizationPath, visualizationCopy, true);
            }

            Console.WriteLine($"Visualization data exported to: {outputDirectory}");
            Console.WriteLine($"Open {visualizationCopy} in a web browser and load the data files.");

            return (networkFile, resultsFile);
        }

        /// <summary>
        /// Save pipeline network data in a format optimized for web visualization
        /// The JSON format includes all necessary data for rendering the network topology
        /// </summary>
        /// <param name="network">Network to save</param>
        /// <param name="filePath">Output file path</param>
        private async Task SaveNetworkForVisualizationAsync(PipelineNetwork network, string filePath)
        {
            // Create a visualization-optimized representation of the network
            var visualizationData = new
            {
                metadata = new
                {
                    name = network.Name ?? "Pipeline Network",
                    description = network.Description ?? "Gas pipeline network for optimization",
                    exportTime = DateTime.UtcNow,
                    totalPoints = network.Points.Count,
                    totalSegments = network.Segments.Count
                },
                points = network.Points.ToDictionary(kvp => kvp.Key, kvp => new
                {
                    id = kvp.Value.Id,
                    name = kvp.Value.Name,
                    type = kvp.Value.Type.ToString(),
                    x = kvp.Value.X,
                    y = kvp.Value.Y,
                    isActive = kvp.Value.IsActive,
                    currentPressure = kvp.Value.CurrentPressure,
                    minPressure = kvp.Value.MinPressure,
                    maxPressure = kvp.Value.MaxPressure,
                    supplyCapacity = kvp.Value.SupplyCapacity,
                    demandRequirement = kvp.Value.DemandRequirement,
                    maxPressureBoost = kvp.Value.MaxPressureBoost,
                    fuelConsumptionRate = kvp.Value.FuelConsumptionRate,
                    unitCost = kvp.Value.UnitCost
                }),
                segments = network.Segments.ToDictionary(kvp => kvp.Key, kvp => new
                {
                    id = kvp.Value.Id,
                    name = kvp.Value.Name,
                    fromPointId = kvp.Value.FromPointId,
                    toPointId = kvp.Value.ToPointId,
                    capacity = kvp.Value.Capacity,
                    length = kvp.Value.Length,
                    diameter = kvp.Value.Diameter,
                    currentFlow = kvp.Value.CurrentFlow,
                    isActive = kvp.Value.IsActive,
                    isBidirectional = kvp.Value.IsBidirectional,
                    minFlow = kvp.Value.MinFlow,
                    transportationCost = kvp.Value.TransportationCost,
                    frictionFactor = kvp.Value.FrictionFactor,
                    pressureDropConstant = kvp.Value.PressureDropConstant
                })
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(visualizationData, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent);
        }

        /// <summary>
        /// Save optimization results in a format suitable for web visualization overlay
        /// </summary>
        /// <param name="result">Optimization result to save</param>
        /// <param name="filePath">Output file path</param>
        private async Task SaveResultsForVisualizationAsync(OptimizationResult result, string filePath)
        {
            var visualizationResults = new
            {
                metadata = new
                {
                    algorithmUsed = result.AlgorithmUsed,
                    solverUsed = result.SolverUsed,
                    status = result.Status.ToString(),
                    solutionTimeMs = result.SolutionTimeMs,
                    objectiveValue = result.ObjectiveValue,
                    exportTime = DateTime.UtcNow
                },
                pointPressures = result.PointPressures,
                segmentFlows = result.SegmentFlows,
                compressorBoosts = result.PointPressures.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.PressureBoost),
                statistics = new
                {
                    totalThroughput = result.SegmentFlows.Values.Where(f => f.Flow > 0).Sum(f => f.Flow),
                    maxUtilization = result.SegmentFlows.Any() ? result.SegmentFlows.Values.Max(f => f.Flow) : 0,
                    avgUtilization = result.SegmentFlows.Any() ? result.SegmentFlows.Values.Average(f => f.Flow) : 0,
                    activeSegments = result.SegmentFlows.Count(kvp => Math.Abs(kvp.Value.Flow) > 0.1),
                    activeCompressors = result.PointPressures.Count(kvp => kvp.Value.PressureBoost > 0.1)
                },
                messages = result.Messages
            };

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(visualizationResults, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent);
        }

        /// <summary>
        /// Start a simple HTTP server to serve the visualization files
        /// Uses Python's built-in HTTP server for cross-platform compatibility
        /// </summary>
        /// <returns>Process handle for the HTTP server, or null if failed to start</returns>
        private async Task<Process?> StartVisualizationServerAsync()
        {
            try
            {
                var visualizationDirectory = Path.GetDirectoryName(_visualizationPath);
                if (string.IsNullOrEmpty(visualizationDirectory))
                {
                    return null;
                }

                // Try to start Python HTTP server (most reliable cross-platform option)
                var pythonProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"-m http.server {_defaultPort}",
                        WorkingDirectory = visualizationDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                pythonProcess.Start();

                // Wait a moment for the server to start
                await Task.Delay(2000);

                if (!pythonProcess.HasExited)
                {
                    return pythonProcess;
                }

                // If Python3 failed, try python
                pythonProcess.StartInfo.FileName = "python";
                pythonProcess.Start();
                await Task.Delay(2000);

                if (!pythonProcess.HasExited)
                {
                    return pythonProcess;
                }

                Console.WriteLine("Failed to start Python HTTP server. Please ensure Python is installed.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start visualization server: {ex.Message}");
                Console.WriteLine("You can manually open the visualization HTML file in a web browser.");
                return null;
            }
        }

        /// <summary>
        /// Open the visualization in the default web browser
        /// Uses platform-specific commands to launch the browser
        /// </summary>
        private async Task OpenVisualizationInBrowserAsync()
        {
            try
            {
                var url = $"http://localhost:{_defaultPort}/PipelineVisualization.html";
                
                // Platform-specific browser launching
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", url);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", url);
                }
                else
                {
                    Console.WriteLine($"Please open your web browser and navigate to: {url}");
                }

                await Task.Delay(1000); // Give browser time to start
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open browser automatically: {ex.Message}");
                Console.WriteLine($"Please manually open: http://localhost:{_defaultPort}/PipelineVisualization.html");
            }
        }

        /// <summary>
        /// Create a default visualization file if one doesn't exist
        /// This ensures the service can function even if the HTML file is missing
        /// </summary>
        private async Task CreateDefaultVisualizationAsync()
        {
            var directory = Path.GetDirectoryName(_visualizationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // The HTML content would be embedded here or loaded from resources
            // For now, we'll just create a placeholder
            var defaultHtml = @"<!DOCTYPE html>
<html>
<head>
    <title>Pipeline Visualization</title>
</head>
<body>
    <h1>Gas Pipeline Network Visualization</h1>
    <p>This is a placeholder visualization. Please ensure the full visualization HTML file is available.</p>
    <p>The visualization service attempted to create this file automatically.</p>
</body>
</html>";

            await File.WriteAllTextAsync(_visualizationPath, defaultHtml);
        }

        /// <summary>
        /// Generate a static visualization report as an HTML file with embedded data
        /// This creates a self-contained HTML file that doesn't require a server
        /// </summary>
        /// <param name="network">Pipeline network to include</param>
        /// <param name="optimizationResult">Optimization results to include</param>
        /// <param name="outputPath">Path for the output HTML file</param>
        /// <returns>True if report was generated successfully</returns>
        public async Task<bool> GenerateStaticReportAsync(PipelineNetwork network, OptimizationResult? optimizationResult, string outputPath)
        {
            try
            {
                // Read the base visualization template
                var templateContent = await File.ReadAllTextAsync(_visualizationPath);

                // Embed the network and optimization data directly into the HTML
                var networkJson = JsonSerializer.Serialize(network, new JsonSerializerOptions { WriteIndented = false });
                var resultJson = optimizationResult != null ? 
                    JsonSerializer.Serialize(optimizationResult, new JsonSerializerOptions { WriteIndented = false }) : "null";

                // Inject the data into the HTML template
                var embeddedScript = $@"
<script>
    // Embedded network and optimization data
    window.embeddedNetworkData = {networkJson};
    window.embeddedOptimizationData = {resultJson};
    
    // Override the default network loading to use embedded data
    function loadDefaultNetwork() {{
        if (window.embeddedNetworkData) {{
            networkData = window.embeddedNetworkData;
            if (window.embeddedOptimizationData) {{
                optimizationResult = window.embeddedOptimizationData;
            }}
            refreshVisualization();
        }}
    }}
</script>";

                // Insert the embedded script before the closing body tag
                var modifiedContent = templateContent.Replace("</body>", embeddedScript + "\n</body>");

                // Add a header indicating this is a generated report
                var reportHeader = $@"
<!-- Generated Gas Pipeline Optimization Report -->
<!-- Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss} -->
<!-- Network: {network.Name ?? "Unknown"} -->
<!-- Algorithm: {optimizationResult?.AlgorithmUsed ?? "N/A"} -->
";

                modifiedContent = modifiedContent.Replace("<!DOCTYPE html>", reportHeader + "<!DOCTYPE html>");

                await File.WriteAllTextAsync(outputPath, modifiedContent);
                
                Console.WriteLine($"Static visualization report generated: {outputPath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to generate static report: {ex.Message}");
                return false;
            }
        }
    }
}