# DK Self Extractor Builder

간단한 Windows용 **단일 자동 압축 해제(SFX) EXE 생성기**입니다. 원본 폴더를 지정해 만들고, 생성한 EXE 파일 하나를 다른 PC에 전달하면 별도 압축 프로그램 설치 없이 압축을 해제할 수 있습니다.

## 기능

- 원본 폴더 및 하위 폴더를 하나의 EXE로 묶기 (빈 폴더 포함)
- 생성할 EXE 파일명과 저장 경로 선택
- 생성된 EXE에서 처음 표시할 압축 해제 경로 지정 (실행 시 변경 가능)
- 압축률 3단계: 무압축 / 빠른 압축 / 최적 압축
- 사용자 메모/안내 문구
- 암호 사용 선택: 설정 시 ZIP 전체 데이터를 PBKDF2-HMAC-SHA256(150,000회) + AES-256-CBC + HMAC-SHA256으로 암호화 및 무결성 검증
- 생성된 EXE는 기존 파일 덮어쓰기 **기본 체크**, 실행할 때 체크 해제 가능
- ZIP 항목의 상위 경로 이동 방지 및 연결된 폴더/파일 덮어쓰기 차단
- 생성기와 자동 압축 해제 EXE에 자체 제작 아이콘 적용
- 간결한 한국어 UI, 관리자 권한이나 별도 압축 프로그램 불필요

## 사용법

1. GitHub Actions에서 통과한 빌드의 DK-Self-Extractor-Builder-win 아티팩트를 내려받아 EXE 실행.
2. 원본 폴더, 결과 EXE 저장 위치, 압축 해제 기본 경로를 설정.
3. 압축률을 선택하고 메모 또는 암호를 필요에 따라 입력.
4. EXE 생성을 누름.
5. 생성된 EXE를 실행하면 해제 위치, 암호(설정 시), 기존 파일 덮어쓰기 체크를 확인하고 압축 해제 가능.

**폴더 구조:** 선택한 폴더의 내용이 해제 위치에 복원됩니다. 선택한 최상위 폴더를 한 번 더 만들지는 않습니다.

**압축률:** .NET Framework 기본 ZIP의 실제 지원 구분(NoCompression, Fastest, Optimal)에 대응합니다. 임의의 0~9 단계를 표시하지 않습니다.

## 실행 환경

- Windows 10 22H2 / Windows 11, .NET Framework 4.8 이상
- 생성기 및 생성된 EXE는 각각 단일 파일. 기존 Windows 제공 .NET Framework를 이용하므로 대형 런타임을 함께 묶지 않습니다.
- MSVC, 7-Zip, 별도 설치 프로그램이나 관리자 권한 불필요.
- 오래된 Windows에서 .NET Framework 4.8이 없다면 별도 설치가 필요할 수 있습니다.
- 코드 서명은 포함되지 않아 다운로드된 EXE에 Windows SmartScreen 경고가 표시될 수 있습니다.

## 소스 빌드

Visual Studio 2022 Build Tools(MSBuild, .NET Framework 4.8 targeting pack) 설치 후 Windows PowerShell:

    .\build.ps1
    .\smoke.ps1

- 생성기: src/Builder/bin/Release/DK-Self-Extractor-Builder.exe
- SFX 스텁: src/Stub/bin/Release/DKSelfExtractor.Stub.exe (생성기 안에 내장)
- build.ps1이 자체 아이콘을 코드로 그려 생성합니다. 외부 아이콘 또는 유료 에셋은 없습니다.

## 동작 및 제한

- ZIP 생성과 암호화 과정에서 임시 작업 파일을 만들며 완료 시 정리합니다. 파일 크기에 비례한 임시 디스크 여유 공간이 필요합니다.
- 암호 분실 시 복구 불가. 일반 ZIP 암호 방식과 호환되지 않으며, 생성된 EXE로만 해제합니다.
- 원본 심볼릭 링크/정션 등 재분석 지점은 거부합니다.
- 내부 ZIP과 메타데이터는 독자적인 SFX 래퍼 형식입니다. 다른 ZIP 앱에서 EXE를 바로 열 수 있다고 보장하지 않습니다.
- 긴 경로 또는 권한 없는 시스템 경로는 Windows 설정에 따라 사용이 제한될 수 있습니다.
- 덮어쓰기 체크를 해제하면 기존 파일을 건너뛰며, 기본 체크 상태에서는 같은 파일을 덮어씁니다.

## 라이선스

[MIT License](LICENSE). 압축과 암호화에는 .NET Framework 기본 라이브러리만 사용하며, 별도의 압축 라이브러리를 복제하거나 재배포하지 않습니다.
