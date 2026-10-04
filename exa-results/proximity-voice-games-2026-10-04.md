# 근접 음성 채팅 거리 사례

조사일: 2026-10-04 (Asia/Seoul)

게임, 음성 모드, 플랫폼을 포함한 추가 사례 19개를 정리했다. Exa 검색 요청의 결과 수 합계는 180개이며, 중복 검색 결과를 포함한다. 검색 영역은 협동 공포, 생존·전술 게임, 설정을 공개한 음성 모드·플랫폼이다. 직접 게임을 실행하여 측정한 결과는 아니다.

## 수치 해석

- **최대 청취/무음 거리**: 이 거리에서 음성을 더 이상 들을 수 없다는 문서 또는 설정 설명.
- **감쇠 시작 거리**: 이 거리부터 작아지는 설정. 무음 거리로 해석하지 않는다.
- **희미하게 들리는 거리**: 실측 청취 기준. 소프트웨어의 정확한 차단 거리와 다를 수 있다.
- **게임 단위**: blocks, studs, GTA distance units 등은 해당 단위로 기록했다. 근거 없이 미터로 환산하지 않았다.
- **수치 미확인**: 이번에 검토한 자료로 정확한 최대 청취 거리를 확인하지 못했다는 의미다.

공식 개발 문서, 모드 작성자의 소스, 커뮤니티 위키를 구분했다. 커뮤니티 위키 수치는 공식 확정값으로 취급하지 않는다. 서버·월드·섬 제작자가 수정할 수 있는 수치는 기본값 또는 특정 설정값이다.

## 수치가 공개된 사례 10개

