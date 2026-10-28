using GhostHunter.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// Bootstrap 씬이 소유하는 영속 네트워크 리그를 선택한다.
    /// </summary>
    public static class NetworkRigSetup
    {
        [MenuItem("GhostHunter/Bootstrap 네트워크 리그 선택", priority = 0)]
        public static void SelectNetworkRig()
        {
            Scene bootstrap = SceneManager.GetSceneByPath(PrototypeSceneSetup.BootstrapScenePath);
            if (!bootstrap.isLoaded)
            {
                bootstrap = EditorSceneManager.OpenScene(
                    PrototypeSceneSetup.BootstrapScenePath,
                    OpenSceneMode.Additive);
            }

            ConnectionManager connection = null;
            foreach (GameObject root in bootstrap.GetRootGameObjects())
            {
                connection = root.GetComponentInChildren<ConnectionManager>(true);
                if (connection != null)
                    break;
            }

            if (connection == null)
            {
                Debug.LogError("[NetworkRigSetup] Bootstrap 씬에서 NetworkRig를 찾지 못했습니다.");
                return;
            }

            Selection.activeGameObject = connection.gameObject;
            EditorGUIUtility.PingObject(connection.gameObject);
        }
    }
}
