# 13. 문서화·배포 산출물 정리

> PRD 섹션: 배포 산출물, publish와 channel 연동, 외부 host 연동, 서명과 무결성

## 목표

README와 배포 산출물을 정리해 외부 사용자가 문서만으로 CLI, package 설정,
Runtime 통합, publisher 계약을 적용할 수 있게 한다.

## 선행 단계

01~12

## 작업 항목

### README

- [ ] 개요와 설치: .NET tool `gpk`, NuGet 4종(`Core`, `Runtime`,
      `Compression.NativeCompressions`, `DotNet`)
- [ ] `gamepatchkit.yml` 설정: 전체 필드 표, group 설계 규칙, 단일-document·mapping
      root와 금지 YAML 기능, 예시 설정
- [ ] `gamepatchkit.yml`(사용자 입력), `package-config.schema.json`(versioned 설정
      계약), release manifest(Packager 생성 canonical JSON)의 역할과 검증 순서
- [ ] CLI 명령별 사용법, exit code 표, `--json` 출력, `--dry-run`
- [ ] `dataVersion`·`compactVersion`·`manifestHash`의 역할과 package·incremental·
      compact 시 변화 규칙
- [ ] compression은 새 artifact 생성 정책이며 incremental에서는 기존 artifact를
      실제 compression metadata 그대로 재사용할 수 있음을 설명
- [ ] compression 설정을 바꿔도 재사용 artifact가 요구하는 codec은 계속 지원해야 함을
      명시
- [ ] Runtime 통합 가이드: DotNet adapter 사용 예시, 외부 host(Unity)의
      `IArtifactTransport`·`IRuntimeStorage`·codec 주입 구현 가이드,
      iOS IL2CPP 기본 codec 미지원 제약 명시
- [ ] target pointer·active pointer·`PackageState`·group install state의 차이와
      required-only 최초 설치·optional 후속 설치 흐름
- [ ] `package-state.json` 필드·형식·최초 상태·불변 조건·filesystem 배치·writer
      lock·원자적 교체·trusted target 기반 손상 복구 규칙
- [ ] 여러 group 요청의 activation batch 경계, 병렬 다운로드·staging과 단일 state
      commit, 실패·revision 충돌 시 all-or-nothing 규칙
- [ ] publisher 계약: 업로드 순서(artifact → 검증 → manifest → 재검증 → channel
      교체), cache 정책, rollback
- [ ] 서명 운영: 키 생성·보관, key rotation, 서명 필수 모드

### schema·버전 정책

- [ ] versioned JSON Schema 3종의 배포 방식과 manifest 호환 버전 정책 문서화
      (schema와 manifest 호환 버전은 같은 repository release에서 함께 관리)
- [ ] adapter conformance fixture·test suite 사용법 문서화

### 배포 검증

- [ ] NuGet 4종과 `gpk` tool의 패키징 메타데이터 정리 (license, repository URL 등)
- [ ] 문서의 예시 명령·설정·코드가 실제 산출물로 동작하는지 확인

## 산출물

- README, schema 버전 정책 문서, conformance 사용법, 패키징 메타데이터

## 완료 기준

- 신규 사용자가 README만으로 최초 package → publish tree → Runtime 다운로드·활성화
  시나리오를 재현할 수 있다.
- 문서의 명령·설정·API가 구현과 일치한다.
- PRD 배포 산출물 목록이 모두 빌드 가능한 상태로 준비된다.
