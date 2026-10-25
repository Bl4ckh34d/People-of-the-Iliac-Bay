"""Same frozen focused speech protocol through each local model's native chat template."""
import argparse,hashlib,json
from pathlib import Path
from model_client import post_json
from run_evaluation import get_json,game_closed
from run_speech_stage_trial import payload,state_for,MAP,OPTIONS

ap=argparse.ArgumentParser();ap.add_argument('--run-dir',required=True);ap.add_argument('--url',default='http://127.0.0.1:8080');ap.add_argument('--reasoning',action='store_true');ap.add_argument('--max-tokens',type=int,default=160);args=ap.parse_args()
assert 160 <= args.max_tokens <= 2048
game_closed();out=Path(args.run_dir);out.mkdir(parents=True,exist_ok=False)
suite=Path('tools/NpcArchitectureEval/fixtures/speech-act-heldout-20261022.json')
cases=json.loads(suite.read_text(encoding='utf-8'))['cases']
model=get_json(args.url+'/v1/models')['data'][0]['id'];props=get_json(args.url+'/props')
(out/'manifest.json').write_text(json.dumps({'model':model,'props':props,'maxTokens':args.max_tokens,'reasoningRequested':args.reasoning,'suiteSha256':hashlib.sha256(suite.read_bytes()).hexdigest(),'protocolSha256':hashlib.sha256(Path('tools/NpcArchitectureEval/run_speech_stage_trial.py').read_bytes()).hexdigest()},indent=2))
for c in cases:
 for reverse in [False,True]:
  assert get_json(args.url+'/v1/models')['data'][0]['id']==model, 'Model changed during run'
  data=payload('qwen',state_for(c['input']),reverse);data['model']=model
  data['chat_template_kwargs']={'enable_thinking':args.reasoning};data['max_tokens']=args.max_tokens
  response=post_json(args.url+'/v1/chat/completions',data,out/'attempts.jsonl',{'case':c['id'],'reverse':reverse,'model':model})
  choice=None;reading=None
  try:
   body=response['response_json'];assert response['status_code']==200 and body['model']==model
   assert body['choices'][0]['finish_reason']=='stop', 'Incomplete response'
   answer=json.loads(body['choices'][0]['message']['content']);choice=answer['choice'];reading=answer.get('reading');assert choice in OPTIONS
  except (AssertionError,KeyError,TypeError,ValueError):choice=None
  allowed=[MAP.get(x,x) for x in c['expected']['allowedChoices']]
  row={'id':c['id'],'model':model,'reverse':reverse,'choice':choice,'reading':reading,'allowed':allowed,'correct':choice in allowed,'falseThreat':choice=='DirectThreat' and 'DirectThreat' not in allowed,'latencyMs':response['latency_ms']}
  with (out/'results.jsonl').open('a') as f:f.write(json.dumps(row)+'\n')
 print(c['id'],flush=True)
rows=[json.loads(x) for x in (out/'results.jsonl').read_text().splitlines()]
summary={'count':len(rows),'correct':sum(x['correct'] for x in rows),'falseThreat':sum(x['falseThreat'] for x in rows)}
(out/'summary.json').write_text(json.dumps(summary));print(summary)
