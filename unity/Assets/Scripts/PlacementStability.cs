using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    // Consistency of observed hits is not a guarantee of absolute accuracy.
    public sealed class PlacementStability
    {
        public const float WindowSeconds = .4f;
        public const float MaxPositionSpread = .025f;
        public const float MaxNormalSpread = 5f;
        const int MinSamples = 6;
        struct Sample { public Pose pose; public Vector3 normal; public float time; }
        readonly List<Sample> samples = new List<Sample>(64);
        TrackableId planeId;
        bool consistent;
        public Pose LatestPose { get; private set; }
        public int SampleCount => samples.Count;
        public float PositionSpread { get; private set; }
        public float NormalSpread { get; private set; }
        public float Progress => samples.Count < 2 ? 0 : Mathf.Clamp01(Mathf.Min(
            (samples[samples.Count - 1].time - samples[0].time) / WindowSeconds, samples.Count / (float)MinSamples));

        public void Clear()
        { samples.Clear(); consistent = false; PositionSpread = NormalSpread = 0; }

        public void Add(TrackableId id, Pose pose, float now)
        {
            var normal = pose.rotation * Vector3.up;
            if (!Finite(pose.position) || !Finite(normal) || normal.sqrMagnitude < .5f || float.IsNaN(now) || float.IsInfinity(now))
            { Clear(); return; }
            if (samples.Count > 0)
            {
                var previous = samples[samples.Count - 1];
                if (now == previous.time)
                {
                    if (id == planeId && Vector3.Distance(previous.pose.position, pose.position) < .0001f
                        && Vector3.Angle(previous.normal, normal) < .01f) return;
                    Clear();
                }
                else if (id != planeId || now < previous.time || now - previous.time > .2f
                    || Vector3.Distance(previous.pose.position, pose.position) > .06f
                    || Vector3.Angle(previous.normal, normal) > 12f) Clear();
            }
            planeId = id;
            LatestPose = pose;
            samples.Add(new Sample { pose = pose, normal = normal.normalized, time = now });
            // Retain one sample at the window boundary, independent of frame rate.
            while (samples.Count > 1 && now - samples[1].time >= WindowSeconds) samples.RemoveAt(0);
            Vector3 center = Vector3.zero, averageNormal = Vector3.zero;
            foreach (var sample in samples) { center += sample.pose.position; averageNormal += sample.normal; }
            center /= samples.Count;
            averageNormal.Normalize();
            PositionSpread = NormalSpread = 0;
            foreach (var sample in samples)
            {
                PositionSpread = Mathf.Max(PositionSpread, Vector3.Distance(center, sample.pose.position));
                NormalSpread = Mathf.Max(NormalSpread, Vector3.Angle(averageNormal, sample.normal));
            }
            consistent = averageNormal.sqrMagnitude > .5f && PositionSpread <= MaxPositionSpread && NormalSpread <= MaxNormalSpread;
        }

        public bool IsReady(float now) => samples.Count >= MinSamples && consistent
            && now >= samples[samples.Count - 1].time && now - samples[samples.Count - 1].time <= .15f
            && samples[samples.Count - 1].time - samples[0].time >= WindowSeconds - .001f;

        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y)
            && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
    }
}
