using System;
using System.Collections;
using System.Collections.Generic;
using GhostHunter.Furniture;
using GhostHunter.Networking;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.DebugTools
{
    /// <summary>
    /// 빌드 검증 전용. -smoke-test 인자가 있을 때 Local Host를 띄워 플레이어와 가구의
    /// 실제 네트워크 스폰을 확인한 뒤 프로세스를 종료한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeRuntimeSmoke : MonoBehaviour
    {
        private const string SmokeArgument = "-smoke-test";
        private static bool _failed;

        private void Start()
        {
            if (!HasSmokeArgument())
                return;

            StartCoroutine(RunSmokeTest());
        }

        private IEnumerator RunSmokeTest()
        {
            float setupDeadline = Time.realtimeSinceStartup + 10f;
            while ((ConnectionManager.Instance == null || NetworkManager.Singleton == null)
                   && Time.realtimeSinceStartup < setupDeadline)
            {
                yield return null;
            }

            ConnectionManager connection = ConnectionManager.Instance;
            NetworkManager network = NetworkManager.Singleton;
            if (connection == null || network == null)
            {
                Fail("ConnectionManager 또는 NetworkManager가 준비되지 않았습니다.");
                yield break;
            }

            connection.SetTransportMode(TransportMode.Local);
            connection.StartHost();

            float spawnDeadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < spawnDeadline)
            {
                if (network.IsHost
                    && network.LocalClient != null
                    && network.LocalClient.PlayerObject != null
                    && CountSpawnedFurniture(network) >= 8)
                {
                    yield return VerifyThrowMechanics(network);

                    if (_failed)
                        yield break;

                    Debug.Log(
                        "[PrototypeRuntimeSmoke] PASS: Local Host, 플레이어 1명, 가구 8개, " +
                        "1인 투척 준비/발사와 2인 잡기 전환 정책 확인.");
                    Application.Quit(0);
                    yield break;
                }

                yield return null;
            }

            Fail(
                $"스폰 제한 시간 초과. IsHost={network.IsHost}, " +
                $"Player={(network.LocalClient?.PlayerObject != null)}, " +
                $"Furniture={CountSpawnedFurniture(network)}");
        }

        private static IEnumerator VerifyThrowMechanics(NetworkManager network)
        {
            var furniture = new List<FurnitureGrabTarget>();
            foreach (NetworkObject networkObject in network.SpawnManager.SpawnedObjects.Values)
            {
                if (networkObject != null
                    && networkObject.TryGetComponent(out FurnitureGrabTarget target))
                {
                    furniture.Add(target);
                }
            }

            if (furniture.Count < 2 || network.LocalClient?.PlayerObject == null)
            {
                Fail("던지기 검증에 필요한 플레이어 또는 가구가 없습니다.");
                yield break;
            }

            if (!VerifyLaunchAngleCorrection())
                yield break;

            Transform player = network.LocalClient.PlayerObject.transform;
            Vector3 origin = player.position + Vector3.up * 1.65f;
            Vector3 direction = player.forward;

            FurnitureGrabTarget solo = furniture[0];
            if (!solo.ServerTryAddHolder(network.LocalClientId, origin, direction))
            {
                Fail("1인 홀드 시작에 실패했습니다.");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.15f);
            if (solo.State != FurnitureState.ThrowReady
                || solo.HolderCount != 1
                || !solo.GetComponent<Rigidbody>().useGravity)
            {
                Fail("1인 입력이 투척 준비 상태로 유지되지 않았습니다.");
                yield break;
            }

            solo.ServerRelease(network.LocalClientId, direction, false);
            yield return new WaitForFixedUpdate();
            if (solo.State != FurnitureState.Launched
                || solo.GetComponent<Rigidbody>().linearVelocity.magnitude < 5f)
            {
                Fail("1인 발사 속도가 충분히 적용되지 않았습니다.");
                yield break;
            }

            const ulong simulatedPartnerId = 999;
            FurnitureGrabTarget cooperative = furniture[1];
            bool firstAttached = cooperative.ServerTryAddHolder(
                network.LocalClientId,
                origin,
                direction);
            bool secondAttached = cooperative.ServerTryAddHolder(
                simulatedPartnerId,
                origin + Vector3.right,
                direction);

            if (!firstAttached
                || !secondAttached
                || cooperative.State != FurnitureState.Held
                || cooperative.HolderCount != 2
                || cooperative.GetComponent<Rigidbody>().useGravity)
            {
                Fail("2인 동시 입력이 잡기 상태로 전환되지 않았습니다.");
                yield break;
            }

            cooperative.ServerRelease(network.LocalClientId, direction, false);
            if (cooperative.State != FurnitureState.ThrowReady
                || cooperative.HolderCount != 1
                || !cooperative.GetComponent<Rigidbody>().useGravity)
            {
                Fail("2인 중 첫 해제에서 1인 투척 준비로 돌아가지 않았습니다.");
                yield break;
            }

            cooperative.ServerRelease(simulatedPartnerId, direction, false);
            yield return new WaitForFixedUpdate();
            if (cooperative.State != FurnitureState.Launched
                || cooperative.GetComponent<Rigidbody>().linearVelocity.sqrMagnitude <= 0.01f)
            {
                Fail("남은 1인의 마지막 해제에서 발사가 일어나지 않았습니다.");
            }
        }

        private static bool VerifyLaunchAngleCorrection()
        {
            Vector3 belowHorizon = new(0f, -1f, 1f);
            Vector3 aboveLimit = new(0f, 10f, 1f);
            Vector3 minimum = FurnitureLauncher.ResolveLaunchDirection(
                belowHorizon,
                20f,
                70f);
            Vector3 maximum = FurnitureLauncher.ResolveLaunchDirection(
                aboveLimit,
                20f,
                70f);

            float minimumAngle = Mathf.Asin(minimum.y) * Mathf.Rad2Deg;
            float maximumAngle = Mathf.Asin(maximum.y) * Mathf.Rad2Deg;

            if (Mathf.Abs(minimumAngle - 20f) <= 0.1f
                && Mathf.Abs(maximumAngle - 70f) <= 0.1f)
            {
                return true;
            }

            Fail(
                $"발사각 보정 실패. minimum={minimumAngle:0.00}, " +
                $"maximum={maximumAngle:0.00}");
            return false;
        }

        private static int CountSpawnedFurniture(NetworkManager network)
        {
            int count = 0;
            foreach (NetworkObject networkObject in network.SpawnManager.SpawnedObjects.Values)
            {
                if (networkObject != null
                    && networkObject.TryGetComponent(out FurnitureGrabTarget _))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasSmokeArgument()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, SmokeArgument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void Fail(string reason)
        {
            _failed = true;
            Debug.LogError($"[PrototypeRuntimeSmoke] FAIL: {reason}");
            Application.Quit(2);
        }
    }
}
