using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    [DefaultExecutionOrder(-50)]
    public class DepthAvailability : MonoBehaviour
    {
        public AROcclusionManager occlusion;
        public ARCameraManager cameraManager;
        readonly DepthFrameFreshness freshness = new DepthFrameFreshness();
        double? cameraTimestamp;
        float nextSampleTime;
        string state = "Depth checking";
        public string Status => state;
        public bool IsReady => ARSession.state == ARSessionState.SessionTracking && occlusion != null
            && occlusion.enabled && occlusion.descriptor?.environmentDepthImageSupported == Supported.Supported
            && occlusion.currentEnvironmentDepthMode != EnvironmentDepthMode.Disabled && freshness.IsFresh(Time.unscaledTime);

        void OnEnable()
        {
            ResetFrames();
            if (cameraManager != null) cameraManager.frameReceived += OnCameraFrame;
        }

        void OnDisable()
        {
            if (cameraManager != null) cameraManager.frameReceived -= OnCameraFrame;
            ResetFrames();
        }

        void OnCameraFrame(ARCameraFrameEventArgs args)
        { if (args.timestampNs.HasValue && args.timestampNs.Value > 0) cameraTimestamp = args.timestampNs.Value * 1e-9; }

        void Update()
        {
            if (ARSession.state != ARSessionState.SessionTracking)
            { freshness.Clear(); SetState("Depth checking"); return; }
            if (occlusion == null || occlusion.descriptor == null)
            { freshness.Clear(); SetState("Depth checking; plane fallback"); return; }
            var support = occlusion.descriptor.environmentDepthImageSupported;
            if (support == Supported.Unsupported)
            { freshness.Clear(); SetState("Depth unsupported; plane fallback"); return; }
            if (support != Supported.Supported || !occlusion.enabled || occlusion.currentEnvironmentDepthMode == EnvironmentDepthMode.Disabled)
            { freshness.Clear(); SetState("Depth waiting; plane fallback"); return; }

            // Acquire/dispose the CPU handle only to check freshness. SDK raycasts provide the world pose.
            if (Time.unscaledTime >= nextSampleTime)
            {
                nextSampleTime = Time.unscaledTime + .1f;
                if (occlusion.TryAcquireEnvironmentDepthCpuImage(out var image))
                {
                    using (image)
                    {
                        if (image.width > 0 && image.height > 0) freshness.Observe(image.timestamp, Time.unscaledTime, cameraTimestamp);
                        else freshness.Clear();
                    }
                }
            }
            SetState(IsReady ? "Depth ready" : "Depth waiting/stale; plane fallback");
        }

        void OnApplicationPause(bool paused) { if (paused) ResetFrames(); }
        void ResetFrames() { freshness.Clear(); cameraTimestamp = null; nextSampleTime = 0; }
        void SetState(string value)
        {
            if (state == value) return;
            state = value;
            Debug.Log("Inspection depth status: " + value);
        }
    }
}
