using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    public enum SurfaceSource { None, Plane, Depth, PlaneWithDepth, Conflict }

    public struct SurfaceDecision
    {
        public SurfaceSource source;
        public Pose pose;
        public float separation;
        public bool IsUsable => source == SurfaceSource.Plane || source == SurfaceSource.Depth || source == SurfaceSource.PlaneWithDepth;
    }

    // Both inputs must be world-space hit poses from the same screen target and frame.
    public static class SurfaceHitPolicy
    {
        public const float MinDepthRange = .2f;
        public const float MaxDepthRange = 5f;
        public const float MaxNormalDifference = 30f;
        static readonly TrackableId DepthKey = new TrackableId(ulong.MaxValue, 1);

        public static SurfaceDecision Choose(bool hasPlane, Pose plane, bool hasDepth, Pose depth, Vector3 cameraPosition)
        {
            hasPlane = hasPlane && IsFinitePose(plane);
            hasDepth = hasDepth && IsUsableDepthPose(depth, cameraPosition);
            if (!hasPlane && !hasDepth) return default;
            if (!hasPlane) return new SurfaceDecision { source = SurfaceSource.Depth, pose = depth };
            if (!hasDepth) return new SurfaceDecision { source = SurfaceSource.Plane, pose = plane };

            float separation = Vector3.Distance(plane.position, depth.position);
            float range = Vector3.Distance(cameraPosition, plane.position);
            // Tunable heuristic, not a sensor confidence or accuracy guarantee.
            float tolerance = Mathf.Min(.15f, .05f + .02f * range);
            float angle = Vector3.Angle(plane.rotation * Vector3.up, depth.rotation * Vector3.up);
            angle = Mathf.Min(angle, 180f - angle); // Opposite normal signs can describe the same surface.
            if (separation > tolerance || angle > MaxNormalDifference)
                return new SurfaceDecision { source = SurfaceSource.Conflict, separation = separation };
            // Keep a real plane hit, rather than averaging disagreeing or differently oriented points.
            return new SurfaceDecision { source = SurfaceSource.PlaneWithDepth, pose = plane, separation = separation };
        }

        // DepthPoint IDs need not persist across frames. Depth continuity is checked spatially by PlacementStability.
        public static TrackableId StabilityKey(SurfaceSource source, TrackableId planeId) => source == SurfaceSource.Depth ? DepthKey : planeId;

        public static bool IsUsableDepthPose(Pose pose, Vector3 cameraPosition)
        {
            if (!IsFinitePose(pose) || !Finite(cameraPosition)) return false;
            float range = Vector3.Distance(cameraPosition, pose.position);
            return range >= MinDepthRange && range <= MaxDepthRange;
        }

        public static bool IsFinitePose(Pose pose)
        {
            var q = pose.rotation;
            if (!Finite(pose.position) || !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)) return false;
            float magnitude = Quaternion.Dot(q, q);
            return magnitude > .5f && magnitude < 1.5f;
        }

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }

    // Tracks advancing image timestamps. Reacquiring an old image must not keep depth "ready".
    public sealed class DepthFrameFreshness
    {
        public const float MaxAgeSeconds = .5f;
        double lastTimestamp;
        float lastObserved, lastAdvanced;
        int advancingFrames;
        public void Clear() { advancingFrames = 0; lastTimestamp = 0; lastObserved = lastAdvanced = float.NegativeInfinity; }

        public void Observe(double timestamp, float now, double? cameraTimestamp)
        {
            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp) || timestamp <= 0
                || float.IsNaN(now) || float.IsInfinity(now)
                || (cameraTimestamp.HasValue && System.Math.Abs(cameraTimestamp.Value - timestamp) > MaxAgeSeconds))
            { Clear(); return; }
            if (advancingFrames > 0 && (timestamp < lastTimestamp || now < lastObserved || now - lastObserved > MaxAgeSeconds)) Clear();
            if (advancingFrames == 0 || timestamp > lastTimestamp)
            { advancingFrames = Mathf.Min(2, advancingFrames + 1); lastAdvanced = now; }
            lastTimestamp = timestamp;
            lastObserved = now;
        }

        public bool IsFresh(float now) => advancingFrames >= 2 && now >= lastAdvanced && now - lastAdvanced <= MaxAgeSeconds;
    }
}
