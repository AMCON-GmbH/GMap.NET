using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace UnitTest.GMap.NET.WindowsForms;

[TestClass]
public class GMapControlPositionBearingTests
{
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public void SetPositionAndBearing_WhenBothValuesChange_RefreshesOnceWithFinalRouteGeometry()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap(3, 2);
            var position = new PointLatLng(53.5511, 9.9937);
            float observedBearing = float.NaN;
            map.OnPositionChanged += _ => observedBearing = map.Bearing;

            map.SetPositionAndBearing(position, 42f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(42f, map.Bearing);
            Assert.AreEqual(42f, observedBearing);
            Assert.AreEqual(1, map.RefreshCount);
            AssertRoutesMatchFinalTransform(map);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenOnlyPositionChanges_RefreshesOnce()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            map.Bearing = 15f;
            map.ResetRefreshCount();
            var position = new PointLatLng(52.5200, 13.4050);

            map.SetPositionAndBearing(position, 15f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(15f, map.Bearing);
            Assert.AreEqual(1, map.RefreshCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenOnlyBearingChanges_RefreshesOnceWithoutPositionNotification()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var position = map.Position;
            int positionNotifications = 0;
            map.OnPositionChanged += _ => positionNotifications++;

            map.SetPositionAndBearing(position, 90f);

            Assert.AreEqual(position, map.Position);
            Assert.AreEqual(90f, map.Bearing);
            Assert.AreEqual(0, positionNotifications);
            Assert.AreEqual(1, map.RefreshCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenNeitherValueChanges_DoesNotRefreshOrNotify()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var position = map.Position;
            var bearing = map.Bearing;
            int positionNotifications = 0;
            map.OnPositionChanged += _ => positionNotifications++;

            map.SetPositionAndBearing(position, bearing);

            Assert.AreEqual(0, map.RefreshCount);
            Assert.AreEqual(0, positionNotifications);
        });
    }

    [TestMethod]
    public void IndividualSetters_PreserveUpdatesNotificationsAndRefreshBehavior()
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
            Assert.AreEqual(1, map.RefreshCount);

            map.ResetRefreshCount();
            map.Position = position;

            Assert.AreEqual(2, positionNotifications);
            Assert.AreEqual(1, map.RefreshCount);

            map.ResetRefreshCount();
            map.Bearing = 120f;

            Assert.AreEqual(120f, map.Bearing);
            Assert.AreEqual(1, map.RefreshCount);
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
            Assert.AreEqual(1, map.RefreshCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_WhenOverlayUpdateThrows_DoesNotLeaveUpdatesDeferred()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            var overlay = map.Overlays.Single();
            overlay.Routes.Add(null!);

            Assert.ThrowsException<NullReferenceException>(() =>
                map.SetPositionAndBearing(new PointLatLng(49.0069, 8.4037), 30f));

            overlay.Routes.Remove(null!);
            map.Bearing = 60f;

            Assert.AreEqual(2, map.RefreshCount);
            Assert.AreEqual(60f, map.Bearing);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_BeforeStartMatchesSetterLifecycle()
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
            Assert.AreEqual(0, map.RefreshCount);
        });
    }

    [TestMethod]
    public void SetPositionAndBearing_PreservesHoldInvalidationSemantics()
    {
        RunInSta(() =>
        {
            using var map = CreateStartedMap();
            map.HoldInvalidation = true;

            map.SetPositionAndBearing(map.Position, 25f);

            Assert.AreEqual(0, map.RefreshCount);
            Assert.IsTrue(map.HoldInvalidation);

            map.SetPositionAndBearing(new PointLatLng(52.3759, 9.7320), 25f);

            Assert.AreEqual(1, map.RefreshCount);
            Assert.IsFalse(map.HoldInvalidation);
        });
    }

    [TestMethod]
    public void LargeRoutes_AtomicUpdateHalvesDeterministicOverlayPassCount()
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
            int sequentialPasses = map.RefreshCount;
            map.ResetRefreshCount();
            stopwatch.Restart();

            for (int i = 1; i <= updateCount; i++)
            {
                map.SetPositionAndBearing(
                    new PointLatLng(51 + i * 0.001, 9 + i * 0.001),
                    100 + i);
            }

            stopwatch.Stop();
            int atomicPasses = map.RefreshCount;

            Assert.AreEqual(updateCount * 2, sequentialPasses);
            Assert.AreEqual(updateCount, atomicPasses);
            AssertRoutesMatchFinalTransform(map);
            TestContext?.WriteLine(
                $"WinForms large-route diagnostic ({routeCount * pointsPerRoute} points, {updateCount} updates): " +
                $"sequential={sequentialPasses} overlay passes/{sequentialMilliseconds} ms; " +
                $"atomic={atomicPasses} overlay passes/{stopwatch.ElapsedMilliseconds} ms. " +
                $"Each pass rebuilt all {routeCount} route geometries.");
        });
    }

    private static CountingGMapControl CreateStartedMap(int routeCount = 1, int pointsPerRoute = 2)
    {
        var map = new CountingGMapControl();
        map.Start();

        for (int routeIndex = 0; routeIndex < routeCount; routeIndex++)
        {
            var points = Enumerable.Range(0, pointsPerRoute)
                .Select(index => new PointLatLng(50 + routeIndex * 0.01 + index * 0.00001,
                    8 + routeIndex * 0.01 + index * 0.00001));
            var overlay = new GMapOverlay($"route-{routeIndex}");
            overlay.Routes.Add(new GMapRoute(points, $"route-{routeIndex}"));
            map.Overlays.Add(overlay);
        }

        map.ResetRefreshCount();
        return map;
    }

    private static void AssertRoutesMatchFinalTransform(GMapControl map)
    {
        foreach (var route in map.Overlays.SelectMany(overlay => overlay.Routes))
        {
            using var expectedRoute = new GMapRoute(route.Points, "expected");
            map.UpdateRouteLocalPosition(expectedRoute);
            CollectionAssert.AreEqual(expectedRoute.LocalPoints, route.LocalPoints);
        }
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
        public int RefreshCount { get; private set; }

        public override void Refresh()
        {
            RefreshCount++;
            base.Refresh();
        }

        public void Start()
        {
            OnLoad(EventArgs.Empty);
        }

        public void ResetRefreshCount()
        {
            RefreshCount = 0;
        }
    }
}
