# 12. 성능·메모리 검증

> PRD 섹션: 성능·관측 기준, 재현성과 멱등성

## 목표

1만 파일·1GiB 기준 fixture로 package·verify·diff·download 전 구간의 streaming
동작과 결정성을 검증하고, 관측 지표 출력을 확정한다.

## 선행 단계

05~11

## 작업 항목

- [ ] deterministic fixture generator: 최소 1만 파일·원본 합계 1GiB, seed 고정
      (파일 크기 분포와 file·bundle group 구성 포함)
- [ ] scaling fixture: 같은 1만 파일·seed·group 분포에서 payload 크기만 조정한
      256MiB와 1GiB 입력 생성
- [ ] blocking 환경 고정: Ubuntu 24.04 x64, repository 고정 .NET 10, Release build,
      Workstation GC, 4 vCPU, 8GiB RAM, local SSD, 병렬도 4, debugger·profiler 없음
- [ ] 시나리오 측정: 최초 package, incremental(소수 파일 변경), compact, verify,
      signed verify, plan-download, Runtime download·활성화
- [ ] 각 시나리오는 별도 process로 3회 실행하고 GNU `/usr/bin/time -v`의
      `Maximum resident set size`를 MiB로 변환해 3회 중 최대값을 peak RSS로 판정
- [ ] 1GiB fixture의 package·compact·verify·download 각 process peak RSS ≤ 512MiB
- [ ] 256MiB에서 1GiB로 입력을 늘렸을 때 각 streaming 시나리오의 peak RSS 증가
      ≤ 64MiB
- [ ] 다른 지원 OS의 같은 측정값은 참고 결과로 기록하고 Linux 기준만 blocking gate로
      사용
- [ ] 병렬 처리 결정성: 병렬도를 바꿔도 파일 순서, bundle 경계, manifest byte가
      바뀌지 않는다
- [ ] 관측 지표 출력 검증: 전체·group별 파일 수·byte, 생성·재사용 artifact 수·byte,
      추가·변경·삭제 수, 예상 다운로드 byte·요청 수, 임시 저장공간, compact 전후
      신규 설치 byte 차이, cache hit byte, 단계별 실행 시간, 취소·재시도·검증 실패
      결과
- [ ] 환경·명령·3회 개별값·최대 peak RSS·scaling delta와 기준 통과 여부를 문서로
      기록해 회귀 비교 기준으로 삼는다

## 산출물

- fixture generator, 성능 테스트(일반 테스트와 분리된 category), 측정 결과 문서

## 완료 기준

- 기준 Linux 환경에서 1만 파일·1GiB fixture의 package·compact·verify·download
  peak RSS가 각각 512MiB 이하이고 256MiB 대비 peak RSS 증가가 64MiB 이하다
  (검증 기준 17).
- 병렬 실행이 artifact·manifest byte를 바꾸지 않는다(검증 기준 1 재확인).
- PRD 성능·관측 기준의 지표가 package·diff·Runtime 결과에 모두 출력된다.
