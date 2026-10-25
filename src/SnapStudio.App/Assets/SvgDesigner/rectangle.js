(() => {
 'use strict';
 const P=window.snapPerspective;
 const inside=(p,v)=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&p.x>=v.x-1e-7&&p.x<=v.x+v.width+1e-7&&p.y>=v.y-1e-7&&p.y<=v.y+v.height+1e-7;
 function flat(a,c){if(![a.x,a.y,c.x,c.y].every(Number.isFinite)||Math.abs(a.x-c.x)<1e-6||Math.abs(a.y-c.y)<1e-6)return null;const corners=[a,{x:c.x,y:a.y},c,{x:a.x,y:c.y}];return{corners,outline:[...corners,a]};}
 function intersect(f,g,expected,disk){
  let b,c,d;
  if(Math.abs(f.a)<1e-12&&Math.abs(g.a)<1e-12){const det=f.b*g.c-g.b*f.c;if(Math.abs(det)<1e-12)return null;return{x:(f.c*g.d-g.c*f.d)/det,y:(g.b*f.d-f.b*g.d)/det};}
  if(Math.abs(f.a)<1e-12){({b,c,d}=f);f=g;}else if(Math.abs(g.a)<1e-12)({b,c,d}=g);else{b=g.a*f.b-f.a*g.b;c=g.a*f.c-f.a*g.c;d=g.a*f.d-f.a*g.d;}
  const length=Math.hypot(b,c);if(length<1e-12)return null;b/=length;c/=length;d/=length;
  const q={x:-d*b,y:-d*c},dir={x:-c,y:b},bb=f.b*dir.x+f.c*dir.y,cc=f.a*(q.x*q.x+q.y*q.y)+f.b*q.x+f.c*q.y+f.d,discriminant=bb*bb-4*f.a*cc;if(discriminant< -1e-10)return null;
  const root=Math.sqrt(Math.max(0,discriminant));return[(-bb-root)/(2*f.a),(-bb+root)/(2*f.a)].map(t=>({x:q.x+dir.x*t,y:q.y+dir.y*t})).filter(p=>!disk||p.x*p.x+p.y*p.y<=1+1e-8).sort((p,q)=>Math.hypot(p.x-expected.x,p.y-expected.y)-Math.hypot(q.x-expected.x,q.y-expected.y))[0]||null;
 }
 function trace(f,from,to){
  if(Math.abs(f.a)<1e-8)return[from,to];const x=-f.b/(2*f.a),y=-f.c/(2*f.a),r=Math.hypot(from.x-x,from.y-y),start=Math.atan2(from.y-y,from.x-x);let delta=Math.atan2(to.y-y,to.x-x)-start;delta-=Math.round(delta/(Math.PI*2))*Math.PI*2;
  const count=Math.max(2,Math.ceil(Math.abs(delta)*96/Math.PI)),points=[from];for(let i=1;i<count;i++){const angle=start+delta*i/count;points.push({x:x+r*Math.cos(angle),y:y+r*Math.sin(angle)});}points.push(to);return points;
 }
 function rectangle(start,end,view,settings,plane=1){
  if(![start.x,start.y,end.x,end.y,view.x,view.y,view.x+view.width,view.y+view.height].every(Number.isFinite)||view.width<=0||view.height<=0||!inside(start,view))return null;
  end={x:Math.max(view.x,Math.min(view.x+view.width,end.x)),y:Math.max(view.y,Math.min(view.y+view.height,end.y))};const s=P.normalize(settings);if(!s.mode)return flat(start,end);if(![0,1,2].includes(plane))plane=1;
  const cx=view.x+view.width*s.centerX/100,cy=view.y+view.height*s.horizon/100,radius=Math.min(view.width,view.height)*s.radius/100,local=p=>({x:(p.x-cx)/radius,y:(p.y-cy)/radius}),world=p=>({x:cx+p.x*radius,y:cy+p.y*radius});
  const [u,v]=s.mode===1?(plane===0?['h','v']:plane===2?['h','depth']:['depth','v']):s.mode===5?(plane===0?['ch','cv']:plane===2?['ch','depth']:['depth','cv']):[plane===1?'right':'left',plane===2?'right':s.mode===3?'third':s.mode===4?'cv':'v'];
  const curved=['ch','cv'].includes(u)||['ch','cv'].includes(v),a=local(start);if(curved&&a.x*a.x+a.y*a.y>=1-1e-9)return null;
  function through(f,p){
   if(f==='cv')return{a:p.x,b:1-p.x*p.x-p.y*p.y,c:0,d:-p.x};if(f==='ch')return{a:p.y,b:0,c:1-p.x*p.x-p.y*p.y,d:-p.y};if(f==='h')return{a:0,b:0,c:1,d:-p.y};if(f==='v')return{a:0,b:1,c:0,d:-p.x};
   const target=f==='left'?local({x:view.x+view.width*s.left/100,y:cy}):f==='right'?local({x:view.x+view.width*s.right/100,y:cy}):f==='third'?local({x:cx,y:view.y+view.height*s.vertical/100}):{x:0,y:0},dx=target.x-p.x,dy=target.y-p.y;return{a:0,b:-dy,c:dx,d:dy*p.x-dx*p.y};
  }
  function build(t){
   const c=local({x:start.x+(end.x-start.x)*t,y:start.y+(end.y-start.y)*t});if(curved&&c.x*c.x+c.y*c.y>1+1e-9)return null;const au=through(u,a),av=through(v,a),cu=through(u,c),cv=through(v,c),b=intersect(au,cv,{x:c.x,y:a.y},curved),d=intersect(av,cu,{x:a.x,y:c.y},curved);if(!b||!d)return null;
   const points=[a,b,c,d],edges=[au,cv,cu,av];let sign=0;for(let i=0;i<4;i++){const p=points[i],q=points[(i+1)%4],r=points[(i+2)%4],cross=(q.x-p.x)*(r.y-q.y)-(q.y-p.y)*(r.x-q.x);if(!Number.isFinite(cross)||Math.abs(cross)<1e-10||(sign&&Math.sign(cross)!==sign))return null;sign=Math.sign(cross);}
   const outline=edges.flatMap((f,i)=>trace(f,points[i],points[(i+1)%4]).slice(i?1:0).map(world));if(outline.some(p=>!inside(p,view)||curved&&(((p.x-cx)/radius)**2+((p.y-cy)/radius)**2>1+1e-8)))return null;return{corners:points.map(world),outline};
  }
  const full=build(1);if(full)return full;let high=1,low=.5,best=null;
  for(let i=0;i<20&&!best;i++){best=build(low);if(!best){high=low;low/=2;}}if(!best)return null;
  for(let i=0;i<24;i++){const mid=(low+high)/2,candidate=build(mid);if(!candidate)high=mid;else{low=mid;best=candidate;}}return best;
 }
 P.rectangle=rectangle;P.flatRectangle=flat;
})();
