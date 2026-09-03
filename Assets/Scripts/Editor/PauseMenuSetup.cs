using System;
using GhostHunter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// Game 씬에 일시정지 메뉴 UI를 <b>덧붙인다</b>. 씬을 재생성하지 않으므로
    /// 손으로 배치한 가구가 사라지지 않는다. 같은 메뉴를 다시 실행하면 기존 루트를 지우고
    /// 동일하게 다시 만든다.
    ///
    /// 규칙은 docs/project/pause-menu-system.md, 배선 표는 docs/architecture/pause-menu.md.
    /// </summary>
    public static class PauseMenuSetup
    {
        internal const string ScenePath = "Assets/Scenes/Game.unity";
        internal const string RootObjectName = "PauseMenuUI";
        internal const string CanvasObjectName = "PauseMenuCanvas";
        internal const string EventSystemObjectName = "EventSystem";

        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string TitleScenePath = "Assets/Scenes/Title.unity";

        internal const string ReturnToLobbyButtonName = "ReturnToLobbyButton";

        /// <summary>메뉴 항목 순서는 확정 사항이다 → pause-menu-system.md §4.1</summary>
        internal static readonly string[] MenuButtonOrder =
        {
            "ResumeButton", "SettingsButton", "TitleButton", "QuitButton",
        };

        [MenuItem("GhostHunter/일시정지 메뉴 설치", priority = 5)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the pause menu.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the pause menu. " +
                    $"The active scene is '{scene.path}'.");
            }

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (actions == null)
                throw new InvalidOperationException($"{InputActionsPath} 를 찾지 못했습니다.");

            if (actions.FindAction("Player/Pause") == null)
            {
                throw new InvalidOperationException(
                    "Player/Pause 액션이 없습니다. InputSystem_Actions 를 먼저 갱신하세요.");
            }

            RemoveExistingRoot(scene);
            BuildRoot(scene, actions);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateInstallation();

            bool titleAdded = InstallReturnToLobbyButton();

            if (logCompletion)
            {
                Debug.Log(
                    "[PauseMenuSetup] Game 씬에 일시정지 메뉴 설치 완료.\n" +
                    "플레이 중 ESC 로 열고 닫습니다. 시간은 멈추지 않습니다(멀티플레이).\n" +
                    (titleAdded
                        ? "Title 씬에 '로비로 돌아가기' 버튼을 추가했습니다."
                        : "Title 씬의 '로비로 돌아가기' 버튼은 이미 있습니다."));
            }
        }

        /// <summary>
        /// Title 씬에 로비 복귀 버튼을 <b>덧붙인다</b>. 씬을 재생성하지 않으므로 다른 배치가
        /// 사라지지 않고, 이미 있으면 아무것도 하지 않는다 → pause-menu-system.md §4.4 (PM-14).
        /// </summary>
        private static bool InstallReturnToLobbyButton()
        {
            string gameScenePath = SceneManager.GetActiveScene().path;
            Scene title = EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);

            try
            {
                var controller = Object.FindFirstObjectByType<MainMenuController>(FindObjectsInactive.Include);
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        $"{TitleScenePath} 에 MainMenuController 가 없습니다. " +
                        "메인메뉴·로비 씬 생성을 먼저 실행하세요.");
                }

                Transform canvas = controller.transform;

                foreach (Transform child in canvas)
                {
                    if (child.name == ReturnToLobbyButtonName)
                        return false;
                }

                Button button = MenuScenesSetup.CreateButton(
                    canvas, ReturnToLobbyButtonName, "로비로 돌아가기", 26,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -235f), new Vector2(380f, 62f), out _);

                // 로비에 속해 있을 때만 컨트롤러가 켠다.
                button.gameObject.SetActive(false);

                PrototypeSceneSetup.SetObjectReference(controller, "_returnToLobbyButton", button);

                EditorSceneManager.MarkSceneDirty(title);
                EditorSceneManager.SaveScene(title);
                return true;
            }
            finally
            {
                // 호출부는 Game 씬을 열어 둔 상태였다. 원래대로 되돌린다.
                EditorSceneManager.OpenScene(gameScenePath, OpenSceneMode.Single);
            }
        }

        private static void RemoveExistingRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == RootObjectName)
                    Object.DestroyImmediate(root);
            }
        }

        private static void BuildRoot(Scene scene, InputActionAsset actions)
        {
            var root = new GameObject(RootObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // Game 씬에는 EventSystem 이 없었다. 버튼을 누르려면 반드시 필요하다.
            // 레거시 StandaloneInputModule 은 activeInputHandler:1 에서 예외를 던진다.
            GameObject eventSystem = MenuScenesSetup.CreateEventSystem();
            eventSystem.transform.SetParent(root.transform, false);

            GameObject canvasObject = MenuScenesSetup.CreateCanvas(CanvasObjectName);
            canvasObject.transform.SetParent(root.transform, false);

            // 메뉴가 World Space 정신력 모니터나 IMGUI HUD 뒤로 숨지 않게 위로 올린다.
            canvasObject.GetComponent<Canvas>().sortingOrder = 100;

            Transform canvas = canvasObject.transform;

            GameObject menuPanel = BuildMenuPanel(canvas, out Button resume, out Button settings,
                out Button title, out Button quit, out Text statusText);
            GameObject confirmQuitPanel = BuildConfirmQuitPanel(canvas,
                out Button quitConfirm, out Button quitCancel);
            GameObject disconnectedPanel = BuildDisconnectedPanel(canvas, out Button disconnectedConfirm);

            var controller = canvasObject.AddComponent<PauseMenuController>();
            PrototypeSceneSetup.SetObjectReference(controller, "_inputActions", actions);
            PrototypeSceneSetup.SetObjectReference(controller, "_menuPanel", menuPanel);
            PrototypeSceneSetup.SetObjectReference(controller, "_confirmQuitPanel", confirmQuitPanel);
            PrototypeSceneSetup.SetObjectReference(controller, "_disconnectedPanel", disconnectedPanel);
            PrototypeSceneSetup.SetObjectReference(controller, "_resumeButton", resume);
            PrototypeSceneSetup.SetObjectReference(controller, "_settingsButton", settings);
            PrototypeSceneSetup.SetObjectReference(controller, "_titleButton", title);
            PrototypeSceneSetup.SetObjectReference(controller, "_quitButton", quit);
            PrototypeSceneSetup.SetObjectReference(controller, "_quitConfirmButton", quitConfirm);
            PrototypeSceneSetup.SetObjectReference(controller, "_quitCancelButton", quitCancel);
            PrototypeSceneSetup.SetObjectReference(
                controller, "_disconnectedConfirmButton", disconnectedConfirm);
            PrototypeSceneSetup.SetObjectReference(controller, "_statusText", statusText);

            menuPanel.SetActive(false);
            confirmQuitPanel.SetActive(false);
            disconnectedPanel.SetActive(false);
        }

        private static GameObject BuildMenuPanel(
            Transform canvas,
            out Button resume,
            out Button settings,
            out Button title,
            out Button quit,
            out Text statusText)
        {
            GameObject panel = MenuScenesSetup.CreateUiObject("MenuPanel", canvas);
            MenuScenesSetup.StretchFull(panel);
            Image dim = panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.65f);

            GameObject box = MenuScenesSetup.CreatePanel(panel.transform, "MenuBox",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 470f));

            MenuScenesSetup.CreateText(box.transform, "MenuTitle", "일시정지", 40, FontStyle.Bold,
                MenuScenesSetup.TitleColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(400f, 60f));

            // 순서는 확정 사항이다. 자식 순서가 곧 표시 순서이므로 테스트가 이 순서를 검사한다.
            resume = MenuScenesSetup.CreateButton(box.transform, MenuButtonOrder[0], "계속하기", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 95f), new Vector2(360f, 66f), out _);
            settings = MenuScenesSetup.CreateButton(box.transform, MenuButtonOrder[1], "설정", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(360f, 66f), out _);
            title = MenuScenesSetup.CreateButton(box.transform, MenuButtonOrder[2], "타이틀로", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -55f), new Vector2(360f, 66f), out _);
            quit = MenuScenesSetup.CreateButton(box.transform, MenuButtonOrder[3], "종료", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -130f), new Vector2(360f, 66f), out _);

            statusText = MenuScenesSetup.CreateText(box.transform, "StatusText", string.Empty, 20,
                FontStyle.Normal, MenuScenesSetup.StatusColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(420f, 40f));

            return panel;
        }

        private static GameObject BuildConfirmQuitPanel(
            Transform canvas,
            out Button confirm,
            out Button cancel)
        {
            GameObject panel = MenuScenesSetup.CreateUiObject("ConfirmQuitPanel", canvas);
            MenuScenesSetup.StretchFull(panel);
            Image dim = panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.75f);

            GameObject box = MenuScenesSetup.CreatePanel(panel.transform, "ConfirmQuitBox",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540f, 260f));

            MenuScenesSetup.CreateText(box.transform, "ConfirmQuitText", "게임을 종료하시겠습니까?", 30,
                FontStyle.Bold, MenuScenesSetup.TitleColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -80f), new Vector2(500f, 60f));

            confirm = MenuScenesSetup.CreateButton(box.transform, "QuitConfirmButton", "종료", 26,
                new Vector2(0.5f, 0f), new Vector2(-110f, 70f), new Vector2(190f, 62f), out _);
            cancel = MenuScenesSetup.CreateButton(box.transform, "QuitCancelButton", "취소", 26,
                new Vector2(0.5f, 0f), new Vector2(110f, 70f), new Vector2(190f, 62f), out _);

            return panel;
        }

        private static GameObject BuildDisconnectedPanel(Transform canvas, out Button confirm)
        {
            GameObject panel = MenuScenesSetup.CreateUiObject("DisconnectedPanel", canvas);
            MenuScenesSetup.StretchFull(panel);
            Image dim = panel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.85f);

            GameObject box = MenuScenesSetup.CreatePanel(panel.transform, "DisconnectedBox",
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 260f));

            // 사유를 구분하지 않는다. 네 경우 모두 이 문구 하나다 → pause-menu-system.md §5.3
            MenuScenesSetup.CreateText(box.transform, "DisconnectedText",
                "호스트와 연결이 끊겼습니다.", 30, FontStyle.Bold,
                MenuScenesSetup.TitleColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, -85f), new Vector2(580f, 60f));

            confirm = MenuScenesSetup.CreateButton(box.transform, "DisconnectedConfirmButton", "확인", 26,
                new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(220f, 62f), out _);

            return panel;
        }

        internal static void ValidateInstallation()
        {
            Scene scene = SceneManager.GetActiveScene();

            GameObject root = null;
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == RootObjectName)
                    root = candidate;
            }

            if (root == null)
                throw new InvalidOperationException($"{RootObjectName} 루트를 만들지 못했습니다.");

            if (root.GetComponentInChildren<EventSystem>(true) == null)
                throw new InvalidOperationException("EventSystem 이 설치되지 않았습니다.");

            var controller = root.GetComponentInChildren<PauseMenuController>(true);
            if (controller == null)
                throw new InvalidOperationException("PauseMenuController 가 설치되지 않았습니다.");

            if (root.GetComponentInChildren<Canvas>(true) == null)
                throw new InvalidOperationException("PauseMenuCanvas 가 설치되지 않았습니다.");

            // 메뉴는 완전한 로컬 UI 다 → pause-menu-system.md §7 (PM-12)
            if (root.GetComponentInChildren<Unity.Netcode.NetworkObject>(true) != null)
            {
                throw new InvalidOperationException(
                    "일시정지 메뉴에 NetworkObject 가 붙었습니다. 이 UI 는 복제하지 않습니다.");
            }
        }
    }
}
