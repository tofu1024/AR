using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    // The committed scene contains this entry point. Build a single small scene at runtime.
    public class InspectionBootstrap : MonoBehaviour
    {
        ARPlaneManager planes;

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            var session = new GameObject("AR Session");
            session.AddComponent<ARSession>();
            session.AddComponent<ARInputManager>();

            var originObject = new GameObject("XR Origin");
            originObject.SetActive(false);
            var origin = originObject.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originObject.transform, false);
            var cameraObject = new GameObject("AR Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var cameraManager = cameraObject.AddComponent<ARCameraManager>();
            cameraObject.AddComponent<ARCameraBackground>();
            var occlusion = cameraObject.AddComponent<AROcclusionManager>();
            occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Fastest;
            occlusion.environmentDepthTemporalSmoothingRequested = true;
            // Request depth data for hit-testing without changing the marker's rendering/occlusion.
            occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
            var depth = cameraObject.AddComponent<DepthAvailability>();
            depth.occlusion = occlusion;
            depth.cameraManager = cameraManager;
            var tracking = cameraObject.AddComponent<TrackedPoseDriver>();
            tracking.SetPoseSource(TrackedPoseDriver.DeviceType.GenericXRDevice, TrackedPoseDriver.TrackedPose.Center);
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            origin.CameraYOffset = 0;

            planes = originObject.AddComponent<ARPlaneManager>();
            planes.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            var raycasts = originObject.AddComponent<ARRaycastManager>();
            var anchors = originObject.AddComponent<ARAnchorManager>();
            var placement = originObject.AddComponent<ARPlacement>();
            placement.planes = planes;
            placement.raycasts = raycasts;
            placement.anchors = anchors;
            placement.depth = depth;
            originObject.SetActive(true);

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
            var form = gameObject.AddComponent<IssueForm>();
            form.Initialize(placement);
        }

        void Update()
        {
            foreach (var plane in planes.trackables)
                if (plane.GetComponent<PlaneOutline>() == null) plane.gameObject.AddComponent<PlaneOutline>();
        }
    }
}
