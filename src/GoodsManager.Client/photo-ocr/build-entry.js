// Build-only: PhotoOcrClient starts the authenticated, fixed Worker URL at runtime.
new Worker(new URL('./ocr.worker.js',import.meta.url),{type:'module'});
