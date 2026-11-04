"""Local admission proxy for explicitly budgeted DeepSeek Flash evals.
Reserve UTF-8 payload bytes + framing as input tokens and max_tokens as output.
Unknown usage/network outcomes consume the entire reservation. Ledger is private.
Prices are conservative peak rates, not cache-discount estimates.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import threading
import uuid
import httpx

class Budget:
    def __init__(self, cap, ledger):
        if cap <= 0: raise ValueError('positive cap required')
        self.cap=cap; self.spent=0.; self.pending={}; self.lock=threading.Lock(); self.ledger=Path(ledger)
        if self.ledger.exists(): raise ValueError('Use a new ledger; do not reset an existing budget')
    def log(self, event):
        with self.ledger.open('a') as f:f.write(json.dumps(event)+'\n')
    def reserve(self, payload):
        if payload.get('model') not in ('deepseek-v4-flash','deepseek-flash'): raise ValueError('unpriced model')
        output=payload.get('max_tokens')
        if not isinstance(output,int) or output <= 0 or output > 32768:raise ValueError('bounded max_tokens required')
        amount=((len(json.dumps(payload,ensure_ascii=False).encode())+1024)*.3+output*1.2)/1e6
        with self.lock:
            if self.spent+sum(self.pending.values())+amount > self.cap: raise ValueError('budget exhausted')
            key=uuid.uuid4().hex;self.pending[key]=amount;self.log({'id':key,'reserved_usd':amount})
        return key
    def finish(self,key,usage,status):
        if not isinstance(usage,dict): usage={}
        with self.lock:
            reserve=self.pending.pop(key)
            charge=(usage['prompt_tokens']*.3+usage['completion_tokens']*1.2)/1e6 if all(isinstance(usage.get(k),int) and usage[k]>=0 for k in ('prompt_tokens','completion_tokens')) else reserve
            self.spent+=charge
            self.log({'id':key,'charged_upper_usd':charge,'status':status,'usage':usage,'total_upper_usd':self.spent})

def serve(budget, upstream, port):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_POST(self):
            if self.path!='/chat/completions':self.send_error(403);return
            try:
                body=self.rfile.read(int(self.headers['Content-Length']));payload=json.loads(body);key=budget.reserve(payload)
            except (ValueError,KeyError):
                with budget.lock: budget.log({'status':402,'admission_refused':True})
                self.send_error(402,'Budget or model admission refused');return
            status=502;data=b'upstream unavailable';usage={}
            try:
                r=httpx.post(upstream.rstrip('/')+self.path,content=body,
                    headers={'Authorization':self.headers.get('Authorization',''),'Content-Type':'application/json'},timeout=180)
                status=r.status_code;data=r.content;usage=r.json().get('usage',{})
            except Exception: pass
            finally: budget.finish(key,usage,status)
            self.send_response(status);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(data)
    server=ThreadingHTTPServer(('127.0.0.1',port),Handler)
    threading.Thread(target=server.serve_forever,daemon=True).start()
    return server
