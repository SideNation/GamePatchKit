# 09. DotNet adapter·통합 테스트

> PRD 섹션: DotNet adapter, 프로젝트 책임 `GamePatchKit.DotNet`, publish와 channel 연동

## 목표

`GamePatchKit.DotNet`으로 Runtime contract를 일반 .NET 환경에 연결한다:
`HttpClient` 전송, filesystem cache·staging, 원자적 current pointer, 기본 zstd
codec 구성.

## 선행 단계

04, 08

## 작업 항목

### transport

- [ ] `HttpClient`를 외부에서 주입받는 `IArtifactTransport` 구현 (연결 재사용·테스트
      지원)
- [ ] streaming download와 파일 write, cancellation token과 progress 전달

### storage

- [ ] 일반 filesystem 기반 content-addressed cache·staging 구현
- [ ] current pointer 교체는 해당 OS에서 가능한 원자적 filesystem 연산으로 구현하고
      OS별 전략(rename/`File.Replace` 등)을 문서화
- [ ] 프로세스 중단 후 재시작 시 검증된 cache를 인식하고 이어받는다

### 구성

- [ ] `GamePatchKit.Compression.NativeCompressions` 기본 codec 구성 helper
- [ ] ASP.NET·worker가 시작 전 필수 package를 검증하고 준비되지 않으면 기동을
      실패시킬 수 있는 옵션

### 통합 테스트

- [ ] mock HTTP server 기반: 정상 다운로드·활성화, 네트워크 오류·재시도, 취소 후
      재개, 손상 응답 거부, 활성화 실패 시 이전 release 유지
- [ ] Packager(05·06) 출력물을 fixture로 사용하는 end-to-end 시나리오
      (package → publish tree → 다운로드 → 활성화)

## 산출물

- DotNet adapter와 mock HTTP 통합 테스트

## 완료 기준

- 취소 후 재개·cache 재사용이 실제 filesystem·HTTP 경로에서 통과한다(검증 기준 15).
- staging·활성화 실패 시 이전 release 유지가 실제 경로에서 통과한다(검증 기준 16).
- compact 전후 동일 파일을 재다운로드하지 않음이 end-to-end로 통과한다(검증 기준 10).
- 전송 중 손상된 byte가 cache·활성화를 오염시키지 않는다(검증 기준 11).
