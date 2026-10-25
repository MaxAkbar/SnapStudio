(() => {
 'use strict';
 const defaults={mode:0,centerX:50,horizon:50,left:-50,right:150,vertical:-100,radius:70,density:12};
 function normalize(value={}){
  const result={};for(const [key,fallback]of Object.entries(defaults)){
   const limits=key==='mode'?[0,5]:key==='density'?[4,32]:key==='radius'?[10,300]:['left','right','vertical'].includes(key)?[-500,500]:[-300,400];
   let n=Object.hasOwn(value,key)?Number(value[key]):fallback;if(!Number.isFinite(n))n=fallback;
   result[key]=Math.max(limits[0],Math.min(limits[1],['mode','density'].includes(key)?Math.round(n):n));
  }return result;
 }
 const dist=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
 function clip(a,b,r){
  let low=0,high=1;const dx=b.x-a.x,dy=b.y-a.y;
  function edge(p,q){if(Math.abs(p)<1e-12)return q>=0;const t=q/p;if(p<0){if(t>high)return false;low=Math.max(low,t);}else{if(t<low)return false;high=Math.min(high,t);}return true;}
  if(!edge(-dx,a.x-r.x)||!edge(dx,r.x+r.width-a.x)||!edge(-dy,a.y-r.y)||!edge(dy,r.y+r.height-a.y))return null;
  const from={x:a.x+low*dx,y:a.y+low*dy},to={x:a.x+high*dx,y:a.y+high*dy};return dist(from,to)>1e-9?[from,to]:null;
 }
 function create(view,options){
  const s=normalize(options);if(!s.mode||![view.x,view.y,view.width,view.height].every(Number.isFinite)||view.width<=0||view.height<=0)return[];
  const strokes=[],cx=view.x+view.width*s.centerX/100,cy=view.y+view.height*s.horizon/100,radius=Math.min(view.width,view.height)*s.radius/100;
  function curve(points,major=false){let run=[];function flush(){if(run.length>1)strokes.push({points:run,major});run=[];}
   for(let i=1;i<points.length;i++){const pair=clip(points[i-1],points[i],view);if(!pair){flush();continue;}if(run.length&&dist(run.at(-1),pair[0])>1e-6)flush();if(!run.length)run.push(pair[0]);run.push(pair[1]);}flush();}
  const line=(a,b,major=false)=>curve([a,b],major);
  function fan(point,circular=false){const reach=Math.max(view.width,view.height)*20;for(let i=0;i<s.density*2;i++){const angle=i*Math.PI/(s.density*2),x=Math.cos(angle),y=Math.sin(angle),length=circular?radius:reach;line({x:point.x-x*length,y:point.y-y*length},{x:point.x+x*length,y:point.y+y*length},i%4===0);}}
  function arcs(horizontal){for(let i=-s.density;i<=s.density;i++){
   const t=i/s.density,a=t===0?0:Math.abs((t*t-1)/(2*t)),points=[];
   for(let j=0;j<=96;j++){const angle=-Math.PI/2+Math.PI*j/96,y=Math.sin(angle),c=Math.cos(angle),x=t===0?0:Math.sign(t)*c*c/(Math.sqrt(a*a+c*c)+a);points.push(horizontal?{x:cx+y*radius,y:cy+x*radius}:{x:cx+x*radius,y:cy+y*radius});}curve(points,i%4===0);
  }}
  if(s.mode===1){fan({x:cx,y:cy});for(let i=0;i<=s.density;i++){const x=view.x+view.width*i/s.density,y=view.y+view.height*i/s.density;line({x,y:view.y},{x,y:view.y+view.height},i%4===0);line({x:view.x,y},{x:view.x+view.width,y},i%4===0);}}
  else if(s.mode>=2&&s.mode<=4){fan({x:view.x+view.width*s.left/100,y:cy});fan({x:view.x+view.width*s.right/100,y:cy});if(s.mode===3)fan({x:cx,y:view.y+view.height*s.vertical/100});else if(s.mode===4)arcs(false);else for(let i=0;i<=s.density;i++){const x=view.x+view.width*i/s.density;line({x,y:view.y},{x,y:view.y+view.height},i%4===0);}}
  else{arcs(false);arcs(true);fan({x:cx,y:cy},true);}return strokes;
 }
 function find(point,strokes,tolerance,direction=null,feasible=null,scaleX=1,scaleY=1){
  if(![point.x,point.y,tolerance,scaleX,scaleY].every(Number.isFinite)||tolerance<0||scaleX<=0||scaleY<=0)return null;
  let best=null;for(const stroke of strokes)for(let i=1;i<stroke.points.length;i++){
   const a=stroke.points[i-1],b=stroke.points[i],dx=b.x-a.x,dy=b.y-a.y;let t;
   if(direction){const cross=direction.x*dy-direction.y*dx;if(Math.abs(cross)<1e-10)continue;t=((a.x-point.x)*direction.y-(a.y-point.y)*direction.x)/cross;if(t< -1e-8||t>1+1e-8)continue;}
   else{const length=dx*dx*scaleX*scaleX+dy*dy*scaleY*scaleY;if(length<1e-16)continue;t=Math.max(0,Math.min(1,((point.x-a.x)*dx*scaleX*scaleX+(point.y-a.y)*dy*scaleY*scaleY)/length));}
   const target={x:a.x+t*dx,y:a.y+t*dy},distance=Math.hypot((target.x-point.x)*scaleX,(target.y-point.y)*scaleY);
   if(distance<=tolerance+1e-8&&(!best||distance<best.distance-1e-8)&&(!feasible||feasible(target)))best={point:target,stroke,distance};
  }return best;
 }
 const icons=[
  'M3 3H21V21H3Z M9 3V21 M15 3V21 M3 9H21 M3 15H21',
  'M3 5H21 M12 8L3 21 M12 8L21 21 M12 8V21 M5 16H19',
  'M2 8H22 M2 8L12 21L22 8 M2 8L12 15L22 8 M12 15V21',
  'M2 7L12 22L22 7Z M12 2L8 15 M12 2L16 15 M2 7L16 15 M22 7L8 15',
  'M12 2C2 7 2 17 12 22 M12 2C22 7 22 17 12 22 M2 10L20 17 M22 10L4 17 M12 2V22',
  'M12 2A10 10 0 1 1 12 22A10 10 0 1 1 12 2 M12 2C3 7 3 17 12 22 M12 2C21 7 21 17 12 22 M2 12C7 3 17 3 22 12 M2 12C7 21 17 21 22 12 M2 12H22 M12 2V22'
 ];
 window.snapPerspective={normalize,create,find,icons};
})();
