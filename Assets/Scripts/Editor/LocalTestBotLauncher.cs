using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GhostHunter.EditorTools
{
    /// <summary>에디터 Host에 실제 Local 클라이언트 프로세스를 붙이는 개발 전용 메뉴.</summary>
    [InitializeOnLoad]
    public static class LocalTestBotLauncher
    {
        private const string MenuRoot = "GhostHunter/로컬 테스트 봇/";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string ExecutableName = "GhostHunter.exe";
        private const int MaxBots = 3;
        private static readonly List<Process> Bots = new();
        private static int _nextSlot = 1;

        static LocalTestBotLauncher()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopBots;
            EditorApplication.quitting += StopBots;
            EditorApplication.playModeStateChanged += HandlePlayModeChanged;
        }

        [MenuItem(MenuRoot + "1. 개발 빌드 만들기", priority = 100)]
        public static void BuildClient()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[LocalTestBot] 빌드는 Play 모드를 끈 상태에서 실행하세요.");
                return;
            }

            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                Debug.LogError("[LocalTestBot] 현재 런처는 Windows 개발 빌드만 지원합니다.");
                return;
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0 || scenes[0] != BootstrapScenePath)
            {
                Debug.LogError("[LocalTestBot] Build Settings의 첫 활성 씬이 Bootstrap이어야 합니다.");
                return;
            }

            string executable = ExecutablePath;
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = executable,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[LocalTestBot] 개발 빌드 실패: {report.summary.result}, "
                    + $"errors={report.summary.totalErrors}");
                return;
            }

            Debug.Log($"[LocalTestBot] 개발 빌드 완료: {executable}");
        }

        [MenuItem(MenuRoot + "2. 봇 1명 추가", priority = 101)]
        public static void LaunchOne() => Launch(1);

        [MenuItem(MenuRoot + "3. 봇 2명 추가", priority = 102)]
        public static void LaunchTwo() => Launch(2);

        [MenuItem(MenuRoot + "4. 실행한 봇 종료", priority = 103)]
        public static void StopBots()
        {
            for (int i = Bots.Count - 1; i >= 0; i--)
            {
                Process bot = Bots[i];
                try
                {
                    if (!bot.HasExited)
                        bot.Kill();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LocalTestBot] 봇 종료 중 오류: {exception.Message}");
                }
                finally
                {
                    bot.Dispose();
                }
            }
            Bots.Clear();
            _nextSlot = 1;
        }

        private static void Launch(int count)
        {
            if (!EditorApplication.isPlaying
                || !Services.TryGet(out IConnectionService connection)
                || !Services.TryGet(out ISceneFlow sceneFlow)
                || connection.Mode != TransportMode.Local
                || !connection.IsHost
                || !sceneFlow.Current.IsStage()
                || NetworkManager.Singleton == null
                || !NetworkManager.Singleton.IsHost)
            {
                Debug.LogError("[LocalTestBot] 에디터에서 Bootstrap Play → Tab 접속 HUD → Local → Host로 스테이지(인게임 로비 → 스테이지 출발, 또는 ProtoTypeGame)에 입장한 뒤 실행하세요.");
                return;
            }

            string executable = ExecutablePath;
            if (!File.Exists(executable))
            {
                Debug.LogError("[LocalTestBot] 개발 빌드가 없습니다. 먼저 '1. 개발 빌드 만들기'를 실행하세요.");
                return;
            }

            PruneExited();
            if (Bots.Count + count > MaxBots)
            {
                Debug.LogError($"[LocalTestBot] 봇은 최대 {MaxBots}명까지 실행할 수 있습니다.");
                return;
            }

            string logFolder = Path.Combine(Path.GetDirectoryName(executable), "Logs");
            Directory.CreateDirectory(logFolder);
            for (int i = 0; i < count; i++)
            {
                int slot = _nextSlot++;
                string logPath = Path.Combine(logFolder, $"bot-{slot}.log");
                var start = new ProcessStartInfo(executable)
                {
                    Arguments = "-batchmode -nographics -transport=local -gh-auto=client "
                        + $"-gh-bot-follow={slot} -gh-quit-on-end -gh-quit-after=900 "
                        + $"-logFile \"{logPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(executable),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };

                try
                {
                    Process bot = Process.Start(start);
                    if (bot == null)
                        throw new InvalidOperationException("프로세스 시작 결과가 없습니다.");
                    Bots.Add(bot);
                    Debug.Log($"[LocalTestBot] 봇 {slot} 시작 PID={bot.Id}, 로그={logPath}");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[LocalTestBot] 봇 {slot} 시작 실패: {exception}");
                }
            }
        }

        private static void PruneExited()
        {
            for (int i = Bots.Count - 1; i >= 0; i--)
            {
                if (!Bots[i].HasExited)
                    continue;
                Bots[i].Dispose();
                Bots.RemoveAt(i);
            }
        }

        private static void HandlePlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
                StopBots();
        }

        private static string ExecutablePath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Build", "LocalBots", ExecutableName));
    }
}
