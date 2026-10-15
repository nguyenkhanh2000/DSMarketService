#!/usr/bin/env python3
"""Kiểm tra tĩnh cấu hình Kafka/3 môi trường/deploy; không thay thế build & UAT."""
from pathlib import Path
import json
import re
import zipfile
import xml.etree.ElementTree as ET

try:
    import yaml
except ImportError:
    yaml = None

def read_configmap(path: Path) -> dict:
    content = path.read_text(encoding='utf-8')
    if yaml is not None:
        return yaml.safe_load(content)
    # Fallback stdlib: chỉ hỗ trợ manifest ConfigMap đơn giản do repo sở hữu.
    # Tất cả giá trị data đều là chuỗi JSON quoted, tránh phụ thuộc PyYAML trong CI.
    data = {}
    for line in content.splitlines():
        match = re.fullmatch(r'  ([A-Za-z][\w]*): (".*")', line)
        if match:
            data[match.group(1)] = json.loads(match.group(2))
    return {'kind': 'ConfigMap' if 'kind: ConfigMap' in content else '',
            'metadata': {'name': 'dsmarketservice-config' if 'name: dsmarketservice-config' in content else ''},
            'data': data}

ROOT = Path(__file__).resolve().parents[1]
api = ROOT / 'src/DSMarketService'

for project in ('DSMarketService', 'DSMarketService.Contracts', 'DSMarketService.Infrastructure'):
    assert (ROOT / 'src' / project / 'GlobalUsings.cs').is_file(), project

program = (api / 'Program.cs').read_text(encoding='utf-8')
ext = (api / 'Extensions/ServiceCollectionExtensions.cs').read_text(encoding='utf-8')
app_setting = (api / 'Settings/AppSetting.cs').read_text(encoding='utf-8')
assert 'builder.Services.AddKafkaLogger(appSetting)' in program
assert 'AppSetting.MapValue(builder.Configuration)' in program
assert 'config.AddKafka(appSetting.LoggingSetting);' in ext
assert 'config.ClearProviders();' in ext
assert 'public static IServiceCollection AddKafkaLogger(this IServiceCollection services, AppSetting appSetting)' in ext
assert 'GetSection("KafkaLogger")' in app_setting
assert 'configuration.GetValue("AllowConsoleLog", true)' in app_setting
assert 'UseMiddleware<ApiCompletionLoggingMiddleware>()' in program
assert program.index('UseRouting()') < program.index('UseMiddleware<ApiCompletionLoggingMiddleware>()')
assert program.index('UseMiddleware<ApiCompletionLoggingMiddleware>()') < program.index('UseMiddleware<MarketExceptionMiddleware>()')
assert 'AddHostedService<KafkaLogWorker>' not in program
assert 'MapGet("/health/logging"' not in program

for cls in ('ApiRequestMetrics.cs',):
    assert not (api / 'Middleware' / cls).exists(), cls
assert not (api / 'Logging/LogWriteFailureCounter.cs').exists()
for src in api.rglob('*.cs'):
    code = src.read_text(encoding='utf-8')
    for forbidden in ('System.Diagnostics.Metrics', 'ApiRequestMetrics', 'LogWriteFailureCounter', 'Meter.Create', 'CreateCounter<', 'CreateHistogram<'):
        assert forbidden not in code, (src, forbidden)

for filename, method in (
    ('ApiCompletionLoggingMiddleware.cs', 'logger.LogInformation(CompletedEvent'),
    ('MarketExceptionMiddleware.cs', 'logger.LogError('),
    ('SlowRequestMiddleware.cs', 'logger.LogWarning('),
):
    code = (api / 'Middleware' / filename).read_text(encoding='utf-8')
    assert method in code and 'catch (Exception)' in code, filename
completion = (api / 'Middleware/ApiCompletionLoggingMiddleware.cs').read_text(encoding='utf-8')
assert 'new(1001, "API_COMPLETED")' in completion
assert 'LogInformation(CompletedEvent' in completion
assert 'Request.QueryString' not in completion

