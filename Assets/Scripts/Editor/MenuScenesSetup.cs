using GhostHunter.Systems.Steam;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 메인메뉴/로비 씬을 에디터 API로 생성한다. 씬 YAML을 손으로 만들지 않고,
    /// 같은 메뉴를 다시 실행하면 두 씬을 동일하게 재생성한다.
    /// 흐름: Title(방 생성/참가) → Lobby(대기실) → Game(NGO 세션 시작).
    /// </summary>
    public static class MenuScenesSetup
    {
        internal static readonly Color BackgroundColor = new(0.05f, 0.06f, 0.09f);
        internal static readonly Color PanelColor = new(0.09f, 0.11f, 0.16f, 0.92f);
        internal static readonly Color ButtonColor = new(0.16f, 0.2f, 0.3f);
        internal static readonly Color TitleColor = new(0.92f, 0.95f, 1f);
        internal static readonly Color StatusColor = new(0.65f, 0.7f, 0.78f);

        [MenuItem("GhostHunter/메인메뉴·로비 씬 생성", priority = 2)]
        public static void SetupMenuScenes()
        {
            CreateTitleScene();
            CreateLobbyScene();

            PrototypeSceneSetup.SyncBuildScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[MenuScenesSetup] Title / Lobby 씬 생성 완료.\n" +
                "Bootstrap 씬에서 Play → 방 생성(방 코드 발급) 또는 방 참가(코드 입력) → " +
                "로비에서 초대/준비 → 호스트가 게임 시작을 누르면 Game 씬으로 넘어갑니다.\n" +
                "메뉴 흐름은 Steam 로비 기반이므로 Steam 클라이언트가 실행 중이어야 합니다.");
        }

        /// <summary>-batchmode -executeMethod 진입점.</summary>
        public static void SetupBatch()
        {
            SetupMenuScenes();
        }

        #region 메인메뉴 씬

        private static void CreateTitleScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateMenuCamera();
            CreateEventSystem();

            GameObject canvasObject = CreateCanvas("TitleCanvas");
            Transform canvas = canvasObject.transform;

            CreateText(canvas, "Title", "GhostHunter", 96, FontStyle.Bold, TitleColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1000f, 130f));

            Button createButton = CreateButton(canvas, "CreateRoomButton", "방 생성", 30,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(380f, 70f), out _);
            Button joinButton = CreateButton(canvas, "JoinRoomButton", "방 참가", 30,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(380f, 70f), out _);
            Button settingsButton = CreateButton(canvas, "SettingsButton", "설정", 30,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(380f, 70f), out _);
            Button quitButton = CreateButton(canvas, "QuitButton", "게임 종료", 30,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(380f, 70f), out _);

            // 매치에서만 빠져나온 게스트는 Steam 로비 멤버로 남는다. 그 로비로 돌아가는 진입점이다
            // → docs/project/pause-menu-system.md §4.4 (PM-14). 로비에 없으면 컨트롤러가 숨긴다.
            Button returnToLobbyButton = CreateButton(canvas, "ReturnToLobbyButton", "로비로 돌아가기", 26,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -235f), new Vector2(380f, 62f), out _);
            returnToLobbyButton.gameObject.SetActive(false);

            Text statusText = CreateText(canvas, "StatusText", "", 22, FontStyle.Normal, StatusColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1400f, 60f));

            // ── 방 참가 패널(코드 입력) ──
            GameObject joinPanel = CreateUiObject("JoinPanel", canvas);
            StretchFull(joinPanel);
            Image dim = joinPanel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);

            GameObject joinBox = CreatePanel(joinPanel.transform, "JoinBox",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540f, 320f));

            CreateText(joinBox.transform, "JoinTitle", "방 코드 입력", 34, FontStyle.Bold, TitleColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(480f, 60f));

            InputField roomCodeInput = CreateInputField(joinBox.transform, "RoomCodeInput",
                "AB3CD9", new Vector2(0f, 10f), new Vector2(420f, 72f), 40);

            Button joinConfirmButton = CreateButton(joinBox.transform, "JoinConfirmButton", "참가", 28,
                new Vector2(0.5f, 0f), new Vector2(-110f, 70f), new Vector2(190f, 62f), out _);
            Button joinCancelButton = CreateButton(joinBox.transform, "JoinCancelButton", "뒤로", 28,
                new Vector2(0.5f, 0f), new Vector2(110f, 70f), new Vector2(190f, 62f), out _);

            joinPanel.SetActive(false);

            var controller = canvasObject.AddComponent<MainMenuController>();
            PrototypeSceneSetup.SetObjectReference(controller, "_createRoomButton", createButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_joinRoomButton", joinButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_settingsButton", settingsButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_quitButton", quitButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_returnToLobbyButton", returnToLobbyButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_joinPanel", joinPanel);
            PrototypeSceneSetup.SetObjectReference(controller, "_roomCodeInput", roomCodeInput);
            PrototypeSceneSetup.SetObjectReference(controller, "_joinConfirmButton", joinConfirmButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_joinCancelButton", joinCancelButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_statusText", statusText);

            EditorSceneManager.SaveScene(scene, PrototypeSceneSetup.TitleScenePath);
        }

        #endregion

        #region 로비 씬

        private static void CreateLobbyScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateMenuCamera();
            CreateEventSystem();

            GameObject canvasObject = CreateCanvas("LobbyCanvas");
            Transform canvas = canvasObject.transform;

            CreateText(canvas, "Title", "로비", 56, FontStyle.Bold, TitleColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(600f, 80f));

            Text roomCodeText = CreateText(canvas, "RoomCodeText", "방 코드: -", 40, FontStyle.Bold,
                new Color(0.55f, 0.85f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(-70f, -160f), new Vector2(700f, 60f));

            Button copyCodeButton = CreateButton(canvas, "CopyCodeButton", "복사", 22,
                new Vector2(0.5f, 1f), new Vector2(340f, -160f), new Vector2(110f, 50f), out _);

            // ── 멤버 목록 ──
            GameObject memberList = CreatePanel(canvas, "MemberList",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(760f, 420f));

            VerticalLayoutGroup layout = memberList.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 14, 14);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            LobbyMemberEntry entryTemplate = CreateMemberEntryTemplate(memberList.transform);

            // ── 하단 버튼 ──
            Button inviteButton = CreateButton(canvas, "InviteButton", "초대", 28,
                new Vector2(0.5f, 0f), new Vector2(-240f, 120f), new Vector2(210f, 66f), out _);
            Button readyButton = CreateButton(canvas, "ReadyButton", "준비", 28,
                new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(210f, 66f), out Text readyLabel);
            Button startButton = CreateButton(canvas, "StartButton", "게임 시작", 28,
                new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(210f, 66f), out _);
            Button leaveButton = CreateButton(canvas, "LeaveButton", "나가기", 28,
                new Vector2(0.5f, 0f), new Vector2(240f, 120f), new Vector2(210f, 66f), out _);

            Text statusText = CreateText(canvas, "StatusText", "", 22, FontStyle.Normal, StatusColor,
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(1400f, 60f));

            var controller = canvasObject.AddComponent<LobbyController>();
            PrototypeSceneSetup.SetObjectReference(controller, "_roomCodeText", roomCodeText);
            PrototypeSceneSetup.SetObjectReference(controller, "_statusText", statusText);
            PrototypeSceneSetup.SetObjectReference(controller, "_memberListRoot", memberList.transform);
            PrototypeSceneSetup.SetObjectReference(controller, "_memberEntryTemplate", entryTemplate);
            PrototypeSceneSetup.SetObjectReference(controller, "_copyCodeButton", copyCodeButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_inviteButton", inviteButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_readyButton", readyButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_readyButtonLabel", readyLabel);
            PrototypeSceneSetup.SetObjectReference(controller, "_startButton", startButton);
            PrototypeSceneSetup.SetObjectReference(controller, "_leaveButton", leaveButton);

            EditorSceneManager.SaveScene(scene, PrototypeSceneSetup.LobbyScenePath);
        }

        private static LobbyMemberEntry CreateMemberEntryTemplate(Transform parent)
        {
            GameObject entry = CreateUiObject("MemberEntryTemplate", parent);
            var rect = entry.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 84f);

            Image background = entry.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.05f);

            LayoutElement layoutElement = entry.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 84f;

            GameObject avatarObject = CreateUiObject("Avatar", entry.transform);
            var avatarRect = avatarObject.GetComponent<RectTransform>();
            avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(0f, 0.5f);
            avatarRect.anchoredPosition = new Vector2(52f, 0f);
            avatarRect.sizeDelta = new Vector2(64f, 64f);
            RawImage avatarImage = avatarObject.AddComponent<RawImage>();
            avatarImage.color = Color.white;

            GameObject nameObject = CreateUiObject("Name", entry.transform);
            var nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(104f, 0f);
            nameRect.offsetMax = new Vector2(-220f, 0f);
            Text nameText = nameObject.AddComponent<Text>();
            ConfigureText(nameText, "이름", 28, FontStyle.Normal, TitleColor, TextAnchor.MiddleLeft);

            GameObject stateObject = CreateUiObject("State", entry.transform);
            var stateRect = stateObject.GetComponent<RectTransform>();
            stateRect.anchorMin = stateRect.anchorMax = new Vector2(1f, 0.5f);
            stateRect.anchoredPosition = new Vector2(-110f, 0f);
            stateRect.sizeDelta = new Vector2(190f, 50f);
            Text stateText = stateObject.AddComponent<Text>();
            ConfigureText(stateText, "대기 중", 24, FontStyle.Normal, StatusColor, TextAnchor.MiddleRight);

            var entryComponent = entry.AddComponent<LobbyMemberEntry>();
            PrototypeSceneSetup.SetObjectReference(entryComponent, "_avatarImage", avatarImage);
            PrototypeSceneSetup.SetObjectReference(entryComponent, "_nameText", nameText);
            PrototypeSceneSetup.SetObjectReference(entryComponent, "_stateText", stateText);

            entry.SetActive(false);
            return entryComponent;
        }

        #endregion

        #region UI 팩토리

        private static void CreateMenuCamera()
        {
            var cameraObject = new GameObject("MenuCamera")
            {
                tag = "MainCamera",
            };

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            camera.orthographic = true;
            cameraObject.AddComponent<AudioListener>();
        }

        internal static GameObject CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();

            // 프로젝트가 신규 Input System 전용이므로 StandaloneInputModule 은 예외를 던진다.
            // 액션 에셋을 비워 두면 모듈이 런타임에 기본 UI 액션을 스스로 만든다.
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
            return eventSystemObject;
        }

        internal static GameObject CreateCanvas(string name)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform));
            canvasObject.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            return canvasObject;
        }

        internal static GameObject CreateUiObject(string name, Transform parent)
        {
            var uiObject = new GameObject(name, typeof(RectTransform));
            uiObject.layer = LayerMask.NameToLayer("UI");
            uiObject.transform.SetParent(parent, false);
            return uiObject;
        }

        internal static void StretchFull(GameObject uiObject)
        {
            var rect = uiObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        internal static GameObject CreatePanel(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 position,
            Vector2 size)
        {
            GameObject panel = CreateUiObject(name, parent);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = panel.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            return panel;
        }

        internal static Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment,
            Vector2 anchor,
            Vector2 position,
            Vector2 size)
        {
            GameObject textObject = CreateUiObject(name, parent);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = textObject.AddComponent<Text>();
            ConfigureText(text, content, fontSize, fontStyle, color, alignment);
            return text;
        }

        internal static void ConfigureText(
            Text text,
            string content,
            int fontSize,
            FontStyle fontStyle,
            Color color,
            TextAnchor alignment)
        {
            // 내장 LegacyRuntime 폰트에는 한글 글리프가 없지만, 다이나믹 폰트라
            // OS 폰트 폴백으로 한글이 렌더링된다. (TMP 는 폴백이 없어 별도 폰트가 필요)
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
        }

        internal static Button CreateButton(
            Transform parent,
            string name,
            string label,
            int fontSize,
            Vector2 anchor,
            Vector2 position,
            Vector2 size,
            out Text labelText)
        {
            GameObject buttonObject = CreateUiObject(name, parent);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = buttonObject.AddComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = ButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.35f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.9f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;

            GameObject labelObject = CreateUiObject("Label", buttonObject.transform);
            StretchFull(labelObject);
            labelText = labelObject.AddComponent<Text>();
            ConfigureText(labelText, label, fontSize, FontStyle.Normal, TitleColor, TextAnchor.MiddleCenter);

            return button;
        }

        private static InputField CreateInputField(
            Transform parent,
            string name,
            string placeholder,
            Vector2 position,
            Vector2 size,
            int fontSize)
        {
            GameObject fieldObject = CreateUiObject(name, parent);
            var rect = fieldObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image background = fieldObject.AddComponent<Image>();
            background.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            background.type = Image.Type.Sliced;
            background.color = new Color(0.95f, 0.96f, 1f);

            InputField field = fieldObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.characterLimit = SteamLobbyManager.RoomCodeLength;

            GameObject placeholderObject = CreateUiObject("Placeholder", fieldObject.transform);
            StretchFull(placeholderObject);
            var placeholderText = placeholderObject.AddComponent<Text>();
            ConfigureText(placeholderText, placeholder, fontSize, FontStyle.Italic,
                new Color(0.35f, 0.38f, 0.45f, 0.6f), TextAnchor.MiddleCenter);

            GameObject textObject = CreateUiObject("Text", fieldObject.transform);
            StretchFull(textObject);
            var valueText = textObject.AddComponent<Text>();
            ConfigureText(valueText, string.Empty, fontSize, FontStyle.Bold,
                new Color(0.08f, 0.1f, 0.14f), TextAnchor.MiddleCenter);
            valueText.supportRichText = false;

            field.textComponent = valueText;
            field.placeholder = placeholderText;
            return field;
        }

        #endregion
    }
}
