# Phase 2 산출물 — 기초 및 스캐폴딩

> 입력: `docs/phase-1-분석.md` §6 Phase 2 인계 항목
> 작성일: 2026-07-19 (개정: Unity 6000.5 호환 패치 및 컴파일 검증 완료)
> 상태: **완료 — 전체 어셈블리 컴파일 에러 0, Core.Tests 28케이스 통과 검증됨**

---

## 0. 검증 상태

Unity 에디터(MCP)에 연결하지 않고 진행했지만, 최종적으로는 **추측이 아니라 실제 컴파일러로 검증**했다. Unity가 생성한 csproj 12개를 `dotnet build`로 Unity 6000.5.4f1의 관리 DLL을 참조해 빌드했고, `Core.Tests`는 리플렉션 러너로 실제 실행했다. 상세는 아래 §2.9 참조.

여전히 Unity 에디터에서만 확인 가능한 항목은 §1에 남겨뒀다.

---

## 1. Unity 에디터에서 확인할 항목

컴파일은 검증됐으므로, 남은 것은 에디터/런타임 동작 영역이다.

1. **패키지 해석**: Facepunch.Steamworks(OpenUPM)를 내려받는다. FishNet은 더 이상 git 패키지가 아니라 `Assets/FishNet/`에 벤더링되어 있어 네트워크 없이도 컴파일된다.
2. **Test Runner**: `Window > General > Test Runner > EditMode`에서 `Core.Tests` 실행. 로컬 검증에서는 28케이스 전부 통과했다.
3. **Steamworks 초기화**: Steam 클라이언트가 켜진 상태에서 Play 진입 시 초기화 에러 여부 확인(`steam_appid.txt`는 480 = Spacewar 테스트 AppID). Steam 미로그인 상태에서 초기화가 실패하는 것은 정상이다.
4. **Burst**: FishNet 컴파일이 정상화되면 기존 `Unity.Settings.Editor` 해석 오류는 사라져야 한다(§2.9 참조).

---

## 2. 완료된 항목

### 2.1 패키지 (`Packages/manifest.json`)

| 패키지 | 방식 | 버전/URL |
|---|---|---|
| FishNet | UPM git URL | `com.firstgeargames.fishnet` → `https://github.com/FirstGearGames/FishNet.git?path=Assets/FishNet` |
| Facepunch.Steamworks | OpenUPM 스코프 레지스트리 | `com.facepunch.steamworks` 2.3.6 |
| ProBuilder | Unity 공식 레지스트리 | `com.unity.probuilder` 6.1.2 |

세 패키지 모두 §5(무료 제작 제약) 검토를 통과한 무료 항목이다. package.json의 실제 `name` 필드와 최신 버전 태그를 원격에서 직접 확인한 뒤 반영했다(추측 없음).

### 2.2 FishyFacepunch 트랜스포트

`FirstGearGames/FishyFacepunch` 저장소의 `FishNet/Plugins/FishyFacepunch/` 하위 전체(소스 8개 파일 + meta)를 `Assets/_Project/Net/Transports/FishyFacepunch/`로 원본 GUID를 보존한 채 옮겼다. `Net.asmdef`가 `FishNet.Runtime`을 참조하므로 이 트랜스포트는 별도 조치 없이 Net 어셈블리에 포함되어 컴파일된다.

### 2.3 어셈블리 구조 (§15.2)

```
Assets/_Project/
  Core/            (Core.asmdef, 참조 없음 — FishNet 모름)
    GameFlow/      GameFlowState, GameFlowManager
    Role/          RoleType
    SoundPulse/    SoundType, DirectionOctant, SoundPulse, PerceivedPulse,
                    IOcclusionProbe, SoundPulseResolver
  Net/             (Net.asmdef, Core + FishNet.Runtime 참조)
    NetworkBridge.cs
    Transports/FishyFacepunch/
  Presentation/    (Presentation.asmdef, Core만 참조)
    Palette/       ColorPalette.cs + ColorPalette.asset
  Tests/EditMode/  (Core.Tests.asmdef, Core + nunit 참조, autoReferenced: false)
    SoundPulseResolverTests.cs (12케이스)
    GameFlowManagerTests.cs (9케이스: 7개 전이 + 잘못된 전이 거부 + 이벤트 발생)
```

