"""Seed synthetic portfolio fixtures in an explicitly isolated Docker PostgreSQL.
Requires a migrated database ending in _eval, a runtime/eval-labelled compose
project, and a loopback API using that database. Does not delete existing data.
The private manifest contains temporary tokens; keep it outside the repository.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import uuid
import httpx
from .portfolio import fixture, load_cases, require_local


def sql_quote(value):
    return "'" + str(value).replace("'", "''") + "'"

def seed(base_url, container, database, username, manifest_path, as_of):
    require_local(base_url)
    if not database.endswith('_eval'):
        raise ValueError('Use a dedicated database ending in _eval')
    inspect = json.loads(subprocess.check_output(['docker','inspect',container],text=True))[0]
    project = inspect['Config'].get('Labels',{}).get('com.docker.compose.project','')
    if project not in ('equitylens-runtime','equitylens-portfolio-eval'):
        raise ValueError('Refusing non-isolated database container')
    def sql(statements):
        return subprocess.run(['docker','exec','-i',container,'psql','-X','-v','ON_ERROR_STOP=1','-tA','-U',username,'-d',database],
                              input=statements,text=True,capture_output=True,check=True).stdout.strip()
    fixtures={}; entries={}
    for c in load_cases():
        f=fixture(c['fixture'],as_of); fixtures[c['id']]=f
        email=f"portfolio-eval-{uuid.uuid4().hex}@example.invalid"
        password=secrets.token_urlsafe(24)+'aA1!'
        with httpx.Client(base_url=base_url,timeout=120) as client:
            r=client.post('/api/auth/register',json={'email':email,'password':password,'displayName':'Eval'});r.raise_for_status()
            auth=r.json(); user_id=auth['user']['id']; token=auth['accessToken']
        # Bind API identity to this exact DB before any fixture write.
        if sql(f"select count(*) from app_user where id={sql_quote(user_id)} and email={sql_quote(email)};")!='1':
            raise ValueError('API is not using the specified isolated database')
        if c.get('saved_profile'):
            if c['saved_profile'] not in ('Conservative', 'Balanced', 'Aggressive'):
                raise ValueError('invalid synthetic profile')
            saved = httpx.put(base_url + '/api/settings/risk-preference', json={'value': c['saved_profile']}, headers={'Authorization':'Bearer '+token})
            saved.raise_for_status()
            if saved.json().get('source') != 'Manual': raise ValueError('profile was not persisted')
        pid=str(uuid.uuid4()); q=sql_quote
        commands=['BEGIN;',f"insert into portfolio(id,owner_user_id,name,base_currency) values({q(pid)},{q(user_id)},'組合甲','TWD');"]
        total_cost=0
        for a in f['assets']:
            sid=str(uuid.uuid4()); qty=a['quantity']; cost=qty*a['prices'][0];total_cost+=cost
            commands.append(f"insert into security(id,ticker,exchange,name,currency,sector) values({q(sid)},{q(a['ticker'])},{q('E'+pid[:8])},{q(a['name'])},'TWD','Technology');")
            commands.append(f"insert into portfolio_holding(portfolio_id,security_id,quantity,average_cost,cost_currency) values({q(pid)},{q(sid)},{qty},100,'TWD');")
            commands.append(f"insert into portfolio_transaction(portfolio_id,security_id,transaction_type,quantity,price,transaction_date) values({q(pid)},{q(sid)},'BUY',{qty},100,{q(f['dates'][0])});")
            rows=[f"({q(sid)},{q(date+'T00:00:00Z')},'1d',{price},{price},{price},{price},{price},'synthetic-eval-v1')" for date,price in zip(f['dates'],a['prices'])]
            commands.append('insert into market_price(security_id,price_time,interval,open,high,low,close,adjusted_close,data_source) values '+','.join(rows)+';')
        commands.append(f"insert into portfolio_cash_flow(portfolio_id,flow_type,amount,currency,effective_date,status) values({q(pid)},'Deposit',{total_cost:.6f},'TWD',{q(f['dates'][0])},'Applied');")
        commands.append('COMMIT;'); sql('\n'.join(commands))
        entries[c['id']]={'portfolio_id':pid,'token':token}
    manifest={'suite':'portfolio-v1','api_url':base_url,'as_of':as_of,'cases':entries,'fixtures':fixtures,
              'fixture_hash':hashlib.sha256(json.dumps(fixtures,sort_keys=True).encode()).hexdigest()}
    fd=os.open(manifest_path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'w') as file:json.dump(manifest,file)
    print(f'Seeded {len(entries)} isolated portfolios; private manifest saved')

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--base-url',required=True);p.add_argument('--container',required=True)
    p.add_argument('--database',required=True);p.add_argument('--username',required=True)
    p.add_argument('--manifest',required=True);p.add_argument('--as-of',default=dt.date.today().isoformat())
    a=p.parse_args();seed(a.base_url,a.container,a.database,a.username,a.manifest,a.as_of)
