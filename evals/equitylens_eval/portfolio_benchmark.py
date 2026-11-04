"""Deterministic local FinMind-compatible total-return benchmark for isolated eval."""
import datetime as dt
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import threading
from urllib.parse import urlparse, parse_qs

def serve(port):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_GET(self):
            query=parse_qs(urlparse(self.path).query)
            if urlparse(self.path).path!='/api/v4/data' or query.get('dataset')!=['TaiwanStockTotalReturnIndex']:
                self.send_error(403);return
            start=dt.date.fromisoformat(query['start_date'][0]);end=dt.date.fromisoformat(query['end_date'][0])
            if (end-start).days > 800: self.send_error(400);return
            data=[{'date':(start+dt.timedelta(days=i)).isoformat(),'price':100.0} for i in range((end-start).days+1)]
            body=json.dumps({'status':200,'data':data}).encode()
            self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(body)
    server=ThreadingHTTPServer(('127.0.0.1',port),Handler)
    threading.Thread(target=server.serve_forever,daemon=True).start()
    return server
