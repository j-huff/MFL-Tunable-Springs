#!/usr/bin/env python3
"""Local, dependency-free control panel for the Mars First Logistics spring mod."""
import argparse
import json
import math
import os
from pathlib import Path
import tempfile
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import webbrowser

ROOT = Path(__file__).resolve().parent
DEFAULTS = [[400, 30, 30, 30, 30, .5, .5], [1000, 60, 60, 60, 60, .5, .5], [4000, 200, 200, 200, 200, .5, .5],
            [5000, 20, 20, 20, 20, .5, .5], [20000, 100, 100, 100, 100, .5, .5], [50000, 100, 100, 100, 100, .5, .5]]


def validate(data):
    if not isinstance(data, dict) or set(data) != {'enabled', 'springs'}:
        raise ValueError('Expected enabled and springs fields.')
    if type(data['enabled']) is not bool:
        raise ValueError('enabled must be a boolean.')
    rows = data['springs']
    if not isinstance(rows, list) or len(rows) != 6:
        raise ValueError('Expected six spring settings.')
    for row in rows:
        if not isinstance(row, list) or len(row) != 7:
            raise ValueError('Each spring needs stiffness, four damping values, and two shaft-speed crossovers.')
        for i, value in enumerate(row):
            maximum = 100000 if i == 0 else (100 if i >= 5 else 20000)
            minimum = .001 if i >= 5 else 0
            if type(value) not in (int, float) or not math.isfinite(value) or not minimum <= value <= maximum:
                raise ValueError(f'Values must be finite and between {minimum} and {maximum}.')
    return data


def read_config(root=ROOT):
    try:
        words = (root / 'tuning.txt').read_text().split()
        if len(words) == 11 and words[0] == '1' and words[1] in ('0', '1'):
            values = [float(v) for v in words[2:]]
            rows = [[values[i], values[i+1], values[i+1], values[i+2], values[i+2], .5, .5] for i in (0, 3, 6)]
        elif len(words) == 23 and words[0] == '2' and words[1] in ('0', '1'):
            values = [float(v) for v in words[2:]]
            rows = [values[i:i+7] for i in (0, 7, 14)]
        elif len(words) == 44 and words[0] == '3' and words[1] in ('0', '1'):
            values = [float(v) for v in words[2:]]
            rows = [values[i:i+7] for i in range(0, 42, 7)]
        else:
            raise ValueError('Invalid tuning.txt format.')
        if len(rows) == 3:
            rows.extend([row[:] for row in DEFAULTS[3:]])
        return validate({'enabled': words[1] == '1', 'springs': rows}), None
    except FileNotFoundError:
        return {'enabled': False, 'springs': [row[:] for row in DEFAULTS]}, None
    except (OSError, ValueError) as error:
        return {'enabled': False, 'springs': [row[:] for row in DEFAULTS]}, str(error)


def write_config(data, root=ROOT):
    validate(data)
    line = '3 ' + str(int(data['enabled'])) + ' ' + ' '.join(format(v, '.9g') for row in data['springs'] for v in row) + '\n'
    name = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', dir=root, prefix='.tuning-', delete=False) as output:
            name = output.name
            output.write(line)
            output.flush()
            os.fsync(output.fileno())
        os.replace(name, root / 'tuning.txt')
    finally:
        if name and os.path.exists(name):
            os.unlink(name)


def snapshot(root=ROOT):
    config, error = read_config(root)
    result = {'config': config, 'config_error': error, 'status': None, 'age': None, 'fresh': False}
    try:
        path = root / 'status.json'
        with path.open() as stream:
            age = max(0, time.time() - os.fstat(stream.fileno()).st_mtime)
            status = json.load(stream)
        if not isinstance(status, dict):
            raise ValueError('Invalid status data.')
        result.update(status=status, age=age, fresh=age <= 4)
    except FileNotFoundError:
        pass
    except (OSError, ValueError) as error:
        result['status_error'] = str(error)
    return result


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):
        pass

    def respond(self, code, data, content_type='application/json'):
        body = json.dumps(data, allow_nan=False).encode() if content_type == 'application/json' else data
        self.send_response(code)
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.send_header('X-Content-Type-Options', 'nosniff')
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == '/api/status':
            self.respond(200, snapshot(self.server.root))
        elif self.path == '/':
            self.respond(200, (self.server.root / 'panel.html').read_bytes(), 'text/html; charset=utf-8')
        else:
            self.respond(404, {'error': 'Not found.'})

    def do_POST(self):
        if self.path != '/api/config':
            return self.respond(404, {'error': 'Not found.'})
        expected = f'http://127.0.0.1:{self.server.server_port}'
        if self.headers.get('Origin') != expected or self.headers.get('Host') != expected.removeprefix('http://'):
            return self.respond(403, {'error': 'Open the panel at ' + expected})
        if self.headers.get_content_type() != 'application/json':
            return self.respond(415, {'error': 'Expected application/json.'})
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if not 0 < length <= 4096:
                raise ValueError('Invalid request size.')
            data = json.loads(self.rfile.read(length))
            write_config(data, self.server.root)
        except (ValueError, UnicodeError) as error:
            return self.respond(400, {'error': str(error)})
        except OSError as error:
            return self.respond(500, {'error': str(error)})
        self.respond(200, {'saved': True, 'config': data})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--no-browser', action='store_true')
    parser.add_argument('--port', type=int, default=8765)
    args = parser.parse_args()
    try:
        server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    except OSError as error:
        parser.exit(1, f'Cannot start panel: {error}\n')
    server.root = ROOT
    url = f'http://127.0.0.1:{server.server_port}'
    print(f'Spring tuner: {url}', flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
