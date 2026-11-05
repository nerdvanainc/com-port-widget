# 서드파티 고지 / Third-party notices

## 실행 파일 / The executable

`ComPortWidget.exe`에는 서드파티 코드가 들어 있지 않습니다. 앱 코드만 담은 단일 파일이며, 빌드 결과물은 이 exe 하나뿐입니다. 프로그램은 Windows에 들어 있는 .NET Framework 4.8을 사용하고, 그 구성 요소는 이 zip에 들어 있지 않습니다.

`ComPortWidget.exe` contains no third-party code. It is a single file with the app's own code, and the build output is that one exe. The program uses the .NET Framework 4.8 that is part of Windows; none of it is included in this zip.

NuGet 패키지는 앱에 들어 있지 않습니다. / No NuGet package is part of the app.

## 테스트에만 쓰는 구성 요소 / Test-only

다음은 단위 테스트 프로젝트에서만 참조하며 실행 파일과 릴리스 zip에는 들어 있지 않습니다.

The following are referenced only by the unit test projects. They are not in the executable or the release zip.

| 구성 요소 / Component | 라이선스 / License |
|---|---|
| xUnit (xunit, xunit.runner.visualstudio) | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
