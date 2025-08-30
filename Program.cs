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
                Console.WriteLine("1. Flow Analysis & Validation");
                Console.WriteLine("2. Pressure Analysis");
                Console.WriteLine("3. Compressor Analysis");
                Console.WriteLine("4. Network Information");
                Console.WriteLine("5. Export Analysis Results");
                Console.WriteLine("0. Exit");
                Console.Write("Select option: ");

                var choice = Console.ReadLine();

                switch (choice)
                {
                    case "1":
                        await RunFlowAnalysis(network);
                        break;
                    case "2":
                        await RunPressureAnalysis(network);
                        break;
                    case "3":
                        await RunCompressorAnalysis(network);
                        break;
                    case "4":
                        DisplayNetworkInformation(network);
                        break;
                    case "5":
                        await ExportAnalysisResults(network);
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
            
            return new OptimizationSettings
            {
                EnablePressureConstraints = enablePressure,
                EnableCompressorStations = enableCompressors,
                MaxSolutionTimeSeconds = maxTime,
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

        static async Task RunFlowAnalysis(PipelineNetwork network)
        {
            Console.WriteLine("\n=== Gas Flow Analysis & Validation ===");
            Console.WriteLine("Analyzing gas flow from delivery points upstream...");
            
            var flowService = new FlowCalculationService();
            
            try
            {
                // Calculate upstream flows
                var result = flowService.CalculateUpstreamFlow(network);
                
                // Display summary
                Console.WriteLine($"\nAnalysis Status: {result.CalculationStatus}");
                Console.WriteLine($"Network Feasible: {(result.IsNetworkFeasible ? "✓ YES" : "✗ NO")}");
                Console.WriteLine();
                
                // Show key metrics
                Console.WriteLine("=== Key Metrics ===");
                Console.WriteLine($"Total Demand: {result.NetworkMetrics.TotalDemandRequired:F2} MMscfd");
                Console.WriteLine($"Total Supply: {result.NetworkMetrics.TotalSupplyAvailable:F2} MMscfd");
                Console.WriteLine($"Supply-Demand Balance: {result.NetworkMetrics.SupplyDemandBalance:F2} MMscfd");
                Console.WriteLine($"Average Utilization: {result.NetworkMetrics.AverageUtilization:F1}%");
                Console.WriteLine($"Peak Utilization: {result.NetworkMetrics.PeakUtilization:F1}%");
                Console.WriteLine($"Segments Over Capacity: {result.NetworkMetrics.SegmentsOverCapacity} of {result.NetworkMetrics.TotalSegments}");
                Console.WriteLine();
                
                // Show capacity violations if any
                if (result.ValidationIssues.Any())
                {
                    Console.WriteLine("=== CAPACITY VIOLATIONS DETECTED ===");
                    foreach (var issue in result.ValidationIssues)
                    {
                        Console.WriteLine($"⚠ {issue}");
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("✓ All segments are within capacity limits");
                    Console.WriteLine();
                }
                
                // Show top utilized segments
                var topSegments = result.SegmentAnalysis.Values
                    .OrderByDescending(s => s.UtilizationPercentage)
                    .Take(5)
                    .ToList();
                
                if (topSegments.Any())
                {
                    Console.WriteLine("=== Top 5 Most Utilized Segments ===");
                    Console.WriteLine("Segment".PadRight(12) + "Flow".PadRight(10) + "Capacity".PadRight(10) + "Utilization".PadRight(12) + "Status");
                    Console.WriteLine(new string('-', 50));
                    
                    foreach (var segment in topSegments)
                    {
                        var id = segment.SegmentId.PadRight(12);
                        var flow = segment.RequiredFlow.ToString("F1").PadRight(10);
                        var capacity = segment.Capacity.ToString("F1").PadRight(10);
                        var utilization = $"{segment.UtilizationPercentage:F1}%".PadRight(12);
                        var status = segment.IsOverCapacity ? "OVER CAP" : "OK";
                        
                        Console.WriteLine($"{id}{flow}{capacity}{utilization}{status}");
                    }
                    Console.WriteLine();
                }
                
                // Show comprehensive segment table
                Console.WriteLine("\n=== COMPREHENSIVE SEGMENT ANALYSIS TABLE ===");
                var headerLine = string.Format("{0,-10} {1,-25} {2,-15} {3,-15} {4,-12} {5,-12} {6,-10} {7,-12}",
                    "Segment", "Name", "From Point", "To Point", "Flow", "Capacity", "Usage %", "Status");
                Console.WriteLine(headerLine);
                Console.WriteLine(new string('=', headerLine.Length));

                foreach (var analysis in result.SegmentAnalysis.Values.OrderByDescending(s => s.UtilizationPercentage))
                {
                    var status = analysis.IsOverCapacity ? "OVER CAP" : "OK";
                    
                    var line = string.Format("{0,-10} {1,-25} {2,-15} {3,-15} {4,-12:F2} {5,-12:F2} {6,-10:F1} {7,-12}",
                        analysis.SegmentId,
                        analysis.SegmentName.Length > 25 ? analysis.SegmentName.Substring(0, 22) + "..." : analysis.SegmentName,
                        analysis.FromPointId,
                        analysis.ToPointId,
                        analysis.RequiredFlow,
                        analysis.Capacity,
                        analysis.UtilizationPercentage,
                        status);
                    
                    Console.WriteLine(line);
                }
                Console.WriteLine();
                
                // Interactive options
                Console.WriteLine("=== Analysis Options ===");
                Console.WriteLine("1. View Detailed Report");
                Console.WriteLine("2. Export Analysis to File");
                Console.WriteLine("3. Analyze Specific Segment");
                Console.WriteLine("0. Return to Main Menu");
                Console.Write("Select option: ");
                
                var choice = ReadLineWithDefault("0");
                
                switch (choice)
                {
                    case "1":
                        Console.WriteLine("\n" + flowService.GenerateDetailedFlowReport(result, network));
                        break;
                    case "2":
                        await ExportFlowAnalysis(flowService, result, network);
                        break;
                    case "3":
                        await AnalyzeSpecificSegment(network, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during flow analysis: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                // Handle automation environments
                Console.WriteLine("Flow analysis completed.");
            }
        }
        
        static async Task RunPressureAnalysis(PipelineNetwork network)
        {
            Console.WriteLine("\n=== Pressure Analysis ===");
            Console.WriteLine("Analyzing pressure distribution and constraints across the network...");
            
            var flowService = new FlowCalculationService();
            
            try
            {
                // Calculate flows first
                var result = flowService.CalculateUpstreamFlow(network);
                
                Console.WriteLine($"\nPressure Analysis Status: {result.CalculationStatus}");
                Console.WriteLine();
                
                // Pressure constraints analysis
                Console.WriteLine("=== PRESSURE CONSTRAINTS ANALYSIS ===");
                Console.WriteLine(string.Format("{0,-8} {1,-25} {2,-12} {3,-12} {4,-12} {5,-10}",
                    "Point", "Name", "Current", "Min", "Max", "Status"));
                Console.WriteLine(new string('-', 75));
                
                foreach (var point in network.Points.Values.Where(p => p.IsActive).OrderBy(p => p.Id))
                {
                    var status = "OK";
                    if (point.CurrentPressure < point.MinPressure)
                        status = "LOW";
                    else if (point.CurrentPressure > point.MaxPressure)
                        status = "HIGH";
                    
                    Console.WriteLine(string.Format("{0,-8} {1,-25} {2,-12:F1} {3,-12:F1} {4,-12:F1} {5,-10}",
                        point.Id,
                        point.Name.Length > 25 ? point.Name.Substring(0, 22) + "..." : point.Name,
                        point.CurrentPressure,
                        point.MinPressure,
                        point.MaxPressure,
                        status));
                }
                Console.WriteLine();
                
                // Pressure drop analysis for segments with high utilization
                var highUtilSegments = result.SegmentAnalysis.Values
                    .Where(s => s.UtilizationPercentage > 80)
                    .OrderByDescending(s => s.UtilizationPercentage)
                    .ToList();
                
                if (highUtilSegments.Any())
                {
                    Console.WriteLine("=== HIGH UTILIZATION SEGMENTS (PRESSURE RISK) ===");
                    Console.WriteLine(string.Format("{0,-10} {1,-12} {2,-12} {3,-15}",
                        "Segment", "Utilization", "Length", "Pressure Risk"));
                    Console.WriteLine(new string('-', 50));
                    
                    foreach (var segment in highUtilSegments)
                    {
                        var networkSegment = network.Segments[segment.SegmentId];
                        var pressureRisk = segment.UtilizationPercentage > 95 ? "HIGH" : "MEDIUM";
                        
                        Console.WriteLine(string.Format("{0,-10} {1,-12:F1}% {2,-12:F1} {3,-15}",
                            segment.SegmentId,
                            segment.UtilizationPercentage,
                            networkSegment.Length,
                            pressureRisk));
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during pressure analysis: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Pressure analysis completed.");
            }
        }
        
        static async Task RunCompressorAnalysis(PipelineNetwork network)
        {
            Console.WriteLine("\n=== Compressor Analysis ===");
            Console.WriteLine("Analyzing compressor stations and their impact on network flow...");
            
            var flowService = new FlowCalculationService();
            
            try
            {
                // Calculate flows first
                var result = flowService.CalculateUpstreamFlow(network);
                
                Console.WriteLine($"\nCompressor Analysis Status: {result.CalculationStatus}");
                Console.WriteLine();
                
                // Compressor stations analysis
                var compressors = network.GetCompressorStations().ToList();
                
                if (compressors.Any())
                {
                    Console.WriteLine("=== COMPRESSOR STATIONS ANALYSIS ===");
                    Console.WriteLine(string.Format("{0,-8} {1,-25} {2,-12} {3,-12} {4,-12} {5,-10}",
                        "Station", "Name", "Current P", "Max Boost", "Fuel Rate", "Status"));
                    Console.WriteLine(new string('-', 85));
                    
                    foreach (var compressor in compressors.OrderBy(c => c.Id))
                    {
                        var status = compressor.IsActive ? "ACTIVE" : "OFFLINE";
                        
                        Console.WriteLine(string.Format("{0,-8} {1,-25} {2,-12:F1} {3,-12:F1} {4,-12:F4} {5,-10}",
                            compressor.Id,
                            compressor.Name.Length > 25 ? compressor.Name.Substring(0, 22) + "..." : compressor.Name,
                            compressor.CurrentPressure,
                            compressor.MaxPressureBoost,
                            compressor.FuelConsumptionRate,
                            status));
                    }
                    Console.WriteLine();
                    
                    // Flow through compressor stations
                    Console.WriteLine("=== FLOW THROUGH COMPRESSOR STATIONS ===");
                    Console.WriteLine(string.Format("{0,-8} {1,-15} {2,-15} {3,-12}",
                        "Station", "Incoming Flow", "Outgoing Flow", "Net Balance"));
                    Console.WriteLine(new string('-', 55));
                    
                    foreach (var compressor in compressors)
                    {
                        var incomingFlow = network.GetIncomingSegments(compressor.Id)
                            .Where(s => result.SegmentAnalysis.ContainsKey(s.Id))
                            .Sum(s => result.SegmentAnalysis[s.Id].RequiredFlow);
                        
                        var outgoingFlow = network.GetOutgoingSegments(compressor.Id)
                            .Where(s => result.SegmentAnalysis.ContainsKey(s.Id))
                            .Sum(s => result.SegmentAnalysis[s.Id].RequiredFlow);
                        
                        var netBalance = incomingFlow - outgoingFlow;
                        
                        Console.WriteLine(string.Format("{0,-8} {1,-15:F2} {2,-15:F2} {3,-12:F2}",
                            compressor.Id,
                            incomingFlow,
                            outgoingFlow,
                            netBalance));
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.WriteLine("No compressor stations found in the network.");
                    Console.WriteLine();
                }
                
                // Segments requiring compression
                var segmentsNeedingCompression = result.SegmentAnalysis.Values
                    .Where(s => s.IsOverCapacity)
                    .OrderByDescending(s => s.ExcessFlow)
                    .ToList();
                
                if (segmentsNeedingCompression.Any())
                {
                    Console.WriteLine("=== SEGMENTS REQUIRING ADDITIONAL COMPRESSION ===");
                    Console.WriteLine(string.Format("{0,-10} {1,-12} {2,-12} {3,-15}",
                        "Segment", "Excess Flow", "Utilization", "Recommendation"));
                    Console.WriteLine(new string('-', 50));
                    
                    foreach (var segment in segmentsNeedingCompression)
                    {
                        var recommendation = segment.ExcessFlow > 100 ? "URGENT" : "MONITOR";
                        
                        Console.WriteLine(string.Format("{0,-10} {1,-12:F2} {2,-12:F1}% {3,-15}",
                            segment.SegmentId,
                            segment.ExcessFlow,
                            segment.UtilizationPercentage,
                            recommendation));
                    }
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during compressor analysis: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Compressor analysis completed.");
            }
        }
        
        static async Task ExportAnalysisResults(PipelineNetwork network)
        {
            Console.WriteLine("\n=== Export Analysis Results ===");
            Console.WriteLine("Generating comprehensive analysis reports...");
            
            var flowService = new FlowCalculationService();
            
            try
            {
                var result = flowService.CalculateUpstreamFlow(network);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Export detailed flow analysis
                var detailedReport = flowService.GenerateDetailedFlowReport(result, network);
                var filename = $"comprehensive_analysis_{timestamp}.txt";
                await File.WriteAllTextAsync(filename, detailedReport);
                Console.WriteLine($"Comprehensive analysis exported to: {filename}");
                
                // Export summary report
                var summaryReport = flowService.GenerateFlowReport(result);
                var summaryFilename = $"flow_summary_{timestamp}.txt";
                await File.WriteAllTextAsync(summaryFilename, summaryReport);
                Console.WriteLine($"Flow summary exported to: {summaryFilename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export failed: {ex.Message}");
            }
            
            Console.WriteLine("\nPress any key to continue...");
            try
            {
                Console.ReadKey();
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Export completed.");
            }
        }
        
        static async Task ExportFlowAnalysis(FlowCalculationService flowService, FlowCalculationResult result, PipelineNetwork network)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var filename = $"flow_analysis_{timestamp}.txt";
                
                var report = flowService.GenerateDetailedFlowReport(result, network);
                await File.WriteAllTextAsync(filename, report);
                
                Console.WriteLine($"Flow analysis exported to: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Export failed: {ex.Message}");
            }
        }
        
        static async Task AnalyzeSpecificSegment(PipelineNetwork network, FlowCalculationResult result)
        {
            Console.WriteLine("\nAvailable segments:");
            var segments = result.SegmentAnalysis.Values.OrderBy(s => s.SegmentId).ToList();
            
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var status = segment.IsOverCapacity ? " [OVER CAPACITY]" : "";
                Console.WriteLine($"{i + 1}. {segment.SegmentId} - {segment.SegmentName}{status}");
            }
            
            Console.Write($"Select segment (1-{segments.Count}): ");
            if (int.TryParse(ReadLineWithDefault("1"), out int segmentIndex) && 
                segmentIndex > 0 && segmentIndex <= segments.Count)
            {
                var selectedSegment = segments[segmentIndex - 1];
                var networkSegment = network.Segments[selectedSegment.SegmentId];
                
                Console.WriteLine($"\n=== Detailed Analysis: {selectedSegment.SegmentId} ===");
                Console.WriteLine($"Name: {selectedSegment.SegmentName}");
                Console.WriteLine($"Route: {selectedSegment.FromPointId} → {selectedSegment.ToPointId}");
                Console.WriteLine($"Required Flow: {selectedSegment.RequiredFlow:F2} MMscfd");
                Console.WriteLine($"Capacity: {selectedSegment.Capacity:F2} MMscfd");
                Console.WriteLine($"Utilization: {selectedSegment.UtilizationPercentage:F1}%");
                Console.WriteLine($"Length: {networkSegment.Length:F1} miles");
                Console.WriteLine($"Diameter: {networkSegment.Diameter:F0} inches");
                Console.WriteLine($"Transport Cost: ${networkSegment.TransportationCost:F3}/MMscf");
                
                if (selectedSegment.IsOverCapacity)
                {
                    Console.WriteLine($"⚠ CAPACITY EXCEEDED by {selectedSegment.ExcessFlow:F2} MMscfd");
                    Console.WriteLine("Recommendations:");
                    Console.WriteLine("- Consider capacity expansion");
                    Console.WriteLine("- Evaluate alternative routing");
                    Console.WriteLine("- Review demand projections");
                }
                else
                {
                    var remaining = selectedSegment.Capacity - selectedSegment.RequiredFlow;
                    Console.WriteLine($"✓ Available capacity: {remaining:F2} MMscfd");
                }
            }
            else
            {
                Console.WriteLine("Invalid selection.");
            }
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
