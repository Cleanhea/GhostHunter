using System.Runtime.CompilerServices;

// 맵 생성 도구(HousePrototypeBuilder)의 검증 함수를 테스트에서 직접 부르기 위해 연다.
// 생성 도구는 씬을 통째로 다시 만드는 무거운 작업이라 손으로 돌려 보기 어렵다 —
// 검증만 떼어 EditMode 테스트로 돌리는 것이 유일한 자동 확인 수단이다.
[assembly: InternalsVisibleTo("GhostHunter.Tests.EditMode")]
