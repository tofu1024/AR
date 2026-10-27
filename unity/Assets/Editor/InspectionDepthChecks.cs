using System;
using Inspection;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

public static class InspectionDepthChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message)
        { checks++; if (!ok) throw new InvalidOperationException("Depth check failed: " + message); }
        Pose At(float z) => new Pose(new Vector3(0, 0, z), Quaternion.identity);
        SurfaceDecision Choose(bool plane, Pose pp, bool depth, Pose dp) => SurfaceHitPolicy.Choose(plane, pp, depth, dp, Vector3.zero);

        Check(Choose(true, At(1), false, default).source == SurfaceSource.Plane, "unsupported/missing depth preserves plane fallback");
        var alone = Choose(false, default, true, At(1));
        Check(alone.IsUsable && alone.source == SurfaceSource.Depth && alone.pose.position == At(1).position, "depth works without any plane");
        var both = Choose(true, At(1), true, At(1.02f));
        Check(both.source == SurfaceSource.PlaneWithDepth && both.pose.position == At(1).position, "agreement keeps a real hit, not an average");
        var conflict = Choose(true, At(2), true, At(.7f));
        Check(conflict.source == SurfaceSource.Conflict && !conflict.IsUsable, "foreground/background conflict cannot be confirmed");
        Check(!Choose(false, default, false, default).IsUsable, "no observations must not invent a position");
        Check(!Choose(false, default, true, At(.1f)).IsUsable, "too-close depth is rejected");
        Check(!Choose(false, default, true, At(6)).IsUsable, "out-of-range depth is rejected");
        Check(Choose(true, At(1), true, At(6)).source == SurfaceSource.Plane, "invalid depth does not masquerade as validation");
        var bad = new Pose(new Vector3(float.NaN, 0, 1), Quaternion.identity);
        Check(!Choose(false, default, true, bad).IsUsable, "invalid depth coordinate rejected");
        Check(Choose(true, bad, true, At(1)).source == SurfaceSource.Depth, "bad plane cannot block good depth");
        Check(!Choose(false, default, true, new Pose(Vector3.forward, new Quaternion())).IsUsable, "invalid rotation rejected");
        Check(Choose(true, At(1), true, new Pose(Vector3.forward, Quaternion.Euler(0, 0, 180))).source == SurfaceSource.PlaneWithDepth, "opposite normal signs describe the same surface");
        Check(Choose(true, At(1), true, new Pose(Vector3.forward, Quaternion.Euler(90, 0, 0))).source == SurfaceSource.Conflict, "incompatible normals are rejected");

        var id = new TrackableId(1, 1); var other = new TrackableId(2, 2);
        Check(SurfaceHitPolicy.StabilityKey(SurfaceSource.Depth, id) == SurfaceHitPolicy.StabilityKey(SurfaceSource.Depth, other), "depth stability does not require persistent plane/depth point IDs");
        Check(SurfaceHitPolicy.StabilityKey(SurfaceSource.Plane, id) != SurfaceHitPolicy.StabilityKey(SurfaceSource.Plane, other), "plane stability still distinguishes plane IDs");
        var stable = new PlacementStability();
        for (int i = 0; i <= 8; i++) stable.Add(SurfaceHitPolicy.StabilityKey(SurfaceSource.Depth, new TrackableId((ulong)i, 1)), At(1), i * .05f);
        Check(stable.IsReady(.4f), "depth-only samples reach confirmation without an ARPlane");

        var frames = new DepthFrameFreshness();
        frames.Observe(10, 0, 10);
        Check(!frames.IsFresh(0), "one image does not prove a live stream");
        frames.Observe(10, .1f, 10.1);
        Check(!frames.IsFresh(.1f), "reacquiring an old image cannot produce readiness");
        frames.Observe(10.1, .15f, 10.15);
        Check(frames.IsFresh(.15f), "advancing, camera-aligned images enable depth");
        frames.Observe(10.1, .4f, null);
        Check(!frames.IsFresh(.7f), "repeated timestamps cannot keep old depth fresh");
        frames.Observe(1, .8f, 10);
        Check(!frames.IsFresh(.8f), "stale relative to camera frame is rejected");
        frames.Observe(double.NaN, .9f, null);
        Check(!frames.IsFresh(.9f), "invalid image timestamp is rejected");
        frames.Observe(11, 1, null); frames.Observe(11.1, 1.1f, null);
        Check(frames.IsFresh(1.1f), "advancing images work when camera timestamp is unavailable");
        frames.Clear();
        Check(!frames.IsFresh(1.1f), "pause/reset invalidates prior depth");
        Debug.Log("Depth selection and freshness checks passed (" + checks + " checks).");
    }
}
