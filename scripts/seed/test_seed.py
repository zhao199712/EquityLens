import copy
import datetime as dt
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import seed
from fetch import SafeRedirect
from urllib.request import Request


def fixture():
    selected = seed.read_selection(seed.ROOT/'data/seed/constituents-2026-11-04.json')
    tables = {t: [] for t in seed.TABLES}
    for code in selected['selected']:
        sid = seed.uid(code)
        tables['security'].append(dict(id=sid, ticker=code, exchange='TWSE', currency='TWD'))
        for day in range(360):
            date = str(dt.date(2025, 1, 1) + dt.timedelta(days=day))
            tables['market_price'].append(dict(id=seed.uid(code+date), security_id=sid,
                price_time=date+'T00:00:00Z', interval='1d', close=100+day/100, adjusted_close=100+day/100))
    tables['uploaded_file'] = [dict(id='file', uploaded_by_user_id=None, storage_provider='SeedMetadata',
        upload_status='Unavailable', object_key='unavailable/file.pdf', bucket_name='seed-unavailable')]
    tables['document'] = [dict(id='document', uploaded_file_id='file')]
    tables['document_chunk'] = [dict(id='chunk', document_id='document')]
    tables['document_embedding'] = [dict(id='vector', document_chunk_id='chunk', embedding_model='text-embedding-3-small',
        dimensions=1536, embedding=json.dumps([0.1]*1536))]
    return dict(formatVersion=1, tables=tables, selection=selected, asOf=date, migrations=['test'])


class SeedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.valid = fixture()

    def test_complete_bundle(self):
        self.assertEqual(seed.validate(self.valid)['commonPriceDates'], 360)

    def test_zero_price_rejected(self):
        b=copy.deepcopy(self.valid); b['tables']['market_price'][0]['close']=0
        with self.assertRaisesRegex(ValueError, 'closing price'): seed.validate(b)

    def test_orphan_rejected(self):
        b=copy.deepcopy(self.valid); b['tables']['document_chunk'][0]['document_id']='absent'
        with self.assertRaisesRegex(ValueError, 'Orphan'): seed.validate(b)

    def test_incomplete_embedding_rejected(self):
        b=copy.deepcopy(self.valid); b['tables']['document_embedding']=[]
        with self.assertRaisesRegex(ValueError, 'cover every chunk'): seed.validate(b)

    def test_wrong_dimension_rejected(self):
        b=copy.deepcopy(self.valid); b['tables']['document_embedding'][0]['dimensions']=10
        with self.assertRaisesRegex(ValueError, 'dimension'): seed.validate(b)

    def test_production_identity_rejected(self):
        b=copy.deepcopy(self.valid); b['tables']['uploaded_file'][0]['uploaded_by_user_id']='real-user'
        with self.assertRaisesRegex(ValueError, 'identity'): seed.validate(b)

    def test_production_target_rejected_before_docker(self):
        with patch('seed.run', side_effect=AssertionError('must not call Docker')):
            with self.assertRaises(ValueError): seed.target_guard('equitylens-prod-postgres', 'equitylens')
            with self.assertRaises(ValueError): seed.target_guard('equitylens-prod-postgres', 'equitylens_cloud_seed')

    def test_unlabelled_target_rejected(self):
        with patch('seed.run', return_value='{}'):
            with self.assertRaisesRegex(ValueError, 'label'): seed.target_guard('equitylens-seed-postgres', 'equitylens_cloud_seed')

    def test_checksum_rejected_before_decompression(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d); (p/'equitylens-seed-v1.json.gz').write_bytes(b'bad')
            (p/'manifest.json').write_text(json.dumps(dict(asset='equitylens-seed-v1.json.gz', bytes=3, sha256='0'*64)))
            with self.assertRaisesRegex(ValueError, 'checksum'): seed.load_bundle(p)

    def test_asset_path_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            (Path(d)/'manifest.json').write_text(json.dumps(dict(asset='../private')))
            with self.assertRaisesRegex(ValueError, 'asset'): seed.load_bundle(d)

    def test_redirect_drops_authorization(self):
        request=Request('https://api.github.com/repos/a/b/releases/assets/1', headers={'Authorization':'Bearer dummy'})
        result=SafeRedirect().redirect_request(request,None,302,'Found',{},'https://release-assets.githubusercontent.com/a')
        self.assertFalse(result.has_header('Authorization'))

    def test_demo_ownership_quantities_costs_and_cash(self):
        rows=seed.demo_rows(self.valid, ['dummy-1', 'dummy-2'])
        self.assertEqual(len(rows['app_user']),2)
        self.assertEqual(len(rows['portfolio']),5)
        for holding in rows['portfolio_holding']:
            tx=[r for r in rows['portfolio_transaction'] if r['portfolio_id']==holding['portfolio_id'] and r['security_id']==holding['security_id']]
            buy=next(r for r in tx if r['transaction_type']=='BUY')
            sell=next(r for r in tx if r['transaction_type']=='SELL')
            self.assertEqual(holding['quantity'],buy['quantity']-sell['quantity'])
            self.assertAlmostEqual(holding['average_cost'],(buy['quantity']*buy['price']+buy['fee'])/buy['quantity'],places=5)
        for portfolio in rows['portfolio']:
            pid=portfolio['id']
            flows=sum(r['amount']*(1 if r['flow_type']=='Deposit' else -1) for r in rows['portfolio_cash_flow'] if r['portfolio_id']==pid)
            trade=sum((r['quantity']*r['price']*(1 if r['transaction_type']=='SELL' else -1)-r['fee']) for r in rows['portfolio_transaction'] if r['portfolio_id']==pid)
            self.assertGreaterEqual(flows+trade,0)
        self.assertEqual({r['role'] for r in rows['app_user']},{'User'})

    def test_sql_transport_does_not_execute_bundle_text(self):
        sql=seed.insert_rows('document',[{'title':"'); DROP TABLE app_user; --",'id':'x'}])[0]
        self.assertNotIn('DROP TABLE',sql)
        with self.assertRaises(ValueError): seed.insert_rows('app_user;DROP TABLE security',[])


if __name__=='__main__': unittest.main()