| 사례 | 공개 수치 | 의미와 조건 | 근거 품질·출처 |
| --- | --- | --- | --- |
| Phasmophobia | Local 최대 20m | 커뮤니티 위키 설명. 감쇠 때문에 실제로 알아들을 수 있는 거리는 더 짧을 수 있다. Global은 거리와 관계없이 현장 플레이어에게 전달된다. | 커뮤니티 위키의 게임 메커니즘 설명. [Voice chat](https://phasmophobia.fandom.com/wiki/Voice_chat) |
| DayZ | 속삭임 7m / 일반 25m / 외침 45m | 커뮤니티 위키의 음성 범위 설명. 거리가 멀수록 음량이 줄어든다. | 커뮤니티 위키의 조작·메커니즘 설명. [Game Controls](https://dayz.wiki.gg/wiki/Game_Controls) |
| VRChat | Voice Distance Far 기본 25m | 공식 문서가 음성 청취 범위의 끝이라고 설명한다. 월드 제작자가 변경할 수 있다. Avatar 효과음의 기본 40m와 구분한다. | 제작사 공식 SDK 문서. [Player Audio](https://creators.vrchat.com/worlds/udon/players/player-audio/) |
| Fortnite Creative / UEFN | Full Volume Distance 기본 5m / Falloff Distance 기본 35m | 공식 섬 설정 문서의 기본값. Attenuate Voice 설명은 Full Volume까지 최대 음량이며 Falloff에서 들리지 않도록 감소한다고 설명한다. 개별 Falloff 행에는 감쇠 시작을 뜻하는 듯한 문구도 있어 문서 표현에 차이가 있다. 섬 제작자가 값을 변경한다. Party Channel에는 섬 설정이 적용되지 않는다. | Epic 공식 제작 문서. [Mode Settings](https://dev.epicgames.com/documentation/en-us/fortnite/mode-settings-in-fortnite-creative) |
| Squad | 약 50m부터 Local 음성 감쇠 | 공식 위키 문구는 감쇠 시작 거리다. 최대 청취·무음 거리로 확정할 수 없다. Local은 아군에게 전달되며 Squad Channel은 위치에 관계없이 분대원에게 전달된다. | 공식 명칭을 사용하는 커뮤니티 위키. [Communication](https://squad.fandom.com/wiki/Communication) |
| Minecraft + Simple Voice Chat | 일반 48 blocks / 속삭임 24 blocks | 모드 작성자 문서의 서버 기본 설정. 서버 관리자가 수정할 수 있다. Minecraft 기본 게임에 원래 있는 기능은 아니다. | 모드 작성자 공식 설정 문서. [Server Config File](https://modrepo.de/minecraft/voicechat/wiki/server_config) |
| GTA V / FiveM + pma-voice | Native Audio 미사용: 속삭임 3 / 일반 7 / 외침 15 GTA units. Native Audio 사용: 1.5 / 3 / 6 GTA units | 작성자 소스의 두 설정 분기. 코드 주석은 Native Audio의 거리 스케일이 일반 GTA units와 다르게 체감될 수 있음을 밝힌다. 실제 미터 청취 거리로 단정하지 않는다. 서버에서 변경할 수 있다. | 모드 작성자 원본 Lua 소스. [shared.lua](https://github.com/AvarianKnight/pma-voice/blob/main/shared.lua) |
| Arma 3 + TFAR | 속삭임 5m / 일반 20m / 외침 60m | Direct speech 거리 기본 상수. 무전기의 수 km 범위와 구분한다. 모드와 미션 설정에 따라 조정할 수 있다. | 모드 작성자 원본 소스. [defines.hpp](https://github.com/michail-nikolaev/task-force-arma-3-radio/blob/master/addons/core/defines.hpp), [음성 모드 변경](https://github.com/michail-nikolaev/task-force-arma-3-radio/blob/master/addons/core/functions/events/keys/fnc_onSpeakVolumeChange.sqf) |
| Arma 3 + ACRE2 | 기본 3/5 단계: 8m 큰 음량 / 30m 작은 음량 / 100m 아주 희미. 최소 1/5 단계: 13m에서 아주 희미. 최대 5/5 단계: 195m에서 아주 희미 | 모드 공식 문서에 수록된 경험적 측정치. 최대 차단 상수로 해석하지 않는다. 장애물·건물·차량에 의한 감쇠도 문서화되어 있다. | 모드 작성자 공식 문서에 실측자와 측정 조건의 성격을 명시. [Direct Speech](https://acre2.idi-systems.com/wiki/user/direct-speech) |
| Roblox 음성 지원 경험 | Legacy attenuation: 최소 7 studs / 최대 80 studs | 플랫폼의 Legacy 음성 감쇠 프리셋. 모든 Roblox 게임에 같은 범위가 적용되는 것은 아니다. Audio API의 Inverse 프리셋과 구분한다. | 플랫폼 공식 개발 문서. [VoiceChatDistanceAttenuationType](https://create.roblox.com/docs/reference/engine/enums/VoiceChatDistanceAttenuationType) |

### ACRE2의 음량 단계별 실측 표

| 단계 | 큰 음량 | 작은 음량 | 아주 희미 |
| --- | --- | --- | --- |
| 1/5 | 1m | 2m | 13m |
| 2/5 | 3m | 15m | 55m |
| 3/5 (기본) | 8m | 30m | 100m |
| 4/5 | 12m | 45m | 145m |
| 5/5 | 15m | 55m | 195m |

출처: [ACRE2 Direct Speech](https://acre2.idi-systems.com/wiki/user/direct-speech). 표는 문서에 기재된 근사 실측값이다.

## 근접 음성 사례는 확인했지만 정확한 최대 거리는 미확인인 9개

| 게임 | 확인된 통신 사례 | 최대 거리 확인 상태 | 근거 품질·출처 |
| --- | --- | --- | --- |
| R.E.P.O. | 거리와 벽을 통한 음량·필터 감쇠. 모드 작성자가 원래 게임의 LowPassFallOffMultiplier 0.8, LowPassVolumeMultiplier 0.5를 기록했다. | 검토 자료에서 정확한 미터 단위 최대 청취 거리 미확인. 이 두 배율을 거리로 환산할 수 없다. | 원래 값을 설명하는 모드 작성자 문서. [BetterVoiceRange](https://github.com/AlberLC/repo-better-voice-range) |
| Content Warning | 근접 음성 통신. 공식 FAQ에서 Voice 음량과 플레이어별 mute 설정, 음성 문제 대응을 설명한다. | 검토 자료에서 정확한 최대 미터 거리 미확인. | 제작사 공식 음성 안내와 실제 이용자의 근접 음성 문제 보고. [공식 FAQ](https://landfall.se/content-warning-faq), [개발자 답변이 있는 근접 음성 문제 보고](https://steamcommunity.com/app/2881650/discussions/0/4358995462054568061/) |
| Hunt: Showdown 1896 | All/Proximity와 Team 통신. 2025-01-20 공식 안내는 팀원의 위치와 관계없이 음성이 도달하도록 Team 통신이 포함되었다고 설명한다. | 검토 자료에서 상대팀에게 들리는 정확한 Proximity 최대 거리 미확인. 과거 근접 전용 팀 통신 설명과 버전 구분이 필요하다. | 제작사 공식 변경 안내. [Voice Chat Improvements](https://www.huntshowdown.com/news/voice-chat-improvements) |
| Call of Duty: Warzone | Nearby opponents와 대화하는 Proximity Chat. Team/Party 통신, Body Shield, Last Words는 별도 기능이다. | 공식 지원 문서에서 정확한 미터 거리 미확인. 검색에서 발견한 50m·55m 요약값은 공식 확정값으로 채택하지 않았다. | Activision 공식 지원 문서. [Communication Features](https://support.activision.com/warzone-2/articles/communication-features-in-warzone) |
| Hell Let Loose | Proximity, Unit, Officers & Commander의 세 음성 채널. | 검토한 공식 FAQ에는 정확한 Proximity 최대 거리 미기재. | 배급사 공식 FAQ. [Hell Let Loose FAQ](https://www.team17.com/hell-let-loose-faq/) |
| Foxhole | 공간 기반 Local 통신. 2023년 개발자 글에서 적 진영과의 Local 대화 복구를 설명한다. | 검토 자료에서 정확한 음성 최대 거리 미확인. 위키의 Local text 25m를 음성 범위로 사용하지 않았다. | 제작사 공식 개발자 글. [Spring Update 2023](https://www.foxholegame.com/post/devblog-spring-update-2023) |
| Sea of Thieves | Speaking Trumpet으로 다른 선원과의 통신 범위를 넓히거나, 뒤집어 가까운 대상에게 속삭임. 일반 증폭은 자신의 Crew 통신에는 적용되지 않는다는 위키 설명이 있다. | 검토 자료에서 정확한 일반·증폭·속삭임 미터 거리 미확인. | 공식 업데이트와 커뮤니티 위키의 아이템 설명. [Season Five](https://www.seaofthieves.com/release-notes/2.4.0), [Speaking Trumpet](https://seaofthieves.wiki.gg/wiki/Speaking_Trumpet) |
| BattleBit Remastered | Local과 Squad 통신을 구분. 공식 위키는 가까운 대상과 V로 Local 대화, 분대에만 전달할 때 B로 Squad 대화를 설명한다. | 검토 자료에서 정확한 Local 최대 거리 미확인. | 공식 명칭을 사용하는 커뮤니티 위키. [How to play](https://battlebit.wiki.gg/wiki/How_to_play_guide_for_BattleBit_Remastered) |
| Arma Reforger | 아군·적 모두에게 들리는 Proximity, 거리 기반 Handheld/Manpack Radio. 무전 송신 중 자신의 발화는 주변 적에게도 들릴 수 있다. | 공식 설명은 VoNComponent의 Speaking Range를 기준으로 한다. 기본 플레이어의 정확한 최대 미터 값은 검토 자료에서 미확인. | 제작사 공식 게임 안내와 엔진 문서. [Boot Camp Communications](https://reforger.armaplatform.com/news/boot-camp-communications), [Voice over Network](https://community.bohemia.net/wiki/Arma_Reforger:Audio:_Voice_over_Network) |

## 원래 비교한 두 게임의 근거 한계

- **PEAK 약 200m**: [KirbyScream 작성자 설명](https://old.thunderstore.io/c/peak/p/toiletking/KirbyScream/)은 게임 근접 음성과 같은 감쇠를 사용하며 약 200m까지 들린다고 설명한다. 기본 게임을 직접 실측하거나 공식 최대 차단 값을 확인한 결과는 아니다.
- **Lethal Company 약 50m 전후**: [Garrys Mod Hear Display 소스](https://thunderstore.io/c/lethal-company/p/closeraven783/Garrys_Mod_Hear_Display/source/)의 일반 청취 45m, 희미한 청취 47m 기본 추정치에 근거한 참고 범위다. 작성자도 일반 범위 설명에 불확실성을 표시한다. 공식 최대 차단 거리로 확정할 수 없다.

## 비교에서 얻을 수 있는 관찰

대화 범위를 단계별로 선택하는 사례(DayZ, TFAR), 근거리 대화와 별도의 무전·팀 통신을 함께 제공하는 사례(Phasmophobia, Squad, Hunt), 제작자가 감쇠 구간을 직접 정하는 사례(Fortnite Creative, VRChat, 음성 모드)가 있다. 여러 사례에서 일반 대화용 수치가 20~50m 근처에 나타나지만, 이는 이번 표의 관찰이며 모든 게임의 공통 표준이나 GhostHunter에 적용할 확정 설계값은 아니다.
