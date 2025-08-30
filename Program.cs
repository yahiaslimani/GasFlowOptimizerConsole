using GasPipelineOptimization.Models;
using GasPipelineOptimization.Services;
using System.Text.Json;

namespace GasPipelineOptimization
{
    /// <summary>
    /// Main program demonstrating the gas pipeline optimization system
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Gas Pipeline Capacity Planning & Optimization System ===");
            Console.WriteLine("Using Google OR-Tools for mathematical optimization");
            Console.WriteLine();

            try
            {
                // Initialize the optimization engine
                var optimizationEngine = new OptimizationEngine();

                // Load network configuration
                PipelineNetwork network;
                if (args.Length > 0 && File.Exists(args[0]))
                {
                    Console.WriteLine($"Loading network from: {args[0]}");
                    network = PipelineNetwork.LoadFromJson(args[0]);
                }
                else
                {
                    Console.WriteLine("Loading default network configuration...");
                    network = LoadDefaultNetwork();
                }

                Console.WriteLine($"Loaded network: {network}");
                Console.WriteLine();

                // Validate the network
                if (!network.IsValid(out List<string> networkErrors))
                {
                    Console.WriteLine("Network validation failed:");
                    foreach (var error in networkErrors)
                    {
                        Console.WriteLine($"  - {error}");
                    }
                    return;
                }

                Console.WriteLine("Network validation passed.");
                Console.WriteLine();

                // Display available algorithms
                Console.WriteLine("Available optimization algorithms:");
                foreach (var algorithmName in optimizationEngine.GetAvailableAlgorithms())
                {
                    var algorithm = optimizationEngine.GetAlgorithm(algorithmName);
                    Console.WriteLine($"  - {algorithmName}: {algorithm?.Description}");
                }
                Console.WriteLine();

                // Interactive menu
                await RunInteractiveMenu(optimizationEngine, network);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("\nPress any key to exit...");
                try
                {
                    Console.ReadKey();
                }
                catch (InvalidOperationException)
                {
                    // Handle automation environments where console input is redirected
                    Console.WriteLine("Application completed.");
                }
            }
        }

        static async Task RunInteractiveMenu(OptimizationEngine engine, PipelineNetwork network)
        {
            while (true)
            {
                Console.WriteLine("\n=== Main Menu ===");
                Console.WriteLine("1. Run Single Optimization");
                Console.WriteLine("2. Compare Multiple Algorithms");
                Console.WriteLine("3. Scenario Analysis");
                Console.WriteLine("4. Network Information");
                Console.WriteLine("5. Settings Configuration");
                Console.WriteLine("6. Export Results");
                Console.WriteLine("0. Exit");
                Console.Write("Select option: ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await RunSingleOptimization(engine, network);
                        break;
                    case "2":
                        await CompareAlgorithms(engine, network);
                        break;
                    case "3":
                        await RunScenarioAnalysis(engine, network);
                        break;
                    case "4":
                        DisplayNetworkInformation(network);
                        break;
                    case "5":
                        await ConfigureSettings();
                        break;
                    case "6":
                        await ExportResults(engine, network);
                        break;
                    case "0":
                        Console.WriteLine("Goodbye!");
                        return;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }
            }
        }

        static async Task RunSingleOptimization(OptimizationEngine engine, PipelineNetwork network)
        {
            Console.WriteLine("\n=== Single Optimization ===");
            
            // Select algorithm
            var algorithms = engine.GetAvailableAlgorithms().ToList();
            Console.WriteLine("Available algorithms:");
            for (int i = 0; i < algorithms.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {algorithms[i]}");
            }
            
            Console.Write("Select algorithm (1-" + algorithms.Count + "): ");
            if (!int.TryParse(Console.ReadLine(), out int algorithmIndex) || 
                algorithmIndex < 1 || algorithmIndex > algorithms.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }

            var selectedAlgorithm = algorithms[algorithmIndex - 1];
            
            // Configure settings
            var settings = ConfigureOptimizationSettings();
            
            Console.WriteLine($"\nRunning optimization with {selectedAlgorithm}...");
            var startTime = DateTime.Now;
            
            var result = engine.RunOptimization(selectedAlgorithm, network, settings);
            
            var endTime = DateTime.Now;
            Console.WriteLine($"Optimization completed in {(endTime - startTime).TotalMilliseconds:F0} ms");
            Console.WriteLine();
            
            // Display results
            Console.WriteLine(result.GenerateSummaryReport());
            
            // Validate result
            if (engine.ValidateOptimizationResult(result, network, out List<string> validationErrors))
            {
                Console.WriteLine("✓ Result validation passed.");
            }
            else
            {
                Console.WriteLine("✗ Result validation failed:");
                foreach (var error in validationErrors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }

            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static async Task CompareAlgorithms(OptimizationEngine engine, PipelineNetwork network)
        {
            Console.WriteLine("\n=== Algorithm Comparison ===");
            
            var settings = ConfigureOptimizationSettings();
            var algorithms = engine.GetAvailableAlgorithms().ToList();
            
            Console.WriteLine("Running all algorithms for comparison...");
            Console.WriteLine();
            
            var results = engine.RunMultipleOptimizations(algorithms, network, settings);
            
            var report = engine.GenerateComparativeReport(results);
            Console.WriteLine(report);
            
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        static async Task RunScenarioAnalysis(OptimizationEngine engine, PipelineNetwork network)
        {
            Console.WriteLine("\n=== Scenario Analysis ===");
            
            // Create scenarios
            var scenarios = new Dictionary<string, PipelineNetwork>
            {
                { "Baseline", network },
                { "High Demand", CreateHighDemandScenario(network) },
                { "Compressor Outage", CreateCompressorOutageScenario(network) },
                { "Segment Maintenance", CreateSegmentMaintenanceScenario(network) }
            };
            
            Console.WriteLine("Available scenarios:");
            foreach (var scenario in scenarios.Keys)
            {
                Console.WriteLine($"  - {scenario}");
            }
            
            Console.Write("Select algorithm for scenario analysis: ");
            var algorithms = engine.GetAvailableAlgorithms().ToList();
            for (int i = 0; i < algorithms.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {algorithms[i]}");
            }
            
            if (!int.TryParse(Console.ReadLine(), out int algorithmIndex) || 
                algorithmIndex < 1 || algorithmIndex > algorithms.Count)
            {
                Console.WriteLine("Invalid selection.");
                return;
            }
            
            var selectedAlgorithm = algorithms[algorithmIndex - 1];
            var settings = ConfigureOptimizationSettings();
            
            Console.WriteLine($"\nRunning scenario analysis with {selectedAlgorithm}...");
            
            var results = engine.RunScenarioAnalysis(selectedAlgorithm, scenarios, settings);
            
            Console.WriteLine("\n=== Scenario Analysis Results ===");
            foreach (var result in results)
            {
                Console.WriteLine($"\n--- {result.Key} Scenario ---");
                if (result.Value.Status == OptimizationStatus.Optimal || result.Value.Status == OptimizationStatus.Feasible)
                {
                    Console.WriteLine($"Status: {result.Value.Status}");
                    Console.WriteLine($"Total Cost: ${result.Value.TotalCost.TotalCost:F2}");
                    Console.WriteLine($"Throughput: {result.Value.Metrics.TotalThroughput:F2} MMscfd");
                    Console.WriteLine($"Average Utilization: {result.Value.Metrics.AverageCapacityUtilization:F1}%");
                }
                else
                {
                    Console.WriteLine($"Failed: {result.Value.Status}");
                    if (result.Value.Messages.Any())
                    {
                        Console.WriteLine($"Messages: {string.Join(", ", result.Value.Messages)}");
                    }
                }
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static void DisplayNetworkInformation(PipelineNetwork network)
        {
            Console.WriteLine("\n=== Network Information ===");
            Console.WriteLine($"Name: {network.Name}");
            Console.WriteLine($"Description: {network.Description}");
            Console.WriteLine();
            
            Console.WriteLine($"Points: {network.Points.Count}");
            Console.WriteLine($"  - Receipt Points: {network.GetReceiptPoints().Count()}");
            Console.WriteLine($"  - Delivery Points: {network.GetDeliveryPoints().Count()}");
            Console.WriteLine($"  - Compressor Stations: {network.GetCompressorStations().Count()}");
            Console.WriteLine();
            
            Console.WriteLine($"Segments: {network.Segments.Count}");
            Console.WriteLine($"  - Active Segments: {network.GetActiveSegments().Count()}");
            Console.WriteLine();
            
            Console.WriteLine($"Total Supply Capacity: {network.GetTotalSupplyCapacity():F2} MMscfd");
            Console.WriteLine($"Total Demand Requirement: {network.GetTotalDemandRequirement():F2} MMscfd");
            Console.WriteLine();
            
            // Display points
            Console.WriteLine("=== Points ===");
            foreach (var point in network.Points.Values.OrderBy(p => p.Id))
            {
                Console.WriteLine($"{point.Id}: {point.Name} ({point.Type})");
                if (point.Type == PointType.Receipt)
                {
                    Console.WriteLine($"  Supply Capacity: {point.SupplyCapacity:F2} MMscfd");
                }
                else if (point.Type == PointType.Delivery)
                {
                    Console.WriteLine($"  Demand: {point.DemandRequirement:F2} MMscfd");
                }
                else if (point.Type == PointType.Compressor)
                {
                    Console.WriteLine($"  Max Pressure Boost: {point.MaxPressureBoost:F1} psi");
                    Console.WriteLine($"  Fuel Rate: {point.FuelConsumptionRate:F4} MMscf/MMscfd");
                }
                Console.WriteLine($"  Pressure Range: {point.MinPressure:F1}-{point.MaxPressure:F1} psia");
            }
            
            Console.WriteLine("\n=== Segments ===");
            foreach (var segment in network.Segments.Values.OrderBy(s => s.Id))
            {
                Console.WriteLine($"{segment.Id}: {segment.Name}");
                Console.WriteLine($"  From: {segment.FromPointId} To: {segment.ToPointId}");
                Console.WriteLine($"  Capacity: {segment.Capacity:F2} MMscfd");
                Console.WriteLine($"  Length: {segment.Length:F1} miles");
                Console.WriteLine($"  Transport Cost: ${segment.TransportationCost:F3}/MMscf");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static async Task ConfigureSettings()
        {
            Console.WriteLine("\n=== Settings Configuration ===");
            Console.WriteLine("Current settings will be displayed and can be modified.");
            
            // This would implement a settings editor
            Console.WriteLine("Settings configuration not yet implemented.");
            Console.WriteLine("Using default settings for all optimizations.");
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static async Task ExportResults(OptimizationEngine engine, PipelineNetwork network)
        {
            Console.WriteLine("\n=== Export Results ===");
            
            var settings = ConfigureOptimizationSettings();
            var algorithms = engine.GetAvailableAlgorithms().ToList();
            
            Console.WriteLine("Generating comprehensive results export...");
            
            var results = engine.RunMultipleOptimizations(algorithms, network, settings);
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"optimization_results_{timestamp}.txt";
            
            var report = engine.GenerateComparativeReport(results);
            
            try
            {
                await File.WriteAllTextAsync(filename, report);
                Console.WriteLine($"Results exported to: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export failed: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();
        }

        static OptimizationSettings ConfigureOptimizationSettings()
        {
            Console.WriteLine("\nOptimization Settings:");
            Console.Write("Enable pressure constraints? (y/n) [y]: ");
            var pressureInput = ReadLineWithDefault("");
            var enablePressure = string.IsNullOrEmpty(pressureInput) || pressureInput.ToLower().StartsWith("y");
            
            Console.Write("Enable compressor stations? (y/n) [y]: ");
            var compressorInput = ReadLineWithDefault("");
            var enableCompressors = string.IsNullOrEmpty(compressorInput) || compressorInput.ToLower().StartsWith("y");
            
            Console.Write("Maximum solution time (seconds) [300]: ");
            var timeInput = ReadLineWithDefault("300");
            var maxTime = double.TryParse(timeInput, out var parsedTime) ? parsedTime : 300.0;
            
            Console.Write("Preferred solver (GLOP/SCIP) [GLOP]: ");
            var solverInput = ReadLineWithDefault("GLOP");
            var solver = string.IsNullOrEmpty(solverInput) ? "GLOP" : solverInput.ToUpper();
            
            return new OptimizationSettings
            {
                EnablePressureConstraints = enablePressure,
                EnableCompressorStations = enableCompressors,
                MaxSolutionTimeSeconds = maxTime,
                PreferredSolver = solver,
                EnableDetailedLogging = true
            };
        }

        static PipelineNetwork LoadDefaultNetwork()
        {
            var configPath = "config.json";
            if (File.Exists(configPath))
            {
                return PipelineNetwork.LoadFromJson(configPath);
            }
            else
            {
                // Create a simple default network
                var network = new PipelineNetwork
                {
                    Name = "Default Test Network",
                    Description = "Simple gas pipeline network for testing optimization algorithms"
                };

                // Add points
                network.AddPoint(new Point("R1", "Receipt Point 1", PointType.Receipt)
                {
                    SupplyCapacity = 1000,
                    MinPressure = 800,
                    MaxPressure = 1000,
                    CurrentPressure = 950,
                    UnitCost = 2.50
                });

                network.AddPoint(new Point("D1", "Delivery Point 1", PointType.Delivery)
                {
                    DemandRequirement = 600,
                    MinPressure = 300,
                    MaxPressure = 800,
                    CurrentPressure = 500
                });

                network.AddPoint(new Point("D2", "Delivery Point 2", PointType.Delivery)
                {
                    DemandRequirement = 400,
                    MinPressure = 300,
                    MaxPressure = 800,
                    CurrentPressure = 500
                });

                network.AddPoint(new Point("C1", "Compressor Station 1", PointType.Compressor)
                {
                    MinPressure = 300,
                    MaxPressure = 1200,
                    CurrentPressure = 600,
                    MaxPressureBoost = 400,
                    FuelConsumptionRate = 0.02
                });

                // Add segments
                network.AddSegment(new Segment("S1", "Main Line 1", "R1", "C1", 800)
                {
                    Length = 50,
                    Diameter = 36,
                    FrictionFactor = 0.015,
                    TransportationCost = 0.10
                });

                network.AddSegment(new Segment("S2", "Distribution Line 1", "C1", "D1", 600)
                {
                    Length = 30,
                    Diameter = 24,
                    FrictionFactor = 0.018,
                    TransportationCost = 0.12
                });

                network.AddSegment(new Segment("S3", "Distribution Line 2", "C1", "D2", 500)
                {
                    Length = 40,
                    Diameter = 20,
                    FrictionFactor = 0.020,
                    TransportationCost = 0.15
                });

                // Calculate pressure drop constants
                foreach (var segment in network.Segments.Values)
                {
                    segment.CalculatePressureDropConstant();
                }

                return network;
            }
        }

        static PipelineNetwork CreateHighDemandScenario(PipelineNetwork baseNetwork)
        {
            var scenario = JsonSerializer.Deserialize<PipelineNetwork>(
                JsonSerializer.Serialize(baseNetwork), 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            scenario!.Name = baseNetwork.Name + " - High Demand";
            scenario.Description = "Scenario with 50% increased demand";
            
            foreach (var point in scenario.GetDeliveryPoints())
            {
                point.DemandRequirement *= 1.5;
            }
            
            return scenario;
        }

        static PipelineNetwork CreateCompressorOutageScenario(PipelineNetwork baseNetwork)
        {
            var scenario = JsonSerializer.Deserialize<PipelineNetwork>(
                JsonSerializer.Serialize(baseNetwork),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            scenario!.Name = baseNetwork.Name + " - Compressor Outage";
            scenario.Description = "Scenario with compressor stations offline";
            
            foreach (var compressor in scenario.GetCompressorStations())
            {
                compressor.IsActive = false;
            }
            
            return scenario;
        }

        static PipelineNetwork CreateSegmentMaintenanceScenario(PipelineNetwork baseNetwork)
        {
            var scenario = JsonSerializer.Deserialize<PipelineNetwork>(
                JsonSerializer.Serialize(baseNetwork),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            scenario!.Name = baseNetwork.Name + " - Maintenance";
            scenario.Description = "Scenario with reduced segment capacity for maintenance";
            
            // Reduce capacity of first segment by 50%
            var firstSegment = scenario.GetActiveSegments().FirstOrDefault();
            if (firstSegment != null)
            {
                firstSegment.Capacity *= 0.5;
            }
            
            return scenario;
        }

        static string ReadLineWithDefault(string defaultValue)
        {
            try
            {
                var input = Console.ReadLine();
                return string.IsNullOrEmpty(input) ? defaultValue : input;
            }
            catch (InvalidOperationException)
            {
                // Console input is redirected, use default
                Console.WriteLine(defaultValue);
                return defaultValue;
            }
        }
    }
}
