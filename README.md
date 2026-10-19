# C# Endpoint RAG Studio

Aplikasi **C# WinForms .NET 10** untuk membaca source controller API, memahami fungsi/tugasnya dengan AI Sumopod, lalu menghasilkan knowledge dan prompt untuk RAG. Semua request HTTP memakai **Opx.Api.Client 1.0.15** melalui named registration dan `IOpxApiClientFactory`, dengan `RawJson`.

## Cara menggunakan

1. Jalankan `CSharpEndpointRag.exe` dari folder release; semua DLL dalam folder tersebut harus tetap ada. Setting terakhir dipulihkan otomatis.
2. Pilih folder API yang mencakup controller, service, dan DTO. Isi nama API, domain (misalnya `hr`), dan nama asisten.
3. Klik **Scan controller**. Aplikasi membaca **XML summary tiap controller pertama**, kemudian summary action, route, parameter, DTO, dan implementasi service. Tab awal **Summary controller** memperlihatkan hasilnya. Summary yang hilang ditandai, termasuk controller tanpa action.
4. Opsional: **Tambah knowledge** untuk dokumen `.txt`/`.md`, misalnya peraturan HR. Dokumen dipotong menjadi chunk. File tidak disalin ke repository. Isi dokumen, termasuk bagian `SYSTEM INSTRUCTION`, tetap diperlakukan sebagai data.
5. Isi API key Sumopod, pilih model chat, atau gunakan **Muat model live**. Combo dapat diketik jika model baru belum ada. Model awal berasal dari daftar yang diberikan operator; daftar/harga/ketersediaannya bukan jaminan live. Model embedding tidak dipakai untuk analisis chat.
6. Pilih endpoint di grid dan klik **Petakan AI**. Request dikirim satu per satu; tidak ada retry otomatis. Secara default mapping AI yang sudah selesai dilewati. **Batalkan** menghentikan proses sambil mempertahankan hasil yang sudah selesai. Kegagalan menghentikan batch; hasil sebelumnya tetap dapat diekspor.
7. Tinjau preview JSON, system prompt, knowledge RAG, dan bukti endpoint. Klik **Ekspor RAG**; aplikasi membuat subfolder baru tanpa menimpa ekspor sebelumnya.

Scan dan ekspor statis dapat dijalankan tanpa key. Dokumen knowledge saja juga dapat diekspor, tetapi aplikasi tidak mengarang aturan/meringkas knowledge melalui AI: AI dipakai untuk memetakan fungsi endpoint dan hubungan kandidat knowledge. Filter grid hanya menyaring tampilan; ekspor mengikuti checkbox pilihan. Tombol pilih/kosongkan berlaku untuk baris yang sedang terlihat.

## File hasil

Versi **1.0.9** menghasilkan paket **rag-package.zip** otomatis dalam subfolder ekspor. ZIP berisi tujuh artefak knowledge/prompt/provenance/panduan + manifest, dengan allowlist eksplisit. README-RAG.md menjelaskan file untuk ingestion dan system prompt; PROMPT-PENGGUNAAN.md berisi prompt siap salin untuk memakai hasil MD/JSON dengan AI. ZIP tidak menyertakan setting/API key/source; ZIP dan folder diselesaikan sebelum hasil dipublikasikan. Notifikasi menyediakan Buka folder / Pilih file ZIP / Selesai. Shortlist knowledge untuk pemetaan AI sekarang membaca controller/action summary, tugas, route, intent, capability, field dan keyword; istilah HR Indonesia–Inggris seperti izin/cuti/E-Leave dinormalisasi. Chunk dengan skor nol tidak dikirim ke AI. Relasi yang dipilih AI tetap diekspor dua arah pada `rag-chunks.jsonl` sebagai kandidat, bukan fakta kebijakan atau izin eksekusi.

