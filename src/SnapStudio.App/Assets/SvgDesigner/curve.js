(() => {
 'use strict';
 const P=window.snapPerspective;
 const inside=(p,v)=>Number.isFinite(p.x)&&Number.isFinite(p.y)&&p.x>=v.x-1e-7&&p.x<=v.x+v.width+1e-7&&p.y>=v.y-1e-7&&p.y<=v.y+v.height+1e-7;
 function curveLine(start,end,view,settings,scaleX=1,scaleY=1,locked=null,targets=63){
  if(![start.x,start.y,end.x,end.y,view.x,view.y,view.x+view.width,view.y+view.height,scaleX,scaleY].every(Number.isFinite)||view.width<=0||view.height<=0||scaleX<=0||scaleY<=0||!inside(start,view))return{points:[],stroke:null,direction:null};
  const ruler=P.constrainLine(start,end,view,settings,scaleX,scaleY,locked,targets),straight={points:[ruler.start,ruler.end],stroke:ruler.stroke,direction:ruler.direction};
  if(![5,6].includes(ruler.direction))return straight;
  const s=P.normalize(settings),cx=view.x+view.width*s.centerX/100,cy=view.y+view.height*s.horizon/100,radius=Math.min(view.width,view.height)*s.radius/100,horizontal=ruler.direction===5;
  let u=(start.x-cx)/radius,v=(start.y-cy)/radius,uMin=(view.x-cx)/radius,uMax=(view.x+view.width-cx)/radius,vMin=(view.y-cy)/radius,vMax=(view.y+view.height-cy)/radius;
  if(horizontal){[u,v]=[v,u];[uMin,vMin]=[vMin,uMin];[uMax,vMax]=[vMax,uMax];}
  if(Math.abs(u)<1e-9||u*u+v*v>1+1e-8)return straight;
  const signedA=(u*u+v*v-1)/(2*u),a=Math.abs(signedA),sign=Math.sign(u);
  function at(phi){const cosine=Math.cos(phi),c2=cosine*cosine,x=sign*c2/(Math.sqrt(a*a+c2)+a),y=Math.sin(phi);return horizontal?{x:cx+y*radius,y:cy+x*radius}:{x:cx+x*radius,y:cy+y*radius};}
  const from=Math.asin(Math.max(-1,Math.min(1,v))),roots=[-Math.PI/2,from,Math.PI/2,0];
  for(const bound of [vMin,vMax])if(bound>=-1&&bound<=1)roots.push(Math.asin(bound));
  for(const bound of [uMin,uMax]){const square=1+2*signedA*bound-bound*bound;if(square<0||square>1)continue;const angle=Math.asin(Math.sqrt(Math.max(0,Math.min(1,square))));roots.push(-angle,angle);}
  let low=from,high=from;
  // Boundary roots keep clipping on the connected visible curve, including narrow exit/reentry intervals.
  for(const next of roots.filter(p=>p<from-1e-12).sort((a,b)=>b-a)){if(!inside(at((low+next)/2),view))break;low=next;}
  for(const next of roots.filter(p=>p>from+1e-12).sort((a,b)=>a-b)){if(!inside(at((high+next)/2),view))break;high=next;}
  let best=from,bestScore=Infinity;
  function score(phi){const p=at(phi),x=(p.x-end.x)*scaleX,y=(p.y-end.y)*scaleY;return x*x+y*y;}
  function consider(phi){const value=score(phi);if(value<bestScore-1e-10||Math.abs(value-bestScore)<=1e-10&&Math.abs(phi-from)<Math.abs(best-from)){best=phi;bestScore=value;}}
  consider(from);consider(low);consider(high);
  const ratio=.6180339887498949;
  for(let segment=0;segment<64;segment++){
   let left=low+(high-low)*segment/64,right=low+(high-low)*(segment+1)/64,c=right-ratio*(right-left),d=left+ratio*(right-left),fc=score(c),fd=score(d);
   for(let step=0;step<48;step++){if(fc<fd){right=d;d=c;fd=fc;c=right-ratio*(right-left);fc=score(c);}else{left=c;c=d;fc=fd;d=left+ratio*(right-left);fd=score(d);}}
   consider((left+right)/2);
  }
  function sample(first,last,exactStart){const count=Math.max(1,Math.ceil(Math.abs(last-first)*96/Math.PI)),points=[];for(let i=0;i<=count;i++)points.push(i===0&&exactStart?start:at(first+(last-first)*i/count));return points;}
  return{points:sample(from,best,true),stroke:{points:sample(low,high,false),major:true},direction:ruler.direction};
 }
 P.curveLine=curveLine;
})();
