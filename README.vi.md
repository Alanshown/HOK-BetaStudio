<div align="center">
  <img src="docs/images/app-icon.png" width="112" alt="Biểu tượng HOK BetaStudio">
  <h1>HOK BetaStudio</h1>
  <p>Không gian làm việc trên máy tính cho tài nguyên Honor of Kings.</p>
  <p>
    <img alt="Phiên bản 1.2" src="https://img.shields.io/badge/version-1.2-147d72?style=flat-square">
    <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows_x64-357b9b?style=flat-square">
    <img alt="C# và React" src="https://img.shields.io/badge/C%23_%2B_React-desktop-667672?style=flat-square">
    <img alt="Ba ngôn ngữ giao diện" src="https://img.shields.io/badge/UI-EN_%C2%B7_%E4%B8%AD%E6%96%87_%C2%B7_VI-147d72?style=flat-square">
    <img alt="Đóng gói lại thử nghiệm" src="https://img.shields.io/badge/DB_rebuilding-Beta-b68a42?style=flat-square">
  </p>
</div>

<!-- README-I18N:START -->

[English](./README.md) | [简体中文](./README.zh.md) | **Tiếng Việt**

<!-- README-I18N:END -->

Duyệt gói DB theo tướng và trang phục, kiểm tra dữ liệu Unity lẫn dữ liệu khác, xem mô hình, nghe âm thanh và xuất hàng loạt. Backend C# tách việc phân tích, xem trước và xuất khỏi tiến trình giao diện.

