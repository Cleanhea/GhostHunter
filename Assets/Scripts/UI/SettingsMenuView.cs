using System;
using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Settings;
using GhostHunter.Core.Voice;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 설정 창 — 오디오 / 마이크 / 조작 / 화면 네 탭. 타이틀과 ESC 메뉴가 <see cref="Create"/> 로 같은 창을 짓는다.
    /// 값은 바꾸는 즉시 적용되고 창을 닫을 때 저장한다.
    /// 규칙은 docs/project/pause-menu-system.md §4.3, 구조는 docs/architecture/settings-menu.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsMenuView : MonoBehaviour
    {
        private const float LevelFloorDb = -60f;
        private const float SilenceDb = -119f;
        private const float MicTestReadSeconds = 0.05f;
        private const float ParticipantRefreshSeconds = 0.5f;
        private const float LevelFallPerSecond = 1.6f;

        private static readonly string[] TabNames = { "오디오", "마이크", "조작", "화면" };
        private static readonly string[] OffOn = { "끔", "켬" };
        private static readonly string[] MicStates = { "켜짐", "꺼짐" };
        private static readonly string[] VoiceModes = { "오픈 마이크", "눌러서 말하기 (V)" };
        private static readonly string[] ScreenModeNames = { "전체 화면", "창 모드 전체 화면", "창 모드" };
        private static readonly FullScreenMode[] ScreenModes =
            { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        private readonly List<Action> _refreshers = new();
        private readonly Text[] _tabLabels = new Text[4];
        private readonly Image[] _tabMarks = new Image[4];
        private readonly GameObject[] _pages = new GameObject[4];
        private readonly List<IVoiceParticipant> _shownParticipants = new(4);
        private readonly byte[] _micTestFrame = new byte[8192];

        private IUserSettings _settings;
        private IVoiceCaptureService _capture;
        private IVoiceChatService _chat;
        private ISceneFlow _sceneFlow;

        private bool _closeOnEscape;
        private bool _isOpen;
        private UserSettingsGroup _tab;

        private RectTransform _levelFill;
        private Text _levelText;
        private Text _micStatusText;
        private Text _steamHintText;
        private RectTransform _participantList;
        private Text _participantEmptyText;
        private bool _ownsMicTest;
        private float _nextMicTestRead;
        private float _nextParticipantRefresh;
        private float _shownLevel;
        private int _shownDb = int.MinValue;

        private Vector2Int[] _resolutions;
        private string[] _resolutionNames;
        private int _resolutionIndex;
        private int _screenModeIndex;

        public bool IsOpen => _isOpen;

        /// <summary>창이 닫혔다(닫기 버튼·ESC·<see cref="Close"/>).</summary>
        public event Action Closed;

        /// <summary>
        /// 닫힌 상태의 설정 창을 <paramref name="owner"/> 와 같은 씬의 루트에 만든다. 만든 쪽이 함께 파괴한다.
        /// </summary>
        /// <param name="owner">창을 여는 메뉴. 그 씬에 창이 생긴다.</param>
        /// <param name="closeOnEscape">
        /// true 면 ESC 로 스스로 닫힌다. 일시정지 메뉴처럼 ESC 를 직접 다루는 쪽은 false 로 만들고
        /// 자기 입력으로 <see cref="Close"/> 를 부른다 — 둘이 같은 프레임의 ESC 를 함께 먹지 않게 한다.
        /// </param>
        public static SettingsMenuView Create(Transform owner, bool closeOnEscape)
        {
            var go = new GameObject("SettingsMenu", typeof(RectTransform));
            // 새 오브젝트는 활성 씬에 생기는데, 스테이지 로드 직후에는 활성 씬이 아직 이전 씬일 수 있다.
            // 메뉴 밑에 붙였다 떼면 메뉴의 씬에 루트로 남는다 — 루트라야 자기 Canvas 스케일을 쓴다.
            go.transform.SetParent(owner, false);
            go.transform.SetParent(null, false);
            var view = go.AddComponent<SettingsMenuView>();
            view._closeOnEscape = closeOnEscape;
            view.Build();
            go.SetActive(false);
            return view;
        }

        private void Update()
        {
            if (!_isOpen)
                return;

            if (_closeOnEscape && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            if (_tab == UserSettingsGroup.Microphone)
                UpdateMicrophone();
            else if (_tab == UserSettingsGroup.Audio && Time.unscaledTime >= _nextParticipantRefresh)
                RefreshParticipants();
        }

        private void OnDestroy()
        {
            StopMicTest();
            if (_settings != null)
                _settings.Changed -= RefreshAll;
        }

        public void Open()
        {
            if (_isOpen)
                return;

            // 씬마다 등록된 서비스가 다르다(음성 참가자는 스테이지·인게임 로비에만 있다). 열 때마다 다시 찾는다.
            Services.TryGet(out _settings);
            Services.TryGet(out _capture);
            Services.TryGet(out _chat);
            Services.TryGet(out _sceneFlow);
            if (_settings == null)
                Debug.LogWarning($"{nameof(SettingsMenuView)}: IUserSettings 가 없다 — Bootstrap 없이 실행해 설정을 바꿀 수 없다.", this);
            else
                _settings.Changed += RefreshAll;

            ReadScreenState();
            _isOpen = true;
            gameObject.SetActive(true);
            SelectTab(_tab);
            RefreshAll();
            RefreshParticipants();
        }

        public void Close()
        {
            if (!_isOpen)
                return;

            _isOpen = false;
            StopMicTest();
            if (_settings != null)
            {
                _settings.Changed -= RefreshAll;
                _settings.Save();
            }

            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        #region 짓기

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 일시정지 메뉴와 HUD 위에 그린다.
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            // 뒤 메뉴를 어둡게 덮고 클릭을 막는다.
            Image backdrop = SettingsUiKit.CreateImage("Backdrop", transform, SettingsUiKit.Backdrop, raycast: true);
            SettingsUiKit.Fill(backdrop.rectTransform);

            Image edge = SettingsUiKit.CreateImage("PanelEdge", transform, SettingsUiKit.PanelEdge, raycast: true);
            edge.rectTransform.sizeDelta = new Vector2(1044f, 764f);
            Image panel = SettingsUiKit.CreateImage("Panel", edge.transform, SettingsUiKit.Panel);
            SettingsUiKit.Stretch(panel.rectTransform, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            Transform body = panel.transform;

            BuildHeader(body);
            BuildTabs(body);

            RectTransform content = SettingsUiKit.CreateRect("Content", body);
            SettingsUiKit.Stretch(content, Vector2.zero, Vector2.one, new Vector2(48f, 96f), new Vector2(-48f, -172f));
            BuildAudioPage(CreatePage(content, UserSettingsGroup.Audio));
            BuildMicrophonePage(CreatePage(content, UserSettingsGroup.Microphone));
            BuildControlsPage(CreatePage(content, UserSettingsGroup.Controls));
            BuildDisplayPage(CreatePage(content, UserSettingsGroup.Display));

            BuildFooter(body);
        }

        private void BuildHeader(Transform body)
        {
            Text title = SettingsUiKit.CreateText("Title", body, "설정", 44, SettingsUiKit.TextBright, TextAnchor.MiddleLeft);
            SettingsUiKit.Stretch(title.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(48f, -92f), new Vector2(-48f, -24f));

            Text subtitle = SettingsUiKit.CreateText("Subtitle", body, "S E T T I N G S", 16, SettingsUiKit.TextDim, TextAnchor.MiddleRight);
            SettingsUiKit.Stretch(subtitle.rectTransform, new Vector2(0f, 1f), Vector2.one, new Vector2(48f, -92f), new Vector2(-48f, -24f));
        }

        private void BuildTabs(Transform body)
        {
            RectTransform bar = SettingsUiKit.CreateRect("Tabs", body);
            SettingsUiKit.Stretch(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(48f, -152f), new Vector2(-48f, -100f));

            Image rule = SettingsUiKit.CreateImage("Rule", bar, SettingsUiKit.AccentFaint);
            SettingsUiKit.Stretch(rule.rectTransform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 1f));

            for (int i = 0; i < TabNames.Length; i++)
            {
                var group = (UserSettingsGroup)i;
                float left = i / (float)TabNames.Length;
                float right = (i + 1) / (float)TabNames.Length;

                Image hit = SettingsUiKit.CreateImage("Tab " + TabNames[i], bar, Color.clear, raycast: true);
                SettingsUiKit.Stretch(hit.rectTransform, new Vector2(left, 0f), new Vector2(right, 1f));
                var button = hit.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                button.onClick.AddListener(() => SelectTab(group));

                _tabLabels[i] = SettingsUiKit.CreateText("Label", hit.transform, TabNames[i], 24,
                    SettingsUiKit.TextDim, TextAnchor.MiddleCenter);
                SettingsUiKit.Fill(_tabLabels[i].rectTransform);

                _tabMarks[i] = SettingsUiKit.CreateImage("Mark", hit.transform, SettingsUiKit.Accent);
                SettingsUiKit.Stretch(_tabMarks[i].rectTransform, new Vector2(0.2f, 0f), new Vector2(0.8f, 0f),
                    Vector2.zero, new Vector2(0f, 3f));
            }
        }

        private Transform CreatePage(RectTransform content, UserSettingsGroup group)
        {
            RectTransform page = SettingsUiKit.CreateRect("Page " + TabNames[(int)group], content);
            SettingsUiKit.Fill(page);
            var layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _pages[(int)group] = page.gameObject;
            return page;
        }

        private void BuildFooter(Transform body)
        {
            Button reset = SettingsUiKit.CreateButton("Reset", body, "이 탭 기본값으로", 20, out _);
            SettingsUiKit.Stretch((RectTransform)reset.transform, Vector2.zero, Vector2.zero, new Vector2(48f, 28f), new Vector2(268f, 80f));
            reset.onClick.AddListener(HandleResetClicked);

            Button close = SettingsUiKit.CreateButton("Close", body, "닫기", 22, out _);
            SettingsUiKit.Stretch((RectTransform)close.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-268f, 28f), new Vector2(-48f, 80f));
            close.onClick.AddListener(Close);
        }

        private void BuildAudioPage(Transform page)
        {
            AddSlider(page, "전체 음량", 0f, 1f,
                () => _settings?.MasterVolume ?? 1f,
                value => { if (_settings != null) _settings.MasterVolume = value; },
                FormatPercent);
            AddSlider(page, "음성 채팅 음량", 0f, 1f,
                () => _settings?.VoiceVolume ?? 1f,
                value => { if (_settings != null) _settings.VoiceVolume = value; },
                FormatPercent);
            SettingsUiKit.CreateNote(page, "음성 채팅 음량은 다른 플레이어의 목소리 전체에 곱해집니다. 전체 음량은 목소리를 포함한 모든 소리에 적용됩니다.");

            SettingsUiKit.CreateSectionHeader(page, "플레이어별 음량");
            _participantList = SettingsUiKit.CreateRect("Participants", page);
            var list = _participantList.gameObject.AddComponent<VerticalLayoutGroup>();
            list.spacing = 8f;
            list.childControlWidth = true;
            list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
            _participantEmptyText = SettingsUiKit.CreateNote(page,
                "게임 중 함께 있는 플레이어의 목소리 크기를 따로 조절할 수 있습니다. 이 값은 이번 게임에만 적용됩니다.");
        }

        private void BuildMicrophonePage(Transform page)
        {
            Text micValue = AddChoice(page, "마이크", MicStates,
                () => _settings != null && _settings.MicMuted ? 1 : 0,
                index => { if (_settings != null) _settings.MicMuted = index == 1; });
            _refreshers.Add(() => micValue.color = _settings != null && _settings.MicMuted
                ? SettingsUiKit.Danger
                : SettingsUiKit.TextBright);

            AddChoice(page, "송신 방식", VoiceModes,
                () => _settings != null && _settings.VoiceMode == VoiceMode.PushToTalk ? 1 : 0,
                index => { if (_settings != null) _settings.VoiceMode = index == 1 ? VoiceMode.PushToTalk : VoiceMode.OpenMic; });

            RectTransform meter = SettingsUiKit.CreateRow(page, "입력 레벨", out _);
            Image track = SettingsUiKit.CreateImage("Track", meter, SettingsUiKit.Track);
            SettingsUiKit.Stretch(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.8f, 0.5f), new Vector2(0f, -8f), new Vector2(0f, 8f));
            Image fill = SettingsUiKit.CreateImage("Fill", track.transform, SettingsUiKit.Accent);
            _levelFill = fill.rectTransform;
            SettingsUiKit.Stretch(_levelFill, Vector2.zero, new Vector2(0f, 1f));
            _levelText = SettingsUiKit.CreateText("Value", meter, "—", SettingsUiKit.LabelSize, SettingsUiKit.TextDim, TextAnchor.MiddleRight);
            SettingsUiKit.Stretch(_levelText.rectTransform, new Vector2(0.8f, 0f), Vector2.one);

            _micStatusText = SettingsUiKit.CreateNote(page, string.Empty);

            RectTransform steam = SettingsUiKit.CreateRow(page, "입력 장치 · 노이즈 제거", out _);
            Button open = SettingsUiKit.CreateButton("OpenSteamVoice", steam, "Steam 음성 설정 열기", 20, out _);
            SettingsUiKit.Fill((RectTransform)open.transform);
            open.onClick.AddListener(HandleOpenSteamVoiceClicked);
            _steamHintText = SettingsUiKit.CreateNote(page,
                "마이크 장치 선택·입력 감도·노이즈 제거는 Steam 이 처리합니다. 게임은 소리를 거르지 않고 그대로 보냅니다.");

            SettingsUiKit.CreateNote(page, "게임 중 단축키 — M: 마이크 켜기/끄기 · V: 누르는 동안 말하기(눌러서 말하기 모드)");
        }

        private void BuildControlsPage(Transform page)
        {
            AddSlider(page, "마우스 감도", UserSettingsLimits.MinMouseSensitivity, UserSettingsLimits.MaxMouseSensitivity,
                () => _settings?.MouseSensitivity ?? UserSettingsLimits.DefaultMouseSensitivity,
                value => { if (_settings != null) _settings.MouseSensitivity = value; },
                value => "x" + value.ToString("0.00"));
            AddChoice(page, "상하 반전", OffOn,
                () => _settings != null && _settings.InvertMouseY ? 1 : 0,
                index => { if (_settings != null) _settings.InvertMouseY = index == 1; });
            SettingsUiKit.CreateNote(page, "감도와 반전은 관전 카메라에도 같이 적용됩니다.");
        }

        private void BuildDisplayPage(Transform page)
        {
            BuildResolutionList();

            AddChoice(page, "화면 모드", ScreenModeNames,
                () => _screenModeIndex,
                index =>
                {
                    _screenModeIndex = index;
                    Screen.fullScreenMode = ScreenModes[index];
                });
            AddChoice(page, "해상도", _resolutionNames,
                () => _resolutionIndex,
                index =>
                {
                    _resolutionIndex = index;
                    Vector2Int size = _resolutions[index];
                    Screen.SetResolution(size.x, size.y, ScreenModes[_screenModeIndex]);
                });
            AddChoice(page, "수직 동기화", OffOn,
                () => _settings != null && _settings.VSync ? 1 : 0,
                index => { if (_settings != null) _settings.VSync = index == 1; });

            int[] limits = UserSettingsLimits.FrameRateLimits;
            var limitNames = new string[limits.Length];
            for (int i = 0; i < limits.Length; i++)
                limitNames[i] = limits[i] > 0 ? limits[i] + " FPS" : "무제한";
            AddChoice(page, "프레임 제한", limitNames,
                () => _settings != null ? Mathf.Max(0, Array.IndexOf(limits, _settings.FrameRateLimit)) : 0,
                index => { if (_settings != null) _settings.FrameRateLimit = limits[index]; });

            SettingsUiKit.CreateNote(page, "수직 동기화를 켜면 프레임 제한 대신 모니터 주사율을 따릅니다.");
#if UNITY_EDITOR
            SettingsUiKit.CreateNote(page, "에디터에서는 화면 모드·해상도가 바뀌지 않습니다. 빌드에서 확인하세요.");
#endif
        }

        #endregion

        #region 위젯

        private void AddSlider(Transform page, string label, float min, float max,
            Func<float> read, Action<float> write, Func<float, string> format)
        {
            RectTransform control = SettingsUiKit.CreateRow(page, label, out _);
            Slider slider = SettingsUiKit.CreateSlider(control);
            SettingsUiKit.Stretch((RectTransform)slider.transform, Vector2.zero, new Vector2(0.8f, 1f));
            slider.minValue = min;
            slider.maxValue = max;

            Text value = SettingsUiKit.CreateText("Value", control, string.Empty, SettingsUiKit.LabelSize,
                SettingsUiKit.Accent, TextAnchor.MiddleRight);
            SettingsUiKit.Stretch(value.rectTransform, new Vector2(0.8f, 0f), Vector2.one);

            slider.onValueChanged.AddListener(next =>
            {
                write(next);
                value.text = format(next);
            });
            _refreshers.Add(() =>
            {
                float current = read();
                slider.SetValueWithoutNotify(current);
                value.text = format(current);
            });
        }

        /// <summary>"&lt; 값 &gt;" 로 넘기는 선택 줄. 끝에서 반대쪽 끝으로 돈다. 값 글자를 돌려준다.</summary>
        private Text AddChoice(Transform page, string label, string[] options, Func<int> read, Action<int> write)
        {
            RectTransform control = SettingsUiKit.CreateRow(page, label, out _);

            Button previous = SettingsUiKit.CreateButton("Previous", control, "<", 24, out _);
            SettingsUiKit.Stretch((RectTransform)previous.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(48f, 0f));
            Button next = SettingsUiKit.CreateButton("Next", control, ">", 24, out _);
            SettingsUiKit.Stretch((RectTransform)next.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(-48f, 0f), Vector2.zero);

            Text value = SettingsUiKit.CreateText("Value", control, string.Empty, SettingsUiKit.LabelSize,
                SettingsUiKit.TextBright, TextAnchor.MiddleCenter);
            SettingsUiKit.Stretch(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(56f, 0f), new Vector2(-56f, 0f));

            void Step(int delta)
            {
                if (options.Length == 0)
                    return;

                int current = Mathf.Clamp(read(), 0, options.Length - 1);
                write((current + delta + options.Length) % options.Length);
                RefreshAll();
            }

            previous.onClick.AddListener(() => Step(-1));
            next.onClick.AddListener(() => Step(1));
            _refreshers.Add(() => value.text = options.Length == 0 ? "-" : options[Mathf.Clamp(read(), 0, options.Length - 1)]);
            return value;
        }

        #endregion

        #region 탭·갱신

        private void SelectTab(UserSettingsGroup group)
        {
            if (_tab == UserSettingsGroup.Microphone && group != UserSettingsGroup.Microphone)
                StopMicTest();

            _tab = group;
            for (int i = 0; i < _pages.Length; i++)
            {
                bool selected = i == (int)group;
                _pages[i].SetActive(selected);
                _tabLabels[i].color = selected ? SettingsUiKit.Accent : SettingsUiKit.TextDim;
                _tabMarks[i].enabled = selected;
            }

            _shownLevel = 0f;
            _shownDb = int.MinValue;
            if (group == UserSettingsGroup.Audio)
                RefreshParticipants();
        }

        private void RefreshAll()
        {
            for (int i = 0; i < _refreshers.Count; i++)
                _refreshers[i]();
        }

        private void HandleResetClicked()
        {
            if (_tab == UserSettingsGroup.Display)
            {
                // 화면 모드·해상도는 Unity 가 저장한다. 기본값으로는 수직 동기화·프레임 제한만 되돌린다.
                _settings?.ResetToDefaults(UserSettingsGroup.Display);
            }
            else if (_tab == UserSettingsGroup.Audio)
            {
                _settings?.ResetToDefaults(UserSettingsGroup.Audio);
                for (int i = 0; i < _shownParticipants.Count; i++)
                    _shownParticipants[i].Volume = 1f;

                // 줄은 만들 때의 음량을 그린다. 비워서 다시 짓게 한다.
                _shownParticipants.Clear();
                RefreshParticipants();
            }
            else
            {
                _settings?.ResetToDefaults(_tab);
            }

            RefreshAll();
        }

        private void HandleOpenSteamVoiceClicked()
        {
            bool opened = _capture != null && _capture.OpenSettings();
            _steamHintText.text = opened
                ? "Steam 오버레이에서 음성 설정을 바꾼 뒤 이 창으로 돌아오세요."
                : "Steam 오버레이를 열 수 없습니다. Steam → 설정 → 음성 에서 마이크 장치와 입력 감도를 바꿀 수 있습니다.";
        }

        #endregion

        #region 화면

        private void BuildResolutionList()
        {
            var sizes = new List<Vector2Int>();
            Resolution[] available = Screen.resolutions;
            for (int i = 0; i < available.Length; i++)
            {
                var size = new Vector2Int(available[i].width, available[i].height);
                if (!sizes.Contains(size))
                    sizes.Add(size);
            }

            var current = new Vector2Int(Screen.width, Screen.height);
            if (!sizes.Contains(current))
                sizes.Add(current);

            sizes.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            _resolutions = sizes.ToArray();
            _resolutionNames = new string[_resolutions.Length];
            for (int i = 0; i < _resolutions.Length; i++)
                _resolutionNames[i] = _resolutions[i].x + " × " + _resolutions[i].y;
        }

        /// <summary>
        /// 화면 모드·해상도는 바꾼 다음 프레임에야 Screen 에 반영된다. 그래서 창이 고른 값을 따로 들고,
        /// 열 때만 Screen 에서 다시 읽는다.
        /// </summary>
        private void ReadScreenState()
        {
            _screenModeIndex = Mathf.Max(0, Array.IndexOf(ScreenModes, Screen.fullScreenMode));
            if (Screen.fullScreenMode == FullScreenMode.MaximizedWindow)
                _screenModeIndex = Array.IndexOf(ScreenModes, FullScreenMode.Windowed);

            _resolutionIndex = Mathf.Max(0, Array.IndexOf(_resolutions, new Vector2Int(Screen.width, Screen.height)));
        }

        #endregion

        #region 마이크

        /// <summary>
        /// 입력 막대를 그린다. 타이틀처럼 아무도 마이크를 쓰지 않는 곳에서는 창이 직접 녹음을 켜고
        /// 읽어서 버린다(송신하지 않는다). 게임·로비에서는 음성 송신기가 읽는 값을 그대로 보여 준다 —
        /// 둘이 같은 캡처를 나눠 읽으면 송신 프레임을 빼앗는다.
        /// </summary>
        private void UpdateMicrophone()
        {
            bool canSelfTest = _capture != null && _capture.IsAvailable
                && (_sceneFlow == null || _sceneFlow.Current == SceneId.Title);
            if (canSelfTest && !_ownsMicTest && !_capture.IsRecording)
            {
                _capture.SetRecording(true);
                _ownsMicTest = _capture.IsRecording;
            }

            if (_ownsMicTest && Time.unscaledTime >= _nextMicTestRead)
            {
                _nextMicTestRead = Time.unscaledTime + MicTestReadSeconds;
                _capture.ReadFrame(_micTestFrame);
            }

            float decibels = _capture != null ? _capture.InputLevelDb : -120f;
            float target = Mathf.InverseLerp(LevelFloorDb, 0f, decibels);
            _shownLevel = target >= _shownLevel
                ? target
                : Mathf.MoveTowards(_shownLevel, target, LevelFallPerSecond * Time.unscaledDeltaTime);
            _levelFill.anchorMax = new Vector2(_shownLevel, 1f);

            int rounded = decibels <= SilenceDb ? int.MinValue : Mathf.RoundToInt(decibels);
            if (rounded != _shownDb)
            {
                _shownDb = rounded;
                _levelText.text = rounded == int.MinValue ? "—" : rounded + " dB";
            }

            string status = MicrophoneStatus();
            if (!ReferenceEquals(_micStatusText.text, status))
                _micStatusText.text = status;
        }

        private string MicrophoneStatus()
        {
            if (_capture == null)
                return "음성 기능이 연결되지 않았습니다.";
            if (!_capture.IsAvailable)
                return "Steam 에 연결되지 않아 마이크를 쓸 수 없습니다. Steam 을 켠 상태로 실행하세요.";
            if (_ownsMicTest)
                return "마이크 테스트 중 — 말해 보세요. 막대가 움직이면 정상입니다. (다른 사람에게 들리지 않습니다)";
            if (_settings != null && _settings.MicMuted)
                return "마이크가 꺼져 있어 다른 사람에게 들리지 않습니다.";
            if (_capture.IsRecording)
                return "말해 보세요. 막대가 움직이면 정상입니다.";
            if (_settings != null && _settings.VoiceMode == VoiceMode.PushToTalk)
                return "눌러서 말하기 — V 를 누르는 동안 입력이 표시됩니다.";
            return "마이크 입력을 기다리는 중입니다.";
        }

        private void StopMicTest()
        {
            if (!_ownsMicTest)
                return;

            _ownsMicTest = false;
            if (_capture != null)
                _capture.SetRecording(false);
        }

        #endregion

        #region 플레이어별 음량

        /// <summary>참가자 목록이 바뀌었을 때만 줄을 다시 만든다.</summary>
        private void RefreshParticipants()
        {
            _nextParticipantRefresh = Time.unscaledTime + ParticipantRefreshSeconds;
            if (_participantList == null)
                return;

            IReadOnlyList<IVoiceParticipant> participants = _chat?.Participants;
            IVoiceParticipant local = _chat?.LocalParticipant;
            if (SameParticipants(participants, local))
                return;

            _shownParticipants.Clear();
            for (int i = _participantList.childCount - 1; i >= 0; i--)
                Destroy(_participantList.GetChild(i).gameObject);

            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    if (ReferenceEquals(participants[i], local))
                        continue;

                    _shownParticipants.Add(participants[i]);
                    BuildParticipantRow(participants[i]);
                }
            }

            _participantEmptyText.gameObject.SetActive(_shownParticipants.Count == 0);
        }

        private bool SameParticipants(IReadOnlyList<IVoiceParticipant> participants, IVoiceParticipant local)
        {
            int index = 0;
            if (participants != null)
            {
                for (int i = 0; i < participants.Count; i++)
                {
                    if (ReferenceEquals(participants[i], local))
                        continue;
                    if (index >= _shownParticipants.Count || !ReferenceEquals(_shownParticipants[index], participants[i]))
                        return false;
                    index++;
                }
            }

            return index == _shownParticipants.Count && _participantEmptyText.gameObject.activeSelf == (index == 0);
        }

        private void BuildParticipantRow(IVoiceParticipant participant)
        {
            RectTransform control = SettingsUiKit.CreateRow(_participantList, participant.DisplayName, out _);

            Slider slider = SettingsUiKit.CreateSlider(control);
            SettingsUiKit.Stretch((RectTransform)slider.transform, Vector2.zero, new Vector2(0.62f, 1f));
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(participant.Volume);

            Text value = SettingsUiKit.CreateText("Value", control, FormatPercent(participant.Volume),
                SettingsUiKit.LabelSize, SettingsUiKit.Accent, TextAnchor.MiddleRight);
            SettingsUiKit.Stretch(value.rectTransform, new Vector2(0.62f, 0f), new Vector2(0.76f, 1f));

            Button mute = SettingsUiKit.CreateButton("Mute", control, string.Empty, 18, out Text muteLabel);
            SettingsUiKit.Stretch((RectTransform)mute.transform, new Vector2(0.79f, 0f), Vector2.one);

            void Show(float volume)
            {
                slider.SetValueWithoutNotify(volume);
                value.text = FormatPercent(volume);
                muteLabel.text = volume > 0f ? "음소거" : "해제";
                muteLabel.color = volume > 0f ? SettingsUiKit.TextBright : SettingsUiKit.Danger;
            }

            slider.onValueChanged.AddListener(volume =>
            {
                participant.Volume = volume;
                Show(volume);
            });
            mute.onClick.AddListener(() =>
            {
                participant.Volume = participant.Volume > 0f ? 0f : 1f;
                Show(participant.Volume);
            });
            Show(participant.Volume);
        }

        #endregion

        private static string FormatPercent(float value) => Mathf.RoundToInt(value * 100f) + "%";
    }
}