Core는 UnityEngine 코어(Vector3 등)만 쓰고 FishNet을 전혀 참조하지 않는다 — §15.3의 격리 원칙대로다.

### 2.4 SoundPulseResolver — GAP-1~3 결정 반영 (가장 중요한 산출물)

`Marco.Core.Sound.SoundPulseResolver.Resolve(...)`가 §5.6/§5.7 로직을 순수 함수로 구현한다.

- **GAP-1**: `listenerPlayerId == pulse.SourcePlayerId`면 무조건 `null` — 본인 발생 펄스는 이 경로 자체를 타지 않는다.
- **GAP-2**: `PerceivedPulse.SourcePos`는 벽이 0개(완전히 뚫린 시선)일 때만 값을 가진다. 그 외에는 `DirectionOctant`(8방위)만 제공한다.
- **GAP-3**: 이 함수는 단발 순수 함수다. "0.25초 간격 재판정"은 이 함수를 반복 호출하는 Net 레이어의 책임으로 남겨뒀다(리졸버 자체는 상태를 갖지 않는다).

`Physics.Linecast`를 직접 부르지 않고 `IOcclusionProbe` 인터페이스로 주입받는다 — 그 덕에 유닛 테스트 12케이스가 Unity Physics 없이, EditMode에서 밀리초 단위로 돈다. §9 밸런스 수치(반경 4/9/22m, 벽당 ×0.5 감쇠, 지속시간 최저 배율 0.5, 술래 ×1.2/×1.5)가 전부 이 테스트에 코드로 고정되어 있다.

### 2.5 GameFlowManager (§15.4)

7개 상태 전이(RoundEnd의 두 분기 포함)를 전이표로 강제하는 순수 상태기계. MonoBehaviour도 FishNet도 모른다 — Net 레이어의 `NetworkBridge`가 인스턴스를 들고 있다가 나중에 네트워크로 브로드캐스트하는 방식으로 감쌀 것이다(그 배선 자체는 Phase 3 작업).

### 2.6 씬 구성

`Boot → MainMenu → Lobby → Game` 4개 씬을 만들고 Build Settings에 이 순서로 등록했다.

- `SampleScene.unity`를 `Game.unity`로 리네임(내부 GUID는 그대로 유지 — 기존 참조가 있었다면 안 깨짐)
- `Boot`/`MainMenu`/`Lobby`는 최소 스켈레톤(카메라만 있는 빈 씬) — UI는 Phase 3에서 채운다
- `ProjectSettings/ProjectSettings.asset`의 `templateDefaultScene` 참조도 `Boot.unity`로 정정

### 2.7 컬러 팔레트 (§16.2)

`Marco.Presentation.Palette.ColorPalette` ScriptableObject + 이미 값이 채워진 `.asset` 인스턴스. 기본 팔레트와 색맹 모드 팔레트를 한 에셋에 담고, `GetRunner(bool colorblindMode)` 같은 접근자로 런타임에 토글만으로 전환되게 했다(§19 접근성 요구사항). 5색 모두 기획서 원문 HEX를 그대로 정밀 변환해 넣었다.

### 2.8 NetworkBridge (격리 계층 뼈대)

`FishNet.Managing.NetworkManager`를 감싸는 최소 골격만 만들었다. `ServerManager.Started`/`ClientManager.Started`를 노출하고 `GameFlowManager` 인스턴스를 들고 있다. RPC·SyncVar 배선(§14.3 이벤트 테이블 전체)은 의도적으로 비워뒀다 — 이건 Phase 3(빌드) 스코프다.

---

## 2.9 FishNet Unity 6000.5 호환 패치 (2026-07-19 추가)

Unity 6000.5.4f1에서 FishNet 4.7.2가 컴파일되지 않아, git UPM 패키지 설치를 포기하고 소스를 `Assets/FishNet/`에 벤더링한 뒤 직접 패치했다. FishNet 공식 저장소(main/4.7.2 태그 모두)에 아직 대응 패치가 없다.

### 원인

Unity 6000.5에서 두 가지 API가 폐기됐다.