**[Ảnh chụp](#export) · [Bản phát hành](https://github.com/Alanshown/HOK-BetaStudio/releases) · [Báo lỗi](https://github.com/Alanshown/HOK-BetaStudio/issues)**

![Danh mục tướng trong ứng dụng desktop](docs/images/01-catalog.png)

## Mục lục

- [Không gian làm việc và quét tệp](#workspace)
- [Xem trước và xuất](#export)
- [Thay thế và đóng gói lại — Beta](#beta)
- [Cấu trúc dự án](#structure)
- [Biên dịch và đóng gói](#build)
- [Tải xuống và sử dụng](#download)
- [Kiểm chứng](#verification)
- [Ghi công và phân phối](#attribution)

<a id="workspace"></a>
## Không gian làm việc và quét tệp

- **Duyệt trước khi nhập.** Danh mục cục bộ hiển thị tướng và trang phục khi khởi động. Thẻ xuất hiện theo hiệu ứng domino chéo và tôn trọng tùy chọn giảm chuyển động của hệ thống.
- **Mở tệp, thư mục hoặc kéo thả.** Quét đệ quy các thư mục con thông thường, không giới hạn độ sâu cố định. Bỏ qua junction và liên kết tượng trưng để tránh vòng lặp; báo lỗi khi không có quyền truy cập.
- **Đối chiếu theo tên.** `3200010504.db` → trang phục `10504` → tướng `105`. Hậu tố phân mảnh như `_0` được xử lý riêng. DB không có phần mở rộng phải vượt qua kiểm tra chữ ký.
- **Trang phục mặc định.** ID kết thúc bằng `00`, như `10500`, dùng ảnh tướng và nhãn trang phục mặc định. ID tướng hoặc trang phục chưa biết vẫn hiển thị với ảnh thay thế ổn định theo ID.
- **Phạm vi đã nhập.** Sau khi nhập, chỉ hiển thị tướng và trang phục tương ứng cùng tên DB nguồn. Tìm theo tên hoặc ID, lọc loại tài nguyên, phân trang và chọn hàng loạt.
- **Đổi không gian làm việc.** Lần nhập mới xóa bộ nhớ đệm và các thay thế đang chờ của lần trước. Nút chổi có hiệu ứng xóa bộ nhớ đệm và trở về danh mục ban đầu, không xóa tệp gốc.
- **Ba ngôn ngữ giao diện:** tiếng Anh, tiếng Trung giản thể và tiếng Việt. Giữ nguyên tên tài nguyên và tên trong danh mục.

![Hai gói trang phục thuộc tướng 105](docs/images/02-skins.png)

<a id="export"></a>
## Xem trước và xuất

| Mô-đun | Xem trước / đầu ra |
|---|---|
| Texture và sprite | Xem ảnh, bật/tắt kênh và thu phóng; PNG, TGA, BMP, JPG, dữ liệu thô |
| Mesh | Xoay/thu phóng 3D, tự xoay và khung dây; OBJ, JSON, dữ liệu thô |
| GameObject / Animator | Xuất FBX khi có thư viện native và tham chiếu cần thiết |
| AnimationClip | Unity YAML `.anim`, JSON, dữ liệu thô; không chuyển mọi hoạt ảnh độc lập sang FBX |
| AudioClip / âm thanh Wwise | Phát cục bộ; tệp gốc, WAV và MP3 khi bộ giải mã hỗ trợ |
| WwiseBank | Cây phương tiện nhúng và trình phát; BNK gốc, ZIP chứa WEM gốc, ZIP chứa MP3 đã chuyển đổi |
| Văn bản, shader, phông chữ, video | Nội dung gốc hoặc văn bản phù hợp; JSON và dữ liệu thô khi được hỗ trợ |
| Mục khác trong gói | Liệt kê cả dữ liệu không phải Unity; có thể giữ dữ liệu chưa nhận dạng hoặc chưa giải mã ở dạng thô |

Hộp xem trước làm mờ nền, có nút trước/sau trong cùng loại tài nguyên. Điều hướng BNK chỉ chuyển trong bank hiện tại. Thao tác của người dùng dừng tự xoay mô hình.

`WwiseAudio` và `WwiseBank` là loại phương tiện/vùng chứa, **không cố định tương ứng với “âm thanh trò chuyện” và “giọng kỹ năng”**. Bank có thể chứa phương tiện nhúng, tham chiếu ngoài hoặc cả hai. Chỉ dữ liệu thực sự có mặt mới trích xuất được. Xuất WEM giữ nguyên byte gốc; chưa hỗ trợ mã hóa âm thanh bất kỳ thành WEM.

Xuất hàng loạt ghi kết quả từng mục và tránh trùng tên đầu ra. Phân tích, xem trước và xuất dùng các tiến trình worker riêng có quản lý. Lỗi codec và mục không hỗ trợ được thông báo, không bị coi là chuyển đổi thành công.

![Không gian làm việc với tài nguyên](docs/images/03-assets.png)
![Xem mô hình](docs/images/05-model.png)
![BNK thật chứa sáu mục âm thanh nhúng](docs/images/06-audio-bank.png)
![Tùy chọn xuất bank](docs/images/07-export.png)

<a id="beta"></a>
## Thay thế và đóng gói lại — Beta

> [!WARNING]
> **Tính năng thử nghiệm, không khuyến nghị dùng thông thường. Chưa xác minh tương thích trong game.** Di chuột hoặc đặt tiêu điểm lên nhãn Beta để đọc cảnh báo. Đọc lại thành công bằng công cụ không chứng minh game sẽ chấp nhận gói.

1. Chọn đúng một tài nguyên được hỗ trợ để hiện **Thay thế**.
2. Chọn tệp nhị phân gốc tương thích. Mục thay thế thành công được đánh dấu; có thể thay tiếp mục khác hoặc cùng mục đó.
3. Có ít nhất một thay thế đang chờ thì nút **Đóng gói lại** xuất hiện.
4. Chọn thư mục đầu ra riêng. Gói mới chứa nội dung thay thế và toàn bộ tệp gốc còn lại; không ghi đè gói nguồn.

Hiện chỉ thay thế nhị phân có cùng độ dài và vừa ô nén sẵn có; không nhập PNG/OBJ/MP3 tùy ý, thêm đối tượng bất kỳ hay đổi kích thước đối tượng. Chưa hỗ trợ thay thế tổng quát đối tượng không rõ cấu trúc và phương tiện bên trong BNK. Xem trước/xuất thông thường vẫn dùng dữ liệu gốc cho tới khi nhập lại gói đã xây dựng.

Khối sửa đổi có thể dùng Zstd với phần đệm khung có thể bỏ qua. Trường kiểm tra QTS chưa rõ và ánh xạ `record.bytes` **không được tính lại**. Tham chiếu ngoài chỉ có GUID chưa được giải quyết đầy đủ. Sao chép không sửa đổi, bảo toàn byte không đổi và đọc lại mục chỉ là kiểm tra nền, không bảo đảm tương thích game.

<a id="structure"></a>
## Cấu trúc dự án

```text
HOK-BetaStudio/
├── backend/
│   ├── Hok.Desktop/          # C# WPF + WebView2 host
│   ├── Hok.Worker/           # parsing, preview, export
│   ├── Hok.Contracts/        # filename identity and recursive scanner
│   ├── Hok.Legacy/           # adapters to Studio-HoK readers
│   ├── Hok.Rebuild/          # experimental replacement and rebuild
│   └── *.Tests/             # executable regression suites
├── frontend/                # React + TypeScript + Three.js
├── vendor/Studio-HoK/        # upstream C# source and attribution
├── assets/                  # UI assets, catalog and license notices
├── docs/                    # documentation and real screenshots
├── tooling/                 # build, packaging and verification scripts
├── README.md
├── README.zh.md
└── README.vi.md
```

Không đưa bộ nhớ đệm phụ thuộc, gói runtime native, DB game, tài nguyên xuất và sản phẩm biên dịch vào quản lý nguồn. Kho ảnh game không được phân phối lại như gói tài nguyên mã nguồn; cần tự cung cấp hình ảnh được phép sử dụng để hiển thị danh mục đầy đủ.

<a id="build"></a>
## Biên dịch và đóng gói

Ứng dụng desktop cần Windows x64, .NET 10 SDK, Node.js kèm npm và Microsoft Edge WebView2 Runtime. Đóng gói bộ cài cần thêm NSIS. Phiên bản phụ thuộc JavaScript được ghi trong lockfile.

```powershell
git clone https://github.com/Alanshown/HOK-BetaStudio.git
cd HOK-BetaStudio
npm ci --prefix frontend
npm ci --prefix tooling
dotnet build backend/Hok.Desktop/Hok.Desktop.csproj -c Release
dotnet build backend/Hok.Worker/Hok.Worker.csproj -c Release
dotnet run --project backend/Hok.Contracts.Tests -c Release -- .
```

Biên dịch mã nguồn và tạo bản phân phối desktop đầy đủ là hai bước riêng. Xem [đầu vào xây dựng](docs/BUILD.md) về thư viện FBX/FMOD/codec native, công cụ âm thanh, hình ảnh và bố cục đóng gói. Không chép tệp nhị phân bên thứ ba vào Git.

```powershell
powershell -ExecutionPolicy Bypass -File tooling/build-desktop.ps1 -OutputDirectory build/HOK-BetaStudio-1.2-win-x64
powershell -ExecutionPolicy Bypass -File tooling/package-desktop.ps1 -BuildDirectory build/HOK-BetaStudio-1.2-win-x64
```

Trang trình diễn được duy trì riêng và không nằm trong kho này. Các hình ở trên là ảnh chụp ứng dụng desktop thực tế.

<a id="download"></a>
## Tải xuống và sử dụng

Bản tải công khai sẽ có trên [trang Releases](https://github.com/Alanshown/HOK-BetaStudio/releases) sau kiểm tra phân phối. Khách truy cập thông thường không thấy bản nháp riêng.

Khi được phát hành, giải nén **toàn bộ ZIP portable** rồi chạy `HOK BetaStudio.exe`, hoặc dùng bộ cài Windows. Không tách EXE khỏi các thư mục `worker`, `ui`, `assets`. Gói .NET self-contained vẫn cần WebView2 Runtime. Hiện các gói chưa được ký mã.

Mở thư mục chứa đầy đủ DB chính, các mảnh và tệp liên quan. Chọn tướng, trang phục, tài nguyên rồi xem trước hoặc xuất. Luôn sao lưu gói gốc, đặc biệt khi thử đóng gói lại Beta.

<a id="verification"></a>
## Kiểm chứng

Ảnh chụp từ ứng dụng desktop đã đóng gói, dùng mẫu cục bộ `3200010500` và `3200010504`, không phải dữ liệu giao diện bịa đặt. Không công khai mẫu DB, âm thanh hay mô hình được trích xuất.

| Kiểm tra | Kết quả quan sát |
|---|---|
| Nhận dạng và quét đệ quy | 27 kiểm tra; tìm đủ 14 DB/mảnh qua 12 cấp thư mục |
| Đóng gói lại không sửa đổi | 16 kiểm tra trên hai gói; đầu ra nền giống từng byte |
| Worker thay thế | 15 kiểm tra, gồm thay lại cùng mục và bảo toàn byte không đổi |
| Giao diện thay thế | 11 kiểm tra, gồm chọn đơn và cảnh báo Beta đa ngôn ngữ |
| Worker / desktop BNK | 8 / 9 kiểm tra; xác minh sáu mục phương tiện nhúng trong bank mẫu |

Đây là kết quả hồi quy trong phạm vi mẫu, không khẳng định hỗ trợ mọi phiên bản game hay codec. Một số script tích hợp cần thư mục mẫu riêng và báo cáo sinh cục bộ. Đọc [đầu vào xây dựng](docs/BUILD.md) trước khi chạy.

<a id="attribution"></a>
## Ghi công và phân phối

Dựa trên bộ đọc Studio-HoK / AssetStudio. Giữ [thông báo MIT thượng nguồn](vendor/Studio-HoK/LICENSE). Thành phần bên thứ ba và tài nguyên UI giữ điều khoản riêng; xem [thông báo tài nguyên](assets/licenses/SOURCES.txt) và [thông báo âm thanh](assets/licenses/media/NOTICE.txt).

Biểu tượng huy hiệu kèm chữ HOK là ý tưởng tạo cho dự án, không phải logo game chính thức. Tên Honor of Kings, nhân vật, hình ảnh và nhãn hiệu thuộc chủ sở hữu tương ứng; đây là dự án độc lập. Ảnh chụp minh họa công cụ không cấp quyền phân phối lại nội dung game trong ảnh.

Chỉ xử lý tài nguyên bạn được phép sử dụng. Bản nhị phân công khai vẫn cần kiểm tra quyền hình ảnh, quyền phân phối FBX/FMOD/codec native và nghĩa vụ cung cấp mã nguồn tương ứng đầy đủ của FFmpeg đi kèm. Kho này không cấp giấy phép chung cho các thành phần đó.
