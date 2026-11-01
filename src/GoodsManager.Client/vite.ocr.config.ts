import {defineConfig} from 'vite';
export default defineConfig({
  base:'./',
  resolve:{alias:{'onnxruntime-web':'onnxruntime-web/wasm'}},
  worker:{format:'es',rolldownOptions:{output:{entryFileNames:'workers/[name].js',chunkFileNames:'assets/[name]-[hash].js',assetFileNames:'assets/[name]-[hash][extname]'}}},
  build:{
    outDir:'../GoodsManager.Web/PhotoOcrAssets',emptyOutDir:true,
    // ViteのWorkerビルドを通し、document依存のページ用preloadを混ぜない。
    rolldownOptions:{input:'photo-ocr/build-entry.js',output:{entryFileNames:'build-entry.js'}}
  }
});
