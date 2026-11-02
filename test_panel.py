import copy
import http.client
import json
import os
import subprocess
import shutil
from pathlib import Path
import tempfile
import threading
import time
import unittest
from http.server import ThreadingHTTPServer

import panel


class PanelTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.config = {'enabled': True, 'springs': copy.deepcopy(panel.DEFAULTS)}

    def tearDown(self):
        self.temp.cleanup()

    def test_round_trip_and_disable(self):
        self.config['springs'][0] = [0, 12.5, 500, 20, 600, .25, 1.5]
        panel.write_config(self.config, self.root)
        self.assertEqual(panel.read_config(self.root), (self.config, None))
        self.config['enabled'] = False
        panel.write_config(self.config, self.root)
        self.assertEqual(panel.read_config(self.root), (self.config, None))
        self.assertEqual(list(self.root.glob('.tuning-*')), [])

    def test_legacy_migration(self):
        (self.root / 'tuning.txt').write_text('1 1 411.1 30 368.6 1000 60 60 4000 200 200\n')
        config, error = panel.read_config(self.root)
        self.assertIsNone(error)
        self.assertEqual(config['springs'][0], [411.1, 30, 30, 368.6, 368.6, .5, .5])
        panel.write_config(config, self.root)
        self.assertTrue((self.root / 'tuning.txt').read_text().startswith('3 1 '))
        for value in [0, -1, 101, float('nan')]:
            config['springs'][0][5] = value
            with self.assertRaises(ValueError): panel.validate(config)

    def test_v2_migration_preserves_linear_rows(self):
        rows = [[411.1, 12, 23, 34, 45, .25, .75], [2345, 66, 77, 88, 99, 1, 2], [5555, 110, 220, 330, 440, 3, 4]]
        text = '2 1 ' + ' '.join(str(v) for row in rows for v in row)
        (self.root / 'tuning.txt').write_text(text)
        config, error = panel.read_config(self.root)
        self.assertIsNone(error)
        self.assertEqual(config['springs'][:3], rows)
        self.assertEqual(config['springs'][3:], panel.DEFAULTS[3:])
        config['springs'][4] = [2222, 1, 2, 3, 4, .1, .2]
        panel.write_config(config, self.root)
        self.assertEqual(len((self.root / 'tuning.txt').read_text().split()), 44)
        self.assertEqual(panel.read_config(self.root)[0], config)
        self.assertEqual(panel.read_config(self.root)[0]['springs'][:3], rows)

    def test_torsion_stock_defaults(self):
        self.assertEqual([row[:5] for row in panel.DEFAULTS[3:]], [[5000, 20, 20, 20, 20], [20000, 100, 100, 100, 100], [50000, 100, 100, 100, 100]])

    def test_all_six_types_validate_bounds(self):
        for k in range(6):
            for j, value in [(0, 100001), (1, 20001), (4, -1), (5, 0), (6, 101)]:
                bad = copy.deepcopy(self.config)
                bad['springs'][k][j] = value
                with self.subTest(k=k, j=j), self.assertRaises(ValueError):
                    panel.validate(bad)

    @unittest.skipUnless(shutil.which('node'), 'Node is needed for the dropdown interaction test')
    def test_dropdown_preserves_unsaved_edits_and_selected_defaults(self):
        script = (Path(panel.__file__).parent / 'panel.html').read_text().split('<script>')[1].split('</script>')[0]
        harness = r"""
const vm=require('node:vm'),assert=require('node:assert/strict');
const registry={};
class Element {
 constructor(){this.listeners={};this.children=[];this.value='';}
 set id(value){this._id=value;registry[value]=this;} get id(){return this._id;}
 appendChild(child){this.children.push(child);return child;}
 replaceChildren(...children){this.children=children;}
 addEventListener(event,callback){this.listeners[event]=callback;}
 setAttribute(){} reportValidity(){return true;}
}
const document={createElement:()=>new Element(),getElementById:id=>registry[id]||(registry[id]=new Element()),querySelectorAll:()=>[]};
const context=vm.createContext({document,fetch:()=>new Promise(()=>{}),setInterval:()=>{},console});
vm.runInContext(SCRIPT,context);
vm.runInContext(`
$('v0').value='1234';
$('spring-type').value='4';$('spring-type').listeners.change();
$('v1').value='321';
$('spring-type').value='0';$('spring-type').listeners.change();
if($('v0').value!=='1234')throw Error('Linear draft lost');
$('spring-type').value='4';$('spring-type').listeners.change();
if($('v1').value!=='321')throw Error('Torsion draft lost');
$('defaults').listeners.click();
if(Number($('v1').value)!==defaults[4][1])throw Error('Selected defaults failed');
if(values()[0][0]!==1234)throw Error('Defaults altered another spring');
if(values().length!==6)throw Error('Wrong spring count');
if($('spring-help').textContent.indexOf('radians')<0)throw Error('Missing torsion units');
$('v1').value='';$('spring-type').value='1';$('spring-type').listeners.change();
let rejected=false;try{values();}catch(e){rejected=true;}
if(!rejected)throw Error('Hidden invalid draft was accepted');
`,context);
"""
        completed = subprocess.run(['node', '-e', harness.replace('SCRIPT', json.dumps(script))], capture_output=True, text=True)
        self.assertEqual(completed.returncode, 0, completed.stderr)

    def test_invalid_values_cannot_overwrite_valid_settings(self):
        panel.write_config(self.config, self.root)
        original = (self.root / 'tuning.txt').read_bytes()
        for value in [-1, 100001, float('inf'), float('nan'), True, '500']:
            with self.subTest(value=value):
                bad = copy.deepcopy(self.config)
                bad['springs'][0][0] = value
                with self.assertRaises(ValueError):
                    panel.write_config(bad, self.root)
                self.assertEqual((self.root / 'tuning.txt').read_bytes(), original)
        for bad in [{}, {'enabled': 1, 'springs': panel.DEFAULTS}, {'enabled': True, 'springs': [[1, 2, 3]]}]:
            with self.assertRaises(ValueError):
                panel.validate(bad)

    def test_invalid_file_and_stale_status(self):
        (self.root / 'tuning.txt').write_text('1 1 nan 30 30 1000 60 60 4000 200 200\n')
        _, error = panel.read_config(self.root)
        self.assertIsNotNone(error)
        path = self.root / 'status.json'
        path.write_text(json.dumps({'state': 'hooked', 'enabled': True}))
        self.assertTrue(panel.snapshot(self.root)['fresh'])
        old = time.time() - 10
        os.utime(path, (old, old))
        self.assertFalse(panel.snapshot(self.root)['fresh'])

    def test_api_requires_origin_and_validates_payload(self):
        server = ThreadingHTTPServer(('127.0.0.1', 0), panel.Handler)
        server.root = self.root
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        origin = f'http://127.0.0.1:{server.server_port}'
        def post(data, origin_header):
            connection = http.client.HTTPConnection('127.0.0.1', server.server_port)
            headers = {'Content-Type': 'application/json'}
            if origin_header:
                headers['Origin'] = origin_header
            connection.request('POST', '/api/config', json.dumps(data), headers)
            response = connection.getresponse()
            result = response.status
            response.read()
            connection.close()
            return result
        try:
            self.assertEqual(post(self.config, None), 403)
            self.assertEqual(post(self.config, 'https://example.com'), 403)
            self.assertFalse((self.root / 'tuning.txt').exists())
            self.assertEqual(post({}, origin), 400)
            self.assertEqual(post(self.config, origin), 200)
            self.assertEqual(panel.read_config(self.root)[0], self.config)
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == '__main__':
    unittest.main()
