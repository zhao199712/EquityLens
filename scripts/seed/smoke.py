#!/usr/bin/env python3
"""Exercise actual local API authentication, ownership, valuation and C# risk (no AI)."""
import argparse
import json
from pathlib import Path
import urllib.error
import urllib.request
from urllib.parse import urlparse
from seed import load_bundle, psql, target_guard, uid


def smoke(base, state, data):
    parsed = urlparse(base)
    if parsed.scheme != 'http' or parsed.hostname not in ('localhost', '127.0.0.1'):
        raise ValueError('Smoke accepts a local seed API only')
    target_guard('equitylens-seed-postgres', 'equitylens_cloud_seed')
    bundle, _ = load_bundle(data)
    credentials = json.loads((state/'credentials.json').read_text())
    def request(path, token=None, body=None, status=200):
        headers={'Content-Type':'application/json'}
        if token: headers['Authorization']='Bearer '+token
        req=urllib.request.Request(base+path, headers=headers, data=json.dumps(body).encode() if body is not None else None)
        try:
            with urllib.request.urlopen(req,timeout=90) as response:
                code=response.status; raw=response.read()
        except urllib.error.HTTPError as error:
            code=error.code; raw=error.read()
        if code != status: raise ValueError(f'{path.split("?")[0]} returned {code}, expected {status}')
        return json.loads(raw) if raw else None
    tokens=[]
    for account in credentials['accounts']:
        login=request('/api/auth/login',body=account)
        assert login['user']['role']=='User'
        tokens.append(login['accessToken'])
    first=request('/api/portfolios',tokens[0]); second=request('/api/portfolios',tokens[1])
    assert {r['id'] for r in first}=={uid('portfolio-'+k) for k in ['etf','diversified','concentrated','empty']}
    assert {r['id'] for r in second}=={uid('portfolio-owner-check')}
    request('/api/portfolios/'+uid('portfolio-etf'),tokens[1],status=404)
    pid=uid('portfolio-diversified')
    valuation=request('/api/portfolios/'+pid+'/valuation',tokens[0])
    assert valuation['totalMarketValue']>0
    history=request(f'/api/portfolios/{pid}/valuation/history?from=2026-06-01&to={bundle["asOf"]}',tokens[0])
    assert len(history['points'])>100 and history['points'][-1]['totalAssetValue']>0
    assert history['benchmark'] and any(p['normalizedValue'] is not None for p in history['benchmark'])
    assert history['points'][-1]['cashBalance']>=0
    risk=request(f'/api/portfolios/{pid}/risk?from=2025-01-01&to={bundle["asOf"]}&simulations=1000',tokens[0])
    assert risk['portfolioId']==pid
    file_id=bundle['tables']['uploaded_file'][0]['id']
    request(f'/api/files/{file_id}/download-url',tokens[0],status=404)
    hits=psql('equitylens-seed-postgres','equitylens_cloud_seed',"SELECT count(*) FROM search_document_chunks(query_text=>'營收',ticker_filter=>'2330',top_k=>5,mode=>'bm25'); SELECT count(*) FROM search_document_chunks(query_embedding=>(SELECT embedding FROM document_embedding LIMIT 1),top_k=>5,mode=>'vector');")
    counts=[int(x) for x in hits.splitlines()]
    assert len(counts)==2 and all(x>0 for x in counts)
    print(json.dumps({'loginAccounts':2,'portfolios':len(first)+len(second),'crossOwner':'404',
        'valuationPoints':len(history['points']),'benchmark':'present','risk':'calculated',
        'missingPdf':'404','bm25Hits':counts[0],'vectorHits':counts[1],'paidAiCalls':0},indent=2))


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--base-url',default='http://127.0.0.1:55440')
    p.add_argument('--state',type=Path,default=Path('/tmp/equitylens-seed-runtime'))
    p.add_argument('--data',type=Path,default=Path('/tmp/equitylens-seed-v1'))
    a=p.parse_args(); smoke(a.base_url,a.state,a.data)
