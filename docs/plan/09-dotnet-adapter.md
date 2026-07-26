# 09. DotNet adapter·통합 테스트

> PRD 섹션: DotNet adapter, 프로젝트 책임 `GamePatchKit.DotNet`, publish와 target
> manifest 선택

## 목표

`GamePatchKit.DotNet`으로 Runtime contract를 일반 .NET 환경에 연결한다:
`HttpClient` 전송, filesystem cache·staging·immutable installation, package별
`package-state.json` 원자적 교체, 기본 zstd
codec 구성.

## 선행 단계

04, 08 (Packager 출력물을 fixture로 쓰는 end-to-end 시나리오는 05·06 이후)

## 작업 항목

### transport

- [x] `HttpClient`를 외부에서 주입받는 `IArtifactTransport` 구현 (연결 재사용·테스트
      지원)
- [x] streaming download와 파일 write, cancellation token과 progress 전달

### storage

- [x] package별 root에 state·manifest cache·artifact cache·installation·staging을
      분리하고 package 사이의 local deduplication을 하지 않음
- [x] `packages/<packageId>/{state,manifests,cache,installs,staging}` 고정 layout과
      각 디렉터리 책임 문서화
- [x] `<runtimeRoot>/packages/<packageId>/state/package-state.json` 한 파일에
      `PackageState` 저장
- [x] `state/package-state.lock` 경로를 사용할 수 있으나 파일 존재 여부가 아니라 OS
      file lock 또는 동등한 exclusive handle로 package writer 직렬화
- [x] activation batch의 모든 staging을 검증된 immutable installation으로 rename한
      뒤 state를 한 번만 commit
- [x] state 임시 파일을 최종 state와 같은 filesystem에 쓰고 flush한 뒤 OS별 원자적
      rename/`File.Replace`로 교체하며 in-place 갱신 금지
- [x] `PackageState` 교체 전 기존 state와 referenced installation을 변경·삭제하지 않음
- [x] 손상·알 수 없는 schema·누락 installation state를 활성 근거로 사용하지 않고
      cache와 installation을 보존
- [x] state 복구는 host가 제공한 신뢰 가능한 target manifest reference의
      manifest·file hash를 재검증해 수행하고, reference가 없으면 디렉터리 이름으로
      active release를 추정하지 않음
- [x] 프로세스 중단 후 재시작 시 검증된 cache를 인식하고 이어받는다

### 구성

- [x] `GamePatchKit.Compression.NativeCompressions` 기본 codec 구성 helper
- [x] ASP.NET·worker가 시작 전 필수 package를 검증하고 준비되지 않으면 기동을
      실패시킬 수 있는 옵션

### 통합 테스트

- [x] mock HTTP server 기반: 정상 다운로드·활성화, 네트워크 오류·재시도, 취소 후
      재개, 손상 응답 거부, state 교체 실패 시 이전 state 유지
- [x] 실제 filesystem 기반 required-only 최초 설치, optional 후속 설치, unchanged
      optional 무다운로드 재연결, changed optional `stale` 전환
- [x] 여러 group의 download·staging 완료 순서를 바꿔도 batch당 state replace가 한
      번뿐이며 한 group 실패 시 부분 활성화가 없는지 검증
- [x] state 파일 없음·손상·미지원 schema 각각에서 trusted target 유무에 따른 복구 동작
- [x] state write·flush·installation rename·atomic replace 각 지점의 중단 실패 주입과
      동시 writer 차단
- [x] Packager(05·06) 출력물을 fixture로 사용하는 end-to-end 시나리오
      (package → publish tree → 다운로드 → 활성화)

## 산출물

- DotNet adapter와 mock HTTP 통합 테스트

## 완료 기준

- 취소 후 재개·cache 재사용이 실제 filesystem·HTTP 경로에서 통과한다(검증 기준 15).
- staging·state commit 실패 시 이전 `PackageState`와 installation 유지가 실제 경로에서
  통과한다(검증 기준 16, 23).
- compact 전후 동일 파일을 재다운로드하지 않음이 end-to-end로 통과한다(검증 기준 10).
- 전송 중 손상된 byte가 cache·활성화를 오염시키지 않는다(검증 기준 11).
- `package-state.json`의 in-place 부분 상태가 관찰되지 않고 state 손상 시 검증 가능한
  cache·installation이 보존된다(검증 기준 23).
- 여러 group batch의 실제 filesystem 경로에서도 모든 group이 함께 활성화되거나 모두
  이전 상태를 유지한다(검증 기준 23).
- optional group 상태 전환이 실제 filesystem 경로에서 통과한다(검증 기준 22).
