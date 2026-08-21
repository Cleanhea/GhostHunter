using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GhostHunter.DebugTools")]

// 발사각 보정(FurnitureLauncher.ResolveLaunchDirection)처럼 공개 API 로 낼 이유가 없는
// 순수 계산을 테스트에서 직접 부르기 위해 연다. 테스트 어셈블리 외에는 열지 않는다.
[assembly: InternalsVisibleTo("GhostHunter.Tests.EditMode")]
[assembly: InternalsVisibleTo("GhostHunter.Tests.PlayMode")]
