using System;
using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 레이어나 씬을 넘는 협력에 쓰는 서비스 레지스트리. 소비자는 인터페이스로 받아 간다.
    ///
    /// 등록·해제는 SceneInstaller 파생 컴포넌트만 한다. 서비스 구현체가 스스로 등록하거나
    /// 자기 static 인스턴스를 들고 있으면 안 된다.
    /// </summary>
    public static class Services
    {
        private static readonly Dictionary<Type, object> Registry = new();

        /// <summary>등록된 서비스 수. 진단용.</summary>
        public static int Count => Registry.Count;

        public static void Bind<T>(T service) where T : class
        {
            if (service == null)
            {
                Debug.LogError($"Services.Bind<{typeof(T).Name}>: null 은 등록할 수 없다.");
                return;
            }

            if (Registry.TryGetValue(typeof(T), out object existing))
            {
                // 같은 인스턴스를 두 번 등록하는 것은 배선 실수지 치명적이지는 않다.
                // 다른 인스턴스로 덮어쓰는 것은 어느 쪽이 살아 있는지 알 수 없게 만든다.
                Debug.LogError(
                    $"Services.Bind<{typeof(T).Name}>: 이미 등록되어 있다. " +
                    (ReferenceEquals(existing, service)
                        ? "같은 인스턴스를 두 번 등록했다."
                        : "다른 인스턴스로 덮어쓰려 했다. 인스톨러 배선을 확인할 것."));
                return;
            }

            Registry.Add(typeof(T), service);
        }

        /// <summary>등록된 것과 같은 인스턴스일 때만 해제한다.</summary>
        public static void Unbind<T>(T service) where T : class
        {
            if (Registry.TryGetValue(typeof(T), out object existing) && ReferenceEquals(existing, service))
                Registry.Remove(typeof(T));
        }

        /// <summary>미등록이면 예외. 배선 누락을 조용히 넘기지 않기 위한 것이다.</summary>
        public static T Get<T>() where T : class
        {
            if (Registry.TryGetValue(typeof(T), out object service))
                return (T)service;

            throw new InvalidOperationException(
                $"Services.Get<{typeof(T).Name}>: 등록되지 않았다. " +
                "Bootstrap 씬 없이 단독 실행했거나 인스톨러 배선이 빠졌다.");
        }

        /// <summary>없어도 되는 선택적 의존일 때만 쓴다.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Registry.TryGetValue(typeof(T), out object found))
            {
                service = (T)found;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// 플레이 시작마다 레지스트리를 비운다.
        /// Enter Play Mode Options 로 도메인 리로드를 끄면 static 이 플레이 종료 후에도 살아남아,
        /// 다음 플레이에서 이미 파괴된 오브젝트가 등록된 채로 남는다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode() => Registry.Clear();
    }
}
