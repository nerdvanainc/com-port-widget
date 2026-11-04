# COM Port Widget 고지

한국어(이 문서) · English: [NOTICE.en.md](NOTICE.en.md)

제작·유지보수: © 2026 Nerdvana Inc. (㈜너드바나), https://nerdvana.co.kr

사용 범위, 책임, 상표에 관한 고지를 모은 문서입니다. 라이선스 본문은 [LICENSE](LICENSE)(MIT)입니다.

## 보증 없음

이 소프트웨어는 있는 그대로 제공되며, 상품성이나 특정 목적에 대한 적합성을 포함해 어떤 보증도 하지 않습니다. 사용으로 생긴 손해에 대해 저작권자는 책임지지 않습니다. 자세한 문구는 [LICENSE](LICENSE)를 따릅니다.

## COM 포트 정리 기능의 영향과 사용자 책임

일반 목록 화면은 장치 정보를 읽기만 합니다. "COM 포트 정리"만 시스템을 바꿉니다.

- 선택한 항목의 장치 기록을 `pnputil /remove-device`로 지우고, 해당 COM 번호의 예약(ComDB)을 해제합니다. 기록 없이 예약만 남은 번호를 선택하면 예약만 해제합니다.
- 지운 장치 기록은 되돌릴 수 없습니다. 장비를 다시 꽂으면 Windows가 새 기록을 만들고, 그때 비어 있는 번호를 받습니다. 이전과 같은 번호를 받는다는 보장은 없습니다.
- 드라이버 파일은 지우지 않습니다.
- 실행 전에 ComDB 원본과 삭제 대상 정보를 `%APPDATA%\ComPortWidget\backup-<시각>.json`에 저장합니다. 이 파일은 조사용이며, 앱에 되돌리기 기능은 없습니다. 파일에는 장치 인스턴스 ID(USB 시리얼 포함)가 들어 있으므로 다른 사람에게 보낼 때 유의하십시오.
- 삭제 대상은 사용자가 목록에서 직접 확인하고 선택한 항목입니다. 지워질 번호를 쓰던 프로그램이나 설정(터미널 프로그램의 포트 설정, 스크립트에 적은 COM 번호 등)이 영향을 받는지는 사용자가 판단해야 합니다. 정리 결과에 대한 책임은 사용자에게 있습니다.

### 안전장치와 남아 있는 한계

정리는 연결된 장비를 건드리지 않도록 만들었습니다. 실행 직전에 항목별로 연결 여부를 다시 확인하고, 관리자 권한으로 도는 쪽에서 대상이 미연결 Ports 클래스 기록인지, 예약만 남은 번호가 어느 장치에서도 쓰이지 않는지를 다시 검증합니다. 그래도 다음 한계가 남습니다.

1. 마지막 연결 확인과 `pnputil` 호출 사이(측정하지 않았지만 1초 안팎의 짧은 시간)에 같은 장치가 꽂히면 그 기록이 지워질 수 있습니다. 정리하는 동안에는 장비를 꽂거나 뽑지 마십시오.
2. 정리 계획 파일(`reset-plan.json`)과 결과 파일(`reset-result.json`)은 사용자가 쓸 수 있는 폴더(`%APPDATA%\ComPortWidget`)에 놓입니다. 관리자 권한 쪽은 파일 이름, 같은 폴더 여부, 경로에 링크(재분석 지점)가 있는지를 검사하지만, 검사한 뒤 파일이나 폴더를 바꿔치기하는 것(예: 폴더를 링크로 교체)까지는 막지 못하고, 이것도 남아 있는 경쟁의 일부입니다. 이미 사용자 권한으로 실행 중인 다른 프로세스가 UAC 승인 시점에 맞춰 파일을 바꿀 수 있습니다. 계획 내용은 승격된 쪽에서 다시 검증하므로 연결된 장치나 Ports 클래스 밖의 기록은 지울 수 없지만, 사용자가 고르지 않은 미연결 기록을 지우게 하거나, 예약만 남은 번호를 해제하게 할 수 있습니다. 결과 파일과 백업 파일도 같은 폴더에 쓰이므로 같은 방식으로 바꿔치기하거나 선점할 수 있습니다(결과 파일에 거짓 내용을 쓰거나 백업 파일 쓰기를 방해하는 경우).
3. 승격은 Windows PowerShell(`powershell.exe`, 5.1)이 맡습니다. 이 PowerShell은 사용자 수준 환경 변수에 지정된 .NET 프로파일러 설정(`COR_PROFILER` 등)을 따릅니다. 이미 사용자 권한으로 실행 중인 악성 프로세스가 그 설정을 심어 두었다면 승격된 PowerShell에서 코드가 실행될 수 있습니다. 앱은 PowerShell이 띄우는 자식 프로세스의 환경 변수만 정리할 수 있습니다.
4. 정리가 끝나기 전에 전원이 꺼지거나 폴더를 지우지 못하면 `C:\Program Files\ComPortWidget-Cleanup-<GUID>` 폴더가 남을 수 있습니다. 앱은 이를 알려 주지 않습니다. 남아 있으면 직접 지워도 됩니다.

