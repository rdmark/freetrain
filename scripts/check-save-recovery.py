#!/usr/bin/env python3
"""Exercise real server process crash, graceful restart and independent backup restore.
Requires a Release backend build. Writes measurement evidence under tmp/ only.
"""
import http.cookiejar
import json
import os
from pathlib import Path
import selectors
import secrets
import socket
import struct
from urllib.parse import urlsplit
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parent.parent
DLL = ROOT / 'src/FreeTrain.Server/bin/Release/net10.0/FreeTrain.Server.dll'
BOOTSTRAP = '/api/bootstrap?protocolVersion=1&contentVersion=prototype-content-1'

class Server:
    def __init__(self, directory):
        environment = dict(os.environ, FreeTrain__Saves__Directory=str(directory), FreeTrain__Saves__Interval='00:00:30',
                           FreeTrain__Network__DisconnectGrace='00:00:00')
        started = time.perf_counter()
        self.process = subprocess.Popen(['dotnet', str(DLL), '--urls', 'http://127.0.0.1:0'], cwd=ROOT,
                                        env=environment, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, bufsize=1)
        self.cookies = http.cookiejar.CookieJar()
        self.sockets = []
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies))
        selector = selectors.DefaultSelector()
        selector.register(self.process.stdout, selectors.EVENT_READ)
        lines = []
        while time.perf_counter() - started < 12:
            if selector.select(timeout=0.2):
                line = self.process.stdout.readline()
                lines.append(line)
                if 'Now listening on:' in line:
                    self.url = line.split('Now listening on:', 1)[1].strip()
                    self.join = self.request(BOOTSTRAP)
                    self.start_ms = (time.perf_counter() - started) * 1000
                    selector.close()
                    return
                if not line and self.process.poll() is not None:
                    break
        selector.close()
        self.stop(crash=True)
        raise RuntimeError('Server did not start: ' + ''.join(lines)[-4000:])

    def request(self, path, data=None):
        request = urllib.request.Request(self.url + path, data=json.dumps(data).encode() if data is not None else None,
                                         headers={'Content-Type': 'application/json'} if data is not None else {})
        with self.opener.open(request, timeout=8) as response:
            payload = response.read()
            return json.loads(payload) if payload else None

    def upload(self, save):
        epoch = self.request(BOOTSTRAP)['worldEpoch']
        return self.request('/api/saves/upload?expectedWorldEpoch=' + epoch, save)

    def rename(self, name):
        # Minimal stdlib WebSocket client: one real command, no third-party runtime.
        address = urlsplit(self.url)
        connection = socket.create_connection((address.hostname, address.port), timeout=8)
        self.sockets.append(connection)
        stream = connection.makefile('rb')
        key = __import__('base64').b64encode(secrets.token_bytes(16)).decode()
        cookie = '; '.join(f'{item.name}={item.value}' for item in self.cookies)
        path = '/api/session?protocolVersion=1&contentVersion=prototype-content-1'
        request = (f'GET {path} HTTP/1.1\r\nHost: {address.netloc}\r\nUpgrade: websocket\r\n'
                   f'Connection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: {key}\r\nCookie: {cookie}\r\n\r\n')
        connection.sendall(request.encode())
        assert b'101' in stream.readline()
        while stream.readline() != b'\r\n':
            pass
        def receive():
            header = stream.read(2)
            assert len(header) == 2 and header[0] & 15 == 1, header
            length = header[1] & 127
            if length == 126: length = struct.unpack('!H', stream.read(2))[0]
            if length == 127: length = struct.unpack('!Q', stream.read(8))[0]
            return json.loads(stream.read(length))
        join = receive()
        station = join['snapshot']['stations'][0]
        payload = json.dumps(dict(type='command', requestId=secrets.token_hex(8), expectedWorldEpoch=join['worldEpoch'],
                                  payload=dict(kind='renameStation', stationId=station['id'], expectedRevision=station['revision'], name=name))).encode()
        mask = secrets.token_bytes(4)
        header = bytes([0x81, 0x80 | 126]) + struct.pack('!H', len(payload))
        connection.sendall(header + mask + bytes(value ^ mask[index % 4] for index, value in enumerate(payload)))
        for _ in range(40):
            message = receive()
            if message['type'] == 'accepted': return
            assert message['type'] != 'rejected', message
        raise RuntimeError('Rename was not acknowledged')

    def stop(self, crash=False):
        if self.process.poll() is None:
            self.process.kill() if crash else self.process.terminate()
        try:
            self.process.communicate(timeout=8)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.communicate()
            raise
        for connection in self.sockets: connection.close()
        if not crash:
            assert self.process.returncode == 0, self.process.returncode


