using System;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using NUnit.Framework;

namespace DoctorWho.VoxelUniverse.Tests
{
    public sealed class CoreFoundationTests
    {
        [Test]
        public void BodyId_IsStableAndRoundTrips()
        {
            CelestialBodyId first = CelestialBodyId.FromStableString("sol/earth");
            CelestialBodyId second = CelestialBodyId.FromStableString("sol/earth");
            CelestialBodyId parsed;
            Assert.AreEqual(first, second);
            Assert.IsTrue(CelestialBodyId.TryParse(first.ToString(), out parsed));
            Assert.AreEqual(first, parsed);
            Assert.AreNotEqual(first, CelestialBodyId.FromStableString("sol/mars"));
        }

        [Test]
        public void BinaryOrbit_RemainsCenteredOnBarycentre()
        {
            OrbitalElements elements = new OrbitalElements
            {
                semiMajorAxis = 1000d,
                eccentricity = 0.21d,
                inclinationRadians = 0.3d,
                longitudeAscendingNodeRadians = 0.6d,
                argumentOfPeriapsisRadians = 1.1d,
                meanAnomalyAtEpochRadians = 0.2d,
                epochSeconds = 50d,
                periodSeconds = 2000d
            };
            Double3 primary;
            Double3 secondary;
            const double primaryMass = 3d;
            const double secondaryMass = 1d;
            AnalyticOrbit.EvaluateBinaryBarycentre(elements, primaryMass, secondaryMass, 1234.5d, out primary, out secondary);
            Double3 weightedCenter = (primary * primaryMass + secondary * secondaryMass) / (primaryMass + secondaryMass);
            Assert.Less(weightedCenter.Magnitude, 1e-9d);
        }

        [Test]
        public void PackedSection_Uses4096CellsAndKeepsSnapshotsImmutable()
        {
            PackedVoxelSection section = new PackedVoxelSection();
            BlockState stone = new BlockState(1, 0, 0);
            BlockState logEast = new BlockState(2, 1, 0);
            Assert.AreEqual(VoxelConstants.SectionVolume, 4096);
            Assert.IsTrue(section.Set(15, 15, 15, stone));
            PackedVoxelSection.Snapshot snapshot = section.CreateSnapshot();
            Assert.IsTrue(section.Set(15, 15, 15, logEast));
            Assert.AreEqual(stone, snapshot.Get(15, 15, 15));
            Assert.AreEqual(logEast, section.Get(15, 15, 15));
            Assert.Greater(section.Version, snapshot.Version);
        }

        [TestCase(CubeSphereFace.PositiveX)]
        [TestCase(CubeSphereFace.NegativeX)]
        [TestCase(CubeSphereFace.PositiveY)]
        [TestCase(CubeSphereFace.NegativeY)]
        [TestCase(CubeSphereFace.PositiveZ)]
        [TestCase(CubeSphereFace.NegativeZ)]
        public void FaceBasis_IsRightHandedAndOrthonormal(CubeSphereFace face)
        {
            FaceBasis basis = CubeSphereMapper.GetFaceBasis(face);
            Assert.That(Math.Abs(Double3.Dot(basis.normal, basis.east)), Is.LessThan(1e-12d));
            Assert.That(Math.Abs(Double3.Dot(basis.normal, basis.north)), Is.LessThan(1e-12d));
            Assert.That(Math.Abs(Double3.Dot(basis.east, basis.north)), Is.LessThan(1e-12d));
            Assert.That((Double3.Cross(basis.east, basis.north) - basis.normal).Magnitude, Is.LessThan(1e-12d));
        }

        [TestCase(CubeSphereFace.PositiveX, 0.25d, -0.75d)]
        [TestCase(CubeSphereFace.NegativeX, -0.4d, 0.8d)]
        [TestCase(CubeSphereFace.PositiveY, 0.3d, 0.4d)]
        [TestCase(CubeSphereFace.NegativeY, -0.2d, -0.6d)]
        [TestCase(CubeSphereFace.PositiveZ, 0.9d, -0.1d)]
        [TestCase(CubeSphereFace.NegativeZ, -0.8d, 0.2d)]
        public void FaceUv_RoundTrips(CubeSphereFace face, double u, double v)
        {
            Double3 direction = CubeSphereMapper.FaceUvToDirection(face, u, v);
            CubeSphereFace recoveredFace;
            double recoveredU;
            double recoveredV;
            CubeSphereMapper.DirectionToFaceUv(direction, out recoveredFace, out recoveredU, out recoveredV);
            Assert.AreEqual(face, recoveredFace);
            Assert.That(recoveredU, Is.EqualTo(u).Within(1e-12d));
            Assert.That(recoveredV, Is.EqualTo(v).Within(1e-12d));
        }

        [Test]
        public void SeamCanonicalization_ProducesValidAddress()
        {
            CelestialBodyId body = CelestialBodyId.FromStableString("test/body");
            VoxelAddress outside = new VoxelAddress(body, CubeSphereFace.PositiveX, 64, 20, 3);
            VoxelAddress canonical = CubeSphereMapper.Canonicalize(outside, 64);
            Assert.That(canonical.u, Is.InRange(0, 63));
            Assert.That(canonical.v, Is.InRange(0, 63));
            Assert.AreEqual(outside.radial, canonical.radial);
            Assert.AreEqual(outside.bodyId, canonical.bodyId);
        }

        [Test]
        public void NegativeVoxelCoordinates_UseFloorDivision()
        {
            VoxelAddress address = new VoxelAddress(CelestialBodyId.FromStableString("test/body"), CubeSphereFace.PositiveZ, -1, -17, -16);
            Assert.AreEqual(-1, address.SectionKey.sectionU);
            Assert.AreEqual(-2, address.SectionKey.sectionV);
            Assert.AreEqual(-1, address.SectionKey.sectionRadial);
            Assert.AreEqual(new Int3(15, 0, 15), address.Local);
        }
    }
}
