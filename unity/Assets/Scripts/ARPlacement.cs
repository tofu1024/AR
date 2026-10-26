using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection
{
    public class ARPlacement : MonoBehaviour
    {
        public ARRaycastManager raycasts;
        public ARPlaneManager planes;
        public ARAnchorManager anchors;
        public bool IsSubmitting { get; set; }
        public bool HasMarker => marker != null && markerAnchor != null;
        public bool IsCreatingAnchor { get; private set; }
        public Vector3 Position => markerAnchor != null ? markerAnchor.transform.position : Vector3.zero;
        public Vector2 AimScreenPoint { get; private set; }
        public bool CanConfirm => !IsSubmitting && !IsCreatingAnchor && ValidPlane(candidatePlane)
            && ARSession.state == ARSessionState.SessionTracking && stability.IsReady(Time.unscaledTime);
        public bool CanSubmit => HasMarker && !IsCreatingAnchor
            && markerAnchor.trackingState == TrackingState.Tracking && ARSession.state == ARSessionState.SessionTracking;
        public string Hint { get; private set; } = "Scan a floor, table or wall.";
        public Color PreviewColor { get; private set; } = Color.gray;

        readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        readonly PlacementStability stability = new PlacementStability();
        GameObject marker, preview;
        Material markerMaterial, previewMaterial;
        ARAnchor markerAnchor;
        ARPlane candidatePlane;
        bool previewInitialized;
        int requestVersion;
        string notice;
        float noticeUntil;

        void Start() { CenterAim(); }

        void Update()
        {
            if (AimScreenPoint == Vector2.zero || AimScreenPoint.x > Screen.width || AimScreenPoint.y > Screen.height) CenterAim();
            if (ARSession.state != ARSessionState.SessionTracking || IsSubmitting || IsCreatingAnchor)
            {
                ResetCandidate();
                Hint = IsSubmitting ? "Submitting issue..." : IsCreatingAnchor ? "Creating anchor..." : "AR: " + ARSession.state + ". Move slowly over a textured surface.";
                return;
            }
            // Scene taps select an aim point. UI taps never select or place AR content.
            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began && !IsOverUI(touch.position))
                { AimScreenPoint = touch.position; ResetCandidate(); noticeUntil = 0; }
            }
            if (IsOverUI(AimScreenPoint) || !TryGetHit(AimScreenPoint, out var hit, out var plane))
            {
                ResetCandidate();
                Hint = HasMarker ? "Marker anchored. Aim or tap another tracked surface to reposition." : "No tracked surface. Scan a floor, table or wall; aim inside its boundary.";
                if (Time.unscaledTime < noticeUntil) Hint = notice;
                return;
            }
            if (candidatePlane != plane) { stability.Clear(); previewInitialized = false; }
            candidatePlane = plane;
            stability.Add(plane.trackableId, hit.pose, Time.unscaledTime);
            UpdatePreview(hit.pose);
            Hint = CanConfirm ? "Stable target. Press Place marker to confirm."
                : "Hold aim still: " + Mathf.RoundToInt(stability.Progress * 100) + "% (position "
                    + (stability.PositionSpread * 100).ToString("F1") + " cm, normal " + stability.NormalSpread.ToString("F1") + " deg).";
            if (HasMarker && !CanSubmit) Hint = "Marker tracking limited. Scan the surroundings before submitting.";
            if (Time.unscaledTime < noticeUntil) Hint = notice;
        }

        bool TryGetHit(Vector2 point, out ARRaycastHit hit, out ARPlane plane)
        {
            hit = default; plane = null;
            if (!raycasts.Raycast(point, hits, TrackableType.PlaneWithinPolygon)) return false;
            foreach (var candidate in hits)
            {
                var found = planes.GetPlane(candidate.trackableId);
                if (!ValidPlane(found)) continue;
                hit = candidate; plane = found; return true;
            }
            return false;
        }

        static bool ValidPlane(ARPlane plane) => plane != null && plane.trackingState == TrackingState.Tracking && plane.subsumedBy == null;

        void UpdatePreview(Pose pose)
        {
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                preview.name = "Candidate surface cursor";
                preview.transform.localScale = new Vector3(.06f, .002f, .06f);
                Destroy(preview.GetComponent<Collider>());
                previewMaterial = new Material(Shader.Find("Unlit/Color"));
                preview.GetComponent<Renderer>().sharedMaterial = previewMaterial;
            }
            var target = pose.position + FacingNormal(pose) * .003f;
            float alpha = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            preview.transform.position = previewInitialized ? Vector3.Lerp(preview.transform.position, target, alpha) : target;
            preview.transform.rotation = previewInitialized ? Quaternion.Slerp(preview.transform.rotation, pose.rotation, alpha) : pose.rotation;
            previewInitialized = true;
            PreviewColor = CanConfirm ? new Color(.2f, 1f, .45f) : new Color(1f, .65f, .1f);
            previewMaterial.color = PreviewColor;
            preview.SetActive(true);
        }

        static Vector3 FacingNormal(Pose pose)
        {
            var normal = pose.rotation * Vector3.up;
            var camera = Camera.main;
            if (camera != null && Vector3.Dot(normal, camera.transform.position - pose.position) < 0) normal = -normal;
            return normal;
        }

        public async void ConfirmPlacement()
        {
            if (!CanConfirm) { SetNotice("Target not stable yet. Hold aim still until the cursor turns green."); return; }
            // Recheck a real hit at confirmation; never commit the smoothed cursor position.
            if (!TryGetHit(AimScreenPoint, out var hit, out var plane) || plane != candidatePlane)
            { ResetCandidate(); SetNotice("Target changed. Hold still and try again."); return; }
            stability.Add(plane.trackableId, hit.pose, Time.unscaledTime);
            if (!stability.IsReady(Time.unscaledTime)) { SetNotice("Target moved. Wait for green before confirming."); return; }
            IsCreatingAnchor = true;
            int version = ++requestVersion;
            ARAnchor created = null;
            try
            {
                if (anchors == null || !anchors.enabled || anchors.subsystem == null)
                    throw new InvalidOperationException("Anchor tracking is not available.");
                if (anchors.descriptor != null && anchors.descriptor.supportsTrackableAttachments)
                    created = anchors.AttachAnchor(plane, hit.pose);
                if (created == null)
                {
                    var result = await anchors.TryAddAnchorAsync(hit.pose);
                    if (result.status.IsSuccess()) created = result.value;
                }
                if (created == null) { SetNotice("Anchor creation failed. Scan and try again."); return; }
                if (this == null || version != requestVersion || !isActiveAndEnabled
                    || ARSession.state != ARSessionState.SessionTracking || !ValidPlane(plane))
                { RemoveAnchor(created); return; }
                ReleaseMarker();
                markerAnchor = created;
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Anchored issue marker";
                marker.transform.SetParent(created.transform, false);
                marker.transform.localScale = Vector3.one * .08f;
                float side = Vector3.Dot(FacingNormal(hit.pose), hit.pose.rotation * Vector3.up) >= 0 ? 1 : -1;
                marker.transform.localPosition = Vector3.up * (.04f * side);
                Destroy(marker.GetComponent<Collider>());
                if (markerMaterial == null)
                { markerMaterial = new Material(Shader.Find("Unlit/Color")); markerMaterial.color = new Color(1f, .45f, .12f); }
                marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
                SetNotice("Marker anchored. Fill the form and submit; Place marker again to reposition.");
            }
            catch (Exception error)
            {
                if (created != null && created != markerAnchor) RemoveAnchor(created);
                if (this != null) { Debug.LogWarning("Anchor creation failed: " + error.Message); SetNotice("Anchor unavailable. Scan and try again; previous marker retained."); }
            }
            finally { if (this != null && version == requestVersion) IsCreatingAnchor = false; }
        }

        public bool TryGetSubmissionPosition(out Vector3 position)
        { position = Position; return CanSubmit; }

        bool IsOverUI(Vector2 point)
        {
            if (EventSystem.current == null) return false;
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            uiHits.Clear(); EventSystem.current.RaycastAll(pointer, uiHits);
            return uiHits.Count > 0;
        }

        void CenterAim() { AimScreenPoint = new Vector2(Screen.width * .5f, Screen.height * .5f); }
        void SetNotice(string message) { notice = message; noticeUntil = Time.unscaledTime + 3f; Hint = message; }
        void ResetCandidate()
        {
            stability.Clear(); candidatePlane = null; previewInitialized = false; PreviewColor = Color.gray;
            if (preview != null) preview.SetActive(false);
        }
        void RemoveAnchor(ARAnchor anchor)
        {
            if (anchor == null) return;
            if (anchors != null && anchors.enabled && anchors.subsystem != null && anchors.TryRemoveAnchor(anchor)) return;
            Destroy(anchor.gameObject);
        }
        void ReleaseMarker()
        {
            if (marker != null) Destroy(marker);
            marker = null;
            if (markerAnchor != null) RemoveAnchor(markerAnchor);
            markerAnchor = null;
        }
        public void ClearMarker()
        {
            requestVersion++; IsCreatingAnchor = false;
            ReleaseMarker(); ResetCandidate(); CenterAim(); SetNotice("Submitted. Aim at a surface to report another issue.");
        }
        void OnApplicationPause(bool paused)
        { if (paused) { requestVersion++; IsCreatingAnchor = false; ResetCandidate(); } }
        void OnDisable() { requestVersion++; IsCreatingAnchor = false; ResetCandidate(); }
        void OnDestroy()
        {
            ReleaseMarker();
            if (preview != null) Destroy(preview);
            if (previewMaterial != null) Destroy(previewMaterial);
            if (markerMaterial != null) Destroy(markerMaterial);
        }
    }
}
