namespace GasPipelineOptimization.Utilities
{
    /// <summary>
    /// Mathematical utility functions for gas pipeline optimization calculations
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Calculates pressure drop using the simplified gas flow equation
        /// </summary>
        /// <param name="flow">Gas flow rate (MMscfd)</param>
        /// <param name="frictionConstant">Friction constant k</param>
        /// <returns>Pressure drop (psi²)</returns>
        public static double CalculatePressureDrop(double flow, double frictionConstant)
        {
            return frictionConstant * flow * Math.Abs(flow);
        }

        /// <summary>
        /// Calculates pressure drop using Weymouth equation for gas pipelines
        /// </summary>
        /// <param name="flow">Gas flow rate (MMscfd)</param>
        /// <param name="diameter">Pipe diameter (inches)</param>
        /// <param name="length">Pipe length (miles)</param>
        /// <param name="temperature">Gas temperature (°F)</param>
        /// <param name="specificGravity">Specific gravity of gas</param>
        /// <param name="compressibilityFactor">Gas compressibility factor</param>
        /// <returns>Pressure drop (psi²)</returns>
        public static double CalculateWeymouthPressureDrop(double flow, double diameter, double length,
            double temperature = 60.0, double specificGravity = 0.6, double compressibilityFactor = 0.9)
        {
            // Weymouth equation: ΔP² = (433.5 * Q² * L * T * G * Z) / D^5.31
            var temperatureR = temperature + 459.67; // Convert to Rankine
            var pressureDropSquared = (433.5 * flow * flow * length * temperatureR * specificGravity * compressibilityFactor) 
                                    / Math.Pow(diameter, 5.31);
            
            return Math.Max(0, pressureDropSquared);
        }

        /// <summary>
        /// Calculates pressure drop using Panhandle A equation
        /// </summary>
        public static double CalculatePanhandleAPressureDrop(double flow, double diameter, double length,
            double temperature = 60.0, double specificGravity = 0.6, double compressibilityFactor = 0.9,
            double efficiency = 1.0)
        {
            // Panhandle A: Q = 435.87 * E * (P1² - P2²)^0.5394 * D^2.6182 / (G^0.5394 * T^0.5394 * L^0.5394 * Z^0.5394)
            var temperatureR = temperature + 459.67;
            var factor = 435.87 * efficiency * Math.Pow(diameter, 2.6182) / 
                        (Math.Pow(specificGravity * temperatureR * length * compressibilityFactor, 0.5394));
            
            var pressureDropTerm = Math.Pow(flow / factor, 1.0 / 0.5394);
            return Math.Max(0, pressureDropTerm);
        }

        /// <summary>
        /// Calculates gas compressibility factor using simplified correlation
        /// </summary>
        public static double CalculateCompressibilityFactor(double pressure, double temperature,
            double specificGravity = 0.6)
        {
            // Simplified Standing-Katz correlation
            var temperatureR = temperature + 459.67;
            var pseudoCriticalPressure = 677 + 15.0 * specificGravity - 37.5 * specificGravity * specificGravity;
            var pseudoCriticalTemperature = 168 + 325 * specificGravity - 12.5 * specificGravity * specificGravity;
            
            var reducedPressure = pressure / pseudoCriticalPressure;
            var reducedTemperature = temperatureR / pseudoCriticalTemperature;
            
            // Simplified Z-factor calculation
            var z = 1.0 - (0.2 * reducedPressure / reducedTemperature);
            z += Math.Pow(reducedPressure, 2) * (0.04 - 0.001 * reducedTemperature) / reducedTemperature;
            
            return Math.Max(0.5, Math.Min(1.2, z));
        }

        /// <summary>
        /// Calculates Reynolds number for gas flow in pipe
        /// </summary>
        public static double CalculateReynoldsNumber(double flow, double diameter, double viscosity,
            double density)
        {
            if (diameter <= 0 || viscosity <= 0)
                return 0;
            
            // Convert flow to velocity (simplified)
            var area = Math.PI * Math.Pow(diameter / 24.0, 2); // Convert diameter to feet and calculate area
            var velocity = flow / (area * 86400); // ft/s (simplified conversion)
            
            return (density * velocity * diameter / 12.0) / viscosity; // Convert diameter to feet
        }

        /// <summary>
        /// Calculates Darcy friction factor using Colebrook-White equation
        /// </summary>
        public static double CalculateFrictionFactor(double reynoldsNumber, double relativeRoughness)
        {
            if (reynoldsNumber <= 0)
                return 0.02; // Default value
            
            if (reynoldsNumber < 2300)
            {
                // Laminar flow
                return 64.0 / reynoldsNumber;
            }
            else
            {
                // Turbulent flow - simplified Colebrook approximation
                var term1 = relativeRoughness / 3.7;
                var term2 = 2.51 / reynoldsNumber;
                
                // Iterative solution approximation
                var f = 0.02; // Initial guess
                for (int i = 0; i < 5; i++)
                {
                    var fNew = Math.Pow(-2.0 * Math.Log10(term1 + term2 / Math.Sqrt(f)), -2);
                    if (Math.Abs(fNew - f) < 1e-6)
                        break;
                    f = fNew;
                }
                
                return Math.Max(0.008, Math.Min(0.1, f));
            }
        }

