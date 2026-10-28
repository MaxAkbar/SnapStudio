(() => {
 'use strict';
 const P=window.snapPerspective,prefix='data-perspective-',keys=['tool','start','end','family','plane','path'];
 const finite=p=>p&&Number.isFinite(p.x)&&Number.isFinite(p.y),n=value=>Math.round(value*1000000)/1000000;
 const pair=value=>{const raw=(value||'').trim().split(/[\s,]+/);if(raw.length!==2||raw.some(p=>!p))return null;const parts=raw.map(Number);return parts.every(Number.isFinite)?{x:parts[0],y:parts[1]}:null;};
 function css(value){
  value=value||'';const parts=[];let position=0;
  while(position<value.length){const open=value.indexOf('/*',position);if(open<0){parts.push(value.slice(position));break;}parts.push(value.slice(position,open));const close=value.indexOf('*/',open+2);if(close<0)break;position=close+2;}
  return parts.join('');
 }
 function points(d){
  const number='[-+]?(?:\\d*\\.\\d+|\\d+\\.?\\d*)(?:[eE][-+]?\\d+)?',command=new RegExp('([ML])\\s*('+number+')[\\s,]+('+number+')','g');
  if(typeof d!=='string'||!d.trim()||d.length>1000000)return null;
  const closed=/[zZ]\s*$/.test(d),body=d.replace(/[zZ]\s*$/,'').trim(),result=[];let end=0;
  for(const match of body.matchAll(command)){if(body.slice(end,match.index).trim()||(result.length===0?match[1]!=='M':match[1]!=='L'))return null;const p={x:Number(match[2]),y:Number(match[3])};if(!finite(p))return null;result.push(p);end=match.index+match[0].length;}
  if(body.slice(end).trim()||result.length<2||result.length>4096)return null;
  return{points:result,closed};
 }
 function distanceTo(p,a,b){const dx=b.x-a.x,dy=b.y-a.y,length=dx*dx+dy*dy,t=length?Math.max(0,Math.min(1,((p.x-a.x)*dx+(p.y-a.y)*dy)/length)):0;return Math.hypot(p.x-a.x-t*dx,p.y-a.y-t*dy);}
 function fits(a,b,tolerance){
  if(!a?.length||!b?.length)return false;
  if(Math.hypot(a[0].x-b[0].x,a[0].y-b[0].y)>tolerance||Math.hypot(a.at(-1).x-b.at(-1).x,a.at(-1).y-b.at(-1).y)>tolerance)return false;
  const length=line=>line.slice(1).reduce((sum,p,i)=>sum+Math.hypot(p.x-line[i].x,p.y-line[i].y),0),oldLength=length(a);
  if(Math.abs(oldLength-length(b))>Math.max(tolerance*2,oldLength*.001))return false;
  const distance=(p,line)=>{let best=Infinity;for(let i=1;i<line.length;i++)best=Math.min(best,distanceTo(p,line[i-1],line[i]));return best;};
  return a.every(p=>distance(p,b)<=tolerance)&&b.every(p=>distance(p,a)<=tolerance);
 }
 function legacy(el,view,settings){
  const kind=el.getAttribute('data-name'),parsed=points(el.getAttribute('d'));
  if(!parsed||parsed.points.length>512)return null;const list=parsed.points,start=list[0],tolerance=Math.max(.005,Math.max(view.width,view.height)*.00002);
  if(kind==='Perspective line'&&!parsed.closed){
   const end=list.at(-1);
   for(let family=0;family<=7;family++){const line=P.curveLine(start,end,view,settings,1,1,family);if(line.direction===family&&fits(list,line.points,tolerance))return{kind:'line',start,end,family,plane:1};}
  }
  if(kind==='Perspective rectangle'&&parsed.closed&&list.length>=4){
   if(Math.hypot(list.at(-1).x-start.x,list.at(-1).y-start.y)<1e-8)list.pop();
   const closed=[...list,start],candidates=[];
   for(let i=1;i<list.length;i++){const p=list[i],a=list[(i+list.length-1)%list.length],b=list[(i+1)%list.length],angle=Math.abs(Math.atan2((p.x-a.x)*(b.y-p.y)-(p.y-a.y)*(b.x-p.x),(p.x-a.x)*(b.x-p.x)+(p.y-a.y)*(b.y-p.y)));candidates.push({index:i,angle});}
   candidates.sort((a,b)=>b.angle-a.angle);if(list.length===4)candidates.unshift({index:2,angle:Infinity});
   for(const {index}of candidates)for(let plane=0;plane<=2;plane++){const shape=P.rectangle(start,list[index],view,settings,plane);if(shape&&fits(closed,shape.outline,tolerance))return{kind:'rectangle',start,end:list[index],family:-1,plane};}
  }
  return null;
 }
 function read(el,view,settings){
  if(el.localName!=='path')return null;
  const root=el.ownerSVGElement,geometryCss=/(?:^|[;{])\s*(?:transform(?:-[a-z]+)?|translate|rotate|scale|d|x|y)\s*:/i;
  if(root?.hasAttribute('transform')||[...(root?.querySelectorAll('style')||[])].some(style=>geometryCss.test(css(style.textContent))))return null;
  for(let ancestor=el;ancestor;ancestor=ancestor===root?null:ancestor.parentElement)if(geometryCss.test(css(ancestor.getAttribute('style'))))return null;
  const marked=keys.some(key=>el.hasAttribute(prefix+key));
  if(!marked)return legacy(el,view,settings);
  if(keys.some(key=>!el.hasAttribute(prefix+key)))return null;
  const kind=el.getAttribute(prefix+'tool'),start=pair(el.getAttribute(prefix+'start')),end=pair(el.getAttribute(prefix+'end'));
  const familyText=el.getAttribute(prefix+'family').trim(),planeText=el.getAttribute(prefix+'plane').trim();if(!/^[+-]?\d+$/.test(familyText)||!/^[+-]?\d+$/.test(planeText))return null;
  const family=Number(familyText),plane=Number(planeText);
  if(!['line','rectangle'].includes(kind)||!start||!end||el.getAttribute(prefix+'path')!==el.getAttribute('d')||!points(el.getAttribute('d'))
   ||!Number.isInteger(family)||family< -1||family>7||!Number.isInteger(plane)||plane<0||plane>2)return null;
  return{kind,start,end,family,plane};
 }
 function attach(el,binding){
  el.setAttribute(prefix+'tool',binding.kind);el.setAttribute(prefix+'start',`${n(binding.start.x)},${n(binding.start.y)}`);el.setAttribute(prefix+'end',`${n(binding.end.x)},${n(binding.end.y)}`);
  el.setAttribute(prefix+'family',Number.isInteger(binding.family)?binding.family:-1);el.setAttribute(prefix+'plane',Number.isInteger(binding.plane)?binding.plane:1);el.setAttribute(prefix+'path',el.getAttribute('d')||'');
 }
 const transform=(p,m)=>{const q=new DOMPoint(p.x,p.y).matrixTransform(m);return{x:q.x,y:q.y};};
 const rendered=el=>!el.closest('defs,symbol,clipPath,mask,pattern');
 function snapshot(elements,rootMatrix,view,settings){
  const paths=[],seen=new Set();let ordinary=false;
  for(const root of elements){
   const items=root.localName==='path'?[root]:[root,...root.querySelectorAll('path,rect,circle,ellipse,line,polyline,polygon,text,use,switch')];
   for(const el of items){if(!rendered(el)||seen.has(el)||['g','switch'].includes(el.localName))continue;seen.add(el);const binding=read(el,view,settings),parsed=binding&&points(el.getAttribute('d'));
    if(!binding||!parsed){ordinary=true;continue;}
    const m=rootMatrix(el),det=m.a*m.d-m.b*m.c;if(![m.a,m.b,m.c,m.d,m.e,m.f,det].every(Number.isFinite)||Math.abs(det)<1e-12){ordinary=true;continue;}
    paths.push({el,binding,d:el.getAttribute('d'),attrs:keys.map(key=>el.getAttribute(prefix+key)),worldStart:transform(binding.start,m),worldEnd:transform(binding.end,m),worldPoints:parsed.points.map(p=>transform(p,m)),closed:parsed.closed});
   }
  }
  return{paths,ordinary,roots:elements.map(el=>({el,transform:el.getAttribute('transform')}))};
 }
 function restore(item){item.el.setAttribute('d',item.d);keys.forEach((key,i)=>{if(item.attrs[i]===null)item.el.removeAttribute(prefix+key);else item.el.setAttribute(prefix+key,item.attrs[i]);});}
 function pathData(list,closed){return list.map((p,i)=>`${i?'L':'M'}${n(p.x)} ${n(p.y)}`).join(' ')+(closed?' Z':'');}
 function reproject(snapshots,delta,state,rootMatrix,enabled){
  let stroke=null,valid=0,invalid=0;
  for(const item of snapshots.paths){
   const targets=(P.targets?.read(item.el)??63)&(state.targets??63);
   if(!enabled||targets===0){item.el.setAttribute('d',item.d);attach(item.el,item.binding);continue;}
   const start={x:item.worldStart.x+delta.x,y:item.worldStart.y+delta.y},end={x:item.worldEnd.x+delta.x,y:item.worldEnd.y+delta.y};
   let shape=null,list=null,worldStart=null,worldEnd=null,family=item.binding.family,closed=item.binding.kind==='rectangle';
   if(closed){shape=P.rectangle(start,end,state.view,state,item.binding.plane);if(shape){list=shape.outline.slice(0,-1);worldStart=shape.corners[0];worldEnd=shape.corners[2];}}
   else{
    const settings=P.normalize(state),cx=state.view.x+state.view.width*settings.centerX/100,cy=state.view.y+state.view.height*settings.horizon/100,radius=Math.min(state.view.width,state.view.height)*settings.radius/100;
    const fixedCurve=(family===6&&(settings.mode===4||settings.mode===5)||family===5&&settings.mode===5)&&(P.targets?.allows(targets,family,settings.mode)??true),outside=((start.x-cx)/radius)**2+((start.y-cy)/radius)**2>1+1e-8;
    if(!fixedCurve||!outside){const retain=family>=0&&(P.targets?.allows(targets,family,settings.mode)??true);shape=P.curveLine(start,end,state.view,state,state.scale.x,state.scale.y,family<0?null:family,targets);list=shape.points;worldStart=list[0];worldEnd=list.at(-1);if(!retain)family=shape.direction??-1;}
   }
   let length=0;if(list)for(let i=1;i<list.length;i++)length+=Math.hypot(list[i].x-list[i-1].x,list[i].y-list[i-1].y);
   const xs=list?.map(p=>p.x)||[],ys=list?.map(p=>p.y)||[];
   const good=finite(worldStart)&&finite(worldEnd)&&list?.length>=2&&list.every(finite)&&length>=1e-6&&(!closed||(Math.max(...xs)-Math.min(...xs)>=1e-6&&Math.max(...ys)-Math.min(...ys)>=1e-6));
   const inverse=rootMatrix(item.el).inverse();
   if(!good){invalid++;const local=item.worldPoints.map(p=>transform(p,inverse));item.el.setAttribute('d',pathData(local,item.closed));attach(item.el,{...item.binding,start:transform(item.worldStart,inverse),end:transform(item.worldEnd,inverse)});continue;}
   valid++;const local=list.map(p=>transform(p,inverse));item.el.setAttribute('d',pathData(local,closed));attach(item.el,{...item.binding,start:transform(worldStart,inverse),end:transform(worldEnd,inverse),family});
   stroke=closed?{points:shape.outline,major:true}:shape.stroke;
  }
  if(enabled&&invalid&&!valid&&!snapshots.ordinary){for(const root of snapshots.roots){if(root.transform===null)root.el.removeAttribute('transform');else root.el.setAttribute('transform',root.transform);}snapshots.paths.forEach(restore);}
  return{stroke,valid,invalid};
 }
 P.binding={read,attach,snapshot,reproject};
})();
