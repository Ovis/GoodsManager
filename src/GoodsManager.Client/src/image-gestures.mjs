export function boundedScale(value){return Math.max(1,Math.min(5,value));}
export function swipeDirection(dx,dy,scale){return scale===1&&Math.abs(dx)>50&&Math.abs(dx)>Math.abs(dy)*1.4?(dx<0?1:-1):0;}
export function clampPan(value,scale,size){const limit=Math.max(0,(scale-1)*size/2);return Math.max(-limit,Math.min(limit,value));}
