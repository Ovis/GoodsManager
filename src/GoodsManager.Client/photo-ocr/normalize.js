// The original File is decoded only inside the sequential OCR Worker.
export async function normalize(file,edge=2048){
    let bitmap,canvas;
    try{
        if(!file.size||file.size>30*1024*1024)throw Error('画像は空でない30MiB以下のファイルにしてください。');

        bitmap=await createImageBitmap(file,{imageOrientation:'from-image'});
        if(bitmap.width*bitmap.height>40_000_000||Math.max(bitmap.width,bitmap.height)>16384)throw Error('文字読み取りの画像サイズ上限を超えています。');

        const ratio=Math.min(1,edge/Math.max(bitmap.width,bitmap.height));
        const width=Math.max(1,Math.round(bitmap.width*ratio)),height=Math.max(1,Math.round(bitmap.height*ratio));
        canvas=new OffscreenCanvas(width,height);

        const ctx=canvas.getContext('2d',{alpha:false});
        ctx.fillStyle='#fff';
        ctx.fillRect(0,0,width,height);
        ctx.drawImage(bitmap,0,0,width,height);

        const blob=await canvas.convertToBlob({type:'image/png'});
        if(blob.size>32*1024*1024)throw Error('文字読み取り用の画像が大きすぎます。');

        return {blob,width,height};
    }finally{
        bitmap?.close();
        if(canvas){
            canvas.width=0;
            canvas.height=0;
        }
    }
}
