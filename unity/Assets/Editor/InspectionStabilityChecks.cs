using System;
using Inspection;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

// Deterministic checks for the temporal filter, run before every Android build.
public static class InspectionStabilityChecks
{
    public static void Run()
    {
        var id = new TrackableId(1, 1);
        var other = new TrackableId(2, 2);
        var filter = new PlacementStability();
        for (int i = 0; i <= 10; i++) filter.Add(id, Hit(new Vector3(i % 2 * .003f, 0, 1)), i * .05f);
        Check(filter.IsReady(.5f), "small stationary jitter should become stable");
        Check(!filter.IsReady(.66f), "old observations must not authorize a new placement");
        filter.Add(other, Hit(Vector3.forward), .55f);
        Check(filter.SampleCount == 1 && !filter.IsReady(.55f), "switching planes must restart the window");

        filter.Clear();
        for (int i = 0; i <= 10; i++) filter.Add(id, Hit(new Vector3(i * .015f, 0, 1)), i * .05f);
        Check(!filter.IsReady(.5f), "a moving aim must not be averaged into a stable target");

        filter.Clear();
        for (int i = 0; i <= 8; i++) filter.Add(id, new Pose(Vector3.forward, Quaternion.AngleAxis(-8 + i * 2, Vector3.forward)), i * .05f);
        Check(!filter.IsReady(.4f), "changing surface normals must reject confirmation");

        filter.Clear();
        filter.Add(id, Hit(Vector3.forward), 0);
        filter.Add(id, Hit(Vector3.forward), .2f);
        filter.Add(id, Hit(Vector3.forward), .4f);
        Check(!filter.IsReady(.4f), "two or three samples are insufficient even over a long duration");

        filter.Clear();
        for (int i = 0; i <= 8; i++) filter.Add(id, Hit(Vector3.forward), i * .05f);
        Check(filter.IsReady(.4f), "a complete stationary window should pass");
        filter.Add(id, Hit(Vector3.forward * 2), .4f);
        Check(!filter.IsReady(.4f) && filter.SampleCount == 1, "same-frame confirmation jump must invalidate stability");
        filter.Add(id, Hit(new Vector3(float.NaN, 0, 1)), .45f);
        Check(filter.SampleCount == 0, "invalid coordinates must clear the candidate");

        filter.Clear();
        var wall = new Pose(new Vector3(0, 1, 2), Quaternion.Euler(90, 0, 0));
        for (int i = 0; i <= 8; i++) filter.Add(id, wall, i * .05f);
        Check(filter.IsReady(.4f), "vertical wall poses should pass the same consistency check");
        filter.Clear();
        Check(!filter.IsReady(.4f), "tracking loss reset must prevent confirmation");
        Debug.Log("Placement stability checks passed (11 checks).");
    }

    static Pose Hit(Vector3 position) => new Pose(position, Quaternion.identity);
    static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Placement stability check failed: " + message); }
}
