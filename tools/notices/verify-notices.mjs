import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';

/** 解決済み依存を審査済みの版・ライセンス文書と照合する。 */
export async function verifyNotices(root, depsPath) {
    const directory = path.join(root, 'third-party/application');
    const inventory = JSON.parse(await fs.readFile(path.join(directory, 'inventory.json'), 'utf8'));
    const client = path.join(root, 'src/GoodsManager.Client');
    const lock = JSON.parse(await fs.readFile(path.join(client, 'package-lock.json'), 'utf8'));
    const visited = new Set();

    /** ブラウザへ同梱する依存の推移的な追加・更新も見逃さない。 */
    async function visit(name) {
        if (visited.has(name)) {
            return;
        }

        visited.add(name);
        const installed = JSON.parse(await fs.readFile(path.join(client, 'node_modules', name, 'package.json'), 'utf8'));
        const reviewed = inventory.npm.find(item => item.name === name);
        if (!reviewed || reviewed.version !== installed.version || reviewed.integrity !== lock.packages['node_modules/' + name]?.integrity) {
            throw new Error('Review application dependency notices: ' + name);
        }

        for (const dependency of Object.keys(installed.dependencies || {})) {
            await visit(dependency);
        }
    }

    for (const name of ['react', 'react-dom', '@ericblade/quagga2']) {
        await visit(name);
    }
    if (visited.size !== inventory.npm.length) {
        throw new Error('Browser notice inventory contains dependencies that are no longer used.');
    }

    if (depsPath) {
        const deps = JSON.parse(await fs.readFile(depsPath, 'utf8'));
        for (const [identity, library] of Object.entries(deps.libraries)) {
            if (library.type !== 'package') {
                continue;
            }

            const [name, version] = identity.split('/');
            if (!inventory.nuget.some(item => item.name === name && item.version === version)) {
                throw new Error('Review server dependency notices: ' + identity);
            }
        }
    }

    for (const file of new Set([...inventory.npm, ...inventory.nuget].flatMap(item => item.notices))) {
        const text = await fs.readFile(path.join(directory, file), 'utf8');
        if (!text.trim()) {
            throw new Error('Empty dependency notice: ' + file);
        }
    }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    await verifyNotices(fileURLToPath(new URL('../../', import.meta.url)), process.argv[2]);
}
