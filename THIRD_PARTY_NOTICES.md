# 서드파티 고지 / Third-party notices

## 실행 파일에 포함되는 구성 요소 / Included in the executable

`ComPortWidget.exe`는 런타임을 포함한 단일 파일입니다. 앱 코드 외에 다음이 들어 있습니다.

`ComPortWidget.exe` is a self-contained single file. Besides the app code it contains:

| 구성 요소 / Component | 라이선스 / License | 출처 / Source |
|---|---|---|
| .NET 런타임과 기본 클래스 라이브러리 / .NET Runtime and base class libraries | MIT | https://github.com/dotnet/runtime |
| WPF (Windows Presentation Foundation) | MIT | https://github.com/dotnet/wpf |
| Windows Forms (트레이 아이콘 `NotifyIcon`과 `DeviceWatcher`의 타이머에 사용 / used for the tray icon `NotifyIcon` and the timers in `DeviceWatcher`) | MIT | https://github.com/dotnet/winforms |

.NET 런타임은 자체 서드파티 구성 요소(압축 라이브러리 등)를 포함하며, 그 고지는 .NET 저장소의 `THIRD-PARTY-NOTICES.TXT`에 있습니다: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

The .NET runtime carries its own third-party components (compression libraries and others). Their notices are in `THIRD-PARTY-NOTICES.TXT` in the .NET repository (link above).

위 구성 요소 외에 NuGet 패키지는 앱에 들어 있지 않습니다. / No NuGet package is part of the app itself.

## 테스트에만 쓰는 구성 요소 / Test-only

다음은 단위 테스트 프로젝트에서만 참조하며 실행 파일과 릴리스 zip에는 들어 있지 않습니다.

The following are referenced only by the unit test projects. They are not in the executable or the release zip.

| 구성 요소 / Component | 라이선스 / License |
|---|---|
| xUnit (xunit, xunit.runner.visualstudio) | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |

## MIT 라이선스 / MIT License

.NET, WPF, Windows Forms의 저작권은 .NET Foundation and Contributors에 있습니다. / Copyright (c) .NET Foundation and Contributors.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
