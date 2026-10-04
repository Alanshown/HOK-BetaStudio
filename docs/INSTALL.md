# Install HOK BetaStudio

[English](#english) · [简体中文](#简体中文) · [Tiếng Việt](#tiếng-việt)

Version **1.3** is published. Download its actual Release attachments and verify the matching checksums; never rename an older package to match a newer version.

**1.3** 已发布。请下载对应发行附件并核对校验值，不要把旧包改名后套用新版本校验值。

Phiên bản **1.3** đã phát hành. Tải đúng tệp đính kèm và kiểm tra mã tương ứng; không đổi tên gói cũ để dùng mã kiểm tra mới.

## English

1. Download the **Setup EXE** or **portable ZIP** from [Release page](https://github.com/Alanshown/HOK-BetaStudio/releases/tag/1.3). GitHub's automatic **Source code** archives are for developers, not runnable applications.
2. Use Windows x64 with [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download). Choose the Evergreen Bootstrapper for online installation or Evergreen Standalone Installer **x64** for offline installation. HOK Setup does not install WebView2 automatically. The distributed self-contained app does not require the .NET SDK.
3. Check the downloaded file using the [SHA-256 commands below](#verify-downloads). These checksums describe the two complete release files, not the earlier transfer volumes.
4. Run Setup to install for the current user, or extract the **entire ZIP** into a writable folder and run `HOK BetaStudio.exe`. Keep `worker`, `ui` and `assets` together with the EXE; do not run it from inside the ZIP.
5. Open the folder containing the main DB, its shards and associated files. Select a hero, skin and asset to preview or export. Keep backups before trying experimental rebuilding.

**Troubleshooting**

- Missing WebView2 or an empty window: install/repair the official x64 Evergreen Runtime, fully extract the app and restart it.
- Missing worker or decoder: confirm that the whole package was extracted. Review any security-software report rather than disabling protection or blindly restoring quarantined files.
- Unsigned-binary warning: packages are currently unsigned. Verify the repository, filename and hash; a matching hash confirms file integrity, not that an application is risk-free. Do not disable Windows security protections.
- Import/preview/export failure: include the version, Windows version, reproduction steps, DB/shard filenames and relevant redacted log excerpt in a [bug report](https://github.com/Alanshown/HOK-BetaStudio/issues/new?template=bug_report.yml). Logs are under `%LOCALAPPDATA%\HokBetaStudio` (`worker.log`, `preview.log`, `export.log` when present). Remove personal paths, keys and other private data; do not upload game DBs or extracted content without permission.

## 简体中文

1. 从 [发行页面](https://github.com/Alanshown/HOK-BetaStudio/releases/tag/1.3)下载 **Setup EXE** 或**便携 ZIP**。GitHub 自动生成的 **Source code** 是开发用源码，不是可运行程序。
2. 使用 Windows x64，并安装 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download)。联网时选 Evergreen Bootstrapper，离线安装选 Evergreen Standalone Installer 的 **x64** 版本。HOK 安装程序不会自动安装 WebView2；发行包自带 .NET 运行时，不需要 .NET SDK。
3. 按[下方 SHA-256 命令](#verify-downloads)核对下载文件。这里校验的是两个完整发行文件，不是之前的传输分卷。
4. 运行 Setup 安装到当前用户，或将**整个 ZIP**解压到可写目录，运行 `HOK BetaStudio.exe`。不要拆开 EXE 和 `worker`、`ui`、`assets`，也不要直接在 ZIP 内运行程序。
5. 打开含主 DB、分片及关联文件的完整目录，选择英雄、皮肤和资产进行预览／导出。试用实验性重建前保留原包备份。

**故障排查**

- 提示缺少 WebView2 或窗口空白：安装／修复官方 x64 Evergreen Runtime，完整解压程序后重新启动。
- 缺少 worker 或解码器：核对是否完整解压；查看安全软件具体报告，不要关闭防护或盲目恢复隔离文件。
- 未签名警告：当前程序未签名。核对仓库来源、文件名与校验值；哈希相符只说明文件一致，不是无风险保证。不要关闭 Windows 安全保护。
- 导入／预览／导出失败：在[错误反馈](https://github.com/Alanshown/HOK-BetaStudio/issues/new?template=bug_report.yml)中提供程序版本、Windows 版本、复现步骤、DB／分片文件名和脱敏日志片段。日志位于 `%LOCALAPPDATA%\HokBetaStudio`，包括存在时的 `worker.log`、`preview.log`、`export.log`。请删除个人路径、密钥等隐私信息，未经许可不要上传游戏 DB 或提取内容。

## Tiếng Việt

1. Tải **Setup EXE** hoặc **ZIP portable** từ [trang Release](https://github.com/Alanshown/HOK-BetaStudio/releases/tag/1.3). Tệp **Source code** tự tạo của GitHub dành cho lập trình viên, không phải ứng dụng chạy được.
2. Dùng Windows x64 và cài [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download). Chọn Evergreen Bootstrapper khi có mạng hoặc Evergreen Standalone Installer **x64** để cài ngoại tuyến. HOK Setup không tự cài WebView2. Ứng dụng self-contained không cần .NET SDK.
3. Kiểm tra tệp bằng [lệnh SHA-256 bên dưới](#verify-downloads). Mã kiểm tra áp dụng cho hai tệp phát hành hoàn chỉnh, không phải các phần chia để truyền tệp trước đây.
4. Chạy Setup để cài cho người dùng hiện tại, hoặc giải nén **toàn bộ ZIP** vào thư mục có quyền ghi rồi chạy `HOK BetaStudio.exe`. Giữ các thư mục `worker`, `ui`, `assets` cùng EXE; không chạy ứng dụng ngay trong ZIP.
5. Mở thư mục có DB chính, các mảnh và tệp liên quan. Chọn tướng, trang phục và tài nguyên để xem hoặc xuất. Sao lưu gói gốc trước khi thử đóng gói lại Beta.

**Khắc phục sự cố**

- Thiếu WebView2 hoặc cửa sổ trống: cài/sửa Evergreen Runtime x64 chính thức, giải nén đầy đủ rồi khởi động lại.
- Thiếu worker hoặc bộ giải mã: kiểm tra đã giải nén toàn bộ gói. Đọc báo cáo phần mềm bảo mật; không tắt bảo vệ hoặc khôi phục tệp cách ly khi chưa kiểm tra.
- Cảnh báo chưa ký mã: các gói hiện chưa được ký. Kiểm tra nguồn, tên tệp và SHA-256; mã khớp chỉ xác nhận tính toàn vẹn, không bảo đảm ứng dụng không có rủi ro. Không tắt bảo vệ Windows.
- Lỗi nhập/xem/xuất: gửi phiên bản ứng dụng, Windows, bước tái hiện, tên DB/mảnh và đoạn log đã ẩn thông tin riêng trong [báo lỗi](https://github.com/Alanshown/HOK-BetaStudio/issues/new?template=bug_report.yml). Log nằm tại `%LOCALAPPDATA%\HokBetaStudio` (`worker.log`, `preview.log`, `export.log` nếu có). Xóa đường dẫn cá nhân và khóa; không đăng DB game hoặc nội dung trích xuất khi chưa được phép.

## Verify downloads

The commands and sizes below describe the published **1.3 packages**, whose GitHub asset hashes match the locally verified files. Download the matching version. Expected 1.3 hashes are in [SHA256SUMS.txt](releases/1.3/SHA256SUMS.txt); historical 1.2 hashes remain [available separately](releases/1.2/SHA256SUMS.txt). Run PowerShell in the download directory and compare the `Hash` values (case does not matter):

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\HOK-BetaStudio-1.3-win-x64-portable.zip'
Get-FileHash -Algorithm SHA256 -LiteralPath '.\HOK-BetaStudio-1.3-win-x64-setup.exe'
```

| File | Exact size (bytes) |
| --- | ---: |
| `HOK-BetaStudio-1.3-win-x64-portable.zip` | 212023978 |
| `HOK-BetaStudio-1.3-win-x64-setup.exe` | 124039755 |

If a hash differs, do not run the file. Re-download from the release page and check again.
