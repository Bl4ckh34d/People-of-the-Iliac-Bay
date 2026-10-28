"""Compare CPU preparation on one existing complete WAV without changing its audio."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import tempfile
import time

from align_words import run_alignment
from rhubarb_visemes import run_rhubarb


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--wav', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--rhubarb', type=Path, required=True)
    parser.add_argument('--aligner', type=Path, required=True)
    parser.add_argument('--model', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    text = json.loads(args.evidence.read_text(encoding='utf-8-sig'))['finalizedText']
    rows = []
    with tempfile.TemporaryDirectory(prefix='speech-preparation-', dir=args.output.parent) as temporary:
        root = Path(temporary)

        def measure(kind, threads, label):
            path = root / (label + '.json')
            start = time.perf_counter()
            if kind == 'rhubarb':
                result = run_rhubarb(str(args.rhubarb), str(args.wav), text, str(path), threads=threads)
                values = result['cues']
            else:
                result = run_alignment(str(args.aligner), str(args.model), str(args.wav), text, str(path), threads=threads)
                values = result['words']
            row = dict(kind=kind, threads=threads, seconds=round(time.perf_counter()-start, 4),
                       count=len(values), timingHash=hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest())
            print(json.dumps(row), flush=True)
            return row

        for threads in (2, 4, 8):
            rows.append(measure('rhubarb', threads, 'rhubarb-' + str(threads)))
        for threads in (1, 4, 8):
            rows.append(measure('alignment', threads, 'alignment-' + str(threads)))
        best = {kind: min((row for row in rows if row['kind'] == kind), key=lambda row: row['seconds'])
                for kind in ('rhubarb', 'alignment')}
        start = time.perf_counter()
        with ThreadPoolExecutor(max_workers=2) as pool:
            jobs = [pool.submit(measure, kind, best[kind]['threads'], 'parallel-' + kind) for kind in best]
            parallel = [job.result() for job in jobs]
        elapsed = time.perf_counter() - start
        report = dict(wavSha256=hashlib.sha256(args.wav.read_bytes()).hexdigest(),
                      text=text, sequential=rows, parallel=parallel, parallelSeconds=round(elapsed, 4),
                      audioRegenerated=False, recognizer='pocketSphinx')
        args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
        print('Parallel total: {:.3f}s'.format(elapsed), flush=True)


if __name__ == '__main__':
    main()
