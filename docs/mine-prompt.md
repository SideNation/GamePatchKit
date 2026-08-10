# 프로젝트 사양

- dotnet 10
- NativeCompressions 사용 ([https://github.com/Cysharp/NativeCompressions](https://github.com/Cysharp/NativeCompressions))]([https://github.com/Cysharp/NativeCompressions)https://github.com/Cysharp/NativeCompressionshttps://github.com/Cysharp/NativeCompressions](https://github.com/Cysharp/NativeCompressions))))
- log는 ZLogger 사용
- IL2CPP 사용을 위한 AOT 프리하게 제작
- json은 Newtonsoft Json 13.0.2 사용 (Unity도 사용 가능하게)

# cli 주요 기능

- 폴더 별로 패치 데이터를 관리하게 yaml 관리 (yaml은 cli에서만 사용)
- 압축 할지 파일 그대로 전송할지는 선택 가능하게
- 압축 알고리즘은 디폴트로 Zstandard 사용
- 패치를 위한 매니패스트 json으로 리스트 저장
- 깃으로 데이터 추적 변경사항만 패치 데이터로 개발
- 모두 다시 리페키징하는 기능 필요

웹으로 패치 전략을 리서치한 뒤 추가로 만들어야 될 사항 포함해서 prd 문서를 만들어줘

# cli에 supabase에 업로드 기능 추가

- supabase용 닷넷 패키지 이용
- 업로드에 필요한 변수 값은 패치 데이터 프로젝트에서 한다.
  - 패치를 위한 환경 변수를 이용한다.
  - 환경 변수 파일을 인자로 받을 수 있는 옵션도 추가한다.


# GamePatchKit 패치 데이터 배포와 소비

## dotnet 용 패치 다운로드 프로젝트

## 유니티용 패치 다운로드 프로젝트

