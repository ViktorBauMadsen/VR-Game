using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Casino.Multiplayer
{
    public sealed class LobbyPanel : MonoBehaviour
    {
        CasinoMultiplayer session;
        GameObject panel;
        Text status, addressLabel;
        Button host, join;
        string address = "";
        InputAction toggle;
        Font font;
        EventSystem ownedEventSystem;

        public void Initialize(CasinoMultiplayer multiplayer)
        {
            session = multiplayer;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (EventSystem.current == null)
                ownedEventSystem = new GameObject("Multiplayer XR EventSystem", typeof(EventSystem), typeof(XRUIInputModule)).GetComponent<EventSystem>();
            else if (EventSystem.current.GetComponent<XRUIInputModule>() == null)
            {
                // XRI requires one active UI input module; retain the existing EventSystem.
                foreach (var module in EventSystem.current.GetComponents<BaseInputModule>()) module.enabled = false;
                EventSystem.current.gameObject.AddComponent<XRUIInputModule>();
            }
            panel = new GameObject("Multiplayer Lobby", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            panel.transform.SetParent(transform, false);
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = session.Head.GetComponent<Camera>();
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(680, 790);
            rect.localScale = Vector3.one * .0014f;
            var background = panel.AddComponent<Image>();
            background.color = new Color(.025f, .04f, .075f, .97f);
            Label("CASINO • MULTIPLAYER", 330, 40, 30);
            Label("Two players • Same Wi-Fi • Point and trigger", 287, 35, 20);
            status = Label(session.Status, 218, 85, 22);
            Label("Your IP: " + session.LocalAddresses, 145, 50, 19);
            host = Button("HOST", -155, 78, 270, 52, session.Host);
            join = Button("JOIN", 155, 78, 270, 52, () => session.Join(address));
            addressLabel = Label("Host IP: _", 18, 45, 25);
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "⌫" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                Button(key, (i % 3 - 1) * 155, -45 - (i / 3) * 60, 140, 52, () =>
                {
                    if (key == "⌫") address = address.Length > 0 ? address.Substring(0, address.Length - 1) : "";
                    else if (address.Length < 15) address += key;
                    addressLabel.text = "Host IP: " + address + "_";
                });
            }
            Button("LEAVE", -155, -295, 270, 50, session.Leave);
            Button("HIDE", 155, -295, 270, 50, () => panel.SetActive(false));
            Label("Left controller Y: show/hide • Keyboard M in Editor", -350, 40, 19);
            toggle = new InputAction("Multiplayer Menu", InputActionType.Button);
            toggle.AddBinding("<XRController>{LeftHand}/secondaryButton");
            toggle.AddBinding("<Keyboard>/m");
            toggle.performed += Toggle;
            toggle.Enable();
            PlaceInFront();
        }

        void Toggle(InputAction.CallbackContext context)
        {
            panel.SetActive(!panel.activeSelf);
            if (panel.activeSelf) PlaceInFront();
        }

        void PlaceInFront()
        {
            Vector3 forward = Vector3.ProjectOnPlane(session.Head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            panel.transform.position = session.Head.position + forward * 1.5f;
            panel.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        void Update()
        {
            if (session == null || status == null) return;
            status.text = session.Status;
            host.interactable = join.interactable = !session.Busy;
        }

        Text Label(string value, float y, float height, int size)
        {
            var text = new GameObject(value, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(panel.transform, false);
            text.rectTransform.anchoredPosition = new Vector2(0, y);
            text.rectTransform.sizeDelta = new Vector2(630, height);
            text.text = value;
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        Button Button(string label, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(panel.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            go.GetComponent<Image>().color = new Color(.1f, .3f, .45f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            var text = Label(label, 0, height, 24);
            text.transform.SetParent(go.transform, false);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = new Vector2(width, height);
            return button;
        }

        void OnDestroy()
        {
            toggle?.Dispose();
            if (ownedEventSystem != null) Destroy(ownedEventSystem.gameObject);
        }
    }
}
