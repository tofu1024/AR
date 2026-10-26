using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    [RequireComponent(typeof(ARPlane))]
    public class PlaneOutline : MonoBehaviour
    {
        ARPlane plane;
        LineRenderer line;
        Material material;

        void Awake()
        {
            plane = GetComponent<ARPlane>();
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.widthMultiplier = .004f;
            material = new Material(Shader.Find("Sprites/Default"));
            line.sharedMaterial = material;
            line.startColor = line.endColor = new Color(.25f, 1f, .7f, .8f);
        }

        void LateUpdate()
        {
            var boundary = plane.boundary;
            line.enabled = ARSession.state == ARSessionState.SessionTracking
                && plane.trackingState == TrackingState.Tracking && !plane.subsumedBy
                && boundary.IsCreated && boundary.Length > 1;
            if (!line.enabled) return;
            line.positionCount = boundary.Length;
            for (int i = 0; i < boundary.Length; i++)
                line.SetPosition(i, new Vector3(boundary[i].x, .005f, boundary[i].y));
        }

        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
