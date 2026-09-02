using System.Diagnostics;
using System.Runtime.ExceptionServices;
using GMap.NET.WindowsPresentation;

namespace UnitTest.GMap.NET.WindowsPresentation;

[TestClass]
public class GMapControlPositionBearingTests
{
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public void SetPositionAndBearing_WhenBothValuesChange_RegeneratesEachOverlayOnceWithFinalState()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap(3, 2);
            var position = new PointLatLng(53.5511, 9.9937);

            map.SetPositionAndBearing(position, 42f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(42f, map.Bearing);
            Assert.AreEqual(3, map.TotalRegenerationCount);
            Assert.IsTrue(map.RegenerationCounts.Values.All(count => count == 1));
            Assert.IsTrue(map.RegeneratedStates.All(state => state.Position == position && state.Bearing == 42f));
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenOnlyPositionChanges_RegeneratesOverlaysOnce()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            map.Bearing = 15f;
            map.ResetRegenerationCounts();
            var position = new PointLatLng(52.5200, 13.4050);

            map.SetPositionAndBearing(position, 15f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(15f, map.Bearing);
            Assert.AreEqual(1, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenOnlyBearingChanges_RegeneratesOverlaysOnce()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var position = map.Position;

            map.SetPositionAndBearing(position, 90f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(90f, map.Bearing);
            Assert.AreEqual(1, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenNeitherValueChanges_DoesNotRegenerateOverlays()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var position = map.Position;
            var bearing = map.Bearing;

            map.SetPositionAndBearing(position, bearing);

            Assert.AreEqual(0, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void IndividualSetters_PreserveUpdatesNotificationsAndOverlayRegeneration()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            int positionNotifications = 0;
            map.OnPositionChanged += _ => positionNotifications++;
            var position = new PointLatLng(48.1351, 11.5820);

            map.Position = position;

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(1, positionNotifications);
            Assert.AreEqual(1, map.TotalRegenerationCount);

            map.ResetRegenerationCounts();
            map.Bearing = 120f;

            Assert.AreEqual(120f, map.Bearing);
            Assert.AreEqual(1, positionNotifications);
            Assert.AreEqual(1, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_PositionNotificationObservesFinalBearing()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            float observedBearing = float.NaN;
            map.OnPositionChanged += _ => observedBearing = map.Bearing;

            map.SetPositionAndBearing(new PointLatLng(50.1109, 8.6821), 75f);

            Assert.AreEqual(75f, observedBearing);
            Assert.AreEqual(1, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_NestedUpdateFlushesOnlyAtOutermostCompletion()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var nestedPosition = new PointLatLng(51.0504, 13.7373);
            bool nested = false;
            map.OnPositionChanged += _ =>
            {
                if (!nested)
                {
                    nested = true;
                    map.SetPositionAndBearing(nestedPosition, 180f);
                }
            };

            map.SetPositionAndBearing(new PointLatLng(51.3397, 12.3731), 45f);

            Assert.AreEqual(nestedPosition, map.Position);
            Assert.AreEqual(180f, map.Bearing);
            Assert.AreEqual(1, map.TotalRegenerationCount);
            Assert.AreEqual(nestedPosition, map.RegeneratedStates.Single().Position);
            Assert.AreEqual(180f, map.RegeneratedStates.Single().Bearing);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenRegenerationThrows_DoesNotLeaveUpdatesDeferred()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            map.ThrowOnNextRegeneration = true;

            Assert.ThrowsException<InvalidOperationException>(() =>
                map.SetPositionAndBearing(new PointLatLng(49.0069, 8.4037), 30f));

            map.Bearing = 60f;

            Assert.AreEqual(2, map.TotalRegenerationCount);
            Assert.AreEqual(60f, map.Bearing);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_BeforeStartMatchesIndividualSetterLifecycle()
    {
        RunInSta(() =>
        {
            using var map = new CountingGMapControl();
            int positionNotifications = 0;
            map.OnPositionChanged += _ => positionNotifications++;
            var position = new PointLatLng(47.3769, 8.5417);

            map.SetPositionAndBearing(position, 25f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(25f, map.Bearing);
            Assert.AreEqual(0, positionNotifications);
            Assert.AreEqual(0, map.TotalRegenerationCount);
        });
    }

    [TestMethod]
    public void LargeRoutes_AtomicUpdateHalvesDeterministicRegenerationCount()
    {
        RunInSta(() =>
        {
            const int routeCount = 3;
            const int pointsPerRoute = 2000;
            const int updateCount = 5;
            using var map = CreateStartedMap(routeCount, pointsPerRoute);
            var stopwatch = Stopwatch.StartNew();

            for (int i = 1; i <= updateCount; i++)
            {
                map.Bearing = i;
                map.Position = new PointLatLng(50 + i * 0.001, 8 + i * 0.001);
            }

            stopwatch.Stop();
            long sequentialMilliseconds = stopwatch.ElapsedMilliseconds;
            int sequentialCount = map.TotalRegenerationCount;
            map.ResetRegenerationCounts();
            stopwatch.Restart();

            for (int i = 1; i <= updateCount; i++)
            {
                map.SetPositionAndBearing(
                    new PointLatLng(51 + i * 0.001, 9 + i * 0.001),
                    100 + i);
            }

            stopwatch.Stop();
            int atomicCount = map.TotalRegenerationCount;

            Assert.AreEqual(routeCount * updateCount * 2, sequentialCount);
            Assert.AreEqual(routeCount * updateCount, atomicCount);
            TestContext?.WriteLine(
                $"Large-route diagnostic ({routeCount * pointsPerRoute} points, {updateCount} updates): " +
                $"sequential={sequentialCount} regenerations/{sequentialMilliseconds} ms; " +
                $"atomic={atomicCount} regenerations/{stopwatch.ElapsedMilliseconds} ms.");
        });
    }

    private static CountingGMapControl CreateStartedMap(int routeCount = 1, int pointsPerRoute = 2)
    {
        var map = new CountingGMapControl();
        map.InitializeForBackgroundRendering(800, 600);

        for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
        {
            var points = Enumerable.Range(0, pointsPerRoute)
                .Select(index => new PointLatLng(50 + routeIndex * 0.01 + index * 0.00001,
                    8 + routeIndex * 0.01 + index * 0.00001));
            map.Markers.Add(new GMapRoute(points));
        }

        map.ResetRegenerationCounts();
        return map;
    }

    private static void RunInSta(Action action)
    {
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = ExceptionDispatchInfo.Capture(caught);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        exception?.Throw();
    }

    private sealed class CountingGMapControl : GMapControl
    {
        public Dictionary<IShapable, int> RegenerationCounts { get; } = new();
        public List<(PointLatLng Position, float Bearing)> RegeneratedStates { get; } = new();
        public int TotalRegenerationCount { get; private set; }
        public bool ThrowOnNextRegeneration { get; set; }

        public override void RegenerateShape(IShapable shapable)
        {
            TotalRegenerationCount++;
            RegenerationCounts.TryGetValue(shapable, out int count);
            RegenerationCounts[shapable] = count + 1;
            RegeneratedStates.Add((Position, Bearing));

            if (ThrowOnNextRegeneration)
            {
                ThrowOnNextRegeneration = false;
                throw new InvalidOperationException("Induced overlay-regeneration failure.");
            }

            base.RegenerateShape(shapable);
        }

        public void ResetRegenerationCounts()
        {
            TotalRegenerationCount = 0;
            RegenerationCounts.Clear();
            RegeneratedStates.Clear();
        }
    }
}
