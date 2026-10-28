(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg', $=id=>document.getElementById(id);
 const drawable=new Set(['rect','ellipse','circle','path','line','polyline','polygon','text','use','g','switch']);
 const allowed=new Set([...drawable,'svg','defs','title','desc','metadata','tspan','textPath','linearGradient','radialGradient','stop','clipPath','mask','pattern','symbol','style']);
 let svg=$('workSvg'), overlay, selected=[], tool='select', drag=null, draft=null, nodes=null, history=[], future=[], loaded=false,hoverTarget=null,lastHoverEvent=null,objectMenu=null,clipboard=null,targetSettings=null;
 const canvasRoot=$('frame').attachShadow({mode:'open'}),canvasStyle=document.createElement('style');
 canvasStyle.textContent=':host>svg{display:block;width:100%!important;height:100%!important;overflow:visible;touch-action:none;user-select:none;cursor:var(--snap-editor-cursor,default)!important}:host>svg [data-editor-overlay]{pointer-events:none}:host>svg [data-editor-overlay] [data-handle],:host>svg [data-editor-overlay] [data-node]{pointer-events:all}';
 canvasRoot.append(canvasStyle,svg);
 const defaults={fill:'#4cc2ff',stroke:'#26364a',strokeWidth:3,opacity:100,dash:'solid',radius:0,font:'Segoe UI',fontSize:32,text:'Text'};
 const bridge=(type,extra={})=>window.chrome?.webview?.postMessage({type,...extra});
 const status=(text,error=false)=>{$('status').textContent=text;$('status').classList.toggle('error',error);};
 const num=v=>Math.round(v*1000)/1000;
 // Preserve the drag length while snapping its direction in every quadrant.
 function snapDirection(x,y){const step=Math.PI/12,angle=Math.round(Math.atan2(y,x)/step)*step,length=Math.hypot(x,y);return{x:Math.cos(angle)*length,y:Math.sin(angle)*length};}
 function arrowPath(start,end,width){const x=end.x-start.x,y=end.y-start.y,length=Math.hypot(x,y);if(length<.001)return'';const ux=x/length,uy=y/length,head=Math.min(Math.max(12,width*4),length*.6),half=head*.5,bx=end.x-ux*head,by=end.y-uy*head;return`M${num(start.x)} ${num(start.y)} L${num(end.x)} ${num(end.y)} M${num(bx-uy*half)} ${num(by+ux*half)} L${num(end.x)} ${num(end.y)} L${num(bx+uy*half)} ${num(by-ux*half)}`;}
 const make=(tag,attrs={})=>{const el=document.createElementNS(NS,tag);for(const [key,value] of Object.entries(attrs))el.setAttribute(key,String(value));return el;};
 const matrix=m=>new DOMMatrix(m?[m.a,m.b,m.c,m.d,m.e,m.f]:undefined);
 const localMatrix=el=>matrix(el.transform?.baseVal.consolidate()?.matrix);
 const rootMatrix=el=>matrix(svg.getScreenCTM()).inverse().multiply(matrix(el.getScreenCTM()));
 const point=(event,el=svg)=>new DOMPoint(event.clientX,event.clientY).matrixTransform(matrix(el.getScreenCTM()).inverse());
 const objects=()=>[...svg.children].filter(el=>drawable.has(el.localName)&&!el.hasAttribute('data-editor-overlay')&&!el.hasAttribute('data-snap-stroke-derived'));
 const setPaint=(el,name,value)=>{el.setAttribute(name,String(value));el.style.removeProperty(name);el.style.setProperty(name,String(value));};
 const view=()=>{const v=svg.viewBox.baseVal;return{x:v.x,y:v.y,width:v.width,height:v.height};};
 const grid=window.createSvgGrid({root:()=>svg,frame:$('frame'),onChange:settings=>{targetSettings?.refresh();bridge('gridChanged',settings);},onToolRequested:tool=>setTool(tool),onOpen:()=>objectMenu?.close(),getTargets:()=>targetSettings?.current()??63,isSnappingEnabled:()=>$('objectSnapToggle').getAttribute('aria-pressed')==='true'});
 const objectSnapping=window.createSvgObjectSnapping({root:()=>svg,frame:$('frame'),onChange:()=>{grid.clearMatch();grid.render();}});
 const strokePatterns=window.createSvgStrokePatterns({root:()=>svg});
 function cleanSvg(fit=false){
  strokePatterns.render();
  const clone=svg.cloneNode(true);clone.querySelector('[data-editor-overlay]')?.remove();
  if(fit){const b=bounds([...objects(),...svg.querySelectorAll('[data-snap-stroke-derived]')]);if(b){const pad=Math.max(1,...objects().map(el=>parseFloat(getComputedStyle(el).strokeWidth)||0))/2+1;clone.setAttribute('viewBox',`${num(b.x-pad)} ${num(b.y-pad)} ${num(Math.max(1,b.width+pad*2))} ${num(Math.max(1,b.height+pad*2))}`);}}
  const b=clone.getAttribute('viewBox').split(/[\s,]+/).map(Number);clone.setAttribute('width',b[2]);clone.setAttribute('height',b[3]);clone.setAttribute('xmlns',NS);
  return new XMLSerializer().serializeToString(clone);
 }
 function before(){return cleanSvg();}
 function commit(previous){const current=cleanSvg();if(current!==previous){history.push(previous);if(history.length>100)history.shift();future=[];bridge('changed');}render();}
 function loadSvg(source,{resetHistory=true}={}){
  objectMenu?.close();
  let doc=new DOMParser().parseFromString(source,'image/svg+xml');
  if(doc.querySelector('parsererror')||doc.doctype||doc.documentElement.localName!=='svg')throw new Error('Enter a valid SVG document.');
  if(!doc.documentElement.namespaceURI){source=source.replace(/<svg\b/,'<svg xmlns="'+NS+'"');doc=new DOMParser().parseFromString(source,'image/svg+xml');}
  if(doc.documentElement.namespaceURI!==NS)throw new Error('The root element must use the SVG namespace.');
  const all=[doc.documentElement,...doc.documentElement.querySelectorAll('*')];
  if(source.length>1000000||all.length>4096)throw new Error('The SVG is too large for a tool.');
  for(const el of all){
   let depth=0;for(let p=el.parentElement;p;p=p.parentElement)depth++;if(depth>32)throw new Error('Use no more than 32 nested groups.');
   if(el.namespaceURI!==NS||!allowed.has(el.localName))throw new Error(`SVG element “${el.localName}” is not supported. Use vector paths, shapes, text, groups or gradients.`);
   for(const a of [...el.attributes]){
    if(/^on/i.test(a.localName)||a.localName==='base')throw new Error('SVG event handlers are not supported.');
    if(a.localName==='href'&&!a.value.startsWith('#'))throw new Error('SVG references must stay inside the document.');
    if(/@import|javascript:|expression\s*\(|\\/i.test(a.value))throw new Error('External SVG resources are not supported.');
    for(const m of a.value.matchAll(/url\s*\(\s*([^)]*)\)/gi))if(!m[1].trim().replace(/^['"]|['"]$/g,'').startsWith('#'))throw new Error('SVG resources must be embedded in the document.');
   }
   if(el.localName==='style'){
    if(/@import|javascript:|\\/i.test(el.textContent))throw new Error('External SVG stylesheets are not supported.');
    for(const m of el.textContent.matchAll(/url\s*\(\s*([^)]*)\)/gi))if(!m[1].trim().replace(/^['"]|['"]$/g,'').startsWith('#'))throw new Error('SVG resources must be embedded in the document.');
   }
  }
  const ids=new Map(),references=el=>[el,...el.querySelectorAll('*')].flatMap(child=>{
   const result=[];for(const a of child.attributes){if(a.localName==='href')result.push(a.value.slice(1));for(const m of a.value.matchAll(/url\s*\(\s*([^)]*)\)/gi))result.push(m[1].trim().replace(/^['"]|['"]$/g,'').slice(1));}
   if(child.localName==='style')for(const m of child.textContent.matchAll(/url\s*\(\s*([^)]*)\)/gi))result.push(m[1].trim().replace(/^['"]|['"]$/g,'').slice(1));return result;
  });
  for(const el of all)if(el.id){if(ids.has(el.id))throw new Error('SVG IDs must be unique.');ids.set(el.id,el);}
  const done=new Set();function visit(id,visiting=new Set()){if(!ids.has(id))throw new Error('An SVG reference points to a missing element.');if(done.has(id))return;if(visiting.has(id))throw new Error('The SVG contains a circular reference.');visiting.add(id);for(const reference of references(ids.get(id)))visit(reference,visiting);visiting.delete(id);done.add(id);}
  for(const id of ids.keys())visit(id);for(const id of references(doc.documentElement))if(!ids.has(id))throw new Error('An SVG reference points to a missing element.');
  const root=document.importNode(doc.documentElement,true);root.querySelector('[data-editor-overlay]')?.remove();
  let box=(root.getAttribute('viewBox')||`0 0 ${parseFloat(root.getAttribute('width'))||256} ${parseFloat(root.getAttribute('height'))||256}`).split(/[\s,]+/).map(Number);
  if(box.length!==4||box.some(n=>!Number.isFinite(n)||Math.abs(n)>1000000)||box[2]<=0||box[3]<=0)throw new Error('Use a valid viewBox with positive dimensions.');
  root.setAttribute('viewBox',box.join(' '));root.setAttribute('width',box[2]);root.setAttribute('height',box[3]);
  svg.replaceWith(root);svg=root;overlay=make('g',{'data-editor-overlay':''});svg.append(overlay);
  grid.clearMatch();objectSnapping.clear();
  selected=[];draft=null;drag=null;nodes=null;if(resetHistory){history=[];future=[];}
  bindCanvas();resizeFrame();loaded=true;render();status('Ready · draw shapes, edit paths, or work directly with SVG source.');
 }
 function resizeFrame(){const b=view();$('frame').style.aspectRatio=`${b.width}/${b.height}`;$('frame').style.width=`${Math.min(500,Math.max(100,($('stage').clientWidth||650)-68),Math.max(100,($('stage').clientHeight||600)-68)*b.width/b.height)}px`;$('dimensions').textContent=`${num(b.width)} × ${num(b.height)}`;}
 function bounds(elements){let result=null;for(const el of elements){try{const b=el.getBBox(),m=rootMatrix(el),p=[[b.x,b.y],[b.x+b.width,b.y],[b.x+b.width,b.y+b.height],[b.x,b.y+b.height]].map(([x,y])=>new DOMPoint(x,y).matrixTransform(m));const xs=p.map(p=>p.x),ys=p.map(p=>p.y);const r={x:Math.min(...xs),y:Math.min(...ys),right:Math.max(...xs),bottom:Math.max(...ys)};result=result?{x:Math.min(result.x,r.x),y:Math.min(result.y,r.y),right:Math.max(result.right,r.right),bottom:Math.max(result.bottom,r.bottom)}:r;}catch{}}
  return result?{x:result.x,y:result.y,width:result.right-result.x,height:result.bottom-result.y}:null;}
 function topObject(target){let el=strokePatterns.resolve(target);while(el&&el.parentNode!==svg)el=el.parentNode;return objects().includes(el)?el:null;}
 function objectSnapTargets(){return objects().filter(el=>{const style=getComputedStyle(el);return !selected.includes(el)&&style.display!=='none'&&!['hidden','collapse'].includes(style.visibility)&&Number(style.opacity)!==0;}).map(el=>bounds([el])).filter(b=>b&&(b.width>0||b.height>0));}
 function snapResizeCorner(start,end,event){
  const target=event.shiftKey?grid.snapRay(start,end,event):grid.snapPoint(end,event),candidate=objectSnapping.snap({x:end.x,y:end.y,width:0,height:0},drag.snapTargets,event,true,event.shiftKey?{x:end.x-start.x,y:end.y-start.y}:null);
  const result=objectSnapping.choose(end,candidate,target,grid.getSnapMatch(),event);if(objectSnapping.hasGuides)grid.clearMatch();return result;
 }
 function select(elements){selected=[...new Set(elements)].filter(el=>el.isConnected);nodes=null;render();}
 function transformed(elements,delta,originals){elements.forEach((el,i)=>{const p=el.parentNode===svg?new DOMMatrix():rootMatrix(el.parentNode);const next=p.inverse().multiply(delta).multiply(p).multiply(originals[i]);el.setAttribute('transform',`matrix(${[next.a,next.b,next.c,next.d,next.e,next.f].map(value=>Math.round(value*1000000)/1000000).join(' ')})`);});}
 function bindingSnapshot(elements){return window.snapPerspective.binding.snapshot(elements,rootMatrix,view(),grid.getState());}
 function moved(elements,delta,originals=elements.map(localMatrix),bindings=bindingSnapshot(elements),event=null){
  transformed(elements,new DOMMatrix().translate(delta.x,delta.y),originals);
  const state=grid.projectionContext(),enabled=state.visible&&state.snap&&!!state.mode&&!event?.altKey;
  const result=window.snapPerspective.binding.reproject(bindings,delta,state,rootMatrix,enabled);
  if(result.valid&&nodes&&bindings.paths.some(item=>item.el===nodes.el)){nodes.sections=parsePath(nodes.el.getAttribute('d'));nodes.active=null;}
  if(bindings.paths.length)grid.showBindingMatch(result.stroke);
 }
 function styles(el){const c=el?getComputedStyle(el):null;const color=value=>{const ctx=document.createElement('canvas').getContext('2d');ctx.fillStyle=value||'#000000';return ctx.fillStyle.startsWith('#')?ctx.fillStyle:'#000000';};return{fill:el?(c.fill==='none'?'none':color(c.fill)):defaults.fill,stroke:el?(c.stroke==='none'?'none':color(c.stroke)):defaults.stroke,strokeWidth:el?parseFloat(c.strokeWidth)||0:defaults.strokeWidth,opacity:el?Math.round((parseFloat(c.opacity)||0)*100):defaults.opacity};}
 function paintTargets(){return selected.flatMap(el=>el.localName==='g'?[...el.querySelectorAll('*')].filter(child=>drawable.has(child.localName)&&child.localName!=='g'&&!strokePatterns.isDerived(child)):[el]);}
 function dashValue(width,value){return value==='dashed'?`${width*4} ${width*2}`:value==='dotted'?`${width} ${width*2}`:value==='dashdot'?`${width*4} ${width*2} ${width} ${width*2}`:'none';}
 function newShape(tag,attrs={}){const el=make(tag,{fill:defaults.fill,stroke:defaults.stroke,'stroke-width':defaults.strokeWidth,opacity:defaults.opacity/100,'stroke-linecap':'round','stroke-linejoin':'round',...attrs});if(targetSettings&&targetSettings.default()!==63)el.setAttribute('data-snap-targets',targetSettings.default());if(defaults.dash!=='solid')el.setAttribute('stroke-dasharray',dashValue(defaults.strokeWidth,defaults.dash));svg.insertBefore(el,overlay);if(defaults.dash==='custom')strokePatterns.apply([el],strokePatterns.chosen());return el;}
 const setCanvasCursor=value=>$('frame').style.setProperty('--snap-editor-cursor',value);
 const baseCursor=()=>['select','nodes'].includes(tool)?'default':'crosshair';
 function paintHover(){
  overlay?.querySelector('[data-editor-hover]')?.remove();
  if(!hoverTarget?.isConnected){hoverTarget=null;return;}
  if(selected.includes(hoverTarget)||drag||draft)return;
  const b=bounds([hoverTarget]),unit=view().width/Math.max(1,svg.getBoundingClientRect().width);
  if(b)overlay.append(make('rect',{x:b.x,y:b.y,width:Math.max(b.width,unit),height:Math.max(b.height,unit),fill:'none',stroke:'#4cc2ff','stroke-width':unit,'stroke-dasharray':`${4*unit} ${2*unit}`,'data-editor-hover':''}));
 }
 function clearHover(){hoverTarget=null;lastHoverEvent=null;overlay?.querySelector('[data-editor-hover]')?.remove();setCanvasCursor(baseCursor());}
 function objectHover(e){
  lastHoverEvent={target:e.target,ctrlKey:e.ctrlKey,metaKey:e.metaKey};
  const target=e.target?.isConnected?topObject(e.target):null,forceDrawing=(e.ctrlKey||e.metaKey)&&!['select','nodes'].includes(tool);
  hoverTarget=!drag&&!draft&&!forceDrawing?target:null;
  setCanvasCursor(hoverTarget&&tool!=='nodes'?'move':baseCursor());paintHover();
 }
 function render(){
  if(!overlay)return;strokePatterns.render();grid.render();grid.setToolState(tool);overlay.replaceChildren();const b=bounds(selected),unit=view().width/Math.max(1,svg.getBoundingClientRect().width),h=unit*7;
  if(b&&!nodes){overlay.append(make('rect',{x:b.x,y:b.y,width:Math.max(b.width,unit),height:Math.max(b.height,unit),fill:'none',stroke:'#007cb9','stroke-width':unit,'stroke-dasharray':`${4*unit} ${3*unit}`}));
   for(const [name,x,y] of [['nw',b.x,b.y],['ne',b.x+b.width,b.y],['sw',b.x,b.y+b.height],['se',b.x+b.width,b.y+b.height]])overlay.append(make('rect',{x:x-h/2,y:y-h/2,width:h,height:h,fill:'white',stroke:'#007cb9','stroke-width':unit,'data-handle':name,style:`cursor:${name}-resize`}));
   overlay.append(make('line',{x1:b.x+b.width/2,y1:b.y,x2:b.x+b.width/2,y2:b.y-20*unit,stroke:'#007cb9','stroke-width':unit}));overlay.append(make('circle',{cx:b.x+b.width/2,cy:b.y-20*unit,r:h/2,fill:'white',stroke:'#007cb9','stroke-width':unit,'data-handle':'rotate',style:'cursor:grab'}));
  }
  if(nodes)renderNodes(unit);
  const items=objects(),focusedIndex=document.activeElement?.closest('#layers [data-object-index]')?.dataset.objectIndex;$('objectCount').textContent=items.length;$('hint').hidden=items.length>0;$('layers').replaceChildren();
  for(const el of [...items].reverse()){const button=document.createElement('button');button.type='button';button.dataset.objectIndex=items.indexOf(el);button.classList.toggle('active',selected.includes(el));button.setAttribute('role','listitem');button.setAttribute('aria-current',String(selected.includes(el)));button.title='Right-click for object editing options';const name=document.createElement('span');name.textContent=el.getAttribute('data-name')||el.id||({g:'Group',path:'Path',rect:'Rectangle',ellipse:'Ellipse',text:el.textContent?.slice(0,24)}[el.localName])||el.localName;button.setAttribute('aria-label',name.textContent);const kind=document.createElement('span');kind.className='kind';kind.textContent=el.localName;button.append(name,kind);button.onclick=e=>{select(e.shiftKey?selected.includes(el)?selected.filter(x=>x!==el):[...selected,el]:[el]);objectRow(el)?.focus({preventScroll:true});};$('layers').append(button);}
  if(focusedIndex!==undefined)$('layers').querySelector(`[data-object-index="${focusedIndex}"]`)?.focus({preventScroll:true});
  if(!items.length){const empty=document.createElement('div');empty.className='empty';empty.textContent='Your vector objects appear here. You can select, group, and arrange them.';$('layers').append(empty);}
  $('selectionTitle').textContent=selected.length?`${selected.length===1?selected[0].localName:selected.length+' objects'} selected`:'Drawing style';
  const s=styles(paintTargets()[0]);$('fillNone').checked=s.fill==='none';$('strokeNone').checked=s.stroke==='none';if(s.fill!=='none')$('fill').value=s.fill;if(s.stroke!=='none')$('stroke').value=s.stroke;$('strokeWidth').value=num(s.strokeWidth);$('opacity').value=s.opacity;
  const painted=paintTargets()[0],line=painted?getComputedStyle(painted).strokeDasharray:null;
  if(painted){const parts=line==='none'?[]:line.split(/[\s,]+/).map(parseFloat);$('dash').value=parts.length===4?'dashdot':parts.length?parts[0]<=s.strokeWidth*1.1?'dotted':'dashed':'solid';if(painted.localName==='rect')$('radius').value=parseFloat(painted.getAttribute('rx'))||0;}
  else{$('dash').value=defaults.dash;$('radius').value=defaults.radius;}
  const perspectiveActive=['perspectiveRect','perspectiveLine'].includes(tool),contextChanged=$('perspectiveToolOptions').hidden===perspectiveActive;$('perspectiveToolOptions').hidden=!perspectiveActive;if(contextChanged){resizeFrame();grid.render();}$('perspectiveToolTitle').textContent=tool==='perspectiveLine'?'Perspective line':'Perspective rectangle';$('perspectiveStrokeColor').value=s.stroke==='none'?defaults.stroke:s.stroke;$('perspectiveStrokeWidth').value=num(s.strokeWidth);$('transformSection').hidden=!b;$('radiusField').hidden=['perspectiveRect','perspectiveLine'].includes(tool)||selected.length>0&&!selected.some(el=>el.localName==='rect');$('rectanglePlaneField').hidden=tool!=='perspectiveRect';
  if(b){$('posX').value=num(b.x);$('posY').value=num(b.y);$('sizeW').value=num(b.width);$('sizeH').value=num(b.height);}
  const text=selected.length===1&&selected[0].localName==='text'?selected[0]:null;$('textFields').hidden=tool!=='text'&&!text;
  if(text){$('textValue').value=text.textContent;$('fontSize').value=parseFloat(getComputedStyle(text).fontSize)||32;const family=text.getAttribute('font-family');if(family&&[...$('font').options].some(o=>o.value===family))$('font').value=family;}
  strokePatterns.updateControls(selected);targetSettings?.refresh();objectSnapping.render(b);paintHover();objectMenu?.refresh();
 }
 function setTool(value){finishPath();clearHover();tool=value;nodes=null;if(['perspectiveRect','perspectiveLine'].includes(tool)){const s=grid.getState();grid.setSettings({mode:s.mode||(tool==='perspectiveLine'?4:2),visible:true});grid.open(false);}else grid.showProperties();document.querySelectorAll('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));setCanvasCursor(baseCursor());status(tool==='perspectiveLine'?'Perspective line · initial drag chooses a guide direction · follows its full curve · Alt draws freely.':tool==='perspectiveRect'?'Perspective rectangle · drag opposite corners · choose Front, Side or Ground · Alt draws flat.':tool==='path'?'Bézier path · click for corners, drag for curves, click the first point to close or Enter to finish.':tool==='nodes'?'Select a path to edit its anchors and curve handles.':['line','arrow'].includes(tool)?'Drag to draw · follows perspective when snapping · Alt draws freely · otherwise Shift aligns to 15°.':'Ready');if(tool==='nodes'&&selected.length===1)editNodes(selected[0]);render();}
 function bindCanvas(){hoverTarget=null;lastHoverEvent=null;svg.onpointerleave=()=>{if(!drag)clearHover();};svg.onpointerdown=e=>{pointerDown(e);if(drag){drag.clientStart={x:e.clientX,y:e.clientY};if(['move','resize'].includes(drag.kind))drag.snapTargets=objectSnapTargets();}};svg.onpointermove=pointerMove;svg.onpointerup=pointerUp;svg.onpointercancel=()=>{objectSnapping.clear();if(drag){const old=drag.before;loadSvg(old,{resetHistory:false});drag=null;}};svg.ondblclick=e=>{const el=topObject(e.target);if(el){select([el]);tool='nodes';document.querySelectorAll('[data-tool]').forEach(b=>b.classList.toggle('active',b.dataset.tool===tool));editNodes(el);e.preventDefault();}};}
 function pointerDown(e){
  if(e.button!==0)return;e.preventDefault();svg.setPointerCapture(e.pointerId);objectSnapping.clear();const raw=point(e);let p=raw;const handle=e.target.dataset.handle,node=e.target.dataset.node,target=topObject(e.target),forceDrawing=(e.ctrlKey||e.metaKey)&&!['select','nodes'].includes(tool),autoEdit=!!target&&!draft&&!forceDrawing;clearHover();
  if(node&&nodes&&!forceDrawing){const [s,i,kind]=node.split(':');const target=nodes.sections[+s].nodes[+i];nodes.active={s:+s,i:+i};drag={kind:'node',start:point(e,nodes.el),before:before(),original:structuredClone(target),target,part:kind};return;}
  if(handle&&selected.length&&!forceDrawing){const b=bounds(selected);drag={kind:handle==='rotate'?'rotate':'resize',handle,start:p,box:b,originals:selected.map(localMatrix),elements:[...selected],before:before()};return;}
  if(!autoEdit&&!['select','nodes','pen','perspectiveRect','perspectiveLine'].includes(tool)&&!(['line','arrow'].includes(tool)&&grid.getState().mode))p=grid.snapPoint(p,e,draft?window.snapPerspective.targets.read(draft.el):targetSettings.default());
  if(tool==='path'&&!autoEdit){
   if(draft&&draft.sections[0].nodes.length>2&&Math.hypot(p.x-draft.sections[0].nodes[0].x,p.y-draft.sections[0].nodes[0].y)<view().width/30){draft.sections[0].closed=true;finishPath();return;}
   if(!draft){const previous=before(),el=newShape('path',{d:'',fill:'none'});draft={el,sections:[{nodes:[],closed:false}],before:previous};selected=[el];}
   const target={x:p.x,y:p.y,ix:p.x,iy:p.y,ox:p.x,oy:p.y};draft.sections[0].nodes.push(target);drag={kind:'path',target,start:p};updatePath(draft);nodes=draft;render();return;
  }
  if(tool==='select'||tool==='nodes'||autoEdit){
   if(target){if(e.shiftKey){select(selected.includes(target)?selected.filter(x=>x!==target):[...selected,target]);return;}if(!selected.includes(target))select([target]);if(tool==='nodes'){editNodes(target);return;}drag={kind:'move',start:p,box:bounds(selected),originals:selected.map(localMatrix),elements:[...selected],before:before(),bindings:bindingSnapshot(selected)};}
   else{nodes=null;drag={kind:'marquee',start:p,original:e.shiftKey?[...selected]:[],before:before()};if(!e.shiftKey)selected=[];}
   return;
  }
  const previous=before();let el;
  if(tool==='text'){el=newShape('text',{x:p.x,y:p.y,stroke:'none',fill:defaults.fill==='none'?'#26364a':defaults.fill,'font-family':defaults.font,'font-size':defaults.fontSize});el.textContent=defaults.text;selected=[el];commit(previous);setTool('select');return;}
  if(tool==='rect')el=newShape('rect',{x:p.x,y:p.y,width:0,height:0,rx:defaults.radius});
  if(tool==='perspectiveRect')el=newShape('path',{d:'','data-name':'Perspective rectangle'});
  if(tool==='ellipse')el=newShape('ellipse',{cx:p.x,cy:p.y,rx:0,ry:0});
  if(tool==='line')el=newShape('line',{x1:p.x,y1:p.y,x2:p.x,y2:p.y,fill:'none',stroke:defaults.stroke==='none'?'#4cc2ff':defaults.stroke});
  if(tool==='perspectiveLine')el=newShape('path',{d:'','data-name':'Perspective line',fill:'none',stroke:defaults.stroke==='none'?'#4cc2ff':defaults.stroke});
  if(tool==='arrow')el=newShape('path',{d:'','data-name':'Arrow',fill:'none',stroke:defaults.stroke==='none'?'#4cc2ff':defaults.stroke});
  if(tool==='pen')el=newShape('path',{d:`M${num(p.x)} ${num(p.y)}`,fill:'none',stroke:defaults.stroke==='none'?'#4cc2ff':defaults.stroke});
  if(el){selected=[el];drag={kind:'draw',tool,el,start:p,rawStart:raw,direction:null,plane:Number($('rectanglePlane').value),before:previous,points:[p]};}
 }
 function pointerMove(e){
  if(!drag){objectHover(e);return;}const p=point(e),dx=p.x-drag.start.x,dy=p.y-drag.start.y;
  if(['move','resize','rotate','node'].includes(drag.kind)&&!drag.moved){if(Math.hypot(e.clientX-drag.clientStart.x,e.clientY-drag.clientStart.y)<2)return;drag.moved=true;}
  if(drag.kind==='move'){
   const raw=e.shiftKey?snapDirection(dx,dy):{x:dx,y:dy},activeBinding=drag.bindings.paths.length&&targetSettings.current()!==0,bound=activeBinding&&!drag.bindings.ordinary,state=grid.getState();let delta=bound?raw:grid.snapMove(drag.box,raw,e);
   // Perspective-bound paths reproject their geometry. Keep their guide family in charge.
   if(!(activeBinding&&state.visible&&state.snap&&state.mode&&objectSnapping.enabled&&!e.altKey)){
    const proposed={...drag.box,x:drag.box.x+raw.x,y:drag.box.y+raw.y},candidate=objectSnapping.snap(proposed,drag.snapTargets,e,false,e.shiftKey?raw:null);
    const result=objectSnapping.choose(proposed,candidate,{x:drag.box.x+delta.x,y:drag.box.y+delta.y},bound?{x:false,y:false,coupled:false}:grid.getSnapMatch(),e);
    delta={x:result.x-drag.box.x,y:result.y-drag.box.y};if(objectSnapping.hasGuides)grid.clearMatch();
   }else objectSnapping.clear();
   moved(drag.elements,delta,drag.originals,drag.bindings,e);render();return;
  }
  if(drag.kind==='rotate'){const b=drag.box,cx=b.x+b.width/2,cy=b.y+b.height/2;let angle=(Math.atan2(p.y-cy,p.x-cx)-Math.atan2(drag.start.y-cy,drag.start.x-cx))*180/Math.PI;if(e.shiftKey)angle=Math.round(angle/15)*15;transformed(drag.elements,new DOMMatrix().translate(cx,cy).rotate(angle).translate(-cx,-cy),drag.originals);render();return;}
  if(drag.kind==='resize'){const b=drag.box,west=drag.handle.includes('w'),north=drag.handle.includes('n'),ax=west?b.x+b.width:b.x,ay=north?b.y+b.height:b.y;let sx=Math.max(.001,(west?ax-p.x:p.x-ax)/Math.max(.01,b.width)),sy=Math.max(.001,(north?ay-p.y:p.y-ay)/Math.max(.01,b.height));let corner;
   if(e.shiftKey){sx=sy=Math.max(sx,sy);corner=snapResizeCorner({x:ax,y:ay},{x:ax+(west?-1:1)*b.width*sx,y:ay+(north?-1:1)*b.height*sy},e);sx=sy=Math.max(.001,b.width>.01?(west?ax-corner.x:corner.x-ax)/b.width:(north?ay-corner.y:corner.y-ay)/Math.max(.01,b.height));}
   else{corner=snapResizeCorner({x:ax,y:ay},p,e);sx=Math.max(.001,(west?ax-corner.x:corner.x-ax)/Math.max(.01,b.width));sy=Math.max(.001,(north?ay-corner.y:corner.y-ay)/Math.max(.01,b.height));}
   transformed(drag.elements,new DOMMatrix().translate(ax,ay).scale(sx,sy).translate(-ax,-ay),drag.originals);render();return;}
  if(drag.kind==='node'){const q=point(e,nodes.el),target=drag.target,o=drag.original;let x=q.x-drag.start.x,y=q.y-drag.start.y;if(drag.part==='a'){const m=rootMatrix(nodes.el),world=new DOMPoint(o.x+x,o.y+y).matrixTransform(m),snapped=grid.snapPoint(world,e),local=new DOMPoint(snapped.x,snapped.y).matrixTransform(m.inverse());x=local.x-o.x;y=local.y-o.y;for(const key of ['x','ix','ox'])target[key]=o[key]+x;for(const key of ['y','iy','oy'])target[key]=o[key]+y;}else{target[drag.part+'x']=q.x;target[drag.part+'y']=q.y;if(!e.altKey){const other=drag.part==='i'?'o':'i';target[other+'x']=target.x*2-q.x;target[other+'y']=target.y*2-q.y;}}updatePath(nodes);render();return;}
  if(drag.kind==='path'){const n=drag.target;n.ox=p.x;n.oy=p.y;n.ix=n.x*2-p.x;n.iy=n.y*2-p.y;updatePath(draft);render();return;}
  if(drag.kind==='marquee'){render();overlay.append(make('rect',{x:Math.min(drag.start.x,p.x),y:Math.min(drag.start.y,p.y),width:Math.abs(dx),height:Math.abs(dy),fill:'#4cc2ff22',stroke:'#007cb9','stroke-width':view().width/svg.clientWidth}));drag.end=p;return;}
  if(drag.kind==='draw'){
   if(drag.tool==='perspectiveLine'){
    const ruler=grid.snapPerspectiveLine(drag.rawStart,p,e,drag.direction);drag.direction=ruler.direction??drag.direction;
    drag.el.setAttribute('d',ruler.points.map((q,i)=>`${i?'L':'M'}${num(q.x)} ${num(q.y)}`).join(' '));if(ruler.points.length>=2)window.snapPerspective.binding.attach(drag.el,{kind:'line',start:ruler.points[0],end:ruler.points.at(-1),family:drag.direction??-1,plane:1});render();return;
   }
   if(drag.tool==='perspectiveRect'){
    const rectangle=grid.snapRectangle(drag.rawStart,p,e,drag.plane);
    drag.el.setAttribute('d',rectangle?rectangle.outline.slice(0,-1).map((q,i)=>`${i?'L':'M'}${num(q.x)} ${num(q.y)}`).join(' ')+' Z':'');if(rectangle)window.snapPerspective.binding.attach(drag.el,{kind:'rectangle',start:rectangle.corners[0],end:rectangle.corners[2],family:-1,plane:drag.plane});render();return;
   }
   if(['line','arrow'].includes(drag.tool)){
    const ruler=grid.snapStraightLine(drag.rawStart,p,e,drag.direction);
    if(ruler){drag.direction=ruler.direction??drag.direction;if(drag.tool==='line'){for(const [a,v]of Object.entries({x1:ruler.start.x,y1:ruler.start.y,x2:ruler.end.x,y2:ruler.end.y}))drag.el.setAttribute(a,num(v));}else drag.el.setAttribute('d',arrowPath(ruler.start,ruler.end,defaults.strokeWidth));render();return;}
   }
   let x=dx,y=dy;if(e.shiftKey){if(['line','arrow'].includes(drag.tool))({x,y}=snapDirection(x,y));else if(['rect','ellipse'].includes(drag.tool)){const side=Math.max(Math.abs(x),Math.abs(y));x=Math.sign(x||1)*side;y=Math.sign(y||1)*side;}}
   if(drag.tool!=='pen'){const end={x:drag.start.x+x,y:drag.start.y+y},snapped=e.shiftKey?grid.snapRay(drag.start,end,e):grid.snapPoint(end,e);x=snapped.x-drag.start.x;y=snapped.y-drag.start.y;}
   const left=e.altKey?drag.start.x-Math.abs(x):Math.min(drag.start.x,drag.start.x+x),top=e.altKey?drag.start.y-Math.abs(y):Math.min(drag.start.y,drag.start.y+y),w=Math.abs(x)*(e.altKey?2:1),h=Math.abs(y)*(e.altKey?2:1);
   if(drag.tool==='rect')for(const [a,v]of Object.entries({x:left,y:top,width:w,height:h}))drag.el.setAttribute(a,num(v));
   if(drag.tool==='ellipse')for(const [a,v]of Object.entries({cx:left+w/2,cy:top+h/2,rx:w/2,ry:h/2}))drag.el.setAttribute(a,num(v));
   if(drag.tool==='line'){drag.el.setAttribute('x2',num(drag.start.x+x));drag.el.setAttribute('y2',num(drag.start.y+y));}
   if(drag.tool==='arrow')drag.el.setAttribute('d',arrowPath(drag.start,{x:drag.start.x+x,y:drag.start.y+y},defaults.strokeWidth));
   if(drag.tool==='pen'){const last=drag.points.at(-1);if(Math.hypot(last.x-p.x,last.y-p.y)>view().width/400){drag.points.push(p);drag.el.setAttribute('d',drag.points.map((p,i)=>`${i?'L':'M'}${num(p.x)} ${num(p.y)}`).join(' '));}}
   render();
  }
 }
 function pointerUp(e){if(!drag)return;pointerMove(e);const current=drag;drag=null;objectSnapping.clear();try{svg.releasePointerCapture(e.pointerId);}catch{}
  if(current.kind==='path'){render();return;}
  if(current.kind==='marquee'){const p=current.end||current.start,left=Math.min(current.start.x,p.x),top=Math.min(current.start.y,p.y),right=Math.max(current.start.x,p.x),bottom=Math.max(current.start.y,p.y);select([...current.original,...objects().filter(el=>{const b=bounds([el]);return b&&b.x<=right&&b.x+b.width>=left&&b.y<=bottom&&b.y+b.height>=top;})]);return;}
  if(current.kind==='draw'){const b=bounds([current.el]);if(!b||b.width+b.height<.1){current.el.remove();selected=[];}}
  commit(current.before);
 }
 function finishPath(){if(!draft)return;const original=draft.before;if(draft.sections[0].nodes.length<2){draft.el.remove();selected=[];}draft=null;nodes=null;commit(original);}
 function pathData(sections){return sections.map(section=>{const a=section.nodes;if(!a.length)return'';let d=`M${num(a[0].x)} ${num(a[0].y)}`;for(let i=1;i<a.length;i++){const p=a[i-1],q=a[i];d+=` C${num(p.ox)} ${num(p.oy)} ${num(q.ix)} ${num(q.iy)} ${num(q.x)} ${num(q.y)}`;}if(section.closed&&a.length>1){const p=a.at(-1),q=a[0];d+=` C${num(p.ox)} ${num(p.oy)} ${num(q.ix)} ${num(q.iy)} ${num(q.x)} ${num(q.y)} Z`;}return d;}).join(' ');}
 function updatePath(state){state.el.setAttribute('d',pathData(state.sections));}
 function parsePath(d){
  const tokens=d.match(/[a-zA-Z]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?/g)||[];let at=0,cmd='',x=0,y=0,startX=0,startY=0,section=null,lastControl=null,lastType='',sections=[];
  const count={M:2,L:2,H:1,V:1,C:6,S:4,Q:4,T:2};const anchor=(x,y,ix=x,iy=y)=>({x,y,ix,iy,ox:x,oy:y});
  while(at<tokens.length){if(/^[a-zA-Z]$/.test(tokens[at]))cmd=tokens[at++];if(!cmd)throw new Error('The path data is incomplete.');const c=cmd.toUpperCase(),relative=cmd!==c;
   if(c==='Z'){if(section){section.closed=true;const first=section.nodes[0],last=section.nodes.at(-1);if(section.nodes.length>1&&Math.hypot(first.x-last.x,first.y-last.y)<.000001){first.ix=last.ix;first.iy=last.iy;section.nodes.pop();}}x=startX;y=startY;lastControl=null;lastType=c;cmd='';continue;}
   if(!count[c])throw new Error('This path uses arc commands. You can transform it visually or edit its SVG source; node editing supports straight lines and Bézier curves.');
   const n=count[c],values=tokens.slice(at,at+n).map(Number);if(values.length!==n||values.some(v=>!Number.isFinite(v)))throw new Error('The path data is incomplete.');at+=n;const ox=x,oy=y;
   const px=i=>values[i]+(relative?ox:0),py=i=>values[i]+(relative?oy:0);
   if(c==='M'){x=px(0);y=py(1);startX=x;startY=y;section={nodes:[anchor(x,y)],closed:false};sections.push(section);cmd=relative?'l':'L';}
   else{if(!section)throw new Error('Start a path with M.');const previous=section.nodes.at(-1);let next;
    if(c==='L'){x=px(0);y=py(1);next=anchor(x,y);lastControl=null;}
    if(c==='H'){x=px(0);next=anchor(x,y);lastControl=null;}
    if(c==='V'){y=py(0);next=anchor(x,y);lastControl=null;}
    if(c==='C'){previous.ox=px(0);previous.oy=py(1);x=px(4);y=py(5);next=anchor(x,y,px(2),py(3));lastControl={x:next.ix,y:next.iy};}
    if(c==='S'){previous.ox=['C','S'].includes(lastType)&&lastControl?2*ox-lastControl.x:ox;previous.oy=['C','S'].includes(lastType)&&lastControl?2*oy-lastControl.y:oy;x=px(2);y=py(3);next=anchor(x,y,px(0),py(1));lastControl={x:next.ix,y:next.iy};}
    if(c==='Q'||c==='T'){const q=c==='Q'?{x:px(0),y:py(1)}:['Q','T'].includes(lastType)&&lastControl?{x:2*ox-lastControl.x,y:2*oy-lastControl.y}:{x:ox,y:oy};x=px(c==='Q'?2:0);y=py(c==='Q'?3:1);previous.ox=ox+(q.x-ox)*2/3;previous.oy=oy+(q.y-oy)*2/3;next=anchor(x,y,x+(q.x-x)*2/3,y+(q.y-y)*2/3);lastControl=q;}
    section.nodes.push(next);
   }lastType=c;
  }return sections;
 }
 function convertToPath(el){if(el.localName==='path')return el;let d='';const n=a=>parseFloat(el.getAttribute(a))||0;
  if(el.localName==='line')d=`M${n('x1')} ${n('y1')} L${n('x2')} ${n('y2')}`;
  if(el.localName==='rect'){const x=n('x'),y=n('y'),w=n('width'),h=n('height'),rx=Math.min(w/2,n('rx')||n('ry')),ry=Math.min(h/2,n('ry')||n('rx')),k=.5522847498;
   d=rx&&ry?`M${x+rx} ${y} H${x+w-rx} C${x+w-rx+k*rx} ${y} ${x+w} ${y+ry-k*ry} ${x+w} ${y+ry} V${y+h-ry} C${x+w} ${y+h-ry+k*ry} ${x+w-rx+k*rx} ${y+h} ${x+w-rx} ${y+h} H${x+rx} C${x+rx-k*rx} ${y+h} ${x} ${y+h-ry+k*ry} ${x} ${y+h-ry} V${y+ry} C${x} ${y+ry-k*ry} ${x+rx-k*rx} ${y} ${x+rx} ${y} Z`:`M${x} ${y} L${x+w} ${y} L${x+w} ${y+h} L${x} ${y+h} Z`;
  }
  if(['polyline','polygon'].includes(el.localName)){const p=el.getAttribute('points').trim().split(/[\s,]+/).map(Number);d=p.map((v,i)=>i%2?'':`${i?'L':'M'}${v} ${p[i+1]}`).join(' ')+(el.localName==='polygon'?' Z':'');}
  if(['circle','ellipse'].includes(el.localName)){const x=n('cx'),y=n('cy'),rx=el.localName==='circle'?n('r'):n('rx'),ry=el.localName==='circle'?n('r'):n('ry'),k=.5522847498;d=`M${x+rx} ${y} C${x+rx} ${y+ry*k} ${x+rx*k} ${y+ry} ${x} ${y+ry} C${x-rx*k} ${y+ry} ${x-rx} ${y+ry*k} ${x-rx} ${y} C${x-rx} ${y-ry*k} ${x-rx*k} ${y-ry} ${x} ${y-ry} C${x+rx*k} ${y-ry} ${x+rx} ${y-ry*k} ${x+rx} ${y} Z`;}
  if(!d)throw new Error('Select a path, line or shape. Ungroup compound artwork to edit its individual paths.');
  const p=make('path');for(const a of el.attributes)p.setAttributeNS(a.namespaceURI,a.name,a.value);for(const a of ['x','y','width','height','rx','ry','cx','cy','r','x1','x2','y1','y2','points'])p.removeAttribute(a);p.setAttribute('d',d);el.replaceWith(p);return p;
 }
 function editNodes(el){try{const old=before(),converted=convertToPath(el);selected=[converted];nodes={el:converted,sections:parsePath(converted.getAttribute('d')),active:null};if(converted!==el)commit(old);status('Drag square anchors or circular curve handles. Alt-drag a handle to break symmetry. Add node splits a segment.');render();}catch(error){nodes=null;status(error.message,true);}}
 function renderNodes(unit){const state=nodes,m=rootMatrix(state.el);state.sections.forEach((section,s)=>section.nodes.forEach((n,i)=>{const p=new DOMPoint(n.x,n.y).matrixTransform(m);for(const k of ['i','o']){const q=new DOMPoint(n[k+'x'],n[k+'y']).matrixTransform(m);if(Math.hypot(q.x-p.x,q.y-p.y)>unit){overlay.append(make('line',{x1:p.x,y1:p.y,x2:q.x,y2:q.y,stroke:'#007cb9','stroke-width':unit}));overlay.append(make('circle',{cx:q.x,cy:q.y,r:3.5*unit,fill:'white',stroke:'#007cb9','stroke-width':unit,'data-node':`${s}:${i}:${k}`,style:'cursor:crosshair'}));}}overlay.append(make('rect',{x:p.x-3.5*unit,y:p.y-3.5*unit,width:7*unit,height:7*unit,fill:state.active?.s===s&&state.active?.i===i?'#007cb9':'white',stroke:'#007cb9','stroke-width':unit,'data-node':`${s}:${i}:a`,style:'cursor:move'}));}));}
 function addNode(){if(!nodes){if(selected.length===1)editNodes(selected[0]);else return;}if(!nodes)return;const old=before(),s=nodes.active?.s||0,section=nodes.sections[s],i=Math.min(nodes.active?.i||0,section.nodes.length-2),a=section.nodes[i],b=section.nodes[i+1];if(!a||!b)return;
  const mid=(p,q)=>({x:(p.x+q.x)/2,y:(p.y+q.y)/2}),p0={x:a.x,y:a.y},p1={x:a.ox,y:a.oy},p2={x:b.ix,y:b.iy},p3={x:b.x,y:b.y},q0=mid(p0,p1),q1=mid(p1,p2),q2=mid(p2,p3),r0=mid(q0,q1),r1=mid(q1,q2),p=mid(r0,r1);a.ox=q0.x;a.oy=q0.y;b.ix=q2.x;b.iy=q2.y;section.nodes.splice(i+1,0,{x:p.x,y:p.y,ix:r0.x,iy:r0.y,ox:r1.x,oy:r1.y});nodes.active={s,i:i+1};updatePath(nodes);commit(old);
 }
 const orderedSelection=()=>objects().filter(el=>selected.includes(el));
 const objectRow=el=>$('layers').querySelector(`[data-object-index="${objects().indexOf(el)}"]`);
 function canAction(name){
  if(['import','export','save'].includes(name))return true;
  if(!$('sourcePane').hidden)return false;
  if(name==='undo')return history.length>0||!!draft;if(name==='redo')return future.length>0;
  if(name==='guides')return true;if(name==='fit'||name==='selectAll')return objects().length>0;
  if(name==='paste')return !!clipboard?.elements.length;if(name==='group')return selected.length>1;
  if(name==='ungroup')return selected.some(el=>el.localName==='g');
  if(name==='editNodes'||name==='addNode')return selected.length===1&&['rect','circle','ellipse','path','line','polyline','polygon'].includes(selected[0].localName);
  if(name==='deleteNode'){const section=nodes?.sections[nodes.active?.s];return !!nodes?.active&&!!section&&section.nodes.length>(section.closed?3:2);}
  if(['front','back','toFront','toBack'].includes(name)){const all=objects(),forward=name==='front'||name==='toFront';return all.some((el,i)=>selected.includes(el)&&all.slice(forward?i+1:0,forward?undefined:i).some(other=>!selected.includes(other)));}
  return selected.length>0;
 }
 function remapIds(elements){
  const mapping=new Map(),all=elements.flatMap(el=>[el,...el.querySelectorAll('*')]);
  for(const el of all)if(el.id){const id='copy-'+crypto.randomUUID().replaceAll('-','');mapping.set(el.id,id);el.id=id;}
  for(const el of all){if(el.hasAttribute('data-snap-stroke-id'))el.removeAttribute('data-snap-stroke-id');for(const a of [...el.attributes]){
   if(a.name.startsWith('data-'))continue;
   if(a.localName==='href'&&a.value.startsWith('#')&&mapping.has(a.value.slice(1)))a.value='#'+mapping.get(a.value.slice(1));
   else a.value=a.value.replace(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/g,(whole,id)=>mapping.has(id)?`url(#${mapping.get(id)})`:whole);
  }}
 }
 function snapshotElements(elements){
  const paint=['color','fill','fill-opacity','fill-rule','stroke','stroke-width','stroke-opacity','stroke-dasharray','stroke-dashoffset','stroke-linecap','stroke-linejoin','stroke-miterlimit','opacity','font-family','font-size','font-weight','font-style','text-anchor','dominant-baseline','stop-color','stop-opacity','clip-path','mask','display','visibility','vector-effect'];
  return elements.map(el=>{const clone=el.cloneNode(true),from=[el,...el.querySelectorAll('*')],to=[clone,...clone.querySelectorAll('*')];
   if(drawable.has(clone.localName)&&clone.id&&!clone.hasAttribute('data-name'))clone.setAttribute('data-name',clone.id);
   const targets=window.snapPerspective.targets.read(el);if(targets!==63)clone.setAttribute('data-snap-targets',targets);
   from.forEach((original,i)=>{const computed=getComputedStyle(original);for(const key of paint){const value=computed.getPropertyValue(key).replace(/url\(["']?[^)]*?#([^)'"\s]+)["']?\)/g,'url(#$1)');if(value)to[i].style.setProperty(key,value,'important');}});
   clone.querySelectorAll('[data-snap-stroke-derived],[data-snap-stroke-defs],style').forEach(child=>child.remove());return clone;
  });
 }
 function copyObjects(){
  const elements=orderedSelection();if(!elements.length)return;
  const resources=[...svg.children].filter(el=>el.localName==='defs'&&!el.hasAttribute('data-snap-stroke-defs'));
  // Include referenced artwork (for example <use> targets), including references between copied objects.
  const included=new Set([...elements,...resources].flatMap(el=>[el,...el.querySelectorAll('*')]).map(el=>el.id).filter(Boolean));
  for(const el of [...elements,...resources])collectReferences(el);
  function collectReferences(el){for(const child of [el,...el.querySelectorAll('*')])for(const a of child.attributes){const ids=a.localName==='href'&&a.value.startsWith('#')?[a.value.slice(1)]:[...a.value.matchAll(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/g)].map(m=>m[1]);for(const id of ids){if(included.has(id))continue;const target=svg.querySelector('#'+CSS.escape(id));if(target){for(const descendant of [target,...target.querySelectorAll('[id]')])if(descendant.id)included.add(descendant.id);resources.push(target);collectReferences(target);}}}}
  // Resolve inherited and stylesheet paints before detaching from the current document.
  clipboard={elements:snapshotElements(elements),resources:snapshotElements(resources),pastes:0};
  status(`Copied ${elements.length===1?'object':elements.length+' objects'} · paste within this SVG editor.`);objectMenu?.refresh();
 }
 function reorder(name){
  const all=objects(),chosen=all.filter(el=>selected.includes(el)),other=all.filter(el=>!selected.includes(el));let next=all.slice();
  if(name==='toFront')next=[...other,...chosen];else if(name==='toBack')next=[...chosen,...other];
  else if(name==='front'){for(let i=next.length-2;i>=0;i--)if(selected.includes(next[i])&&!selected.includes(next[i+1]))[next[i],next[i+1]]=[next[i+1],next[i]];}
  else{for(let i=1;i<next.length;i++)if(selected.includes(next[i])&&!selected.includes(next[i-1]))[next[i-1],next[i]]=[next[i],next[i-1]];}
  next.forEach(el=>svg.insertBefore(el,overlay));
 }
 function removeObjects(){
  const references=new Set(),retained=new Set(),addReferences=el=>{for(const a of el.attributes){if(a.localName==='href'&&a.value.startsWith('#'))references.add(a.value.slice(1));for(const m of a.value.matchAll(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/g))references.add(m[1]);}if(el.localName==='style')for(const m of el.textContent.matchAll(/url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)/g))references.add(m[1]);};
  for(const el of svg.querySelectorAll('*'))if(!selected.some(object=>object===el||object.contains(el)))addReferences(el);
  let changed=true;while(changed){changed=false;for(const object of selected){if(retained.has(object))continue;const tree=[object,...object.querySelectorAll('*')];if(tree.some(el=>el.id&&references.has(el.id))){retained.add(object);tree.forEach(addReferences);changed=true;}}}
  // Removing visible artwork must not leave surviving <use>, gradient or clip references dangling.
  const resources=retained.size?make('defs'):null;
  for(const object of selected){if(retained.has(object)){const clone=snapshotElements([object])[0];resources.append(clone);}object.remove();}
  if(resources)svg.insertBefore(resources,overlay);selected=[];nodes=null;
 }
 function action(name){try{if(name==='import'||name==='export'){bridge(name,name==='export'?{svg:exportSvg()}:{});return;}if(name==='save'){bridge('save');return;}if(name==='undo'||name==='redo'){finishPath();const from=name==='undo'?history:future,to=name==='undo'?future:history;if(from.length){to.push(before());loadSvg(from.pop(),{resetHistory:false});}render();return;}
  finishPath();if(!canAction(name))return;
  if(name==='guides'){grid.open();return;}if(name==='properties'){grid.showProperties();$('selectionTitle').scrollIntoView({block:'nearest'});$('fill').focus({preventScroll:true});return;}
  if(name==='selectAll'){select(objects());return;}if(name==='deselect'){select([]);return;}if(name==='copy'){copyObjects();return;}
  if(name==='editNodes'){setTool('nodes');return;}if(name==='addNode'){addNode();return;}const old=before(),b=bounds(selected);
  if(name==='deleteNode'){const{s,i}=nodes.active;nodes.sections[s].nodes.splice(i,1);nodes.active=null;updatePath(nodes);}
  if(name==='cut'){copyObjects();removeObjects();}
  if(name==='delete')removeObjects();
  if(name==='duplicate'||name==='paste'){
   const copies=name==='duplicate'?snapshotElements(orderedSelection()):clipboard.elements.map(el=>el.cloneNode(true)),resources=name==='paste'?clipboard.resources.map(el=>el.cloneNode(true)):[];
   remapIds([...resources,...copies]);if(resources.length){const defs=make('defs');defs.append(...resources);svg.insertBefore(defs,overlay);}copies.forEach(el=>svg.insertBefore(el,overlay));
   const offset=name==='paste'?10*++clipboard.pastes:10;selected=copies;moved(copies,{x:offset,y:offset});nodes=null;
  }
  if(['front','back','toFront','toBack'].includes(name))reorder(name);
  if(name==='group'&&selected.length>1){const group=make('g'),ordered=orderedSelection();ordered.at(-1).before(group);ordered.forEach(el=>group.append(el));selected=[group];nodes=null;}
  if(name==='ungroup'){const children=[];for(const group of selected){if(group.localName!=='g'){children.push(group);continue;}const m=localMatrix(group);for(const child of [...group.children]){if(strokePatterns.isDerived(child))continue;if(!drawable.has(child.localName)){group.before(child);continue;}const targets=window.snapPerspective.targets.read(child);if(targets!==63)child.setAttribute('data-snap-targets',targets);const c=getComputedStyle(child);for(const p of ['fill','stroke','stroke-width','font-family','font-size'])if(!child.hasAttribute(p)&&!child.style.getPropertyValue(p))child.setAttribute(p,c.getPropertyValue(p));const t=m.multiply(localMatrix(child));child.setAttribute('transform',`matrix(${[t.a,t.b,t.c,t.d,t.e,t.f].join(' ')})`);group.before(child);children.push(child);}group.remove();}selected=children;nodes=null;}
  if(name==='rotate'&&b)transformed(selected,new DOMMatrix().translate(b.x+b.width/2,b.y+b.height/2).rotate(15).translate(-b.x-b.width/2,-b.y-b.height/2),selected.map(localMatrix));
  if((name==='flipH'||name==='flipV')&&b)transformed(selected,new DOMMatrix().translate(b.x+b.width/2,b.y+b.height/2).scale(name==='flipH'?-1:1,name==='flipV'?-1:1).translate(-b.x-b.width/2,-b.y-b.height/2),selected.map(localMatrix));
  if(name==='fit'){const all=bounds(objects());if(all){let pad=8;for(const el of objects().flatMap(el=>[el,...el.querySelectorAll('*')])){const c=getComputedStyle(el);if(c.stroke!=='none'){const m=rootMatrix(el);pad=Math.max(pad,(parseFloat(c.strokeWidth)||0)*Math.max(Math.hypot(m.a,m.b),Math.hypot(m.c,m.d))/2+1);}}svg.setAttribute('viewBox',`${num(all.x-pad)} ${num(all.y-pad)} ${num(Math.max(1,all.width+pad*2))} ${num(Math.max(1,all.height+pad*2))}`);resizeFrame();}}
  commit(old);
 }catch(error){status(error.message,true);}}
 function setProperty(id){try{const old=before();
  if(['strokePattern','strokePatternScale','strokePatternSpacing'].includes(id)||id==='dash'&&$('dash').value==='custom'){const pattern=strokePatterns.chosen();if(!pattern){$('customStrokeFields').hidden=false;status('Choose a saved SVG pattern. Create patterns using Line style in the main editor.');return;}defaults.dash='custom';strokePatterns.apply(selected,pattern);commit(old);return;}
  if(id==='dash')strokePatterns.remove(selected);
  if(['fill','fillNone','stroke','strokeNone','strokeWidth','dash'].includes(id)){defaults.fill=$('fillNone').checked?'none':$('fill').value;defaults.stroke=$('strokeNone').checked?'none':$('stroke').value;defaults.strokeWidth=Math.max(0,+$('strokeWidth').value||0);defaults.dash=$('dash').value;for(const el of paintTargets()){if(id.startsWith('fill'))setPaint(el,'fill',defaults.fill);if(id.startsWith('stroke')&&id!=='strokeWidth')setPaint(el,'stroke',defaults.stroke);if(id==='strokeWidth')setPaint(el,'stroke-width',defaults.strokeWidth);if(id==='dash'||id==='strokeWidth')setPaint(el,'stroke-dasharray',dashValue(defaults.strokeWidth,defaults.dash));}}
  if(id==='opacity'){defaults.opacity=Math.max(0,Math.min(100,+$('opacity').value));selected.forEach(el=>setPaint(el,'opacity',defaults.opacity/100));}
  if(id==='radius'){defaults.radius=Math.max(0,+$('radius').value||0);selected.filter(el=>el.localName==='rect').forEach(el=>el.setAttribute('rx',defaults.radius));}
  if(['font','fontSize','textValue'].includes(id)){defaults.font=$('font').value;defaults.fontSize=Math.max(1,+$('fontSize').value||1);defaults.text=$('textValue').value;selected.filter(el=>el.localName==='text').forEach(el=>{if(id==='textValue')el.textContent=defaults.text;if(id==='font')setPaint(el,'font-family',defaults.font);if(id==='fontSize')setPaint(el,'font-size',defaults.fontSize);});}
  if(['posX','posY','sizeW','sizeH'].includes(id)&&selected.length){const b=bounds(selected),value=+$(id).value;if(!Number.isFinite(value))return;if(id==='posX'||id==='posY')moved(selected,{x:id==='posX'?value-b.x:0,y:id==='posY'?value-b.y:0});else{let delta=new DOMMatrix();if(id==='sizeW')delta=delta.translate(b.x,b.y).scale(Math.max(.001,value/Math.max(.01,b.width)),1).translate(-b.x,-b.y);if(id==='sizeH')delta=delta.translate(b.x,b.y).scale(1,Math.max(.001,value/Math.max(.01,b.height))).translate(-b.x,-b.y);transformed(selected,delta,selected.map(localMatrix));}}
  commit(old);
 }catch(error){status(error.message,true);}}
 function insertPreset(name){if(!name)return;finishPath();const old=before(),v=view(),x=v.x+v.width/4,y=v.y+v.height/4,w=v.width/2,h=v.height/2;let el;
  if(name==='star'){const points=Array.from({length:10},(_,i)=>{const a=i*Math.PI/5-Math.PI/2,r=i%2?.42:1;return`${num(x+w/2+Math.cos(a)*w/2*r)},${num(y+h/2+Math.sin(a)*h/2*r)}`;}).join(' ');el=newShape('polygon',{points});}
  if(name==='triangle')el=newShape('polygon',{points:`${x+w/2},${y} ${x+w},${y+h} ${x},${y+h}`});
  if(name==='diamond')el=newShape('polygon',{points:`${x+w/2},${y} ${x+w},${y+h/2} ${x+w/2},${y+h} ${x},${y+h/2}`});
  if(name==='bubble')el=newShape('path',{d:`M${x} ${y}h${w}v${h*.7}h${-w*.45}l${-w*.3} ${h*.3}v${-h*.3}h${-w*.25}Z`});
  if(name==='check')el=newShape('path',{d:`M${x} ${y+h*.55}L${x+w*.35} ${y+h}L${x+w} ${y}`,fill:'none','stroke-width':Math.max(5,defaults.strokeWidth),stroke:defaults.stroke==='none'?'#4cc2ff':defaults.stroke});
  if(name==='arrow')el=newShape('path',{d:`M${x} ${y+h*.3}h${w*.6}v${-h*.3}l${w*.4} ${h*.5}l${-w*.4} ${h*.5}v${-h*.3}h${-w*.6}Z`});
  if(el){selected=[el];nodes=null;commit(old);setTool('select');}$('preset').value='';
 }
 function applySource(){const previous=before(),value=$('source').value;loadSvg(value,{resetHistory:false});history.push(previous);future=[];bridge('changed');render();return true;}
 function exportSvg(){document.activeElement?.blur();if(!$('sourcePane').hidden)applySource();finishPath();return cleanSvg();}
 targetSettings=window.createPerspectiveTargets({selected:()=>selected,getMode:()=>grid.getState().mode,before,commit,status,render});
 objectMenu=window.createSvgObjectMenu({canAction,onAction:action,onCanvasContext:e=>{finishPath();const target=e.composedPath().filter(el=>el instanceof SVGElement).map(topObject).find(Boolean);if(target&&!selected.includes(target))select([target]);},onListContext:row=>{finishPath();const target=objects()[Number(row.dataset.objectIndex)];if(target&&!selected.includes(target))select([target]);return()=>objectRow(target);}});
 for(const eventName of ['keydown','keyup'])document.addEventListener(eventName,e=>{if((e.key==='Control'||e.key==='Meta')&&lastHoverEvent)objectHover({...lastHoverEvent,ctrlKey:e.ctrlKey,metaKey:e.metaKey});});
 $('tools').onclick=e=>{const button=e.target.closest('[data-tool]');if(button)setTool(button.dataset.tool);};document.querySelectorAll('[data-action]:not([role=menuitem])').forEach(button=>button.onclick=()=>action(button.dataset.action));
 $('preset').onchange=()=>insertPreset($('preset').value);for(const id of ['fill','fillNone','stroke','strokeNone','strokeWidth','opacity','dash','radius','font','fontSize','textValue','posX','posY','sizeW','sizeH','strokePattern','strokePatternScale','strokePatternSpacing'])$(id).onchange=()=>setProperty(id);
 $('perspectiveStrokeColor').oninput=e=>{$('stroke').value=e.target.value;$('strokeNone').checked=false;setProperty('stroke');};$('perspectiveStrokeWidth').onchange=e=>{$('strokeWidth').value=e.target.value;setProperty('strokeWidth');};
 $('sourceToggle').onclick=()=>{objectMenu.close();if($('sourcePane').hidden){finishPath();$('source').value=cleanSvg();$('sourcePane').hidden=false;$('stage').hidden=true;$('sourceToggle').setAttribute('aria-pressed','true');}else{try{applySource();$('sourcePane').hidden=true;$('stage').hidden=false;$('sourceToggle').setAttribute('aria-pressed','false');resizeFrame();}catch(error){status(error.message,true);}}objectMenu.refresh();};
 $('applySource').onclick=()=>{try{applySource();$('sourcePane').hidden=true;$('stage').hidden=false;$('sourceToggle').setAttribute('aria-pressed','false');resizeFrame();objectMenu.refresh();}catch(error){status(error.message,true);}};
 document.addEventListener('keydown',e=>{if(e.target.closest('input:not([type=checkbox]),textarea,select'))return;const control=e.ctrlKey||e.metaKey,key=e.key.toLowerCase(),shortcut=control?({z:e.shiftKey?'redo':'undo',y:'redo',d:'duplicate',s:'save',a:'selectAll',c:'copy',x:'cut',v:'paste',g:e.shiftKey?'ungroup':'group','[':e.shiftKey?'toBack':'back',']':e.shiftKey?'toFront':'front','{':'toBack','}':'toFront'}[key]):null;if(shortcut){e.preventDefault();objectMenu.close(true);action(shortcut);return;}if(e.key==='Delete'||e.key==='Backspace'){e.preventDefault();objectMenu.close(true);action(nodes?.active?'deleteNode':'delete');return;}if(e.key==='Enter'&&draft){e.preventDefault();finishPath();return;}if(e.key==='Escape'){grid.clearMatch();if(drag){const old=drag.before;drag=null;if(old)loadSvg(old,{resetHistory:false});}else if(draft){const old=draft.before;draft=null;loadSvg(old,{resetHistory:false});}else select([]);return;}
  const keys={v:'select',n:'nodes',r:'rect',o:'ellipse',l:'line',a:'arrow',p:'pen',b:'path',t:'text'};if(!control&&keys[e.key.toLowerCase()]){e.preventDefault();setTool(keys[e.key.toLowerCase()]);return;}
  const delta={ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[e.key];if(delta&&selected.length){e.preventDefault();const old=before(),s=e.shiftKey?10:1;moved(selected,{x:delta[0]*s,y:delta[1]*s},undefined,undefined,e);commit(old);}
 });
 window.addEventListener('resize',()=>{resizeFrame();render();});
 function init(data){document.body.classList.toggle('light',data.theme==='Light');strokePatterns.setLibrary(data.strokePatterns);targetSettings.setDefault(data.initialTargets);if(data.grid)grid.setSettings(data.grid,false);if(data.fonts?.length){$('font').replaceChildren(...data.fonts.map(name=>new Option(name,name)));defaults.font=data.fonts.includes('Segoe UI')?'Segoe UI':data.fonts[0];$('font').value=defaults.font;}loadSvg(data.svg);if(data.initialTargets!==undefined&&data.initialTargets!==63)for(const el of objects())if(!el.hasAttribute('data-snap-targets'))el.setAttribute('data-snap-targets',data.initialTargets);if(data.initialStroke){strokePatterns.apply(objects(),data.initialStroke);defaults.dash='custom';}render();bridge('loaded');}
 window.chrome?.webview?.addEventListener('message',event=>{try{if(event.data.type==='load')init(event.data);else if(event.data.type==='replace'){const old=before();loadSvg(event.data.svg,{resetHistory:false});history.push(old);future=[];render();bridge('changed');}}catch(error){status(error.message,true);bridge('error',{message:error.message});}});
 window.snapSvgDesigner={loadSvg,exportSvg,setGrid:grid.setSettings,setStrokePatterns:strokePatterns.setLibrary,getState:()=>({objects:objects().length,selected:selected.length,tool,history:history.length,viewBox:view(),nodes:nodes?.sections.reduce((n,s)=>n+s.nodes.length,0)||0,grid:grid.getState(),snapObjects:objectSnapping.enabled}),action,setTool,insertPreset};
 loadSvg(`<svg xmlns="${NS}" viewBox="0 0 256 256" preserveAspectRatio="none"></svg>`);bridge('ready');
})();