        /// <summary>
        /// Performs linear interpolation between two points
        /// </summary>
        public static double LinearInterpolation(double x, double x1, double y1, double x2, double y2)
        {
            if (Math.Abs(x2 - x1) < 1e-10)
                return y1;
            
            return y1 + (y2 - y1) * (x - x1) / (x2 - x1);
        }

        /// <summary>
        /// Performs piecewise linear interpolation
        /// </summary>
        public static double PiecewiseLinearInterpolation(double x, List<(double X, double Y)> points)
        {
            if (!points.Any())
                return 0;
            
            if (points.Count == 1)
                return points[0].Y;
            
            // Sort points by X value
            var sortedPoints = points.OrderBy(p => p.X).ToList();
            
            // Handle extrapolation
            if (x <= sortedPoints.First().X)
                return sortedPoints.First().Y;
            
            if (x >= sortedPoints.Last().X)
                return sortedPoints.Last().Y;
            
            // Find interpolation interval
            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                if (x >= sortedPoints[i].X && x <= sortedPoints[i + 1].X)
                {
                    return LinearInterpolation(x, 
                        sortedPoints[i].X, sortedPoints[i].Y,
                        sortedPoints[i + 1].X, sortedPoints[i + 1].Y);
                }
            }
            
            return 0;
        }

        /// <summary>
        /// Calculates percentage difference between two values
        /// </summary>
        public static double PercentageDifference(double value1, double value2)
        {
            var average = (Math.Abs(value1) + Math.Abs(value2)) / 2.0;
            return average > 1e-10 ? Math.Abs(value1 - value2) / average * 100.0 : 0;
        }

        /// <summary>
        /// Normalizes a value to a range [0, 1]
        /// </summary>
        public static double Normalize(double value, double min, double max)
        {
            if (Math.Abs(max - min) < 1e-10)
                return 0;
            
            return Math.Max(0, Math.Min(1, (value - min) / (max - min)));
        }

        /// <summary>
        /// Calculates weighted average of values
        /// </summary>
        public static double WeightedAverage(IEnumerable<(double Value, double Weight)> data)
        {
            var totalWeight = data.Sum(d => d.Weight);
            if (totalWeight <= 0)
                return 0;
            
            return data.Sum(d => d.Value * d.Weight) / totalWeight;
        }

        /// <summary>
        /// Calculates standard deviation of a collection of values
        /// </summary>
        public static double StandardDeviation(IEnumerable<double> values)
        {
            var valueList = values.ToList();
            if (!valueList.Any())
                return 0;
            
            var mean = valueList.Average();
            var squaredDeviations = valueList.Select(v => Math.Pow(v - mean, 2));
            var variance = squaredDeviations.Average();
            
            return Math.Sqrt(variance);
        }

        /// <summary>
        /// Solves quadratic equation ax² + bx + c = 0
        /// </summary>
        public static (double? Root1, double? Root2) SolveQuadratic(double a, double b, double c)
        {
            if (Math.Abs(a) < 1e-10)
            {
                // Linear equation: bx + c = 0
                if (Math.Abs(b) < 1e-10)
                    return (null, null);
                
                var root = -c / b;
                return (root, null);
            }
            
            var discriminant = b * b - 4 * a * c;
            
            if (discriminant < 0)
                return (null, null); // No real roots
            
            if (Math.Abs(discriminant) < 1e-10)
            {
                // One root
                var root = -b / (2 * a);
                return (root, null);
            }
            
            // Two roots
            var sqrtDiscriminant = Math.Sqrt(discriminant);
            var root1 = (-b + sqrtDiscriminant) / (2 * a);
            var root2 = (-b - sqrtDiscriminant) / (2 * a);
            
            return (root1, root2);
        }

        /// <summary>
        /// Converts units for gas flow calculations
        /// </summary>
        public static class UnitConversions
        {
            /// <summary>
            /// Converts MMscfd to cubic meters per day
            /// </summary>
            public static double MMscfdToCubicMetersPerDay(double mmscfd)
            {
                return mmscfd * 28316.8; // 1 MMscf = 28,316.8 m³
            }

            /// <summary>
            /// Converts psia to bara
            /// </summary>
            public static double PsiaToBara(double psia)
            {
                return psia * 0.0689476; // 1 psi = 0.0689476 bar
            }

            /// <summary>
            /// Converts miles to kilometers
            /// </summary>
            public static double MilesToKilometers(double miles)
            {
                return miles * 1.60934;
            }

            /// <summary>
            /// Converts inches to millimeters
            /// </summary>
            public static double InchesToMillimeters(double inches)
            {
                return inches * 25.4;
            }

            /// <summary>
            /// Converts Fahrenheit to Celsius
            /// </summary>
            public static double FahrenheitToCelsius(double fahrenheit)
            {
                return (fahrenheit - 32) * 5.0 / 9.0;
            }
        }
    }
}
