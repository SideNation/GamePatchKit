# 성능 측정 결과

`docs/perf/README.md` 참고. `tests/GamePatchKit.PerformanceTests`가 실행될 때마다
아래 표에 행이 추가된다(append-only). "gate" 열이 `pass`/`fail`인 행만 PRD
512MiB/64MiB blocking-gate 판정이고, 그 외(`reference only` 등)는 참고값이다.

아직 Linux blocking-gate 실행 기록이 없다 — 인프라만 구축된 상태다
(`docs/plan/12-performance-validation.md` 참고).

| Timestamp (UTC) | Scenario | Size | OS | Runs | Max | Gate |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-07-26 13:39:41 UTC | compact | smoke | macOS 26.5.2 | 254.8 MiB, 278.3 MiB, 271.6 MiB | 278.3 MiB | reference only (non-Linux) |
| 2026-07-26 13:39:59 UTC | initial-package | smoke | macOS 26.5.2 | 162.9 MiB, 168.4 MiB, 141.2 MiB | 168.4 MiB | reference only (non-Linux) |
| 2026-07-26 13:40:07 UTC | verify | smoke | macOS 26.5.2 | 206.8 MiB, 206.7 MiB, 204.4 MiB | 206.8 MiB | reference only (non-Linux) |
| 2026-07-26 13:40:47 UTC | incremental-package | smoke | macOS 26.5.2 | 213.9 MiB, 234.0 MiB, 234.3 MiB | 234.3 MiB | reference only (non-Linux) |
| 2026-07-26 13:41:25 UTC | runtime-install | smoke | macOS 26.5.2 | 220.9 MiB, 241.7 MiB, 224.8 MiB | 241.7 MiB | reference only (non-Linux) |
| 2026-07-26 13:41:34 UTC | signed-verify | smoke | macOS 26.5.2 | 207.2 MiB, 206.3 MiB, 206.8 MiB | 207.2 MiB | reference only (non-Linux) |
| 2026-07-26 13:41:39 UTC | plan-download | smoke | macOS 26.5.2 | 192.5 MiB, 194.7 MiB, 192.7 MiB | 194.7 MiB | reference only (non-Linux) |
| 2026-07-26 13:47:02 UTC | compact | smoke | macOS 26.5.2 | 289.3 MiB, 293.0 MiB, 249.1 MiB | 293.0 MiB | reference only (non-Linux) |
| 2026-07-26 13:47:20 UTC | initial-package | smoke | macOS 26.5.2 | 155.3 MiB, 161.9 MiB, 181.9 MiB | 181.9 MiB | reference only (non-Linux) |
| 2026-07-26 13:47:28 UTC | verify | smoke | macOS 26.5.2 | 206.7 MiB, 206.8 MiB, 206.6 MiB | 206.8 MiB | reference only (non-Linux) |
| 2026-07-26 13:48:09 UTC | incremental-package | smoke | macOS 26.5.2 | 201.6 MiB, 208.3 MiB, 216.5 MiB | 216.5 MiB | reference only (non-Linux) |
| 2026-07-26 13:48:49 UTC | runtime-install | smoke | macOS 26.5.2 | 273.8 MiB, 277.5 MiB, 218.9 MiB | 277.5 MiB | reference only (non-Linux) |
| 2026-07-26 13:48:59 UTC | signed-verify | smoke | macOS 26.5.2 | 207.1 MiB, 206.3 MiB, 207.2 MiB | 207.2 MiB | reference only (non-Linux) |
| 2026-07-26 13:49:03 UTC | plan-download | smoke | macOS 26.5.2 | 194.7 MiB, 192.3 MiB, 192.5 MiB | 194.7 MiB | reference only (non-Linux) |
| 2026-07-26 13:51:52 UTC | compact | smoke | macOS 26.5.2 | 288.4 MiB, 255.6 MiB, 289.3 MiB | 289.3 MiB | reference only (non-Linux) |
| 2026-07-26 13:52:10 UTC | initial-package | smoke | macOS 26.5.2 | 153.8 MiB, 167.1 MiB, 170.4 MiB | 170.4 MiB | reference only (non-Linux) |
| 2026-07-26 13:52:17 UTC | verify | smoke | macOS 26.5.2 | 204.0 MiB, 206.5 MiB, 207.0 MiB | 207.0 MiB | reference only (non-Linux) |
| 2026-07-26 13:52:56 UTC | incremental-package | smoke | macOS 26.5.2 | 243.6 MiB, 207.8 MiB, 225.4 MiB | 243.6 MiB | reference only (non-Linux) |
| 2026-07-26 13:53:36 UTC | runtime-install | smoke | macOS 26.5.2 | 218.3 MiB, 273.8 MiB, 223.8 MiB | 273.8 MiB | reference only (non-Linux) |
| 2026-07-26 13:53:45 UTC | signed-verify | smoke | macOS 26.5.2 | 206.1 MiB, 206.5 MiB, 206.7 MiB | 206.7 MiB | reference only (non-Linux) |
| 2026-07-26 13:53:50 UTC | plan-download | smoke | macOS 26.5.2 | 195.3 MiB, 192.2 MiB, 195.2 MiB | 195.3 MiB | reference only (non-Linux) |
| 2026-07-26 14:09:35 UTC | compact | smoke | macOS 26.5.2 | 276.7 MiB, 278.0 MiB, 270.0 MiB | 278.0 MiB | reference only (non-Linux) |
| 2026-07-26 14:09:54 UTC | initial-package | smoke | macOS 26.5.2 | 164.3 MiB, 172.9 MiB, 180.5 MiB | 180.5 MiB | reference only (non-Linux) |
| 2026-07-26 14:10:02 UTC | verify | smoke | macOS 26.5.2 | 204.8 MiB, 204.3 MiB, 204.7 MiB | 204.8 MiB | reference only (non-Linux) |
| 2026-07-26 14:10:42 UTC | incremental-package | smoke | macOS 26.5.2 | 246.6 MiB, 215.5 MiB, 216.7 MiB | 246.6 MiB | reference only (non-Linux) |
| 2026-07-26 14:11:23 UTC | runtime-install | smoke | macOS 26.5.2 | 241.0 MiB, 232.1 MiB, 227.0 MiB | 241.0 MiB | reference only (non-Linux) |
| 2026-07-26 14:11:33 UTC | signed-verify | smoke | macOS 26.5.2 | 206.6 MiB, 204.6 MiB, 204.7 MiB | 206.6 MiB | reference only (non-Linux) |
| 2026-07-26 14:11:38 UTC | plan-download | smoke | macOS 26.5.2 | 194.6 MiB, 194.5 MiB, 192.3 MiB | 194.6 MiB | reference only (non-Linux) |
| 2026-07-26 15:09:54 UTC | compact | smoke | macOS 26.5.2 | 312.1 MiB, 277.6 MiB, 278.3 MiB | 312.1 MiB | reference only (non-Linux) |
| 2026-07-26 15:10:12 UTC | initial-package | smoke | macOS 26.5.2 | 167.6 MiB, 183.1 MiB, 182.6 MiB | 183.1 MiB | reference only (non-Linux) |
| 2026-07-26 15:10:19 UTC | verify | smoke | macOS 26.5.2 | 204.8 MiB, 203.8 MiB, 204.3 MiB | 204.8 MiB | reference only (non-Linux) |
| 2026-07-26 15:10:57 UTC | incremental-package | smoke | macOS 26.5.2 | 234.4 MiB, 239.8 MiB, 234.2 MiB | 239.8 MiB | reference only (non-Linux) |
| 2026-07-26 15:11:37 UTC | runtime-install | smoke | macOS 26.5.2 | 274.5 MiB, 279.0 MiB, 272.6 MiB | 279.0 MiB | reference only (non-Linux) |
| 2026-07-26 15:11:47 UTC | signed-verify | smoke | macOS 26.5.2 | 207.2 MiB, 203.8 MiB, 203.7 MiB | 207.2 MiB | reference only (non-Linux) |
| 2026-07-26 15:11:51 UTC | plan-download | smoke | macOS 26.5.2 | 193.0 MiB, 195.2 MiB, 195.3 MiB | 195.3 MiB | reference only (non-Linux) |
| 2026-07-26 15:56:56 UTC | compact | smoke | macOS 26.5.2 | 294.4 MiB, 298.8 MiB, 299.0 MiB | 299.0 MiB | reference only (smoke scale) |
| 2026-07-26 15:57:14 UTC | initial-package | smoke | macOS 26.5.2 | 184.2 MiB, 182.6 MiB, 183.8 MiB | 184.2 MiB | reference only (smoke scale) |
| 2026-07-26 15:57:21 UTC | verify | smoke | macOS 26.5.2 | 207.2 MiB, 207.1 MiB, 206.9 MiB | 207.2 MiB | reference only (smoke scale) |
| 2026-07-26 15:57:59 UTC | incremental-package | smoke | macOS 26.5.2 | 246.2 MiB, 242.4 MiB, 228.5 MiB | 246.2 MiB | reference only (smoke scale) |
| 2026-07-26 15:58:41 UTC | runtime-install | smoke | macOS 26.5.2 | 275.0 MiB, 275.9 MiB, 276.1 MiB | 276.1 MiB | reference only (smoke scale) |
| 2026-07-26 15:58:50 UTC | signed-verify | smoke | macOS 26.5.2 | 207.2 MiB, 207.1 MiB, 209.7 MiB | 209.7 MiB | reference only (smoke scale) |
| 2026-07-26 15:58:54 UTC | plan-download | smoke | macOS 26.5.2 | 195.2 MiB, 195.1 MiB, 197.8 MiB | 197.8 MiB | reference only (smoke scale) |
| 2026-07-26 19:49:23 UTC | verify | smoke | macOS 26.5.2 | 209.5 MiB, 209.7 MiB, 209.5 MiB | 209.7 MiB | reference only (smoke scale) |
| 2026-07-26 19:49:36 UTC | compact | smoke | macOS 26.5.2 | 298.4 MiB, 297.1 MiB, 300.5 MiB | 300.5 MiB | reference only (smoke scale) |
| 2026-07-26 19:49:52 UTC | initial-package | smoke | macOS 26.5.2 | 170.9 MiB, 185.5 MiB, 174.9 MiB | 185.5 MiB | reference only (smoke scale) |
| 2026-07-26 19:49:59 UTC | verify | smoke | macOS 26.5.2 | 209.5 MiB, 207.3 MiB, 207.0 MiB | 209.5 MiB | reference only (smoke scale) |
| 2026-07-26 19:50:35 UTC | incremental-package | smoke | macOS 26.5.2 | 234.0 MiB, 243.9 MiB, 219.5 MiB | 243.9 MiB | reference only (smoke scale) |
| 2026-07-26 19:51:13 UTC | runtime-install | smoke | macOS 26.5.2 | 274.0 MiB, 281.0 MiB, 274.9 MiB | 281.0 MiB | reference only (smoke scale) |
| 2026-07-26 19:51:22 UTC | signed-verify | smoke | macOS 26.5.2 | 209.5 MiB, 209.5 MiB, 209.6 MiB | 209.6 MiB | reference only (smoke scale) |
| 2026-07-26 19:51:26 UTC | plan-download | smoke | macOS 26.5.2 | 194.9 MiB, 195.0 MiB, 194.9 MiB | 195.0 MiB | reference only (smoke scale) |
| 2026-07-26 23:33:11 UTC | compact | smoke | macOS 26.5.2 | 297.3 MiB, 315.1 MiB, 282.1 MiB | 315.1 MiB | reference only (smoke scale) |
| 2026-07-26 23:33:28 UTC | initial-package | smoke | macOS 26.5.2 | 173.5 MiB, 186.3 MiB, 186.2 MiB | 186.3 MiB | reference only (smoke scale) |
| 2026-07-26 23:33:36 UTC | verify | smoke | macOS 26.5.2 | 209.4 MiB, 206.0 MiB, 206.9 MiB | 209.4 MiB | reference only (smoke scale) |
| 2026-07-26 23:34:14 UTC | incremental-package | smoke | macOS 26.5.2 | 235.7 MiB, 233.7 MiB, 265.3 MiB | 265.3 MiB | reference only (smoke scale) |
| 2026-07-26 23:34:52 UTC | runtime-install | smoke | macOS 26.5.2 | 271.6 MiB, 273.9 MiB, 279.0 MiB | 279.0 MiB | reference only (smoke scale) |
| 2026-07-26 23:35:01 UTC | signed-verify | smoke | macOS 26.5.2 | 209.4 MiB, 210.0 MiB, 206.8 MiB | 210.0 MiB | reference only (smoke scale) |
| 2026-07-26 23:35:06 UTC | plan-download | smoke | macOS 26.5.2 | 196.9 MiB, 195.2 MiB, 194.1 MiB | 196.9 MiB | reference only (smoke scale) |
| 2026-07-27 00:34:45 UTC | compact | smoke | macOS 26.5.2 | 300.1 MiB, 278.7 MiB, 307.8 MiB | 307.8 MiB | reference only (smoke scale) |
| 2026-07-27 00:35:02 UTC | initial-package | smoke | macOS 26.5.2 | 185.6 MiB, 172.9 MiB, 183.5 MiB | 185.6 MiB | reference only (smoke scale) |
| 2026-07-27 00:35:09 UTC | verify | smoke | macOS 26.5.2 | 209.8 MiB, 209.3 MiB, 209.7 MiB | 209.8 MiB | reference only (smoke scale) |
| 2026-07-27 00:35:45 UTC | incremental-package | smoke | macOS 26.5.2 | 244.9 MiB, 254.5 MiB, 239.9 MiB | 254.5 MiB | reference only (smoke scale) |
| 2026-07-27 00:36:25 UTC | runtime-install | smoke | macOS 26.5.2 | 277.0 MiB, 276.7 MiB, 269.1 MiB | 277.0 MiB | reference only (smoke scale) |
| 2026-07-27 00:36:34 UTC | signed-verify | smoke | macOS 26.5.2 | 210.1 MiB, 209.8 MiB, 207.0 MiB | 210.1 MiB | reference only (smoke scale) |
| 2026-07-27 00:36:38 UTC | plan-download | smoke | macOS 26.5.2 | 198.0 MiB, 194.5 MiB, 195.1 MiB | 198.0 MiB | reference only (smoke scale) |
| 2026-07-27 01:22:02 UTC | compact | smoke | macOS 26.5.2 | 300.2 MiB, 299.6 MiB, 278.2 MiB | 300.2 MiB | reference only (smoke scale) |
| 2026-07-27 01:23:36 UTC | compact | smoke | macOS 26.5.2 | 295.7 MiB, 298.2 MiB, 296.3 MiB | 298.2 MiB | reference only (smoke scale) |
| 2026-07-27 01:23:53 UTC | initial-package | smoke | macOS 26.5.2 | 185.3 MiB, 175.0 MiB, 174.2 MiB | 185.3 MiB | reference only (smoke scale) |
| 2026-07-27 01:24:01 UTC | verify | smoke | macOS 26.5.2 | 209.2 MiB, 208.2 MiB, 207.3 MiB | 209.2 MiB | reference only (smoke scale) |
| 2026-07-27 01:24:40 UTC | incremental-package | smoke | macOS 26.5.2 | 246.2 MiB, 242.3 MiB, 234.0 MiB | 246.2 MiB | reference only (smoke scale) |
| 2026-07-27 01:25:19 UTC | runtime-install | smoke | macOS 26.5.2 | 272.5 MiB, 268.0 MiB, 272.8 MiB | 272.8 MiB | reference only (smoke scale) |
| 2026-07-27 01:25:29 UTC | signed-verify | smoke | macOS 26.5.2 | 207.1 MiB, 207.0 MiB, 207.4 MiB | 207.4 MiB | reference only (smoke scale) |
| 2026-07-27 01:25:33 UTC | plan-download | smoke | macOS 26.5.2 | 196.2 MiB, 195.2 MiB, 195.3 MiB | 196.2 MiB | reference only (smoke scale) |
