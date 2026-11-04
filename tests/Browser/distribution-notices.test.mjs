import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
import {verifyNotices} from '../../tools/notices/verify-notices.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const client = path.join(root, 'src/GoodsManager.Client');

test('production browser bundles carry complete reviewed notices and a separate notice file', async () => {
    await verifyNotices(root);
    const notices = await fs.readFile(path.join(root, 'third-party/application/client-notices.txt'), 'utf8');
    const bundle = await build({root: client, configFile: path.join(client, 'vite.config.ts'), logLevel: 'silent', build: {write: false}});
    const outputs = Array.isArray(bundle) ? bundle.flatMap(item => item.output) : bundle.output;
    assert.equal(outputs.find(item => item.fileName === 'notices/client-notices.txt')?.source, notices);
    const chunks = outputs.filter(item => item.type === 'chunk');
    assert.ok(chunks.length > 0);
    for (const chunk of chunks) {
        assert.ok(chunk.code.includes(notices), chunk.fileName);
    }
    for (const name of ['Meta Platforms', 'Christoph Oberhofer', 'Brandon Jones', 'Permission is hereby granted']) {
        assert.ok(notices.includes(name), name);
    }
});

test('publish rejects an unreviewed server dependency version', async t => {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'GoodsManager.Notices-'));
    t.after(() => fs.rm(directory, {recursive: true, force: true}));
    const deps = path.join(directory, 'app.deps.json');
    await fs.writeFile(deps, JSON.stringify({libraries: {'SkiaSharp/999.0.0': {type: 'package'}}}));
    await assert.rejects(verifyNotices(root, deps), /Review server dependency notices: SkiaSharp\/999\.0\.0/);
});