1. **`Object.GetInstanceID()`** → `EntityId` 기반으로 전환. 부호 검사(`< 0`)로 "런타임 인스턴스인가"를 판별하던 관용구에 대체 API가 없다.
2. **`Scene.handle`의 암시적 int 변환 제거.** `Scene.handle`이 `int`에서 `SceneHandle` 구조체로 바뀌었고, `SceneHandle ↔ int` 암시적 변환이 삭제됐다. 대신 `SceneHandle.GetRawData()`(→`ulong`)와 `SceneHandle.FromRawData(ulong)`를 쓴다.

### 적용한 수정

**핸들 저장 타입을 `int` → `ulong`(raw data)로 통일.** `EntityId`는 64비트라 int로 담으면 잘린다(Unity 공식 마이그레이션 가이드가 명시적으로 경고). 따라서 truncation 대신 전 구간을 ulong으로 올렸다.

| 파일 | 수정 |
|---|---|
| `Runtime/Observing/NetworkObserver.cs` | `GetInstanceID() < 0` 정리 로직을 `#if UNITY_6000_5_OR_NEWER`로 분기해 6.5+에서는 건너뜀 |
| `Runtime/Managing/Scened/UnloadedScene.cs` | `Handle` 필드 `int`→`ulong`, 생성자 시그니처, 비교부 |
| `Runtime/Managing/Scened/SceneLookupData.cs` | `Handle` 필드 및 생성자·`CreateData` 오버로드 전부 ulong화 |
| `Runtime/Managing/Scened/SceneManager.cs` | `PendingClientSceneLoads` 내부 컬렉션/시그니처 11곳, 로컬 핸들 캐시·`GetScene(ulong)`·`AddPendingLoad` 12곳 |
| `Runtime/Managing/Scened/LoadUnloadDatas/SceneLoadData.cs` | 핸들 기반 생성자 5개 |
| `Runtime/Managing/Scened/LoadUnloadDatas/SceneUnloadData.cs` | 핸들 기반 생성자 3개 |
| `Runtime/Serializing/SceneComparer.cs` | `GetHashCode`가 `SceneHandle`을 그대로 반환하던 것 → `.GetHashCode()` |
| `Runtime/Serializing/Helping/Comparers.cs` | `handle != 0` → `handle.GetRawData() != 0` |

`SceneHandle`은 `==`/`!=` 연산자와 `ToString()`을 제공하므로, `SceneHandle`끼리 비교하거나 문자열 보간하는 코드(`ServerObjects.cs` 등)는 수정하지 않았다.

### 손대지 않은 것 — `#if FISHNET_THREADED_COLLIDER_ROLLBACK`

`RollbackCollection.Threaded.cs` / `RollbackManager.Threaded*.cs`도 `scene.handle`을 `NativeList<int>`에 담지만, 이 define이 프로젝트에 정의돼 있지 않아 **컴파일 대상이 아니다.** 제대로 고치려면 Burst job 구조체와 `Rollback(int sceneHandle, ...)` 공개 API까지 연쇄 수정해야 하는데, 컴파일되지 않는 코드라 검증할 방법이 없어 의도적으로 남겨뒀다. **이 기능을 켜려면 그때 함께 ulong으로 올려야 한다.**

### Burst `Unity.Settings.Editor` 오류

별도 원인이 아니라 위 컴파일 실패의 부수 증상이었다. `Unity.Settings.Editor`는 `com.unity.settings-manager`가 제공하며 ProBuilder의 전이 의존성으로 **2.1.1이 이미 정상 설치**돼 있다. Burst는 컴파일 에러가 있는 어셈블리를 처리할 때 이 형태의 "Failed to resolve assembly"를 내는 알려진 동작이라, FishNet이 컴파일되면 함께 사라진다. (manifest에 명시적으로 고정하려 시도했다가, 이미 해석된 2.1.1을 1.0.3으로 다운그레이드시킬 위험이 있어 되돌렸다.)

### 검증 방법

Unity 에디터 없이 `dotnet build`로 Unity가 생성한 csproj 12개를 전부 컴파일해 확인했다(Unity 6000.5.4f1의 관리 DLL을 그대로 참조).

