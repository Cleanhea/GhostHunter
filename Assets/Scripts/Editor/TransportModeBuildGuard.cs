using System.Text;
using GhostHunter.Core.Networking;
using GhostHunter.Networking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 릴리스 빌드가 로컬(UnityTransport) 모드로 나가는 것을 막는다.
    ///
    /// 트랜스포트 모드는 직렬화된 값이라, 로컬 검증을 하다가 그대로 저장·커밋하면
    /// 아무 신호 없이 빌드에 실린다. 증상이 크래시가 아니라 "Steam 로비는 뜨는데
    /// 아무도 접속하지 못함" 이라 원인을 찾기 어렵다. 빌드 단계에서 끊는다.
    ///
    /// 근거: ADR-0011. 조건부 컴파일 대신 이 검사를 택한 이유가 거기 있다.
    /// </summary>
    public sealed class TransportModeBuildGuard : IPreprocessBuildWithReport, IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        /// <summary>재사용 프리팹에 잘못 들어간 ConnectionManager도 함께 검사한다.</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (IsExempt(report))
                return;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null)
                    continue;

                foreach (ConnectionManager manager in prefab.GetComponentsInChildren<ConnectionManager>(true))
                    Verify(manager, path);
            }
        }

        /// <summary>
        /// Bootstrap 씬에 놓인 리그를 검사한다.
        /// </summary>
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // 플레이 모드 진입 시에도 불리며 그때는 report 가 null 이다.
            if (report == null || IsExempt(report))
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (ConnectionManager manager in root.GetComponentsInChildren<ConnectionManager>(true))
                    Verify(manager, scene.path);
            }
        }

        /// <summary>개발 빌드는 로컬 모드가 정상 용법이므로 검사하지 않는다.</summary>
        private static bool IsExempt(BuildReport report)
        {
            return report.summary.options.HasFlag(BuildOptions.Development);
        }

        private static void Verify(ConnectionManager manager, string location)
        {
            if (manager.Mode == TransportMode.Steam)
                return;

            var message = new StringBuilder()
                .AppendLine("릴리스 빌드를 중단했다. 트랜스포트 모드가 Steam 이 아니다.")
                .AppendLine()
                .AppendLine($"  위치 : {location}")
                .AppendLine($"  대상 : {GetHierarchyPath(manager.transform)}")
                .AppendLine($"  현재 : {manager.Mode}  (Steam 이어야 한다)")
                .AppendLine()
                .AppendLine("Local 은 Steam 없이 혼자 로직을 검증할 때 쓰는 개발 전용 경로다.")
                .AppendLine("이대로 출시되면 Steam 로비는 생성되지만 아무도 접속하지 못한다.")
                .AppendLine()
                .AppendLine("조치: 위 오브젝트의 ConnectionManager > Transport Mode 를 Steam 으로 되돌린다.")
                .AppendLine("      로컬 검증은 플레이 중 F1 HUD 의 모드 버튼으로 전환한다(저장하지 않는다).")
                .Append("근거: docs/architecture/decisions/ADR-0011-local-transport-path.md")
                .ToString();

            throw new BuildFailedException(message);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;

            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }
    }
}
