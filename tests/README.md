# Kiểm thử

- `python tools/verify_source.py`: kiểm tra danh mục đúng 21 route và ba route bỏ đi không xuất hiện (static).
- `python tools/compare_contract.py --old http://old-uat --new http://new-uat --cases tools/contract_cases.example.json`: so JSON từng URL, kiểm tra field, type, array, HTTP status, content-type. Thay URL trường hợp kiểm thử bằng dữ liệu từ UAT thật.
- `BASE_URL=http://new-uat k6 run tests/k6_load.js`: tải tăng 50/200/500 arrivals mỗi giây (mẫu, điều chỉnh theo hạ tầng).

Không chạy load-test trên production. Đo cùng tải và dataset cho bản cũ và bản mới; đo p50/p95/p99, CPU, memory working set, Gen2 GC, threadpool queue length và backend latency. Chạy thêm soak >=2h.

## Kiểm thử logger và tham số

- `dotnet run --project tests/ApiLoggingSmoke/ApiLoggingSmoke.csproj`: kiểm tra trực tiếp `RequestLogFormatter` và middleware `API_COMPLETED` với DefaultHttpContext, không cần kết nối Kafka (không làm thay đổi app nghiệp vụ).
- `python tools/verify_api_logging.py`: rà soát tĩnh wiring, ba môi trường, các hook đếm số byte UTF-8 và bảo vệ logging. Test tĩnh **không thay thế** build hay test Kafka thật.
- Các kịch bản cần đo UAT: đúng query params (bao gồm `from`, `to`, `Code`), marker REDACTED, giới hạn log, BodyLength khi không có Content-Length, ResponseBodyLength byte UTF-8, Kafka broker disconnect, throughput/p95.
