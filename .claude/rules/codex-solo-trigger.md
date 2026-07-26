# codex-solo-workflow 호출 트리거

**Codex를 단독 에이전트로 사용 중**(별도 Claude Code 오케스트레이션 없음)일 때, 다음 상황에서 `codex-solo-workflow` 스킬을 호출한다:

- 방금 코드를 수정했고 자체 검토가 필요함 → standard/adversarial self-review loop
- 고위험 변경(보안·인증·DB·마이그레이션·결제·동시성·공개 API·큰 리팩터·3개 초과 파일) → adversarial self-review loop
- 막혔거나 테스트가 반복 실패함 → rescue 후 변경이 있으면 self-review loop
- PR을 열거나 마감하기 직전 → PR self-review loop

호출하지 않는다:

- 코드 변경이 없고 사용자가 요청하지도 않음

리뷰는 findings 수정·검증 후 변경된 diff를 최대 3회까지 재검토하고, 결과는 사용자 언어로 보고한다.
