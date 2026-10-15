#!/usr/bin/env python3
"""Kiểm tra tĩnh bản net10/Controller. Không thay thế dotnet build hoặc test UAT."""
from pathlib import Path
import json, re
import xml.etree.ElementTree as ET
try:
    import yaml  # pip install PyYAML (không bắt buộc nếu chỉ kiểm tra route/XML/JSON)
except ImportError:
    yaml = None

ROOT = Path(__file__).resolve().parents[1]
CONTROLLERS = ROOT / 'src/DSMarketService/Controllers'
EXPECTED = {
    'MarketCalendarController.cs': ['world_indices','non_workingday','day_before','trading_date'],
    'MarketPricesController.cs': ['price_ladder','pt','matching','history','last_index'],
    'MarketStatisticsController.cs': ['statistics','liquidity','width','leader','portfolio'],
    'MarketRankingsController.cs': ['top_cp','top_value','top_volume','trade_nn/val','trade_nn/vol'],
    'MarketReferencesController.cs': ['cw_info','company_name'],
}
found = {}
for controller, expected in EXPECTED.items():
    src = (CONTROLLERS / controller).read_text(encoding='utf-8')
    assert '[Route("ds_market")]' in src, controller
    routes = re.findall(r'\[HttpGet\("([^"\n]+)"\)\]', src)
    assert routes == expected, (controller, routes, expected)
    for item in routes:
        assert item not in found, ('duplicate route', item)
        found[item] = controller

assert len(found) == 21, len(found)
assert not {'adjust_price', 'realtime_index', 'top_realtime'} & found.keys()
assert not (ROOT/'src/DSMarketService/Endpoints/MarketRoutes.cs').exists()
program = (ROOT/'src/DSMarketService/Program.cs').read_text(encoding='utf-8')
assert 'AddControllers()' in program and 'MapControllers()' in program
assert 'MapMarketRoutes()' not in program and 'AddRateLimiter(' not in program
props = ET.parse(ROOT/'Directory.Build.props').getroot()
assert props.findtext('.//TargetFramework') == 'net10.0'
assert props.findtext('.//LangVersion') == '14.0'
for csproj in ROOT.glob('src/**/*.csproj'):
    ET.parse(csproj)
assert json.loads((ROOT/'global.json').read_text())['sdk']['version'].startswith('10.')
settings = json.loads((ROOT/'src/DSMarketService/appsettings.json').read_text())
# Giá trị thực tế trong appsettings người dùng cung cấp là 1; không thay nghiệp vụ trong PR Kafka.
assert settings['Redis']['Db3'] == 1, 'Redis Db3 changed unexpectedly (verify with UAT)' 
for key in ('Redis','SQL','Oracle','KafkaLogger','Network'): assert key in settings
if yaml is not None:
    for y in ROOT.glob('deploy/**/*.yaml'):
        list(yaml.safe_load_all(y.read_text(encoding='utf-8')))
else:
    print('NOTE: SKIP YAML parse (cài PyYAML để kiểm tra YAML)')
docker = (ROOT/'deploy/Dockerfile').read_text()
assert 'dotnet/sdk:10.0' in docker and 'dotnet/aspnet:10.0' in docker
print('STATIC PASS: net10.0, 5 Controllers, 21 unique routes, 3 removed, Docker 10, valid JSON/XML; YAML if PyYAML available')
