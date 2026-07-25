# 12. 성능·메모리 검증

> PRD 섹션: 성능·관측 기준, 재현성과 멱등성

## 목표

1만 파일·1GiB 기준 fixture로 package·verify·diff·download 전 구간의 streaming
동작과 결정성을 검증하고, 관측 지표 출력을 확정한다.

## 선행 단계

05~09

## 작업 항목

- [ ] deterministic fixture generator: 최소 1만 파일·원본 합계 1GiB, seed 고정
      (파일 크기 분포와 file·bundle group 구성 포함)
- [ ] 시나리오 측정: 최초 package, incremental(소수 파일 변경), compact, verify,
      plan-download, Runtime download·활성화
- [ ] 전체 입력을 메모리에 올리지 않음을 peak 메모리 측정으로 확인
      (streaming hash·compression·download)
- [ ] 병렬 처리 결정성: 병렬도를 바꿔도 파일 순서, bundle 경계, manifest byte가
      바뀌지 않는다
- [ ] 관측 지표 출력 검증: 전체·group별 파일 수·byte, 생성·재사용 artifact 수·byte,
      추가·변경·삭제 수, 예상 다운로드 byte·요청 수, 임시 저장공간, compact 전후
      신규 설치 byte 차이, cache hit byte, 단계별 실행 시간, 취소·재시도·검증 실패
      결과
- [ ] 측정 결과와 기준치를 문서로 기록해 회귀 비교 기준으로 삼는다

## 산출물

- fixture generator, 성능 테스트(일반 테스트와 분리된 category), 측정 결과 문서

## 완료 기준

- 1만 파일·1GiB fixture를 전체 메모리 적재 없이 package·verify·download한다
  (검증 기준 17).
- 병렬 실행이 artifact·manifest byte를 바꾸지 않는다(검증 기준 1 재확인).
- PRD 성능·관측 기준의 지표가 package·diff·Runtime 결과에 모두 출력된다.
