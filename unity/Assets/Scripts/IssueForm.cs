using System;
using UnityEngine;
using UnityEngine.UI;

namespace Inspection
{
    public class IssueForm : MonoBehaviour
    {
        ARPlacement placement;
        RectTransform safeArea;
        Font font;
        InputField address;
        InputField title;
        InputField description;
        Text hint;
        Text feedback;
        Button submit;
        Button placeMarker;
        Text crosshair;
        RectTransform canvasRect;
        Button priorityButton;
        Text priorityText;
        readonly string[] priorities = { "low", "medium", "high" };
        int priorityIndex = 1;
        bool busy;

        public void Initialize(ARPlacement arPlacement)
        {
            placement = arPlacement;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // Prefer an installed CJK font for titles and descriptions when available.
            foreach (string name in Font.GetOSInstalledFontNames())
            {
                if (name.Contains("Noto Sans CJK") || name.Contains("Droid Sans Fallback") || name.Contains("Microsoft YaHei"))
                { font = Font.CreateDynamicFontFromOSFont(name, 28); break; }
            }
            var canvasObject = new GameObject("Inspection UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasRect = canvasObject.GetComponent<RectTransform>();
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            safeArea = Rect("Safe Area", canvasObject.transform);
            ApplySafeArea();
            crosshair = Label(canvasObject.transform, "+", 42);
            crosshair.alignment = TextAnchor.MiddleCenter;
            crosshair.rectTransform.anchorMin = crosshair.rectTransform.anchorMax = new Vector2(.5f, .5f);
            crosshair.rectTransform.sizeDelta = new Vector2(64, 64);

            var top = Panel("AR Guidance", safeArea, new Color(.03f, .09f, .08f, .85f));
            Anchor(top, new Vector2(0, 1), Vector2.one, new Vector2(18, -190), new Vector2(-18, -18));
            hint = Label(top, "Scan a floor, table or wall.", 30);
            Stretch(hint.rectTransform, 24);

            var panel = Panel("Issue Form", safeArea, new Color(.04f, .10f, .09f, .94f));
            Anchor(panel, Vector2.zero, new Vector2(1, 0), new Vector2(18, 18), new Vector2(-18, 880));
            RowLabel(panel, "REPORT AN ISSUE", 18, 45, 34);
            RowLabel(panel, "Backend URL (computer LAN IP, not localhost)", 78, 36, 25);
            address = Field(panel, 119, 62, "Enter computer LAN URL (http://IP:8080)", false);
            address.text = PlayerPrefs.GetString("backendUrl", "");
            address.onEndEdit.AddListener(value => { PlayerPrefs.SetString("backendUrl", value.Trim()); PlayerPrefs.Save(); });
            RowLabel(panel, "Title *", 194, 36, 27);
            title = Field(panel, 235, 66, "Required (max 200 characters)", false);
            title.characterLimit = 200;
            RowLabel(panel, "Description", 314, 36, 27);
            description = Field(panel, 355, 128, "Optional", true);
            description.characterLimit = 5000;
            RowLabel(panel, "Priority (tap to change)", 498, 36, 27);
            priorityButton = MakeButton(panel, "Priority: medium", 540, 62, () => {
                priorityIndex = (priorityIndex + 1) % priorities.Length;
                priorityText.text = "Priority: " + priorities[priorityIndex];
            });
            priorityText = priorityButton.GetComponentInChildren<Text>();
            placeMarker = MakeButton(panel, "Place marker", 620, 72, placement.ConfirmPlacement);
            Anchor(placeMarker.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(.5f, 1), new Vector2(24, -692), new Vector2(-8, -620));
            submit = MakeButton(panel, "Submit issue", 620, 72, Submit);
            Anchor(submit.GetComponent<RectTransform>(), new Vector2(.5f, 1), Vector2.one, new Vector2(8, -692), new Vector2(-24, -620));
            feedback = RowLabel(panel, "Aim or tap a surface. Wait for green, press Place marker, then submit.", 709, 126, 26);
            feedback.color = new Color(.85f, .93f, .88f);
        }

        void Update()
        {
            if (hint == null) return;
            ApplySafeArea();
            hint.text = placement.Hint;
            crosshair.color = placement.PreviewColor;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, placement.AimScreenPoint, null, out var point))
                crosshair.rectTransform.anchoredPosition = point;
            placeMarker.interactable = !busy && placement.CanConfirm;
            placeMarker.GetComponentInChildren<Text>().text = placement.IsCreatingAnchor ? "Anchoring..." : "Place marker";
            submit.interactable = !busy && placement.CanSubmit;
        }

