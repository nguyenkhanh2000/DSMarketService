# DSMarketService (.NET 10) — Manonero Kafka Logger — 3 môi trường

Source gồm 3 project `DSMarketService`, `DSMarketService.Infrastructure`, `DSMarketService.Contracts`.
21 route nghiệp vụ và phân tuyến Redis Sentinel/Legacy được giữ nguyên so với bản trước.

## 1. Thay đổi trong bản này

- Giữ DI `builder.Services.AddKafkaLogger(appSetting)` / `config.AddKafka(appSetting.LoggingSetting)` với NuGet custom `Manonero.Logger.Kafka` phiên bản `6.0.0-beta` (gói local nằm tại `local-packages/`).
- **Bỏ toàn bộ thống kê metrics trong code**: xóa `ApiRequestMetrics.cs` (Meter/Counter/Histogram), `LogWriteFailureCounter.cs` và endpoint `/health/logging`. Không còn tự đếm request hay lỗi logger trong bộ nhớ.
- **Giữ 1 log `API_COMPLETED` mức Information mỗi request nghiệp vụ**. Log bao gồm `Api`, `Method`, `StatusCode`, `DurationMs`, `Outcome`, `TraceId`, và **`InputParams` (route/query), `BodyLength` (request), `ResponseBodyLength` (response)**. `SlowRequestMiddleware` và `MarketExceptionMiddleware` vẫn ghi Warning/Error. Không lưu **nội dung** request/response body.
- Các lệnh ghi log trong 3 middleware được `try/catch` để lỗi **đồng bộ** do logging provider ném ra không làm hỏng HTTP response. Điều này **không thể cam kết** Kafka `Log()` hoàn toàn không chặn khi mạng rớt; phải thử timeout trên UAT và kiểm tra thư viện Manonero.
- InputParams được chuẩn hóa theo định dạng `key=value&key2=value2` (URI percent-encoding), bảo toàn tên và giá trị caller gửi, nhưng **không log mật khẩu/token/secret/cookie**. Giới hạn mặc định: 32 tham số, 256 ký tự/giá trị, 4096 ký tự tổng; thay đổi qua `Diagnostics:ApiInfoLogging`. Những API cũ chỉ nhận query params GET, nên `BodyLength` thường là 0.
- `ResponseBodyLength` là số byte UTF-8 của **payload JSON/text được tạo** trong `LegacyJson`/`LegacyRaw` hoặc error handler; tính trực tiếp từ chuỗi đã serialize, không đọc/ghi lại stream, không tính network transfer/compression. Trường hợp không biết kích thước: `-1`. `BodyLength` lấy `Request.ContentLength`; khi request có thể chứa body nhưng thiếu Content-Length sẽ là `-1`, tuyệt đối không đọc body chỉ để log.
- Tạo ba file cấu hình `appsettings.Development.json`, `appsettings.UAT.json`, `appsettings.Production.json`; giữ `appsettings.json` cho cấu hình chung.
- `deploy/` **chỉ** gồm `Dockerfile` và ba file `configmap-*.yaml`; không còn Deployment, Service, HPA, Kustomize, Secret mẫu.
- Xóa thư mục `docs/`. Mọi hướng dẫn thiết yếu ở README này.

## 2. Cấu hình theo môi trường

| Môi trường | ASPNETCORE_ENVIRONMENT | File được đọc sau appsettings.json | Kafka topic mặc định |
|---|---|---|---|
| Local | `Development` | `appsettings.Development.json` | `DSmarketService-Development` |
| Test/UAT | `UAT` | `appsettings.UAT.json` | `DSmarketService-UAT` |
| Production | `Production` | `appsettings.Production.json` | `DSmarketService` (giữ tên gốc) |

### Ví dụ log Kafka

Request:

```http
GET /ds_market/matching?stockCode=VPB&from=0&to=-1
```

Trường `Message` do Manonero gửi lên Kafka có dạng (giá trị byte/time chỉ minh họa):

```text
API_COMPLETED Api=matching Method=GET StatusCode=200 DurationMs=12 Outcome=completed InputParams=stockCode=VPB&from=0&to=-1 BodyLength=0 ResponseBodyLength=1642 TraceId=...
```

