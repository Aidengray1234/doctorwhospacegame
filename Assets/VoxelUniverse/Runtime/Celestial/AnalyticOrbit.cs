using System;
using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Celestial
{
    public static class AnalyticOrbit
    {
        private const double TwoPi = Math.PI * 2d;

        public static Double3 EvaluateRelativePosition(OrbitalElements elements, double timeSeconds)
        {
            elements.Validate();
            double meanMotion = TwoPi / elements.periodSeconds;
            double meanAnomaly = NormalizeRadians(elements.meanAnomalyAtEpochRadians + meanMotion * (timeSeconds - elements.epochSeconds));
            double eccentricAnomaly = SolveEccentricAnomaly(meanAnomaly, elements.eccentricity);
            double cosE = Math.Cos(eccentricAnomaly);
            double sinE = Math.Sin(eccentricAnomaly);
            double oneMinusESquared = 1d - elements.eccentricity * elements.eccentricity;
            double xOrbital = elements.semiMajorAxis * (cosE - elements.eccentricity);
            double yOrbital = elements.semiMajorAxis * Math.Sqrt(oneMinusESquared) * sinE;
            return RotateFromOrbitalPlane(xOrbital, yOrbital, elements.longitudeAscendingNodeRadians, elements.inclinationRadians, elements.argumentOfPeriapsisRadians);
        }

        public static void EvaluateBinaryBarycentre(OrbitalElements relativeOrbit, double primaryMass, double secondaryMass, double timeSeconds, out Double3 primaryPosition, out Double3 secondaryPosition)
        {
            if (primaryMass <= 0d) throw new ArgumentOutOfRangeException("primaryMass");
            if (secondaryMass <= 0d) throw new ArgumentOutOfRangeException("secondaryMass");
            Double3 separation = EvaluateRelativePosition(relativeOrbit, timeSeconds);
            double totalMass = primaryMass + secondaryMass;
            primaryPosition = -separation * (secondaryMass / totalMass);
            secondaryPosition = separation * (primaryMass / totalMass);
        }

        public static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
        {
            if (eccentricity < 0d || eccentricity >= 1d) throw new ArgumentOutOfRangeException("eccentricity");
            double normalizedMean = NormalizeRadians(meanAnomaly);
            double estimate = eccentricity < 0.8d ? normalizedMean : Math.PI;
            for (int i = 0; i < 12; i++)
            {
                double function = estimate - eccentricity * Math.Sin(estimate) - normalizedMean;
                double derivative = 1d - eccentricity * Math.Cos(estimate);
                double delta = function / derivative;
                estimate -= delta;
                if (Math.Abs(delta) <= 1e-13d) break;
            }
            return estimate;
        }

        public static double NormalizeRadians(double value)
        {
            value %= TwoPi;
            return value < 0d ? value + TwoPi : value;
        }

        private static Double3 RotateFromOrbitalPlane(double x, double y, double longitudeAscendingNode, double inclination, double argumentOfPeriapsis)
        {
            double cosO = Math.Cos(longitudeAscendingNode);
            double sinO = Math.Sin(longitudeAscendingNode);
            double cosI = Math.Cos(inclination);
            double sinI = Math.Sin(inclination);
            double cosW = Math.Cos(argumentOfPeriapsis);
            double sinW = Math.Sin(argumentOfPeriapsis);
            double xBasisX = cosO * cosW - sinO * sinW * cosI;
            double xBasisY = sinO * cosW + cosO * sinW * cosI;
            double xBasisZ = sinW * sinI;
            double yBasisX = -cosO * sinW - sinO * cosW * cosI;
            double yBasisY = -sinO * sinW + cosO * cosW * cosI;
            double yBasisZ = cosW * sinI;
            return new Double3(xBasisX * x + yBasisX * y, xBasisY * x + yBasisY * y, xBasisZ * x + yBasisZ * y);
        }
    }
}