def main():
    with tempfile.TemporaryDirectory(prefix='freetrain-recovery-') as temporary:
        directory = Path(temporary) / 'live'
        backup = Path(temporary) / 'independent-backup'
        backup.mkdir()
        fixture = json.loads((ROOT / 'prototype/protocol/fixtures/save-reference.json').read_text())
        fixture['world']['stations'][0]['name'] = '独立バックアップ駅'
        first = Server(directory)
        try:
            first.upload(fixture)
            epoch = first.request(BOOTSTRAP)['worldEpoch']
            started = time.perf_counter()
            first.request('/api/saves/checkpoint/save?expectedWorldEpoch=' + epoch, {})
            save_ms = (time.perf_counter() - started) * 1000
            checkpoint = directory / 'checkpoint.json'
            assert json.loads(checkpoint.read_text())['world']['stations'][0]['name'] == '独立バックアップ駅'
            shutil.copy2(checkpoint, backup / 'checkpoint.json')
            bytes_saved = checkpoint.stat().st_size
            incarnation = first.join['serverIncarnation']
            first.rename('クラッシュで失われる変更')
            assert first.request(BOOTSTRAP)['snapshot']['stations'][0]['name'] == 'クラッシュで失われる変更'
        finally:
            first.stop(crash=True)
        second = Server(directory)
        try:
            assert second.join['serverIncarnation'] != incarnation
            assert second.join['snapshot']['paused'] is True
            assert second.join['snapshot']['stations'][0]['name'] == '独立バックアップ駅'
            crash_restart_ms = second.start_ms
            second.rename('正常終了で保存した駅')
        finally:
            second.stop()
        third = Server(directory)
        try:
            assert third.join['snapshot']['stations'][0]['name'] == '正常終了で保存した駅'
            assert third.join['snapshot']['paused'] is True
        finally:
            third.stop()
        # Restore from a copy outside the live directory into a fresh save volume.
        restored = Path(temporary) / 'restored'
        restored.mkdir()
        shutil.copy2(backup / 'checkpoint.json', restored / 'checkpoint.json')
        fourth = Server(restored)
        try:
            assert fourth.join['snapshot']['stations'][0]['name'] == '独立バックアップ駅'
            assert fourth.join['snapshot']['paused'] is True
            restore_ms = fourth.start_ms
        finally:
            fourth.stop()
        measurement = dict(saveBytes=bytes_saved, saveHttpRoundTripMs=round(save_ms, 2), crashRestartMs=round(crash_restart_ms, 2),
                           independentRestoreMs=round(restore_ms, 2), checkpointIntervalSeconds=30,
                           checks=['SIGKILL loses only changes after checkpoint', 'restart changes incarnation and starts paused',
                                   'SIGTERM writes final paused checkpoint', 'independent backup restores on fresh directory'],
                           scope='macOS local Release C# service with temporary save directories; Linux persistent-volume deployment remains Stage 7')
        (ROOT / 'tmp/web-prototype-save-measurements.json').write_text(json.dumps(measurement, indent=2, ensure_ascii=False) + '\n')
        print(json.dumps(measurement, indent=2, ensure_ascii=False))

if __name__ == '__main__':
    main()
