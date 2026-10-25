"""Serial native-template diagnostics. Default validates preparation without network calls."""
import argparse
import json
import subprocess
import sys
from pathlib import Path
from urllib.request import build_opener

import prepare

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'NpcArchitectureEval'))
from model_client import post_json, _NoRedirect, _validate_loopback_http, _append_jsonl


def get_json(url):
    _validate_loopback_http(url)
    with build_opener(_NoRedirect).open(url, timeout=10) as response:
        return json.load(response)


def preflight(prepared, endpoint):
    _validate_loopback_http(endpoint)
    manifest = json.loads((prepared / 'manifest.json').read_bytes())
    suite_bytes = (prepared / 'suite.json').read_bytes()
    request_bytes = (prepared / 'requests.jsonl').read_bytes()
    if prepare.sha(suite_bytes) != manifest['suiteSha256'] or prepare.sha(request_bytes) != manifest['requestsSha256']:
        raise ValueError('Prepared inputs changed after freezing.')
    rows = [json.loads(line) for line in request_bytes.splitlines()]
    # Rebuild only from the strict input projection; frozen requests cannot carry hidden oracle fields.
    if rows != prepare.build(json.loads(suite_bytes)):
        raise ValueError('Prepared requests differ from the current input projection.')
    return rows


def validate_completion(response, model):
    if response.get('model') != model:
        raise ValueError('Response model identity changed.')
    if response.get('truncated') or response.get('stop_type') == 'limit':
        raise ValueError('Response truncated or token budget exhausted.')
    if not isinstance(response.get('content'), str) or not response['content'].strip():
        raise ValueError('Empty or malformed completion.')
    return response['content']


def result_row(identifier, response, model, latency):
    content = validate_completion(response, model)
    return {'id': identifier, 'reply': content, 'latencyMs': latency, 'transportVerdict': 'complete',
            'semanticVerdict': 'pending-human-review', 'nativeExecution': False,
            'reasoningMarkerPresent': any(marker in content for marker in ('<think>', '</think>', '<|think|>', '<|channel>thought'))}


def chat_completion(response, model):
    if response.get('model') != model:
        raise ValueError('Response model identity changed.')
    choice=response['choices'][0]
    if choice.get('finish_reason') != 'stop':
        raise ValueError('Chat response incomplete; reasoning/answer token budget exhausted.')
    final={'model':model,'content':choice['message'].get('content')}
    validate_completion(final,model)
    return final


def require_answer_template(rendered):
    if rendered.rstrip().endswith(('<think>', '<|think|>', '<|channel>thought')):
        raise ValueError('Template requires reasoning; use chat transport with an explicit reasoning budget.')


def execute(prepared, output, endpoint, model, rows, temperature, thinking=False):
    # No service lifecycle management. The operator owns GPU release and exact server setup.
    game = subprocess.run(['powershell', '-NoProfile', '-Command',
        "if (Get-Process DaggerfallUnity -ErrorAction SilentlyContinue) { exit 3 }"], capture_output=True)
    if game.returncode != 0:
        raise ValueError('DFU must be closed for offline model diagnostics.')
    output.mkdir(parents=True, exist_ok=False)
    ledger = output / 'calls.jsonl'

    def check_model():
        ids = [entry['id'] for entry in get_json(endpoint + '/v1/models')['data']]
        if ids != [model]:
            raise ValueError('Expected one exact model identity.')

    def post(identifier, stage, path, payload):
        result = post_json(endpoint + path, payload, ledger, {'id': identifier, 'stage': stage}, timeout=90)
        if result['status_code'] != 200 or not isinstance(result['response_json'], dict):
            raise ValueError(f'{stage} failed: HTTP {result["status_code"]}; {result["transport_error"]}')
        return result

    check_model()
    props = get_json(endpoint + '/props')
    context = props['default_generation_settings']['n_ctx']
    budget=2048 if thinking else 384
    manifest = {'preparedManifest': json.loads((prepared / 'manifest.json').read_bytes()), 'model': model,
                'endpoint': endpoint, 'context': context, 'temperature': temperature, 'seed': 42,
                'responseTokensIncludingReasoning': budget, 'thinking': thinking, 'nativeExecution': False,
                'runnerSha256': prepare.sha(Path(__file__).read_bytes()),
                'preparerSha256': prepare.sha(Path(prepare.__file__).read_bytes()),
                'clientSha256': prepare.sha((Path(__file__).resolve().parents[1] / 'NpcArchitectureEval/model_client.py').read_bytes())}
    (output / 'manifest.json').write_bytes(prepare.encoded(manifest))
    (output / 'server-props.json').write_bytes(prepare.encoded(props))
    (output / 'review-template.jsonl').write_bytes((prepared / 'review.jsonl').read_bytes())
    for row in rows:
        check_model()  # A changed service ends the run, never silently continues another model.
        try:
            rendered = post(row['id'], 'template', '/apply-template', {'messages': row['messages'],
                'chat_template_kwargs': {'enable_thinking': thinking}, 'add_generation_prompt': True})['response_json']['prompt']
            if not thinking:
                require_answer_template(rendered)
            tokens = post(row['id'], 'tokens', '/tokenize', {'content': rendered, 'add_special': True})['response_json']['tokens']
            if len(tokens) + budget + 32 > context:
                raise ValueError('Fixed facts exceed context budget; no truncation permitted.')
            if thinking:
                response=post(row['id'],'completion','/v1/chat/completions',{'model':model,'messages':row['messages'],
                    'max_tokens':budget,'temperature':temperature,'top_p':.9,'top_k':40,'seed':42,'stream':False,
                    'chat_template_kwargs':{'enable_thinking':True}})
                final=chat_completion(response['response_json'],model)
            else:
                response = post(row['id'], 'completion', '/completion', {'prompt': rendered, 'n_predict': budget,
                    'temperature': temperature, 'top_p': 0.9, 'top_k': 40, 'seed': 42, 'cache_prompt': False, 'stream': False})
                final=response['response_json']
            result = result_row(row['id'], final, model, response['latency_ms'])
        except (ValueError, KeyError, TypeError) as error:
            result = {'id': row['id'], 'transportVerdict': 'failed', 'error': str(error),
                      'semanticVerdict': 'not-evaluable', 'nativeExecution': False}
        _append_jsonl(output / 'results.jsonl', result)
        print(row['id'], result['transportVerdict'], flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared', type=Path, required=True)
    parser.add_argument('--endpoint', default='http://127.0.0.1:8080')
    parser.add_argument('--model')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--temperature', type=float, default=0.7)
    parser.add_argument('--execute', action='store_true')
    parser.add_argument('--gpu-released', action='store_true', help='Operator confirms the other GPU job is finished.')
    parser.add_argument('--thinking',action='store_true',help='Use native chat transport with separate final content and 2048 total output tokens.')
    args = parser.parse_args()
    rows = preflight(args.prepared, args.endpoint)
    if not args.execute:
        print(f'{len(rows)} frozen diagnostic requests validated. No network, inference or game effects.')
    else:
        if not args.gpu_released or not args.model or not args.output:
            parser.error('Execution requires GPU release, exact model ID and fresh output path.')
        if not 0 <= args.temperature <= 2:
            parser.error('Temperature outside [0,2].')
        execute(args.prepared, args.output, args.endpoint.rstrip('/'), args.model, rows, args.temperature,args.thinking)