        void ApplySafeArea()
        {
            var area = Screen.safeArea;
            safeArea.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }

        void Submit()
        {
            if (busy) return;
            if (!placement.HasMarker) { feedback.text = "Wait for a green cursor and press Place marker first."; return; }
            if (!placement.TryGetSubmissionPosition(out var position)) { feedback.text = "Marker tracking is limited. Scan the surroundings before submitting."; return; }
            if (string.IsNullOrWhiteSpace(title.text)) { feedback.text = "Title is required."; return; }
            string url = address.text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            { feedback.text = "Backend URL is required. Enter http://YOUR_COMPUTER_IP:8080"; return; }
            PlayerPrefs.SetString("backendUrl", url);
            PlayerPrefs.Save();
            var payload = new CreateIssueRequest {
                title = title.text.Trim(), description = description.text, priority = priorities[priorityIndex],
                position = new IssuePosition { x = position.x, y = position.y, z = position.z }
            };
            SetBusy(true);
            feedback.text = "Submitting…";
            StartCoroutine(IssueApi.Submit(url, payload, issue => {
                SetBusy(false);
                placement.ClearMarker();
                title.text = ""; description.text = "";
                feedback.text = "Submitted successfully. Refresh the web list to view it.";
            }, error => {
                SetBusy(false);
                feedback.text = error + " Draft retained. If timed out, check the web list before retrying.";
            }));
        }

        void SetBusy(bool value)
        {
            busy = value;
            placement.IsSubmitting = value;
            placeMarker.interactable = !value && placement.CanConfirm;
            submit.interactable = priorityButton.interactable = address.interactable = title.interactable = description.interactable = !value;
            submit.GetComponentInChildren<Text>().text = value ? "Submitting…" : "Submit issue";
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }

        static void Stretch(RectTransform rect, float padding)
        { Anchor(rect, Vector2.zero, Vector2.one, Vector2.one * padding, Vector2.one * -padding); }

        static RectTransform Panel(string name, Transform parent, Color color)
        {
            var rect = Rect(name, parent);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        Text Label(Transform parent, string value, int size)
        {
            var rect = Rect("Label", parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            return text;
        }

        static void Row(RectTransform rect, float top, float height)
        { Anchor(rect, new Vector2(0, 1), Vector2.one, new Vector2(24, -top - height), new Vector2(-24, -top)); }

        Text RowLabel(Transform parent, string value, float top, float height, int size)
        {
            var label = Label(parent, value, size); Row(label.rectTransform, top, height); return label;
        }

        InputField Field(Transform parent, float top, float height, string placeholder, bool multiline)
        {
            var rect = Panel("Input", parent, new Color(.96f, .98f, .97f));
            Row(rect, top, height);
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = rect.GetComponent<Image>();
            var content = Label(rect, "", 28); Stretch(content.rectTransform, 12); content.color = new Color(.1f, .15f, .12f);
            content.supportRichText = false;
            var prompt = Label(rect, placeholder, 26); Stretch(prompt.rectTransform, 12); prompt.color = new Color(.45f, .5f, .47f);
            field.textComponent = content; field.placeholder = prompt;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            content.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            prompt.alignment = content.alignment;
            return field;
        }

        Button MakeButton(Transform parent, string value, float top, float height, UnityEngine.Events.UnityAction action)
        {
            var rect = Panel("Button", parent, new Color(.17f, .48f, .34f)); Row(rect, top, height);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var text = Label(rect, value, 29); Stretch(text.rectTransform, 8); text.alignment = TextAnchor.MiddleCenter;
            button.onClick.AddListener(action);
            return button;
        }
    }
}