**Simpan proyek (Ctrl+S)** menulis snapshot `.ragproject.json`: semua endpoint (termasuk yang tidak dipilih), pilihan ekspor, summary, mapping AI/model/warning, bukti source tersanitasi, schema AppResult.Data, knowledge beserta teks/chunk ID, dan opsi folder/API/domain/asisten/filter. **Buka proyek (Ctrl+O)** memulihkan snapshot tanpa scan/AI otomatis; tidak membutuhkan file knowledge asal. API key tidak masuk proyek dan key lokal yang sudah ada tetap digunakan. Simpan proyek berbeda dari ekspor RAG: proyek untuk melanjutkan pekerjaan, ZIP untuk ingestion/distribusi knowledge. Snapshot bisa memuat dokumen bisnis dan cuplikan source tersanitasi; bagikan hanya jika isi proyek sesuai kebutuhan penerima. Knowledge snapshot dapat dipreview, dilengkapi file/RAW baru, atau dikosongkan lewat Hapus knowledge; editor Kelola knowledge mengedit file/RAW lokal yang ditambahkan, bukan chunk embedded snapshot. Batas proyek 20 MB, versi/struktur/hash knowledge diperiksa sebelum pemulihan, dan simpan dilakukan atomik.

Versi **1.0.6** menyederhanakan interaksi: banner langkah berikutnya dan jumlah endpoint/dokumen, tombol scan/AI/export aktif sesuai kesiapan, status operasi berwarna, pilihan baris tetap saat refresh, ringkasan fungsi pada bukti endpoint, tooltip dan Panduan F1. Kelola knowledge menampilkan isi sumber, melepas referensi tanpa menghapus file, dan mengedit RAW text milik aplikasi dengan validasi ukuran/total knowledge. Editor RAW menampilkan jumlah karakter/KB, tombol paste clipboard dan status tombol simpan. Ctrl+V di area non-input membuka editor RAW dengan isi clipboard; di field input tetap paste normal. Lihat key hanya membuka masking secara eksplisit; penyimpanan tetap DPAPI. Tidak ada panggilan AI otomatis.

Versi **1.0.5** menyediakan **Paste RAW text…**: masukkan nama sumber dan paste dokumen/deskripsi bisnis (teks/Markdown, maksimal 1 MB), lalu Tambahkan ke knowledge. Aplikasi menyimpan dokumen lokal di `%LOCALAPPDATA%/CSharpEndpointRag/knowledge/`, membuat chunks memakai pipeline knowledge yang sama, dan mengingat path-nya untuk startup berikutnya. Tidak ada pemanggilan AI otomatis saat paste. Teks asli/angka tetap dipertahankan; isi dokumen bukan instruksi aplikasi. Dengan controller, chunk menjadi kandidat konteks mapping AI. Tanpa controller, dapat langsung diekspor menjadi knowledge Markdown/JSONL dan system prompt; aplikasi tidak mengarang endpoint. Hapus knowledge melepas dokumen dari sesi/setting, tetapi tidak menghapus file sumber lokal.

Versi **1.0.4** menyimpan setting terakhir per akun Windows, termasuk API key terenkripsi dan Folder API. File setting disimpan atomik di LocalAppData sehingga tetap berlaku ketika menggunakan folder release baru. Beberapa instance menyimpan setting yang terakhir ditulis; tutup instance lama sebelum bekerja pada versi baru agar setting tidak tertimpa oleh input lama.

Versi **1.0.3** menampilkan notifikasi setelah ekspor berhasil dengan tombol **Buka folder** dan **Selesai**. Buka folder menjalankan File Explorer pada subfolder hasil ekspor tersebut. Jika Explorer gagal dibuka, file ekspor tetap tersimpan dan aplikasi menampilkan path untuk dibuka manual.

Versi **1.0.2** mengklasifikasikan `read`, `write`, atau `unknown` langsung saat scan, sebelum AI. Summary action menjadi dasar pertama (summary controller sebagai fallback); scan juga menelusuri method source hingga tiga tingkat untuk mendeteksi call persistence yang dikenal. HTTP GET/POST bukan dasar tunggal. Bukti klasifikasi ada di tab Bukti endpoint/provenance, dan jumlah tiap operasi ditampilkan per controller. Klasifikasi statis masih perlu review dan tidak membuktikan seluruh efek samping pada dependency dinamis/eksternal.