```
FishNet.Runtime · GameKit.Dependencies · Unity.FishNet.Codegen · Core · Net ·
Presentation · Core.Tests · FishNet.Demos · Assembly-CSharp ·
Assembly-CSharp-Editor · SynapseSocket · FishNet.Codegen.Cecil
→ 총 컴파일 에러 0
```

추가로 리플렉션 기반 러너로 `Core.Tests`를 실제 실행해 **28개 케이스 전부 통과**를 확인했다(SoundPulseResolver 19 + GameFlowManager 9. `[TestCase]`가 케이스별로 전개되어 문서 §2.3의 "21개"보다 많게 집계된다).

---

## 3. 의도적으로 하지 않은 것

- **Steam AppID 등록**: `steam_appid.txt`는 Valve 공개 테스트 AppID `480`(Spacewar)으로 고정. 실제 AppID가 나오면 이 파일과 `FishyFacepunch` 컴포넌트의 `_steamAppID` 필드를 함께 바꿔야 한다.
- **RPC/SyncVar 배선**: NetworkBridge가 실제로 SoundPulse를 리스너별로 브로드캐스트하는 코드는 없다. §14.3 이벤트 테이블 전체(JoinRoom, RoleAssigned, SoundPulse 등)는 Phase 3 스코프.
- **UI**: Boot/MainMenu/Lobby 씬은 카메라만 있는 빈 껍데기다. §12 와이어프레임 구현은 Phase 3.
- **파문 셰이더**: Phase 1 인계 항목에 있었지만, 이건 M1 기술 스파이크(§21) 산출물이라 실제 셰이더 코드 없이는 검증 불가능한 영역이라 스킵했다. Phase 3 착수 시 M1 스파이크로 별도 진행 권장.

---

## 4. 게이트 판정

| # | 기준 | 상태 | 비고 |
|---|---|---|---|
| 1 | CI/CD 파이프라인 | **해당 없음** | 1인 개발·서버 없는 P2P 게임이라 원 플레이북의 CI/CD 파이프라인 항목은 적용 대상이 아님(§14.4 참조) |
| 2 | 데이터베이스 스키마 배포 | **해당 없음** | 서버 DB 없음(호스트 기반 P2P) |
| 3 | API 스캐폴드 헬스체크 | **해당 없음** | REST API 없음 |
| 4 | 프론트엔드 스켈레톤 렌더 | ⚠ 에디터 확인 필요 | Boot/MainMenu/Lobby 씬 자체는 만들어졌으나 실제로 열리는지는 1장 체크리스트로 확인 |
| 5 | 모니터링 대시보드 | **해당 없음** | 클라우드 인프라 없음 |
| 6 | 디자인 시스템 토큰 구현 | ✅ | `ColorPalette` 에셋으로 대체 완료 |
| 7 | Git 워크플로/프로세스 문서화 | 보류 | git 저장소가 아직 초기화되지 않음(사용자 확인 필요) |

**이 프로젝트의 진짜 게이트**는 원 플레이북의 웹서비스 기준이 아니라 1장의 4가지 첫 실행 체크리스트다. 특히 **Core.Tests 21케이스가 전부 통과하는지**가 §9 밸런스 수치의 코드 고정 여부를 결정하는 핵심 기준이다.

---

## 5. Phase 3 인계 항목

Phase 3(빌드·반복)에서 이어갈 것 — `docs/phase-1-분석.md` §3의 M3 태스크 분해(T1~T25)를 그대로 따르되, 이미 끝난 부분은 건너뛴다.

- T1(GameFlowManager) 골격은 완료 — Net 레이어에 배선(브로드캐스트)하는 작업만 남음
- T5(SoundPulseResolver + 유닛 테스트)는 완료 — T6(재판정 주기 실측)·T7(PerceivedPulse 프로토콜 실사용)·T8(파문 렌더러)로 바로 이어갈 수 있음
- T2(맵 그레이박스), T3(밸브 상태기계), T4(승패 판정), T9 이후(음성 파이프라인·역할 시스템·UI)는 전부 미착수

git 저장소가 없는 상태이므로, Phase 3 착수 전에 버전 관리 시작 여부를 사용자에게 확인하는 걸 권한다 — 지금부터는 실제 게임플레이 코드가 쌓이기 시작해서 되돌리기 비용이 커진다.
