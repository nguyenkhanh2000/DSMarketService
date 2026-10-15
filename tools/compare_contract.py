#!/usr/bin/env python3
"""So sánh response dạng JSON của 2 môi trường; không cần thư viện bên ngoài."""
from pathlib import Path
import argparse, json, urllib.request

def get(base, route):
    with urllib.request.urlopen(base.rstrip('/')+'/'+route.lstrip('/'), timeout=15) as response:
        return response.status, response.headers.get('Content-Type'), json.loads(response.read())

def diff(x, y, prefix='$', errors=None):
    if errors is None: errors=[]
    if type(x) is not type(y): errors.append(f'{prefix}: type {type(x).__name__}!={type(y).__name__}')
    elif isinstance(x, dict):
        if x.keys() != y.keys(): errors.append(f'{prefix}: keys {list(x)} != {list(y)}')
        for k in x.keys() & y.keys(): diff(x[k], y[k], f'{prefix}.{k}', errors)
    elif isinstance(x, list):
        if len(x)!=len(y): errors.append(f'{prefix}: length {len(x)}!={len(y)}')
        for i,(a,b) in enumerate(zip(x,y)): diff(a,b,f'{prefix}[{i}]',errors)
    elif x!=y: errors.append(f'{prefix}: {str(x)[:120]} != {str(y)[:120]}')
    return errors

if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--old',required=True)
    parser.add_argument('--new',required=True)
    parser.add_argument('--cases',required=True,help='JSON array các URL path bao gồm querystring')
    args=parser.parse_args()
    failures=0
    for route in json.loads(Path(args.cases).read_text()):
        try:
            old=get(args.old,route);new=get(args.new,route)
            errors=diff(old[2],new[2]); errors += ([] if old[:2]==new[:2] else [f'HTTP header/status: {old[:2]} != {new[:2]}'])
            print(('PASS' if not errors else 'FAIL'),route)
            if errors: print('\n'.join(errors[:15]));failures+=1
        except Exception as e: print('ERROR',route,type(e).__name__,e);failures+=1
    raise SystemExit(1 if failures else 0)