Gunakan dropdown **Read saja / Write saja / Unknown** untuk filter tampilan. Klik **Pilih hanya Read** untuk memilih seluruh endpoint read dan membatalkan pilihan non-read di semua baris, termasuk yang tersembunyi; pilihan ini mengatur mapping dan ekspor. Jika AI kemudian mengubah read menjadi write/unknown, endpoint tersebut dikeluarkan dari pilihan read-only. AI tidak boleh menurunkan bukti write dari source menjadi read. Tombol Pilih semua/Kosongkan pilihan mengakhiri mode pilihan read-only.

| File | Kegunaan |
| --- | --- |
| `api-rag-knowledge.json` | Bentuk key/type mengikuti lampiran knowledge JSON: endpoint, job, request, outputTypes, importantFields, intents, capabilities, scope, quality. |
| `rag-system-prompt.md` | System prompt untuk menjawab dengan knowledge dan mengusulkan kandidat endpoint kepada host. |
| `rag-knowledge.md` | Knowledge terstruktur seperti contoh HR: controller/action summary, fungsi, intent, scope, contoh pertanyaan, dokumen terkait. |
| `rag-chunks.jsonl` | Chunk endpoint dan dokumen beserta ID, source, teks, hubungan, dan content hash untuk ingestion ke vector store. |
| `provenance.json` | File/line source relatif, hash action, DTO schemas, summary, status inferensi/model per endpoint, warning, dan keterkaitan knowledge. |
| `manifest.json` | SHA-256 file ekspor dan jumlah chunk. |

ID endpoint stabil dari nama API + HTTP + route + controller/action. Hash konten membantu mendeteksi perubahan. Summary adalah dokumentasi source yang bisa stale: AI diminta membaca summary lebih dulu, lalu membandingkannya dengan implementasi dan melaporkan konflik.

**Kontrak OPX: yang dipetakan adalah `AppResult.Data`.** `Success(dto)`, initializer `new AppResult { Data = dto }`, dan assignment `result.Data = dto` ditelusuri menuju tipe payload. `outputTypes`, DTO schema, dan `importantFields` tidak memasukkan properti wrapper `Result`, `Message`, atau `Source`. Nama field yang sama dalam DTO bisnis tetap valid. Jika payload tidak dapat dibuktikan dari source, output ditandai unresolved, bukan diganti dengan schema envelope.

## Integrasi RAG

1. Import `rag-chunks.jsonl`, buat embedding menggunakan layanan pilihan host, dan simpan teks beserta metadata/ID. Aplikasi ini menghasilkan materi RAG; tidak menginstal vector database atau membuat embedding.
2. Gunakan `rag-system-prompt.md` sebagai system prompt. Saat menerima pertanyaan, host memasukkan top-k chunk relevan sebagai **data** `retrieved_context`.
3. Host menyuplai konteks login, `allowed_endpoint_ids`, `reviewed_endpoint_ids`, dan `runtime_verified_endpoint_ids`. Model hanya mengusulkan objek kandidat dengan `endpointId`, intent, parameter yang diketahui, dan parameter yang masih kurang.
4. Backend meninjau scope/kontrak, mengotorisasi, menjalankan request, menilai physical HTTP dan OPX logical status, lalu memberikan `api_results` kepada model. Jawaban aktual harus didasarkan pada hasil tersebut; knowledge endpoint bukan data transaksi.

`businessMeaningVerified` selalu `false` dan `runtimeVerificationRequired` selalu `true`. Semua klasifikasi AI adalah inferensi. POST yang diidentifikasi sebagai read tetap `restricted`, demikian pula output/binding/summary yang belum cukup jelas. Host bertanggung jawab atas review dan izin; katalog bukan authorization untuk menjalankan API atau membuat keputusan bisnis/HR.

## Privasi dan batas analisis