PC가 이미 악성코드에 감염되어 있다면, 위 한계 때문에 이 기능을 쓰지 않는 편이 안전합니다.

## 네트워크와 데이터

- 이 프로그램은 네트워크를 쓰지 않습니다. 텔레메트리, 업데이트 확인, 오류 보고가 없고, 이 앱의 코드는 네트워크 호출을 하지 않습니다. (.NET 런타임에는 `System.Net` 같은 네트워크 라이브러리가 들어 있지만 앱이 사용하지 않습니다.)
- 메모, 직접 관측한 연결·해제 시각, 설정은 `%APPDATA%\ComPortWidget\data.json`에만 저장됩니다. 다른 PC로 동기화되지 않습니다. 처리되지 않은 오류가 나면 예외 내용을 같은 폴더의 `crash.log`에 덧붙이며, 어디에도 보내지 않습니다.
- Windows 시작 시 자동 실행을 켜면 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`에 값을 하나 씁니다.
- COM 포트를 열지 않습니다. 장치 정보는 레지스트리와 장치 관리 API에서 읽습니다.

## 서명되지 않은 실행 파일

실행 파일에는 코드 서명이 없습니다. 처음 실행할 때 SmartScreen 경고가 뜰 수 있고, 정리 기능의 UAC 창에는 앱 이름 대신 "Windows PowerShell"이 표시됩니다. 받은 파일이 맞는지는 릴리스의 `SHA256SUMS.txt`와 비교해 확인하십시오.

## 제품명과 상표

이 소프트웨어는 아래 회사나 제품과 제휴, 후원, 보증 관계가 없습니다. 이름은 장치를 설명하는 용도로만 나옵니다. 목록에 나오는 제조사 이름은 USB 장치가 알리는 VID(제조사 번호)에 대응하는 칩 제조사이며, 장비를 실제로 만든 회사와 다를 수 있습니다.

FTDI는 Future Technology Devices International Ltd.의 상표입니다. Silicon Labs는 Silicon Laboratories Inc.의 상표입니다. Espressif는 Espressif Systems의 상표입니다. WCH는 Nanjing Qinheng Microelectronics Co., Ltd.의 상표입니다. Adafruit는 Adafruit Industries의 상표입니다. STMicroelectronics는 STMicroelectronics의 상표입니다. Prolific은 Prolific Technology Inc.의 상표입니다. Arduino는 Arduino S.r.l.의 상표입니다. SEGGER는 SEGGER Microcontroller GmbH의 상표입니다. Arm은 Arm Limited의 상표입니다. Microsoft, Windows, PowerShell은 Microsoft Corporation의 상표입니다. 그 밖의 이름은 각 소유자의 상표입니다.

## 서드파티 구성 요소

실행 파일에 포함된 구성 요소는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 정리했습니다.
