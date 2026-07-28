# quickstart 샘플

GamePatchKit의 전체 흐름을 한 번에 돌려 보는 샘플이다. 게임 데이터 4개 파일을
package하고, 검증·서명하고, 로컬 HTTP로 서빙한 publish tree에서 Runtime이 실제로
다운로드·설치하고, incremental release와 compact까지 이어서 보여준다.

```bash
./run.sh          # 전체 실행
PORT=9000 ./run.sh
```

`dotnet`과 `python3`가 필요하다. 서명 단계는 `openssl`이 Ed25519를 지원할 때만 돌고,
아니면 건너뛴다. 생성물은 전부 `.work/`에 만들고 실행할 때마다 지우므로 여러 번 돌려도
안전하다.

## 구성

```text
samples/quickstart/
├── gamepatchkit.yml           # package 설정
├── game-data/                 # 패키징 대상 (원본, run.sh가 건드리지 않는다)
│   ├── core/{config.json,strings.json}
│   └── maps/{forest.dat,desert.dat}
├── QuickStartClient/          # Runtime을 쓰는 최소 client
│   └── Program.cs
├── run.sh
└── .work/                     # 실행 산출물 (gitignore)
    ├── game-data/             # run.sh가 incremental 단계에서 수정하는 사본
    ├── publish/               # gpk가 만든 publish tree
    └── runtime-root/          # Runtime이 설치한 결과
```

설정은 group 두 개로 나뉘어 있다.

| group | `artifactMode` | `required` | 의도 |
| --- | --- | --- | --- |
| `core` | `file` | `true` | 로그인·첫 화면에 필요한 최소 데이터. 자주 바뀌므로 개별 파일 |
| `maps` | `bundle` | `false` | 필요할 때 받는 큰 정적 콘텐츠. 작은 파일이 많아 묶는다 |

## run.sh가 하는 일

| 단계 | 명령 | 보여주는 것 |
| --- | --- | --- |
| 1 | `gpk package` | 최초 release 생성. `dataVersion`·`compactVersion`·`manifestHash` |
| 2 | - | publish tree 배치: `artifacts/files/<hash>/content.zst`, `artifacts/bundles/maps/<hash>.tar.zst`, `manifests/<hash>/manifest.json` |
| 3 | `gpk verify` | schema → 참조 무결성 → artifact byte → signature 순서 검증. `signature.state: absent` |
| 4 | `gpk sign` + `gpk verify --trusted-key --require-signature` | Ed25519 서명과 `signature.state: verified` |
| 5 | `python3 -m http.server` + `QuickStartClient` | required group만 설치(revision 1) → optional group 설치(revision 2) |
| 6 | - | `package-state.json`의 실제 내용 |
| 7 | `gpk package --previous` | bundle entry 하나가 바뀌면 그 group 전체가 file override로 전환되고, `core` artifact는 재사용 |
| 8 | `gpk compact --group maps` | `dataVersion` 유지, `compactVersion` 0 → 1, `manifestHash` 변경 |

## 눈여겨볼 출력

**required group만 먼저 설치된다.** `InstallOrUpdateAsync` 직후 `maps`는
`NotInstalled`이고, `InstallOptionalGroupsAsync`를 부른 뒤에야 `Ready`가 된다. 이때
`active.dataVersion`은 바뀌지 않고 `stateRevision`만 1 → 2로 올라간다.

```text
  stateRevision : 1          stateRevision : 2
  group 'core' : Ready       group 'core' : Ready
  group 'maps' : NotInstalled →  group 'maps' : Ready
```

**compact는 논리 버전을 바꾸지 않는다.**

```text
changed       : true
dataVersion   : v1-b378...   (incremental과 동일)
compactVersion: 1            (0 -> 1)
manifestHash  : 67a7...      (새 값)
```

물리 배치만 바뀌었으므로, 이미 설치된 client는 경로와 `fileHash`가 같은 파일을 다시
받지 않는다.

**같은 입력이면 항상 같은 byte가 나온다.** `run.sh`를 두 번 돌리면 `manifestHash`가
정확히 같다. artifact와 canonical manifest가 시각·머신·실행 순서에 의존하지 않기
때문이다.

## 실제 프로젝트로 옮기기

1. `gamepatchkit.yml`의 `packageId`·`inputRoot`·`include`·`groups`를 자기 데이터에 맞게
   바꾼다. group은 확장자가 아니라 **소비 시점과 변경 주기**로 나눈다.
2. `publish/` 아래 tree를 CDN·오브젝트 스토리지에 올린다. **artifact 먼저, 검증 후
   manifest와 signature** 순서다 ([publish와 서명 운영](../../docs/guide/publishing.md)).
3. client는 `QuickStartClient/Program.cs`처럼 `HttpArtifactTransport` +
   `FileSystemRuntimeStorage` + `PackageRuntime`을 조립한다. 실제 프로젝트에서는
   `ProjectReference` 대신 `dotnet add package GamePatchKit.DotNet`을 쓴다.
4. `packageId`·`dataVersion`·`manifestHash`는 **서버가 알려주는 값**이다. Runtime은
   최신 release를 스스로 고르지 않는다.

Unity라면 [Unity 통합 가이드](../../docs/guide/unity.md)를 참조한다.
