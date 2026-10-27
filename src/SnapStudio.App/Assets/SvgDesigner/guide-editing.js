(() => {
 'use strict';
 const P=window.snapPerspective;
 P.guideHandles=(view,settings)=>{
  const s=P.normalize(settings),cx=view.x+view.width*s.centerX/100,cy=view.y+view.height*s.horizon/100,r=Math.min(view.width,view.height)*s.radius/100;
  if(![view.x,view.y,view.width,view.height].every(Number.isFinite)||view.width<=0||view.height<=0)return[];
  return P.activeFamilies(s.mode).filter(f=>f!=='grid').map(family=>({kind:family,family,point:
   family==='left'?{x:s.mode===5?cx-r:view.x+view.width*s.left/100,y:cy}:family==='right'?{x:s.mode===5?cx+r:view.x+view.width*s.right/100,y:cy}:
   family==='top'?{x:cx,y:s.mode===3?view.y+view.height*s.vertical/100:cy-r}:family==='bottom'?{x:cx,y:cy+r}:{x:cx,y:cy}}));
 };
 P.moveGuide=(original,view,kind,delta)=>{
  const s=P.normalize(original);
  if(!s.mode||![view.width,view.height,delta.x,delta.y].every(Number.isFinite)||view.width<=0||view.height<=0||kind!=='horizon'&&!P.guideHandles(view,s).some(h=>h.kind===kind))return s;
  const x=delta.x*100/view.width,y=delta.y*100/view.height,rx=delta.x*100/Math.min(view.width,view.height),ry=delta.y*100/Math.min(view.width,view.height);
  if(kind==='horizon')s.horizon+=y;
  else if(kind==='center'){s.centerX+=x;s.horizon+=y;}
  else if(kind==='left'){if(s.mode===5)s.radius-=rx;else s.left+=x;}
  else if(kind==='right'){if(s.mode===5)s.radius+=rx;else s.right+=x;}
  else if(kind==='top'){if(s.mode===3){s.centerX+=x;s.vertical+=y;}else s.radius-=ry;}
  else if(kind==='bottom')s.radius+=ry;
  return P.normalize(s);
 };
})();
