// k6 benchmark có kiểm soát: thay BASE_URL trước khi dùng ở UAT.
// Script chỉ gửi GET, không gọi production khi chưa được phép.
import http from 'k6/http';
import { check } from 'k6';

const base = __ENV.BASE_URL || 'http://localhost:8080';
export const options = {
  scenarios: {
    market_reads: {
      executor: 'ramping-arrival-rate', startRate: 10, timeUnit: '1s',
      preAllocatedVUs: 40, maxVUs: 500,
      stages: [
        { target: 50, duration: '2m' },
        { target: 200, duration: '5m' },
        { target: 500, duration: '5m' },
        { target: 50, duration: '2m' }
      ]
    }
  },
  thresholds: { http_req_failed: ['rate<0.01'] }
};
const paths = [
  '/ds_market/last_index?exchange=HO',
  '/ds_market/trading_date',
  '/ds_market/top_value?exchange=HO&dayNumber=5',
  '/ds_market/matching?stockCode=AAA&from=0&to=-1',
  '/ds_market/world_indices'
];
export default function () {
  const path = paths[Math.floor(Math.random() * paths.length)];
  const res = http.get(base + path);
  check(res, { 'HTTP 200': r => r.status === 200 });
}
