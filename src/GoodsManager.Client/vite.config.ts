import { defineConfig } from 'vite';
import { readFileSync } from 'node:fs';

const notices = readFileSync(new URL('../../third-party/application/client-notices.txt', import.meta.url), 'utf8');

export default defineConfig({
    plugins: [{
        name: 'application-notices',
        augmentChunkHash() {
            return notices;
        },
        generateBundle(_options, bundle) {
            // 最適化によるコメント削除の後に同梱する。上のハッシュ拡張で表示文の変更もURLへ反映する。
            for (const chunk of Object.values(bundle)) {
                if (chunk.type === 'chunk') {
                    chunk.code = '/*!\n' + notices + '\n*/\n' + chunk.code;
                }
            }

            this.emitFile({ type: 'asset', fileName: 'notices/client-notices.txt', source: notices });
        }
    }],
    build: {
        outDir: '../GoodsManager.Web/wwwroot',
        emptyOutDir: true
    },
    server: { proxy: { '/api': { target: 'https://localhost:7443', secure: true } } }
});
