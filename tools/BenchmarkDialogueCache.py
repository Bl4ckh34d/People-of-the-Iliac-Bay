"""Replay a captured local completion to measure prompt-cache reuse, without gameplay effects."""
import argparse
import hashlib
import json
from pathlib import Path
import time
from urllib.request import Request, urlopen


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--endpoint', default='http://127.0.0.1:8080/completion')
    args = parser.parse_args()
    if not args.endpoint.startswith('http://127.0.0.1:'):
        raise ValueError('This benchmark only supports a local loopback server.')
    base = json.loads(args.request.read_text(encoding='utf-8-sig'))
    base.update(stream=False, temperature=0, seed=42)
    rows = []
    for mode, value in [('disabled', False), ('enabled', True), ('default', None)]:
        for repeat in range(3):
            body = dict(base)
            if value is None:
                body.pop('cache_prompt', None)
            else:
                body['cache_prompt'] = value
            start = time.perf_counter()
            request = Request(args.endpoint, data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
            with urlopen(request, timeout=60) as response:
                result = json.load(response)
            row = dict(mode=mode, repeat=repeat, wallSeconds=round(time.perf_counter()-start, 4),
                       timings=result.get('timings'), truncated=result.get('truncated'),
                       responseHash=hashlib.sha256(result['content'].encode()).hexdigest())
            rows.append(row)
            print(json.dumps(row), flush=True)
    args.output.write_text(json.dumps(dict(scope='identical-prompt replay; best-case reuse, not a multi-turn gameplay claim',
        promptSha256=hashlib.sha256(base['prompt'].encode()).hexdigest(), samples=rows), indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
