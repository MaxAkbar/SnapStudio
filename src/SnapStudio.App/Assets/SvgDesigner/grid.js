(() => {
 'use strict';
 const NS='http://www.w3.org/2000/svg',MIN_DISPLAY_SPACING=8,TOLERANCE=6,P=window.snapPerspective;
 window.createSvgGrid=({root,frame,onChange,onToolRequested,onOpen=()=>{},getTargets=()=>63,isSnappingEnabled=()=>true})=>{
  let settings={visible:false,spacing:32,snap:true,...P.normalize()},lastKey='',lastScale={x:1,y:1},geometryKey='',geometry=[];
  let selectedFamily='grid',guideHandles=null,snapMatch={x:false,y:false,coupled:false};
  const familyRows=new Map(),previousOpacity={};
  const $=id=>document.getElementById(id),make=(tag,attrs)=>{const el=document.createElementNS(NS,tag);for(const [key,value]of Object.entries(attrs))el.setAttribute(key,String(value));return el;};
  // Artwork styles stay inside the frame's shadow root; guides have their own isolated surface.
  const host=document.createElement('div');host.setAttribute('data-editor-grid-host','');host.setAttribute('aria-hidden','true');
  host.style.cssText='position:absolute;pointer-events:none;z-index:1';frame.parentElement.append(host);
  const gridRoot=host.attachShadow({mode:'open'}),style=document.createElement('style');style.textContent='svg{display:block;width:100%;height:100%;overflow:hidden;pointer-events:none}svg[hidden]{display:none}';gridRoot.append(style);
  const overlay=make('svg',{'data-editor-grid':'','aria-hidden':'true',focusable:'false',preserveAspectRatio:'none'});gridRoot.append(overlay);
  const names=['Rectangular','1-point perspective','2-point perspective','3-point perspective','4-point curved','5-point fisheye'];
  for(let mode=0;mode<names.length;mode++){const button=document.createElement('button');button.type='button';button.dataset.gridMode=mode;button.setAttribute('aria-label',names[mode]);button.title=names[mode];const icon=make('svg',{viewBox:'0 0 24 24','aria-hidden':'true'});icon.append(make('path',{d:P.icons[mode]}));const label=document.createElement('span');label.textContent=mode?`${mode} pt`:'Grid';button.append(icon,label);button.onclick=()=>{$('guideEditPoints').checked=true;setSettings({mode,visible:true});};$('gridModes').append(button);}
  const familyFields=new Map();
  for(const key of Object.keys(P.normalizeFamilies())){
   const card=document.createElement('section');card.className='guide-family';card.dataset.family=key;
   const heading=document.createElement('h3'),title=document.createElement('span'),color=document.createElement('input');color.type='color';heading.append(title,color);card.append(heading);
   const fields=document.createElement('div');fields.className='fields';card.append(fields);const controls={card,title,color};
   for(const field of ['opacity','density']){
    const wrap=document.createElement('div');wrap.className='grid-value';const label=document.createElement('label'),input=document.createElement('input'),slider=document.createElement('input');
    label.textContent=field==='opacity'?'Opacity (%)':'Density';input.type='number';slider.type='range';input.min=slider.min=field==='opacity'?0:4;input.max=slider.max=field==='opacity'?100:32;input.step=slider.step=1;
    label.append(input);wrap.append(label,slider);fields.append(wrap);controls[field]={wrap,input,slider};
    input.onchange=()=>{updateFamily(key,{[field]:input.value.trim()===''?settings.families[key][field]:Number(input.value)});input.value=settings.families[key][field];};
    slider.oninput=()=>updateFamily(key,{[field]:Number(slider.value)});
   }
   color.oninput=()=>updateFamily(key,{color:color.value});$('gridFamilies').append(card);familyFields.set(key,controls);
   const row=document.createElement('div');row.className='guide-family-row';row.dataset.guideFamily=key;
   const select=document.createElement('button');select.type='button';const dot=document.createElement('span');dot.className='guide-family-dot';const name=document.createElement('span');name.className='guide-family-name';const summary=document.createElement('span');summary.className='guide-family-summary';select.append(dot,name,summary);
   select.onclick=()=>selectFamily(key);
   const eye=document.createElement('button');eye.type='button';eye.className='guide-family-eye';eye.textContent='◉';eye.onclick=()=>{const opacity=settings.families[key].opacity;if(opacity)previousOpacity[key]=opacity;updateFamily(key,{opacity:opacity?0:previousOpacity[key]||75});};
   row.append(select,eye);$('guideFamilyList').append(row);familyRows.set(key,{row,select,dot,name,summary,eye});
  }
  function updateFamily(key,patch){setSettings({families:{[key]:{...settings.families[key],...patch}}});}
  function selectFamily(key){selectedFamily=key;render();}
  function strokes(state){const key=JSON.stringify([state.view,P.normalize(settings)]);if(key!==geometryKey){geometryKey=key;geometry=P.create(state.view,settings);}return geometry;}
  const snapStrokes=(state,targets=getTargets())=>strokes(state).filter(stroke=>targets&P.targets.bit(stroke.family));
  const displayNumber=value=>String(Number(value.toFixed(4)));
  const pathData=points=>points.map((p,i)=>`${i?'L':'M'}${p.x} ${p.y}`).join(' ');
  function paint(family,major){
   const style=settings.families[family],hex=style.color,rgb=parseInt(hex.slice(1),16),r=rgb>>16,g=(rgb>>8)&255,b=rgb&255;
   const alpha=Math.round(style.opacity*255/100),suffix=n=>n.toString(16).padStart(2,'0');
   return{line:hex+suffix(alpha),contrast:(299*r+587*g+114*b>=150000?'#000000':'#ffffff')+suffix(Math.round(alpha*.55)),width:major?1.15:.8};
  }
  function showMatch(match){overlay.querySelector('[data-perspective-match]')?.remove();if(match)overlay.append(make('path',{d:pathData(match.stroke.points),fill:'none',stroke:'#d53fb5','stroke-width':1.5,'stroke-dasharray':'4 3','vector-effect':'non-scaling-stroke','data-perspective-match':''}));}
  function layout(){
   const svg=root(),v=svg.viewBox.baseVal,m=svg.getScreenCTM();
   if(m){const x=Math.hypot(m.a,m.b),y=Math.hypot(m.c,m.d);if(x>0&&y>0)lastScale={x,y};}
   let stride=1;while(settings.spacing*Math.min(lastScale.x,lastScale.y)*stride<MIN_DISPLAY_SPACING&&stride<1048576)stride*=2;
   return{...settings,targets:getTargets(),snap:settings.snap&&isSnappingEnabled(),effectiveSpacing:settings.spacing*stride,stride,scale:lastScale,view:{x:v.x,y:v.y,width:v.width,height:v.height}};
  }
  function render(){
   const state=layout(),v=state.view,key=JSON.stringify([settings,v,state.scale]);
   const rect=root().getBoundingClientRect(),stage=frame.parentElement,stageRect=stage.getBoundingClientRect();
   host.hidden=!settings.visible||rect.width<=0||rect.height<=0;
   if(rect.width>0&&rect.height>0)Object.assign(host.style,{left:`${rect.left-stageRect.left+stage.scrollLeft-stage.clientLeft}px`,top:`${rect.top-stageRect.top+stage.scrollTop-stage.clientTop}px`,width:`${rect.width}px`,height:`${rect.height}px`});
   $('gridToggle').setAttribute('aria-pressed',String(settings.visible));$('gridSnap').checked=settings.snap;$('gridSnap').disabled=!settings.visible;
   $('gridModeIcon').replaceChildren(make('path',{d:P.icons[settings.mode]}));
   for(const button of $('gridModes').children)button.setAttribute('aria-pressed',String(Number(button.dataset.gridMode)===settings.mode));
   $('rectangularGridFields').hidden=!!settings.mode;$('perspectiveFields').hidden=!settings.mode;
   const side=['left','right'].includes(selectedFamily)&&settings.mode>=2&&settings.mode<=4;
   $('horizontalVanishingPoints').hidden=!side;$('guideLeftPosition').hidden=selectedFamily!=='left';$('guideRightPosition').hidden=selectedFamily!=='right';
   $('verticalVanishingPoint').hidden=!(selectedFamily==='top'&&settings.mode===3);$('perspectiveRadiusField').hidden=settings.mode<4;
   $('perspectiveCenterField').hidden=!(selectedFamily==='center'||selectedFamily==='top'&&settings.mode===3);
   $('guideRadiusLink').hidden=!(settings.mode>=4&&selectedFamily!=='grid'&&selectedFamily!=='center'&&!side);
   for(const key of ['centerX','horizon','left','right','vertical','radius']){const id='grid'+key[0].toUpperCase()+key.slice(1),input=$(id);if(document.activeElement!==input)input.value=displayNumber(settings[key]);$(id+'Slider').value=settings[key];}
   if(document.activeElement!==$('gridSpacing'))$('gridSpacing').value=settings.spacing;
   for(const [key,controls]of familyFields){
    const active=P.activeFamilies(settings.mode).includes(key),title=P.familyTitle(key,settings.mode),style=settings.families[key];controls.card.hidden=!active||key!==selectedFamily;controls.card.setAttribute('aria-label',title);controls.title.textContent=title;controls.color.value=style.color;controls.color.setAttribute('aria-label',title+' color');
    for(const field of ['opacity','density']){const {wrap,input,slider}=controls[field];wrap.hidden=field==='density'&&!settings.mode;if(document.activeElement!==input)input.value=style[field];slider.value=style[field];input.setAttribute('aria-label',title+' '+field);slider.setAttribute('aria-label',title+' '+field+' slider');}
    const row=familyRows.get(key);row.row.hidden=!active;row.row.classList.toggle('active',key===selectedFamily);row.row.classList.toggle('off',style.opacity===0);row.dot.style.background=style.color;row.name.textContent=title;row.summary.textContent=`${style.opacity.toFixed(0)}% · ${settings.mode?style.density:settings.spacing+'px'}`;row.select.setAttribute('aria-label','Edit '+title);row.select.setAttribute('aria-expanded',String(key===selectedFamily));row.eye.setAttribute('aria-label',(style.opacity?'Hide ':'Show ')+title);
   }
   $('gridSpacingDescription').textContent=settings.mode?'Perspective positions are percentages of the SVG viewBox. Vanishing points can be outside the artwork.':state.stride===1?'Spacing is measured in SVG coordinate units.':`At this scale, visible lines are ${state.effectiveSpacing} SVG units apart. Enlarge the editor for a finer grid.`;
   $('guideShowToggle').setAttribute('aria-pressed',String(settings.visible));$('guideShowToggle').textContent=settings.visible?'On':'Off';$('guideEditPoints').disabled=!settings.visible||!settings.mode;
   guideHandles?.render(state);
   overlay.toggleAttribute('hidden',!settings.visible);
   if(key===lastKey)return;lastKey=key;overlay.replaceChildren();overlay.setAttribute('viewBox',`${v.x} ${v.y} ${v.width} ${v.height}`);
   if(!settings.visible)return;
   if(settings.mode){for(const [index,stroke]of strokes(state).entries()){const attrs={d:pathData(stroke.points),fill:'none','vector-effect':'non-scaling-stroke'},ink=paint(stroke.family,stroke.major);overlay.append(make('path',{...attrs,stroke:ink.contrast,'stroke-width':2}),make('path',{...attrs,stroke:ink.line,'stroke-width':ink.width,'data-perspective-guide':index,'data-major':stroke.major,'data-family':stroke.family}));}return;}
   if(settings.families.grid.opacity===0)return;
   for(const axis of ['x','y']){
    const extent=axis==='x'?v.width:v.height,count=Math.floor(extent/state.effectiveSpacing);
    for(let i=0;i<=Math.min(count,100000);i++){
     const position=(axis==='x'?v.x:v.y)+i*state.effectiveSpacing,major=i%4===0;
     const attrs=axis==='x'?{x1:position,x2:position,y1:v.y,y2:v.y+v.height}:{y1:position,y2:position,x1:v.x,x2:v.x+v.width};
     const ink=paint('grid',major);overlay.append(make('line',{...attrs,stroke:ink.contrast,'stroke-width':2,'vector-effect':'non-scaling-stroke'}),
      make('line',{...attrs,stroke:ink.line,'stroke-width':ink.width,'vector-effect':'non-scaling-stroke','data-grid-axis':axis,'data-major':major,'data-family':'grid'}));
    }
   }
  }
  function setSettings(value={},notify=true){
   const oldMode=settings.mode;
   if(typeof value.visible==='boolean')settings.visible=value.visible;
   if(typeof value.snap==='boolean')settings.snap=value.snap;
   if(Object.hasOwn(value,'spacing')){const n=Number(value.spacing);settings.spacing=Number.isFinite(n)?Math.max(4,Math.min(512,Math.round(n))):32;}
   let families=Object.fromEntries(Object.entries(settings.families).map(([key,style])=>[key,{...style,...value.families?.[key]}]));
   if(Object.hasOwn(value,'density')&&!Object.hasOwn(value,'families'))for(const style of Object.values(families))style.density=value.density;
   Object.assign(settings,P.normalize({...settings,...value,families}));
   const activeFamilies=P.activeFamilies(settings.mode);if(!activeFamilies.includes(selectedFamily)||oldMode===0&&settings.mode)selectedFamily=activeFamilies.find(k=>k!=='grid')||'grid';
   render();if(notify)onChange({...settings});return getState();
  }
  function nearest(value,axis,state){
   const start=state.view[axis],extent=state.view[axis==='x'?'width':'height'],step=state.effectiveSpacing,index=Math.round((value-start)/step),position=start+index*step;
   return position>=start&&position<=start+extent&&Math.abs(position-value)*state.scale[axis]<=TOLERANCE+1e-6?position-value:null;
  }
  const active=(state,event,targets=getTargets())=>state.visible&&state.snap&&!event?.altKey&&targets!==0&&(state.mode||state.families.grid.opacity>0&&!!(targets&1));
  function snapPoint(p,event,targets=getTargets()){
   snapMatch={x:false,y:false,coupled:false};const state=layout();if(!active(state,event,targets)){showMatch(null);return p;}
   if(settings.mode){const match=P.find(p,snapStrokes(state,targets),TOLERANCE,null,null,state.scale.x,state.scale.y);showMatch(match);snapMatch={x:!!match,y:!!match,coupled:true};return match?.point||p;}
   const x=nearest(p.x,'x',state),y=nearest(p.y,'y',state);snapMatch={x:x!==null,y:y!==null,coupled:false};return{x:p.x+(x??0),y:p.y+(y??0)};
  }
  function snapStraightLine(start,end,event,direction=null){
   const state=layout();if(!settings.mode||!active(state,event)){showMatch(null);return null;}
   const result=P.constrainLine(start,end,state.view,settings,state.scale.x,state.scale.y,direction,getTargets());
   showMatch(result.stroke?{stroke:result.stroke}:null);return result;
  }
  function snapPerspectiveLine(start,end,event,direction=null){
   const state=layout();if(!settings.mode||!active(state,event)){showMatch(null);return{points:[start,end],stroke:null,direction:null};}
   const result=P.curveLine(start,end,state.view,settings,state.scale.x,state.scale.y,direction,getTargets());
   showMatch(result.stroke?{stroke:result.stroke}:null);return result;
  }
  function snapRectangle(start,end,event,plane){
   const state=layout(),enabled=!!settings.mode&&active(state,event);
   const result=enabled?P.rectangle(start,end,state.view,settings,plane):P.flatRectangle(start,end);
   showMatch(enabled&&result?{stroke:{points:result.outline}}:null);return result;
  }
  // Shift constrains a direction or aspect ratio. Snap along that ray, never independently on X/Y.
  function alongRay(anchors,direction,state){
   let best=null;const length=Math.hypot(direction.x,direction.y);if(length<1e-9)return{x:0,y:0};
   const unit={x:direction.x/length,y:direction.y/length};
   for(const anchor of anchors)for(const axis of ['x','y']){
    if(Math.abs(unit[axis])<1e-9)continue;const delta=nearest(anchor[axis],axis,state);if(delta===null)continue;
    const distance=delta/unit[axis],change={x:unit.x*distance,y:unit.y*distance},pixels=Math.hypot(change.x*state.scale.x,change.y*state.scale.y);
    if(pixels<=TOLERANCE+1e-6&&(!best||pixels<best.pixels))best={...change,pixels};
   }
   return{...(best||{x:0,y:0}),matched:!!best};
  }
  function snapRay(start,end,event){snapMatch={x:false,y:false,coupled:true};const state=layout();if(!active(state,event)){showMatch(null);return end;}if(settings.mode){const match=P.find(end,snapStrokes(state),TOLERANCE,{x:end.x-start.x,y:end.y-start.y},null,state.scale.x,state.scale.y);showMatch(match);snapMatch={x:!!match,y:!!match,coupled:true};return match?.point||end;}const delta=alongRay([end],{x:end.x-start.x,y:end.y-start.y},state);snapMatch={x:delta.matched,y:delta.matched,coupled:true};return{x:end.x+delta.x,y:end.y+delta.y};}
  function snapMove(box,delta,event){
   snapMatch={x:false,y:false,coupled:false};const state=layout();if(!active(state,event)){showMatch(null);return delta;}
   if(settings.mode){let best=null,change={x:0,y:0};for(const u of [0,.5,1])for(const v of [0,.5,1]){const anchor={x:box.x+box.width*u+delta.x,y:box.y+box.height*v+delta.y},match=P.find(anchor,snapStrokes(state),TOLERANCE,event?.shiftKey?delta:null,null,state.scale.x,state.scale.y);if(match&&(!best||match.distance<best.distance-1e-8)){best=match;change={x:match.point.x-anchor.x,y:match.point.y-anchor.y};}}showMatch(best);snapMatch={x:!!best,y:!!best,coupled:true};return{x:delta.x+change.x,y:delta.y+change.y};}
   const anchors=[0,.5,1].map(n=>({x:box.x+box.width*n+delta.x,y:box.y+box.height*n+delta.y}));
   if(event?.shiftKey){const change=alongRay(anchors,delta,state);snapMatch={x:change.matched,y:change.matched,coupled:true};return{x:delta.x+change.x,y:delta.y+change.y};}
   const correction={x:0,y:0};for(const axis of ['x','y']){let best=null;for(const anchor of anchors){const candidate=nearest(anchor[axis],axis,state);if(candidate!==null&&(best===null||Math.abs(candidate)<Math.abs(best)))best=candidate;}correction[axis]=best??0;snapMatch[axis]=best!==null;}
   return{x:delta.x+correction.x,y:delta.y+correction.y};
  }
  const getState=()=>{const{effectiveSpacing,stride}=layout();return{...settings,families:P.normalizeFamilies(settings.families),effectiveSpacing,stride};};
  for(const key of ['centerX','horizon','left','right','vertical','radius']){
   const id='grid'+key[0].toUpperCase()+key.slice(1),input=$(id),slider=$(id+'Slider');
   input.onchange=()=>{const value=input.value.trim();setSettings({[key]:value===''?settings[key]:Number(value)});input.value=displayNumber(settings[key]);};
   slider.oninput=()=>setSettings({[key]:Number(slider.value)});
  }
  $('resetPerspective').onclick=()=>setSettings({...P.normalize(),mode:settings.mode,families:settings.families});
  $('resetGridFamilies').onclick=()=>setSettings({families:P.normalizeFamilies()});
  document.addEventListener('pointerup',()=>showMatch(null));document.addEventListener('pointercancel',()=>showMatch(null));
  $('gridToggle').onclick=()=>setSettings({visible:!settings.visible});
  $('gridSpacing').onchange=()=>{setSettings({spacing:$('gridSpacing').value});$('gridSpacing').value=settings.spacing;};
  $('gridSnap').onchange=()=>setSettings({snap:$('gridSnap').checked});
  const panel=$('gridPanel'),toggle=$('gridSettingsToggle');
  function close(){
   guideHandles?.cancel();panel.hidden=true;$('guideInspectorPanel').hidden=true;$('objectInspectorPanel').hidden=false;
   document.body.classList.remove('guides-active');$('objectInspectorTab').setAttribute('aria-selected','true');$('guideInspectorTab').setAttribute('aria-selected','false');toggle.setAttribute('aria-expanded','false');render();
  }
  function open(editPoints=true){
   onOpen();panel.hidden=false;$('guideInspectorPanel').hidden=false;$('objectInspectorPanel').hidden=true;document.body.classList.add('guides-active');
   $('objectInspectorTab').setAttribute('aria-selected','false');$('guideInspectorTab').setAttribute('aria-selected','true');toggle.setAttribute('aria-expanded','true');$('guideEditPoints').checked=editPoints;
   render();if(editPoints)$(settings.mode?'gridHorizon':'gridSpacing').focus({preventScroll:true});
  }
  toggle.onclick=()=>open();$('guideInspectorTab').onclick=()=>open();$('objectInspectorTab').onclick=close;
  $('guideShowToggle').onclick=()=>setSettings({visible:!settings.visible});$('guideEditPoints').onchange=render;
  $('guideLineTool').onclick=()=>onToolRequested?.('perspectiveLine');$('guideRectangleTool').onclick=()=>onToolRequested?.('perspectiveRect');
  $('perspectiveOpenGuides').onclick=()=>open();
  panel.addEventListener('keydown',e=>e.stopPropagation());
  document.addEventListener('keydown',e=>{
   if(e.key==='Escape'&&guideHandles?.isDragging()){guideHandles.cancel();e.preventDefault();e.stopImmediatePropagation();}
  },true);
  guideHandles=window.createPerspectiveGuideHandles({frame,root,getState:layout,setSettings,isEnabled:()=>!panel.hidden&&$('guideEditPoints').checked,onSelect:selectFamily});
  new ResizeObserver(render).observe(frame);
  return{render,setSettings,open,showProperties:close,setToolState:tool=>{$('guideLineTool').setAttribute('aria-pressed',String(tool==='perspectiveLine'));$('guideRectangleTool').setAttribute('aria-pressed',String(tool==='perspectiveRect'));},snapPoint,snapRay,snapMove,snapStraightLine,snapPerspectiveLine,snapRectangle,getSnapMatch:()=>({...snapMatch}),clearMatch:()=>showMatch(null),getState,projectionContext:layout,showBindingMatch:stroke=>showMatch(stroke?{stroke}:null)};
 };
})();
