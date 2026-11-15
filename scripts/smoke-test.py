import json
import sys
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5090'
def read(path):
    return json.load(urllib.request.urlopen(base + path))

snapshot = read('/api/snapshot')
assert snapshot['mode'] in ['Live', 'Replay']
assert len(snapshot['trains']) > 0
assert all(-90 <= t['latitude'] <= 90 and -180 <= t['longitude'] <= 180 for t in snapshot['trains'])
line = snapshot['trains'][0]['line']
filtered = read('/api/snapshot?line=' + line)
assert all(t['line'] == line for t in filtered['trains'])
stats = read('/api/statistics/lines')
assert stats and all(0 <= row['punctuality'] <= 100 for row in stats if row['punctuality'] is not None)
assert read('/api/statistics/trend')
assert read('/api/statistics/stations')
body = json.dumps({'id': '00000000-0000-0000-0000-000000000000', 'name': 'Regla de prueba temporal', 'line': line, 'stationId': None, 'thresholdMinutes': 10, 'enabled': True}).encode()
rule = json.load(urllib.request.urlopen(urllib.request.Request(base + '/api/alerts/rules', data=body, headers={'Content-Type': 'application/json'})))
urllib.request.urlopen(urllib.request.Request(base + '/api/alerts/rules/' + rule['id'], method='DELETE'))
excel = urllib.request.urlopen(base + '/api/export.xlsx').read()
assert excel.startswith(b'PK')
assert urllib.request.urlopen(base + '/_framework/blazor.web.js').status == 200
assert urllib.request.urlopen(base + '/metrics').status == 200
print(json.dumps({'result': 'passed', 'trains': len(snapshot['trains']), 'mode': snapshot['mode'], 'sourceTimestamp': snapshot['observedAt'], 'statistics': len(stats), 'rulesCrud': True, 'excel': True, 'blazorRuntime': True, 'metrics': True}, indent=2))
