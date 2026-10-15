#!/usr/bin/env python3
"""Audit tĩnh phân tuyến 21 route từ source DSMarketIndex gốc.
KHÔNG thay thế integration test với 2 Redis vật lý thực và kiểm thử hợp đồng JSON.
"""
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
api=root/'src/DSMarketService'
inf=root/'src/DSMarketService.Infrastructure'
expected={
 'MarketCalendarController.cs':{
  'world_indices':('DELEGATE','read.WorldIndicesAsync'),
  'non_workingday':('Legacy','redis.StringAsync'),
  'day_before':('Sentinel','read.ReadString1Async'),
  'trading_date':('Legacy','read.ReadString0Async'),
 },
 'MarketPricesController.cs':{
  'price_ladder':('Sentinel','read.ReadSeriesAsync'),
  'pt':('Legacy','read.ReadSeriesAsync'),
  'matching':('Sentinel','read.ReadSeries1Async'),
  'history':('DELEGATE','history.ReadAsync'),
  'last_index':('DELEGATE','read.LastIndexRawAsync'),
 },
 'MarketRankingsController.cs':{
  'top_cp':('Sentinel','read.ReadString1Async'),
  'top_value':('Sentinel','read.ReadString1Async'),
  'top_volume':('Sentinel','read.ReadString1Async'),
  'trade_nn/val':('Sentinel','read.ReadSeries1Async'),
  'trade_nn/vol':('Sentinel','read.ReadSeries1Async'),
 },
 'MarketStatisticsController.cs':{
  'statistics':('NONREDIS','statistics.ReadAsync'),
  'liquidity':('Sentinel','read.ReadSeries1Async'),
  'width':('Sentinel','read.ReadSeries1Async'),
  'leader':('Sentinel','read.ReadString1Async'),
  'portfolio':('DELEGATE','read.PortfolioAsync'),
 },
 'MarketReferencesController.cs':{
  'cw_info':('NONREDIS','oracle.WarrantyInfoAsync'),
  'company_name':('DELEGATE','references.CompaniesAsync'),
 }
}
seen={}
for file,routes in expected.items():
 s=(api/'Controllers'/file).read_text(encoding='utf-8')
 chunks=re.split(r'(?=\[HttpGet\("[^"]+"\)\])',s)
 for route,(target,call) in routes.items():
  matches=[chunk for chunk in chunks if chunk.startswith(f'[HttpGet("{route}")]')]
  assert len(matches)==1,(file,route,len(matches))
  body=matches[0]
  assert call in body,(route,'missing expected service',call)
  if target in ('Sentinel','Legacy'):
   assert re.search(r'RedisSource\.'+target+r'\s*,',body),(route,'RedisSource not explicitly correct')
   other='Legacy' if target=='Sentinel' else 'Sentinel'
   assert not re.search(r'RedisSource\.'+other+r'\s*,',body),(route,'wrong RedisSource present')
  seen[route]=target
assert len(seen)==21,len(seen)
assert not {'adjust_price','realtime_index','top_realtime'} & seen.keys()
# Delegation mapping cannot be guessed from method name: assert actual source passed into RedisReader.
m=(api/'Features/MarketReadService.cs').read_text()
assert 'redis.HashAsync(RedisSource.Sentinel, keys.Db3' in m
assert 'redis.StringAsync(RedisSource.Legacy, keys.Db0, keys.Portfolio(' in m
assert 'redis.StringAsync(RedisSource.Legacy, keys.Db0, "S5G_LAST_INDEX_"' in m
assert 'redis.SortedAsync(RedisSource.Sentinel, keys.Db3' in (api/'Features/HistorySeriesService.cs').read_text()
assert 'redis.StringAsync(RedisSource.Legacy, keys.Db0' in (api/'Features/ReferenceDataService.cs').read_text()
# Hai multiplexers tách biệt và nguồn lựa chọn không bị bỏ qua.
c=(inf/'Redis/RedisConnections.cs').read_text()
assert 'Redis:RedisSentinel' in c and 'Redis:RedisLegacy' in c
assert 'RedisSource.Sentinel => _redisSentinel' in c
assert 'RedisSource.Legacy => _redisLegacy' in c
assert 'return multiplexer.GetDatabase(db)' in c
p=(api/'Program.cs').read_text()
assert 'AddSingleton<RedisConnections>' in p and 'InitializeAsync(app.Configuration)' in p
assert 'redis.IsReady' in p
assert 'AddRateLimiter' not in p and 'ConcurrencyLimiter' not in p
for path in api.rglob('*.cs'):
 if path.name!='Program.cs':
  assert 'ConnectionMultiplexer.Connect' not in path.read_text(),('per-request Connect',path)
print('PASS: 21 routes, source selection explicit, 13 Sentinel / 6 Legacy plus SQL/Oracle; no fallback, two muxes, readiness both')
from collections import Counter
print('Route direct/delegate:',dict(Counter(seen.values())))