- Key dimasking dan disimpan terenkripsi dengan Windows DPAPI `CurrentUser` pada `%LOCALAPPDATA%/CSharpEndpointRag/settings.json`. Key hanya dapat dipulihkan oleh akun Windows yang sama dalam lingkungan Windows terkait. Tidak ada plaintext key, logging bearer, atau key dalam artefak ekspor.
- Endpoint provider tetap `https://ai.sumopod.com/v1/`; `GET models` dan `POST chat/completions` non-streaming memakai OPX. Redirect dan retry unauthorized dinonaktifkan; error body provider tidak ditampilkan.
- Operator menekan **Petakan AI** untuk mengirim summary controller/action, signature, schema DTO, cuplikan method/service tersanitasi, dan maksimal enam kandidat chunk knowledge ke Sumopod. Panggilan memakai key/saldo operator. Source literal string dihilangkan sebelum pengiriman; sanitizer bukan jaminan deteksi semua jenis data sensitif dalam komentar. Tinjau tab bukti sebelum mengirim dan jangan memilih dokumen berisi credential/data personal yang tidak boleh dibagikan.
- Tidak ada eksekusi MSBuild/assembly/source API, koneksi database, atau pemanggilan endpoint bisnis selama scan. Folder `bin`, `obj`, `.git`, tests, samples/reference dan reparse directories dilewati; file C# maksimal 2 MB, total maksimal 10.000 file. Knowledge per file maksimal 1 MB, total 2 juta karakter.
- Analisis mencakup controller dengan atribut HTTP, route konstan/token `[controller]`/`[action]`, alias route, action warisan yang tersedia dalam source, parameter binding eksplisit, dan DTO bertingkat maksimal empat level. Source compilation adalah best effort tanpa restore/build proyek target; SDK reference metadata membantu resolusi MVC bila tersedia.
- Minimal APIs, conventional routing, custom filter/global authorization, gateway prefixes, konfigurasi route runtime, conditional compilation khusus proyek, serta semua jalur dynamic response belum dapat dibuktikan. Binding tanpa atribut, schema tak terselesaikan, context yang terpotong, atau route tak terselesaikan ditandai untuk review. Pilih root satu API agar tipe dari banyak proyek tak bertabrakan.
- Output statis dipetakan dari tipe return/response attribute, response wrapper, local response variable, serta `Success(payload)` dalam service. Flow dinamis, anonim, resolusi overload ambigu, atau dependency di luar folder bisa tetap unresolved; aplikasi tidak mengarang tipe.
- Cuplikan AI maksimal 24.000 karakter per endpoint. Chunk knowledge maksimal 6.000 karakter dengan overlap untuk blok besar. Hubungan aturan-endpoint adalah kandidat relevansi, bukan bukti aturan tersebut diimplementasikan oleh endpoint.
- Hasil scan/AI berada di memori; ekspor sebelum menutup aplikasi. Setting terakhir disimpan otomatis setelah input berubah (debounce 750 ms), sebelum/sesudah operasi dan saat tutup: folder API, nama API, domain, nama asisten, model, key terenkripsi, filter/pilihan read-only, skip mapped, folder ekspor, dan path file knowledge. Dokumen knowledge dipulihkan bila file masih tersedia; scan/hasil AI tidak di-resume otomatis. Mengosongkan field key menghapus key tersimpan. Jika setting/cipher rusak, aplikasi melaporkan warning dan meminta input kembali.

## Pilihan endpoint v1.0.10

Versi 1.0.16: scan mengikuti ProjectReference literal dari csproj dalam folder API secara transitif (maksimal32 folder,10.000 file). Source service/DTO dependency dibaca tanpa build/restore/eksekusi; controller dependency tidak dimasukkan sebagai endpoint API utama. Interface ditelusuri hanya bila satu implementasi source ditemukan; beberapa implementasi tetap unresolved, tanpa menganggap konfigurasi DI runtime sudah terbukti. Reference conditional, MSBuild property/glob, NuGet binary, dan generated source tidak dievaluasi. Pilih folder yang mencakup csproj API, lalu Scan ulang; reference yang tidak dapat dibaca tercatat pada warning scan.

