import { PaddleOCR } from '@paddleocr/paddleocr-js';
import cv from '@techstark/opencv-js';

export async function prepare(root) {
    const ocr = await PaddleOCR.create({
        textDetectionModelName:'PP-OCRv5_mobile_det', textRecognitionModelName:'PP-OCRv5_mobile_rec',
        textDetectionModelAsset:{url:root+'models/PP-OCRv5_mobile_det_onnx_infer.tar'},
        textRecognitionModelAsset:{url:root+'models/PP-OCRv5_mobile_rec_onnx_infer.tar'},
        worker:false, ortOptions:{backend:'wasm',wasmPaths:root+'runtime/ort/',numThreads:1,simd:true}
    });
    return {
        async recognize(blob, edge) {
            let bitmap,canvas,mat;
            try {
                bitmap=await createImageBitmap(blob);
                canvas=new OffscreenCanvas(bitmap.width,bitmap.height);

                const ctx=canvas.getContext('2d');
                ctx.drawImage(bitmap,0,0);
                // The SDK's Blob/ImageData input requires document. Its public Mat input
                // keeps decoding and OCR inside this Worker, without patching the SDK.
                mat=cv.matFromImageData(ctx.getImageData(0,0,canvas.width,canvas.height));
                bitmap.close();
                bitmap=null;
                canvas.width=0;
                canvas.height=0;

                const [result]=await ocr.predict(mat,{
                    textDetLimitSideLen:edge,textDetLimitType:'max',textDetMaxSideLimit:edge,
                    textDetThresh:0.3,textDetBoxThresh:0.6,textDetUnclipRatio:1.5,textRecScoreThresh:0
                });

                return {text:result.items.map(item=>item.text).join('\n'),details:result.metrics};
            } finally {
                mat?.delete();
                bitmap?.close();
                if(canvas){
                    canvas.width=0;
                    canvas.height=0;
                }
            }
        }
    };
}
