#!/usr/bin/env python3
"""Static integrity checks for Kafka request parameter/length logging (does not compile C#)."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / 'src' / 'DSMarketService'
middleware = (API / 'Middleware/ApiCompletionLoggingMiddleware.cs').read_text()
formatter = (API / 'Middleware/RequestLogFormatter.cs').read_text()
base = (API / 'Controllers/MarketControllerBase.cs').read_text()
errors = (API / 'Middleware/MarketExceptionMiddleware.cs').read_text()
extensions = (API / 'Extensions/ServiceCollectionExtensions.cs').read_text()

assert 'RequestLogFormatter.Format(' in middleware
assert 'RequestLogFormatter.GetRequestBodyLength(' in middleware
assert 'ResponseBodyLengthKey' in middleware
assert 'InputParams={InputParams}' in middleware
assert 'BodyLength={BodyLength}' in middleware
assert 'ResponseBodyLength={ResponseBodyLength}' in middleware
assert 'logger.LogInformation(CompletedEvent' in middleware
assert 'catch (Exception)' in middleware
assert 'request.ContentLength' in formatter
assert 'request.Query' in formatter
assert 'request.RouteValues' in formatter
assert 'Uri.EscapeDataString' in formatter
assert 'IsSensitive' in formatter and '[REDACTED]' in formatter
assert 'request.Body.Read' not in formatter and 'EnableBuffering' not in formatter
assert 'Encoding.UTF8.GetByteCount(payload)' in base
assert 'Encoding.UTF8.GetByteCount(json)' in errors
assert 'config.AddKafka(appSetting.LoggingSetting);' in extensions
assert not (API / 'Logging').exists()
config = json.loads((API / 'appsettings.json').read_text())
opts = config['Diagnostics']['ApiInfoLogging']
assert opts == {'Enabled': True, 'MaxParameters': 32, 'MaxParameterValueLength': 256, 'MaxInputParamsLength': 4096}
for env in ('Development', 'UAT', 'Production'):
    assert (API / f'appsettings.{env}.json').exists()
    cmap = (ROOT / 'deploy' / f'configmap-{env.lower()}.yaml').read_text()
    assert 'Diagnostics__ApiInfoLogging__MaxParameters: "32"' in cmap
    assert 'Diagnostics__ApiInfoLogging__MaxParameterValueLength: "256"' in cmap
    assert 'Diagnostics__ApiInfoLogging__MaxInputParamsLength: "4096"' in cmap
assert sorted(p.name for p in (ROOT / 'deploy').iterdir()) == [
    'Dockerfile','configmap-development.yaml','configmap-production.yaml','configmap-uat.yaml']
assert not (ROOT / 'docs').exists()
assert (ROOT / 'tests/ApiLoggingSmoke/ApiLoggingSmoke.csproj').exists()
print('PASS: structured API_COMPLETED parameters, request/response byte length, redaction/limits, 3 env, Manonero wiring')