Versi 1.0.14: grid menampilkan Resolusi tipe. Scan menandai schema request/payload yang hilang, field object/dynamic, dan batas traversal sebagai unresolved beserta warning; parameter FromServices tidak dianggap request. Petakan AI menerima quality/schema dan wajib mempertahankan keterbatasan tanpa mengarang tipe atau field. Kontrak belum lengkap tetap restricted. JSONL/Markdown menyertakan schema dan resolution quality dalam teks retrieval; bentuk JSON paket tetap sama. Nilai default/required yang tidak tersedia tidak ditebak.

Versi 1.0.13 memperluas scan payload `AppResult.Data`: akses `result.Data` dari variabel/service, cast dan null-forgiving, serta field proyeksi anonim dan DTO bertingkat. Wrapper MVC seperti `OkObjectResult` dikecualikan dari payload. Ini analisis struktur source, bukan eksekusi API atau pengambilan nilai transaksi live. Hasil memakai outputTypes/schema/importantFields yang sudah ada sehingga format ekspor UI LLM tetap sama.

Versi 1.0.12: pilih baris grid lalu tekan **Spacebar** untuk mencentang/melepas centang endpoint dari kolom mana pun. Preview dan jumlah pilihan langsung diperbarui. Shortcut dinonaktifkan selama proses scan/AI/ekspor berlangsung.

Versi 1.0.11 menambahkan kolom Deskripsi dan Parameter endpoint pada grid. Deskripsi mengutamakan summary action, lalu deskripsi tugas dan summary controller. Parameter menampilkan nama, tipe, binding, dan penanda nullable/default dari hasil scan. Arahkan pointer ke sel untuk melihat teks lengkap. Format ekspor UI LLM tetap sama.

Tombol **Select All** mencentang seluruh endpoint dan **Unselect All** menghapus seluruh centang, termasuk baris yang tersembunyi oleh filter. Gunakan **Pilih hanya Read** untuk memilih operasi baca saja. Ekspor RAG hanya menyertakan endpoint yang dicentang; format paket untuk UI LLM tetap sama.

## Build dan verifikasi

Versi 1.0.1 memperbaiki error `Scope AI tidak valid`: scope/operation dinormalisasi untuk huruf dan spasi; nilai asing, kosong, null, atau gabungan pilihan menjadi `unknown` disertai warning untuk review. Tidak ada pemetaan alias yang memperluas akses. Prompt meminta satu nilai enum dan contoh JSON memakai nilai konkret. Warning ditampilkan pada tab Proses dan provenance. JSON yang malformed atau knowledge ID yang tidak dikenal tetap ditolak.

Untuk publish saat versi lama masih berjalan, gunakan `./build-release.ps1 -ReleaseName scope-fix-win-x64`; folder/ZIP baru tidak menimpa DLL yang sedang dipakai. Versi lama perlu diganti dengan executable baru untuk memakai perbaikan; hasil di memori versi lama tidak dipindahkan otomatis.

Memerlukan Windows, SDK .NET 10, restore NuGet untuk Roslyn/OPX dan dependensinya.

```powershell
dotnet build CSharpEndpointRag.slnx -c Release
dotnet run --project tests/EndpointRag.Verification -c Release
./build-release.ps1
```

Default release adalah folder portable **self-contained win-x64**, ZIP beserta SHA-256, tanpa PDB. `-FrameworkDependent` menghasilkan paket yang membutuhkan .NET 10 Desktop Runtime. Verification menjalankan fixture kontrak controller, summary-first, OPX request melalui fake provider, chunking, validasi output AI, cancellation, manifest, dan render preview WinForms. Fake provider tidak membuktikan koneksi/inference Sumopod live.

Validasi bentuk JSON terhadap contoh operator, tanpa menyalin isinya ke repository:

```powershell
dotnet run --project tests/EndpointRag.Verification -c Release -- --schema "C:/path/api-rag-knowledge.json"
dotnet run --project tests/EndpointRag.Verification -c Release -- --scan "D:/path/api-source"
```

Scanner source nyata hanya membaca file lokal. Build/test/publish tidak membuktikan deployment API, global authorization, saldo/key, atau keberhasilan model provider.
