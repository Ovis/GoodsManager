import { defineConfig } from 'vite';
export default defineConfig({ build: { outDir: '../GoodsManager.Web/wwwroot', emptyOutDir: true }, server: { proxy: { '/api': { target: 'https://localhost:7443', secure: true } } } });