- `InputParams` ghi tham số **do client gửi**, không tự thêm default từ `QLong/QInt` nếu query thiếu field.
- Với `?password=abc&stockCode=VPB`, output là `password=%5BREDACTED%5D&stockCode=VPB`.
- Với dữ liệu có dấu cách, dấu `/`, ký tự đặc biệt, các giá trị được URI-encode, có thể giải mã bằng URL decoder ở hệ thống đọc Kafka.
- `BodyLength` và `ResponseBodyLength` là **byte**, không phải `.Length` (UTF-16 code units) của chuỗi .NET.
- Chỉ các endpoint `ds_market/*` được log `API_COMPLETED`; không tăng số event mỗi API. Không lưu full body, headers Authorization hoặc cookie.
- Nếu phần ghi log Kafka thất bại đồng bộ, catch bảo vệ response. Không có bảo đảm tuyệt đối về thời gian `ILogger.Log()` khi Kafka mất mạng; kiểm thử latency UAT là bắt buộc.

Điều chỉnh mặc định ở `appsettings.json` (hoặc tương đương `Diagnostics__ApiInfoLogging__*` trong ConfigMap):

```json
"Diagnostics": {
  "ApiInfoLogging": {
    "Enabled": true,
    "MaxParameters": 32,
    "MaxParameterValueLength": 256,
    "MaxInputParamsLength": 4096
  }
}
```

`WebApplication.CreateBuilder(args)` tự đọc `appsettings.json`, rồi `appsettings.{Environment}.json`, sau đó các biến môi trường (biến môi trường được ưu tiên cao hơn).

Cấu hình **Redis Db3 vẫn là 1** theo bản source đã bàn giao, không tự ý đổi thành 8. Các Redis key và stored procedure vẫn nằm ở cấu hình chung, không đụng nghiệp vụ.

**Thông tin bắt buộc cung cấp cho UAT/Production** trước khi start:

```text
Redis__RedisSentinel              Endpoint Redis dùng nhóm dữ liệu Sentinel (có thể là primary endpoint).
Redis__RedisLegacy                Endpoint Redis legacy độc lập.
Redis__SentinelServiceName        Tên master THẬT nếu RedisSentinel trỏ đến Sentinel nodes 26379; nếu trỏ primary 6379 thì để trống.
SQL__Connection64                 SQL Server connection string có credentials.
Oracle__ConnectionString_trd      Oracle connection string có credentials.
KafkaLogger__BootstrapServers     Địa chỉ Kafka bootstrap của chính môi trường.
```

**Quan trọng:** Không có bằng chứng rằng các IP trong source người dùng đưa là môi trường Production, nên các endpoint UAT/Production cố ý để trống, không tự sao chép IP từ bản cũ sang Production. Hãy đưa giá trị xác nhận của từng môi trường qua K8s Secret/environment variables; **không commit mật khẩu vào `appsettings*.json` hoặc ConfigMap**. Development có Redis tại `127.0.0.1:6379` và `127.0.0.1:6380`, Kafka tại `127.0.0.1:9092` để phát triển local; máy chạy cần có những dịch vụ này. Các API SQL/Oracle cần cấu hình connection string tương ứng mới hoạt động.

`KafkaLogger:Targets:Target1:Template`, `Rules`, `AppName` nằm ở file chung; các môi trường chỉ override bootstrap/topic. Nếu muốn dùng topic khác, sửa đồng thời appsettings theo môi trường và ConfigMap (khi dùng K8s). **Topic UAT/Development cần tồn tại hoặc Kafka cluster phải cho phép tự tạo.**

### Ví dụ chạy Development từ terminal

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:SQL__Connection64 = '<SQL_CONNECTION_STRING_DEV>'
$env:Oracle__ConnectionString_trd = '<ORACLE_CONNECTION_STRING_DEV>'
dotnet run --project src/DSMarketService/DSMarketService.csproj
```

Không lưu thông tin thật vào lệnh/script được commit lên Git. Nên dùng secret store hoặc biến môi trường do công cụ triển khai cung cấp.

## 3. Deploy (chỉ Dockerfile + ConfigMap)

```text
deploy/
  Dockerfile
  configmap-development.yaml
  configmap-uat.yaml
  configmap-production.yaml