common = json.loads((api / 'appsettings.json').read_text(encoding='utf-8'))
assert common['Redis']['Db3'] == 1, 'Giữ nguyên Db3 như source (1)'
assert common['KafkaLogger']['Targets']['Target1']['Topic'] == 'DSmarketService'
assert common['KafkaLogger']['Rules'] == [{'WriteTo':'Target1'}]
assert common['KafkaLogger']['AppName'] == 'DSMarketService'
assert common['Diagnostics']['ApiInfoLogging']['Enabled'] is True
assert common['Logging']['LogLevel']['DSMarketService.Middleware.ApiCompletionLoggingMiddleware'] == 'Information'
assert common['SQL']['Connection64'] == '' and common['Oracle']['ConnectionString_trd'] == ''

expected_topics = {
    'Development': 'DSmarketService-Development',
    'UAT': 'DSmarketService-UAT',
    'Production': 'DSmarketService',
}

assert not (ROOT / 'docs').exists(), 'docs phải bị xóa'
actual_deploy = {p.name for p in (ROOT / 'deploy').rglob('*') if p.is_file()}
expected_deploy = {'Dockerfile', 'configmap-development.yaml','configmap-uat.yaml','configmap-production.yaml'}
assert actual_deploy == expected_deploy, (actual_deploy, expected_deploy)
assert not any(p.is_dir() for p in (ROOT/'deploy').iterdir())
for env, topic in expected_topics.items():
    settings = json.loads((api / f'appsettings.{env}.json').read_text(encoding='utf-8'))
    assert settings['KafkaLogger']['Targets']['Target1']['Topic'] == topic
    assert settings['Logging']['LogLevel']['DSMarketService.Middleware.ApiCompletionLoggingMiddleware'] == 'Information'
    assert settings['Redis']['SentinelServiceName'] == ''
    assert 'SQL' not in settings and 'Oracle' not in settings
    if env != 'Development':
        assert settings['KafkaLogger']['BootstrapServers'] == ''
        assert settings['Redis']['RedisSentinel'] == '' and settings['Redis']['RedisLegacy'] == ''

    cmap = ROOT / 'deploy' / f'configmap-{env.lower()}.yaml'
    value = read_configmap(cmap)
    assert value['kind'] == 'ConfigMap' and value['metadata']['name'] == 'dsmarketservice-config'
    data = value['data']
    assert data['ASPNETCORE_ENVIRONMENT'] == data['DOTNET_ENVIRONMENT'] == env
    assert data['KafkaLogger__Targets__Target1__Topic'] == topic
    assert data['Redis__Db3'] == '1'
    assert data['Diagnostics__ApiInfoLogging__Enabled'] == 'true'
    assert not any(x in data for x in ('SQL__Connection64','Oracle__ConnectionString_trd', 'KafkaLogger__BootstrapServers','Redis__RedisSentinel','Redis__RedisLegacy'))
    assert all(isinstance(v,str) for v in data.values()), (cmap, data)

pkg = ROOT / 'local-packages/Manonero.Logger.Kafka.6.0.0-beta.nupkg'
assert pkg.exists()
with zipfile.ZipFile(pkg) as z:
    assert 'lib/netstandard2.0/Logger.Kafka.dll' in z.namelist()
csproj = ET.parse(api / 'DSMarketService.csproj').getroot()
assert any(x.get('Include')=='Manonero.Logger.Kafka' and x.get('Version')=='6.0.0-beta' for x in csproj.findall('.//PackageReference'))
nuget = (ROOT / 'NuGet.Config').read_text()
assert './local-packages' in nuget

docker = (ROOT / 'deploy/Dockerfile').read_text()
assert 'dotnet/sdk:10.0' in docker and 'dotnet/aspnet:10.0' in docker
assert 'dotnet restore src/DSMarketService/DSMarketService.csproj --configfile NuGet.Config' in docker
assert 'DOTNET_GCServer' in docker

print('PASS: 21 API logging retained, no metrics subsystem, 3 environment JSON, 3 ConfigMaps, Dockerfile, local Kafka NuGet')