```

Build Docker **từ thư mục gốc solution**, không build từ `deploy/`:

```bash
docker build -f deploy/Dockerfile -t dsmarketservice:net10 .
```

Dockerfile restore từ `NuGet.Config` để tìm `Manonero.Logger.Kafka.6.0.0-beta.nupkg` trong `local-packages/`. Các package khác cần NuGet.org hoặc cache/feed được cấu hình hợp lệ.

**ConfigMap cho mỗi môi trường có cùng tên `dsmarketservice-config`**; triển khai vào namespace khác nhau để không ghi đè cấu hình:

```bash
kubectl apply -n YOUR_DEV_NAMESPACE -f deploy/configmap-development.yaml
kubectl apply -n YOUR_UAT_NAMESPACE -f deploy/configmap-uat.yaml
kubectl apply -n YOUR_PROD_NAMESPACE -f deploy/configmap-production.yaml
```

Manifest Deployment của bạn (quản lý ngoài thư mục `deploy/` này) cần inject cả ConfigMap và các Secret của môi trường, ví dụ:

```yaml
envFrom:
  - configMapRef:
      name: dsmarketservice-config
  - secretRef:
      name: dsmarketservice-settings
```

Secret `dsmarketservice-settings` được người vận hành tạo và quản lý **riêng**; nó phải có các key `Redis__RedisSentinel`, `Redis__RedisLegacy`, `SQL__Connection64`, `Oracle__ConnectionString_trd`, `KafkaLogger__BootstrapServers` và `Redis__SentinelServiceName` nếu dùng Sentinel thật. Tên Secret trên đây chỉ là ví dụ. Không commit Secret thực tế. Khi inject, bảo đảm đúng container port 8080, tài nguyên và probes phù hợp với manifest ngoài project.

Không apply ba ConfigMap vào **cùng một namespace** do cùng dùng một tên; môi trường cuối sẽ ghi đè môi trường trước.

## 4. Build và kiểm thử

Yêu cầu **.NET 10 SDK**, package feed NuGet khác truy cập được:

```bash
dotnet restore DSMarketService.sln --configfile NuGet.Config
dotnet build DSMarketService.sln -c Release --no-restore
python tools/verify_source.py
python tools/verify_redis_routing.py
python tools/verify_global_logging.py
python tools/verify_api_logging.py
# Cần SDK .NET 10 - test request logger không cần Kafka broker hay NuGet external:
dotnet run --project tests/ApiLoggingSmoke/ApiLoggingSmoke.csproj
```

Hoặc chạy `./build-net10.ps1` bằng PowerShell 7 trên Windows.

Kiểm thử UAT bắt buộc:

1. Gọi 21 API và so sánh HTTP status, content type, JSON và business data với bản trước.
2. Gửi request thành công, lỗi nghiệp vụ, request chậm và input có nhiều tham số; xác nhận mỗi API có **một** `API_COMPLETED` Kafka log, đúng `InputParams`, `BodyLength`, `ResponseBodyLength`, `TraceId`, `Outcome` và đúng topic.
   - Test GET `matching?stockCode=VPB&from=0&to=-1`: phải thấy cả ba input trong log; BodyLength=0.
   - Test param `password=test123`: chỉ thấy marker REDACTED, không có `test123`.
   - Test JSON response dài: so UTF-8 byte length phần payload ứng dụng tạo với ResponseBodyLength trong log (không phải compressed bytes).
3. Ngắt Kafka broker/blackhole network và đo `p50/p95/p99` latency API, tỷ lệ request lỗi. Thư viện custom tự quản lý delivery/retry; `try/catch` chỉ bảo vệ các exception đồng bộ, không bảo đảm độ trễ là không đáng kể.
4. Kiểm tra Redis Sentinel/Legacy **riêng biệt**, readiness `/health/ready`, SQL Server, Oracle trên UAT.
5. Xác nhận log Warning/Error và Kafka khi restart Pod. Ứng dụng không expose counter delivery/lost log.

**Trạng thái kiểm thử:** bộ kiểm tra source là kiểm tra tĩnh. Bản này chưa được xác nhận `dotnet build` hoặc test Kafka/Redis thật trong môi trường không có SDK/broker. Không triển khai thẳng Production nếu chưa chạy UAT.
